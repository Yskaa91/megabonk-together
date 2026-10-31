using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory.Stats;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace MegabonkTogether.Patches.Enemies
{
    [HarmonyPatch(typeof(EnemyStats))]
    internal static class EnemyStatsPatches
    {
        private static readonly IGameBalanceService gameBalanceService = Plugin.Services.GetService<IGameBalanceService>();
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// Adjust enemy HP based on netplay configuration
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyStats.GetHp))]
        private static void GetHpPostfix(Enemy enemy, ref float __result)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;
            if (!isServer)
            {
                return;
            }

            var multiplier = gameBalanceService.GetEnemyHpMultiplier(enemy.enemyFlag);
            __result *= multiplier;
        }

        /// <summary>
        /// Elite spawn chance is per enemy, and the host spawns more enemies with more
        /// players. Divide the chance so the total number of elites stays comparable.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(EnemyStats.GetEliteChance))]
        private static void GetEliteChancePostfix(ref float __result)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            var players = playerManagerService.GetAllPlayers().Count();
            if (players > 1)
            {
                __result /= players;
            }
        }
    }
}
