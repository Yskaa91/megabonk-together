using HarmonyLib;
using MegabonkTogether.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MegabonkTogether.Patches
{
    [HarmonyPatch(typeof(UpgradePicker))]
    internal static class UpgradePickerPatches
    {
        private static readonly ISynchronizationService synchronizationService = Plugin.Services.GetService<ISynchronizationService>();

        /// <summary>
        /// The game is not paused during netplay, so pressing the space bar to jump would
        /// also pick a reward. Ignore keyboard input in reward windows; mouse clicks still
        /// go through UpgradeButton.OnClick.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(UpgradePicker.KeyboardInput))]
        public static bool KeyboardInput_Prefix()
        {
            return !synchronizationService.HasNetplaySessionStarted();
        }
    }
}
