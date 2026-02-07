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
            ConfigSyncManager.EnsureCreated();
        }
    }
}
