using Assets.Scripts.Inventory__Items__Pickups;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(PlayerHealth))]
    internal static class PlayerHealthPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Synchronize player death event
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PlayerHealth.PlayerDied))]
        public static void PlayerDied_Postfix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            synchronizationService.OnPlayerDied();
        }

        /// <summary>
        /// Prevent remote inventory tick
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerHealth.Tick))]
        public static bool Tick_Prefix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            var isRemotePlayer = playerManagerService.IsRemotePlayerHealth(__instance);
            if (isRemotePlayer)
            {
                return false;
            }

            return true;
        }
        /// <summary>
        /// Block all incoming damage on the local player while the reward-screen
        /// invulnerability window is active. Remote players are unaffected.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(PlayerHealth.Damage))]
        public static bool Damage_Prefix(PlayerHealth __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return true;
            }

            if (playerManagerService.IsRemotePlayerHealth(__instance))
            {
                return true;
            }

            if (Plugin.Instance.IS_MANUAL_INVINCIBLE)
            {
                return false;
            }

            return true;
        }
    }
}
