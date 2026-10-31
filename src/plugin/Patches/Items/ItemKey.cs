using Assets.Scripts.Inventory__Items__Pickups.Items.ItemImplementations;
using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace MegabonkTogether.Patches.Items
{
    [HarmonyPatch(typeof(ItemKey))]
    internal static class ItemKeyPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IPlayerManagerService playerManagerService = Plugin.Services.GetService<IPlayerManagerService>();

        /// <summary>
        /// The Key item grants a chance for a free chest. Every player can hold one, so
        /// the total chance scales with the lobby size. Divide it by the player count.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ItemKey.GetChance))]
        private static void GetChancePostfix(ref float __result)
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
