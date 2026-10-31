using Assets.Scripts.Utility;
using HarmonyLib;
using MegabonkTogether.Helpers;
using Microsoft.Extensions.DependencyInjection;
using UnityEngine;

namespace MegabonkTogether.Patches
{
    // Shared invulnerability timer lives in Helpers.InvulnerabilityHelper now.
    [HarmonyPatch(typeof(LevelupScreen))]
    internal static class LevelUpScreenPatches
    {
        private static readonly Services.ISynchronizationService synchronizationService = Plugin.Services.GetService<Services.ISynchronizationService>();
        private static TMPro.TextMeshProUGUI infoText;

        /// <summary>
        /// Unpause timeStep (To prevent pause) and grant 3 seconds of invulnerability
        /// from the moment the screen pops up on non shared experience only.
        /// Unless interacting with a shady guy.
        /// </summary>

        [HarmonyPostfix]
        [HarmonyPatch(nameof(LevelupScreen.ShowLevelupScreen))]
        public static void ShowLevelupScreen_Postfix(LevelupScreen __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            // Banish mutates the shared loot pool and desyncs the other clients.
            if (__instance.b_banish != null)
            {
                __instance.b_banish.gameObject.SetActive(false);
            }

            if (!GameManager.Instance.player.playerInput.CanInput())
            {
                Plugin.Log.LogInfo("Player cannot input, skipping invincibility on level up screen open.");
                return;
            }

            if (infoText == null)
            {
                var go = new GameObject("InfoText");
                UnityEngine.GameObject.DontDestroyOnLoad(go);
                infoText = go.AddComponent<TMPro.TextMeshProUGUI>();
            }

            if (InteractableShadyGuy.currentlyInteracting != null)
            {
                infoText.enabled = true;
                infoText.transform.SetParent(__instance.transform);
                infoText.rectTransform.anchorMin = new Vector2(0, 0);
                infoText.rectTransform.anchorMax = new Vector2(0, 0);
                infoText.rectTransform.pivot = new Vector2(0, 0);
                infoText.rectTransform.anchoredPosition = new Vector2(100, 100);
                infoText.alignment = TMPro.TextAlignmentOptions.Center;
                infoText.fontSize = 36;
                infoText.color = Color.red;

                infoText.text = "You are vulnerable during shady guy interaction!";
                return;
            }

            MyTime.Unpause();

            InvulnerabilityHelper.StartInvulnerable();
        }

        /// <summary>
        /// The invulnerability timer runs its fixed 3 seconds from popup even if the
        /// screen is closed early, so there is nothing to do here besides shady guy cleanup.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(LevelupScreen.OnClose))]
        public static void OnClose_Postfix(LevelupScreen __instance)
        {
            if (!synchronizationService.HasNetplaySessionStarted())
            {
                return;
            }

            if (infoText == null)
            {
                var go = new GameObject("InfoText");
                UnityEngine.GameObject.DontDestroyOnLoad(go);
                infoText = go.AddComponent<TMPro.TextMeshProUGUI>();
            }

            if (InteractableShadyGuy.currentlyInteracting != null)
            {
                infoText.enabled = false;
                infoText.text = "";
                infoText.color = Color.white;
                InteractableShadyGuy.currentlyInteracting = null;
            }
        }

        /// <summary>
        /// Banish changes the shared loot pool, which the other clients do not see and
        /// which can leave their reward window stuck. Disable it in netplay.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(LevelupScreen.StartBanish))]
        public static bool StartBanish_Prefix()
        {
            return !synchronizationService.HasNetplaySessionStarted();
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(LevelupScreen.Banish))]
        public static bool Banish_Prefix()
        {
            return !synchronizationService.HasNetplaySessionStarted();
        }

    }
}
