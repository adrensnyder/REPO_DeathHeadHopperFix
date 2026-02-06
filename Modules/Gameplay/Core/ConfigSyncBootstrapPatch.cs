#nullable enable

using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    [HarmonyPatch(typeof(GameDirector), "Awake")]
    internal static class ConfigSyncBootstrapPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!FeatureFlags.LastChangeMode)
            {
                // Only bootstrap when gameplay mods are relevant.
                // This avoids touching Photon while in menus/lobby flow.
                return;
            }

            ConfigSyncManager.EnsureCreated();
        }
    }
}
