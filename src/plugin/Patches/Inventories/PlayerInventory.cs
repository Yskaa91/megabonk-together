using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace MegabonkTogether.Patches.Inventories
{
    [HarmonyPatch(typeof(PlayerInventory))]
    internal static class PlayerInventoryPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Gold income scales with the number of enemies, which scales with the player
        /// count. Divide shared gold gains by the player count so the shared pool grows
        /// at the same rate as a solo run. Losses (chest cost) keep their progression.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerInventory.ChangeGold))]
        public static void ChangeGold_Prefix(ref int amount)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (!synchronizationService.IsSharedExperienceEnabled())
            {
                return;
            }

            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return; // Change received from the network, already normalized.
            }

            if (amount <= 0)
            {
                return; // Spending gold keeps the normal chest cost progression.
            }

            var players = playerManagerService.GetAllPlayers().Count();
            if (players > 1)
            {
                amount = Math.Max(1, (int)Math.Round((double)amount / players));
            }
        }

        /// <summary>
        /// Prevent inventory tick (spawning projectiles) when dead. Only tick status effects.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerInventory.PhysicsTick))]
        public static bool PhysicsTick_Prefix(PlayerInventory __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (GameManager.Instance.player.inventory != null && __instance == GameManager.Instance.player.inventory && GameManager.Instance.player.IsDead())
            {
                __instance.statusEffects.Tick();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Synchronize gold changes in shared experience mode.
        /// We only synchronize gold gain
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerInventory.ChangeGold))]
        public static void ChangeGold_Postfix(PlayerInventory __instance, int amount)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (!synchronizationService.IsSharedExperienceEnabled())
            {
                return;
            }

            if (!Plugin.CAN_SEND_MESSAGES)
            {
                return;
            }

            if (amount < 0)
            {
                return;
            }

            synchronizationService.OnChangeGold(amount);
        }
    }
}
