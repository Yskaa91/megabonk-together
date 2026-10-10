using Assets.Scripts.Utility;
using HarmonyLib;
using MegabonkTogether.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(ChestOpening))]
    internal static class ChestOpeningPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();

        /// <summary>
        /// Skip the opening animation when in a netplay session and not in shared experience
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ChestOpening.OpenChest))]
        public static void OpenChest_Postfix(ChestOpening __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (synchronizationService.IsSharedExperienceEnabled())
            {
                return;
            }

            __instance.skipped = true;
        }
    }


    [HarmonyPatch(typeof(ChestWindowUi))]
    internal static class ChestWindowUiPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();
        private static MyButton openButton;

        /// <summary>
        /// Diagnostic for issue #93 (soft lock when opening chest)
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ChestWindowUi.Open))]
        public static void Open_Prefix(ChestWindowUi __instance, Assets.Scripts.UI.InGame.Rewards.EEncounter encounterType)
        {
            if (__instance.chestOpening == null || __instance.b_open == null ||
                __instance.b_leave == null || __instance.b_take == null ||
                __instance.b_banish == null || __instance.t_itemName == null)
            {
                Plugin.Log.LogError($"[chest #93] ChestWindowUi.Open about to dereference a destroyed ref: " +
                    $"encounter={encounterType} shared={synchronizationService.IsSharedExperienceEnabled()} " +
                    $"chestOpening={__instance.chestOpening == null} b_open={__instance.b_open == null} " +
                    $"b_leave={__instance.b_leave == null} b_take={__instance.b_take == null} " +
                    $"b_banish={__instance.b_banish == null} t_itemName={__instance.t_itemName == null}");
            }
        }

        /// <summary>
        /// Didn't find a proper way to hide the open button ¯\_(ツ)_/¯
        /// Also let the original method run if shared experience is enabled
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ChestWindowUi.Open))]
        public static void Open_Postfix(ChestWindowUi __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (synchronizationService.IsSharedExperienceEnabled())
            {
                return;
            }

            if (openButton == null)
            {
                openButton = __instance.b_open;
            }

            __instance.OpenButton();
            __instance.b_open = null;
        }

        /// <summary>
        /// Grant 8 seconds of invulnerability from the moment the chest reward pops up
        /// in netplay on non shared experience only.
        /// </summary>
        /// <param name="__instance"></param>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ChestWindowUi.OpeningFinished))]
        public static void OpeningFinished_Postfix(ChestWindowUi __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (synchronizationService.IsSharedExperienceEnabled())
            {
                return;
            }

            if (!GameManager.Instance.player.playerInput.CanInput())
            {
                Plugin.Log.LogInfo("Player cannot input, skipping invulnerability after chest opening.");
                return;
            }

            MyTime.Unpause();

            InvulnerabilityHelper.StartInvulnerable();
        }

        /// <summary>
        /// The invulnerability timer runs its fixed 8 seconds from popup even if the
        /// chest window is closed early, so only the open button is restored here.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(ChestWindowUi.OnClose))]
        public static void OnClose_Postfix(ChestWindowUi __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            __instance.b_open = openButton;
        }
    }
}
