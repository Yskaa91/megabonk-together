# Desync analysis (3+ players)

## TL;DR

The mod uses a **host-authoritative star topology**: clients send their actions to the
host, the host applies them and re-broadcasts them to the other clients. The host never
needs to receive its own broadcasts, which is why **the host never desyncs while clients
do**. The problems get much worse at 3+ players because that is the first time a client's
action has to be **relayed through the host to a third peer** (with 2 players the host only
has to apply it locally).

Two concrete defects explain the reported symptoms (`players no longer doing damage`,
`chests not opening`, ...):

1. **The reliable-ordered channel was being flooded with high-frequency state and combat
   data.** All of those messages share LiteNetLib's single `ReliableOrdered` channel
   (channel 0). Once that channel is saturated, every critical one-shot action (chest
   opened, interactable used, weapon added, ...) is stuck behind thousands of retransmitted
   state packets. The host is fine because it is the sender, not the receiver.
2. **Global "suppression" flags could get stuck after an exception.** Network handlers set
   `Plugin.CAN_SEND_MESSAGES = false` (and friends) while applying a received event, then
   set it back to `true`. If any game method called in between throws, the flag stays
   `false` forever and that client silently stops sending damage / chest / interaction
   messages. This is a permanent, player-specific desync and is much more likely with 3+
   players because each client processes far more remote events.

A third, related bug amplified the traffic: received damage was re-broadcast by every
client back to the host (damage echo), and the host also relayed the same damage a second
time.

## Architecture recap

```
client A ──PlayerUpdate/EnemyDamaged/ChestOpened/...──▶ host ──▶ client B
                                                          └──▶ client C
```

* Host: `UdpClientService` (`src/plugin/Services/UdpClientService.cs`)
  * `SendLobbyUpdate()` – all players, 60 Hz (now 30 Hz)
  * `SendEnemiesUpdate()` – enemy deltas, 40 Hz
  * `SendProjectilesUpdate()` – projectile deltas, 20 Hz
  * `HandleMessage()` – applies a client message and relays it to the other clients
* Clients: `SynchronizationService` (`src/plugin/Services/SynchronizationService.cs`)
  * `OnEnemyDamaged`, `OnChestOpened`, `OnInteractableUsed`, `OnWeaponAdded`, ... send to
    the host.
* `EventManager` marshals every received message to the main thread through
  `MainThreadDispatcher`.

## Root cause 1 – reliable-ordered channel flooding

`DeliveryMethod.ReliableOrdered` is a single FIFO channel with retransmission. Everything
sent through it is delivered in order, so one lost packet blocks every later packet on that
channel until it is retransmitted.

The code was sending all of these through `ReliableOrdered`:

| Message | Frequency | Was |
| --- | --- | --- |
| `LobbyUpdates` (players) | 60 Hz | `Unreliable`, but **switched to `ReliableOrdered` whenever the payload was >= 1000 bytes** (i.e. as soon as 3+ players were in the game) |
| `LobbyUpdates` (enemies) | 40 Hz | same 1000-byte fallback |
| `ProjectilesUpdate` | 20 Hz | same 1000-byte fallback |
| `EnemyDamaged` (client → host) | per hit, AoE = dozens/tick | `ReliableOrdered` |
| `EnemyDamaged` (host → clients) | per hit | relayed **twice** (host `Damage` postfix + `HandleMessage`) |
| `SpawnedPickupOrb` | per XP orb | `ReliableOrdered` |
| `PickupApplied` / `PickupFollowingPlayer` | per pickup | `ReliableOrdered` |
| `SpawnedPickup` | per pickup | `ReliableOrdered` |
| `AddXp` | per XP orb collected | `ReliableOrdered` |
| `GoldChanged` | per gold gain | `ReliableOrdered` |
| `LightningStrike` | per proc | `ReliableOrdered` |

With 3+ players the snapshots exceed 1000 bytes, so the host starts pushing 60 Hz + 40 Hz +
20 Hz **reliable** snapshots to every client, while combat events keep adding more reliable
packets. The reliable queue grows without bound, latency climbs, and eventually the
critical actions (chest opened, interactable used, weapon added) arrive seconds or minutes
late — or the peer times out. That is exactly "chests don't open / players stop doing
damage" on clients while the host is unaffected.

The 1000-byte fallback is also wrong for a second reason: LiteNetLib starts with a
**1024-byte MTU** and an unreliable packet cannot be fragmented, so the fallback was a
workaround for `TooBigPacketException`. The correct fix is to split snapshots into several
unreliable packets, not to make them reliable.

## Root cause 2 – sticky suppression flags

`SynchronizationService` uses global flags to avoid echoing a received event back onto the
network, e.g.:

```csharp
Plugin.CAN_SEND_MESSAGES = false;
enemy.EnemyDied(damageContainer);   // may throw
Plugin.CAN_SEND_MESSAGES = true;    // never reached on exception
```

If `EnemyDied`, `Interact()`, `AddWeapon`, `AddTome`, `SpawnChest`, ... throws, the flag
stays `false`. Every patch that guards outgoing messages with
`if (!Plugin.CAN_SEND_MESSAGES) return;` then stops sending:
`EnemyDied`, `WeaponAdded`, `TomeAdded`, `ItemAdded/Removed`, `InteractableUsed`,
`GoldChanged`, `AddXp`, ... The client keeps playing but its actions never reach the host,
so it "no longer does damage" and "chests don't open". The exception itself is swallowed by
`MainThreadDispatcher`, so it is silent.

