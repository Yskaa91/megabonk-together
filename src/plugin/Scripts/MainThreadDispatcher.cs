using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace MegabonkTogether.Scripts
{
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> _executionQueue = new();

        public void Update()
        {
            while (_executionQueue.TryDequeue(out var action))
            {
                // Network handlers use global "suppression" flags to avoid re-broadcasting
                // events that they are currently applying locally (CAN_SEND_MESSAGES, ...).
                // If one of those handlers throws between setting and clearing a flag, the
                // flag stays stuck and the client silently stops sending damage, chest
                // openings, interactions, ... forever. Snapshot the flags and restore them
                // once the handler is done so a single exception can never wedge a client.
                var canSendMessages = Plugin.CAN_SEND_MESSAGES;
                var canDamageEnemies = Plugin.Instance?.CAN_DAMAGE_ENEMIES ?? false;
                var canSpawnPickups = Plugin.CAN_SPAWN_PICKUPS;
                var canSpawnChests = Plugin.CAN_SPAWN_CHESTS;
                var canEnemyExplode = Plugin.CAN_ENEMY_EXPLODE;
                var canEnemyUseSpecialAttack = Plugin.CAN_ENEMY_USE_SPECIAL_ATTACK;
                var canSpawnTornadoes = Plugin.Instance?.CAN_SPAWN_TORNADOES ?? false;
                var canStartStopStorms = Plugin.Instance?.CAN_START_STOP_STORMS ?? false;
                var isNetplayerAddingTome = Plugin.Instance?.IS_NETPLAYER_ADDING_TOME ?? false;

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(ex);
                }
                finally
                {
                    Plugin.CAN_SEND_MESSAGES = canSendMessages;
                    Plugin.CAN_SPAWN_PICKUPS = canSpawnPickups;
                    Plugin.CAN_SPAWN_CHESTS = canSpawnChests;
                    Plugin.CAN_ENEMY_EXPLODE = canEnemyExplode;
                    Plugin.CAN_ENEMY_USE_SPECIAL_ATTACK = canEnemyUseSpecialAttack;

                    if (Plugin.Instance != null)
                    {
                        Plugin.Instance.CAN_DAMAGE_ENEMIES = canDamageEnemies;
                        Plugin.Instance.CAN_SPAWN_TORNADOES = canSpawnTornadoes;
                        Plugin.Instance.CAN_START_STOP_STORMS = canStartStopStorms;
                        Plugin.Instance.IS_NETPLAYER_ADDING_TOME = isNetplayerAddingTome;
                    }
                }
            }
        }

        public static void Enqueue(Action action)
        {
            _executionQueue.Enqueue(action);
        }
    }
}
