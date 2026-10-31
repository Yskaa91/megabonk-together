using Assets.Scripts.Managers;
using HarmonyLib;
using MegabonkTogether.Helpers;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;
using MonoMod.Utils;
using System.Linq;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(SpawnInteractables))]
    internal class SpawnInteractablesPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();
        private static readonly IGameBalanceService gameBalanceService = Plugin.Services.GetService<IGameBalanceService>();

        // numChestsPerStage is a static game setting, so keep the original value and
        // always scale from it instead of multiplying it in place.
        private static int baseNumChestsPerStage = -1;

        /// <summary>
        /// Add prefabs (Spawned by server)
        /// Also adjust free chest spawn rate
        /// </summary>  
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnChests))]
        public static bool SpawnChests_Prefix(SpawnInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode();
            if (!isServer.HasValue || !isServer.Value)
            {
                Plugin.Log.LogInfo("Skipping chest spawning on client");
                Plugin.Instance.AddPrefab(__instance.chest);
                Plugin.Instance.AddPrefab(__instance.chestFree);
                return false;
            }

            var freeChestMultiplier = gameBalanceService.GetFreeChestSpawnRateMultiplier();

            if (baseNumChestsPerStage < 0)
            {
                baseNumChestsPerStage = SpawnInteractables.numChestsPerStage;
            }

            var chestsBefore = SpawnInteractables.numChestsPerStage;
            var chanceBefore = __instance.chanceForFreeChest;

            __instance.chanceForFreeChest *= freeChestMultiplier;

            // In shared experience every chest is granted to every player, so the map
            // count has to be divided as well. Scaling only the free chest chance left
            // the paid chest count untouched and every player received the full amount.
            if (synchronizationService.IsSharedExperienceEnabled())
            {
                SpawnInteractables.numChestsPerStage = System.Math.Max(1, (int)System.Math.Round(baseNumChestsPerStage * freeChestMultiplier));
            }

            Plugin.Log.LogInfo($"[GameBalance] Chests: count {chestsBefore} -> {SpawnInteractables.numChestsPerStage} (base {baseNumChestsPerStage}), free chance {chanceBefore:F2} -> {__instance.chanceForFreeChest:F2} (multiplier {freeChestMultiplier:F2})");

            return true;
        }

        /// <summary>
        /// Send spawned chests to clients
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnChests))]
        public static void SpawnChests_Postfix(SpawnInteractables __instance)
        {
            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                var inGame = Il2CppFindHelper.FindAllGameObjects();
                foreach (var obj in inGame)
                {
                    if (obj.name.StartsWith(__instance.chest.name) && !obj.name.StartsWith(__instance.chestFree.name) && !obj.name.StartsWith("ChestFreeCrypt"))
                    {
                        var dynamic = DynamicData.For(obj);
                        var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                        if (!hasBeenSet.HasValue)
                        {
                            synchronizationService.OnSpawnedObject(obj);

                            dynamic.Set("hasBeenSetByServer", true);
                        }
                    }

                    if (obj.name.StartsWith(__instance.chestFree.name) && !obj.name.StartsWith("ChestFreeCrypt"))
                    {
                        var dynamic = DynamicData.For(obj);
                        var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                        if (!hasBeenSet.HasValue)
                        {
                            synchronizationService.OnSpawnedObject(obj);
                            dynamic.Set("hasBeenSetByServer", true);
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Add prefabs (Spawned by server)
        /// </summary>
        /// <returns></returns>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnRails))]
        public static bool SpawnRails_Prefix(SpawnInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode();
            if (!isServer.HasValue || !isServer.Value)
            {
                Plugin.Log.LogInfo("Skipping rail spawning on client");
                Plugin.Instance.AddPrefab(__instance.chest);
                Plugin.Instance.AddPrefab(__instance.chestFree);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Send spawned rails to clients
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnRails))]
        public static void SpawnRails_Postfix(SpawnInteractables __instance)
        {
            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                var inGame = Il2CppFindHelper.FindAllGameObjects();
                foreach (var obj in inGame)
                {
                    foreach (var rail in __instance.rails)
                    {
                        if (obj.name.StartsWith(rail.name))
                        {
                            var dynamic = DynamicData.For(obj);
                            var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                            if (!hasBeenSet.HasValue)
                            {
                                synchronizationService.OnSpawnedObject(obj);
                                dynamic.Set("hasBeenSetByServer", true);
                            }
                        }
                    }
                }
            }
        }


        /// <summary>
        /// Prevent shrine spawning on clients (Spawned by server) (Do you know a shadyGuy is a shrine ?)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnShrines))]
        public static bool SpawnShrines_Prefix(SpawnInteractables __instance)
        {
            if (!synchronizationService.HasNetplaySessionInitialized())
            {
                return true;
            }

            var isServer = synchronizationService.IsServerMode() ?? false;

            if (!isServer)
            {
                //For clients, shrines prefabs already added in MapGenerationController
                return false;
            }
            return true;
        }

        /// <summary>
        /// Send spawned shrines to clients. Intentionnaly omit shadyGuy and microwave, we want to wait for the rarity to be set before sending those (Will be sended at ShadyGuy.Start patch and Microwave.Start patch)
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(SpawnInteractables.SpawnShrines))]
        public static void SpawnShrines_Postfix(SpawnInteractables __instance)
        {
            var isServer = synchronizationService.IsServerMode();
            if (isServer.HasValue && isServer.Value)
            {
                var inGame = Il2CppFindHelper.FindAllGameObjects();
                var shrinesWithoutInteractablesRarity = MapController.currentMap.shrines
                    .Where(obj => obj.GetComponentInChildren<InteractableShadyGuy>() == null && obj.GetComponentInChildren<InteractableMicrowave>() == null); //Handle in those respective patches
                foreach (var obj in inGame)
                {
                    foreach (var shrine in shrinesWithoutInteractablesRarity)
                    {
                        if (obj.name.StartsWith(shrine.name))
                        {
                            var dynamic = DynamicData.For(obj);
                            var hasBeenSet = dynamic.Get<bool?>("hasBeenSetByServer");
                            if (!hasBeenSet.HasValue)
                            {
                                synchronizationService.OnSpawnedObject(obj);
                                dynamic.Set("hasBeenSetByServer", true);
                            }
                        }
                    }
                }
            }
        }
    }
}