The same pattern exists for `CAN_DAMAGE_ENEMIES`, `CAN_SPAWN_CHESTS`,
`CAN_SPAWN_PICKUPS`, `CAN_ENEMY_EXPLODE`, `CAN_ENEMY_USE_SPECIAL_ATTACK`,
`CAN_SPAWN_TORNADOES`, `CAN_START_STOP_STORMS` and `IS_NETPLAYER_ADDING_TOME`.

## Root cause 3 – damage echo / double relay

`Enemy.Damage` has a postfix that sends `EnemyDamaged` to the host when the damage source
is an item. When a client receives `EnemyDamaged`, `OnReceivedEnemyDamaged` sets
`CAN_DAMAGE_ENEMIES = true` and calls `enemy.Damage(...)`; the postfix then sends the same
damage back to the host, which applies it again and broadcasts again. The host also
explicitly relayed `EnemyDamaged` in `HandleMessage` even though its own `Damage` postfix
already broadcasts it, so the other clients applied the same damage twice.

## Fixes applied

### `src/plugin/Services/UdpClientService.cs`

* Added a `DeliveryMethod` parameter to `SendToAllClientsExcept` so each relayed message can
  use the right channel.
* High-frequency state (`LobbyUpdates`, `ProjectilesUpdate`, `TumbleWeedsUpdate`) is now
  **always `Unreliable`** and is split into MTU-safe batches by `SendStateInBatches`
  instead of falling back to `ReliableOrdered`.
* `SendToHost` now preserves the requested delivery method on the relay path (it used to
  force `ReliableOrdered` for every relayed client update, including 60 Hz player updates).
* `EnemyDamaged` is no longer relayed explicitly (the host's `Damage` postfix broadcasts
  it), which removes the double application and the reliable flood.
* `EnemyDamaged` from clients is now `Unreliable`; the host is authoritative over HP and
  the next enemy snapshot corrects any loss.
* Projectile spawn/done and pickup events use `ReliableUnordered` (their own channel, no
  head-of-line blocking with critical ordered actions).
* `AddXp` and `GoldChanged` (very high frequency: one per XP orb / gold gain) use
  `ReliableUnordered` so they stay reliable but no longer block chest/damage/interaction
  messages.
* A failing peer send no longer aborts the whole broadcast loop.
* Relay peers are detected by comparing against **all** resolved addresses (IPv4/IPv6) so a
  relay connection is not mistaken for a direct peer.

### `src/plugin/Services/SynchronizationService.cs`

* `OnEnemyDamaged` (client) sends `Unreliable`.
* `OnProjectileDone`, `OnPickupSpawned`, `OnPickupApplied`, `OnPickupFollowingPlayer`,
  `OnWantToStartFollowingPickup`, `OnLightningStrike` no longer use `ReliableOrdered`.
* `OnPickupOrbSpawned` uses `Unreliable` (visual only).
* `OnReceivedEnemyDamaged` and `OnReceivedEnemyDied` restore their flags in `finally`.
* `OnReceivedInteractableUsed` (the largest handler, with many `Interact()` calls) is
  covered by the `MainThreadDispatcher` watchdog below.

### `src/plugin/Patches/Enemies/Enemy.cs`

* `Damage_Postfix` no longer re-broadcasts damage that is currently being applied from the
  network (`CAN_DAMAGE_ENEMIES` guard). This stops the damage echo/amplification.

### `src/plugin/Scripts/MainThreadDispatcher.cs`

* Every dispatched network action now snapshots and restores the network suppression flags
  in a `finally`. A single exception can no longer wedge a client permanently.

### `src/plugin/Scripts/NetworkHandler.cs`

* Lobby (player position) updates reduced from 60 Hz to 30 Hz. Remote players are
  interpolated, so this is visually identical and roughly halves that traffic.

## Why this matches the symptoms

* **Host fine, clients desync**: the host is authoritative and is the sender; only clients
  receive the flooded channel.
* **Only at 3+ players**: with 2 players a client action only has to be applied on the host.
  At 3+ it must also be relayed to a third peer, and the snapshots cross the 1000-byte
  threshold that used to switch them to reliable.
* **No damage / chests not opening**: both are client → host → other-clients actions that
  shared the flooded `ReliableOrdered` channel, and both are gated by the sticky
  `CAN_SEND_MESSAGES` flag on the sending client.

## How to verify

* Enable `Debug`/`Info` logging and watch for `Failed to send message` / `TooBigPacketException`
  (should now be rare) and for `MainThreadDispatcher` warnings (a warning followed by the
  client continuing to send messages means the watchdog did its job).
* `NetPeer.GetPacketsCountInReliableQueue(0, true)` can be logged per peer to confirm the
  reliable queue stays shallow during a 4-6 player run.
* A useful regression test: 4 players, one player spams an AoE weapon while another opens
  chests; chests should open immediately and the AoE player's damage should keep applying.

## Remaining known limitations (not addressed here)

* The host still sends snapshots to each client individually (O(N²) upload). If 5-6 player
  runs still saturate the host's upload, the next step is per-client interest management
  (only send enemies/projectiles near that client) and sending inventories only when they
  change.
* `PlayerManagerService.UpdatePlayer` is a remove-then-add; it is safe today because all
  callers are on the main thread, but it should be made atomic if background updates are
  ever added.
* `ProjectileManagerService.RemoveProjectilesByOwnerId` compares projectile IDs with a
  connection ID; it only runs on disconnect but is incorrect.
