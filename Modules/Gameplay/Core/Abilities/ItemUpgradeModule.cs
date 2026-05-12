#nullable enable

using System;
using System.Reflection;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class ItemUpgradeModule
    {
        internal static void Apply(Harmony harmony)
        {
            PatchItemToggleUpgradeHook(harmony);
        }

        private static void PatchItemToggleUpgradeHook(Harmony harmony)
        {
            if (harmony == null)
                return;

            var method = AccessTools.Method(typeof(ItemToggle), nameof(ItemToggle.ToggleItemLogic), new[] { typeof(bool), typeof(int) });
            if (method == null)
                return;

            var postfix = typeof(ItemUpgradeModule).GetMethod(nameof(ItemToggle_ToggleItemLogic_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
        }

        private static void ItemToggle_ToggleItemLogic_Postfix(ItemToggle __instance, bool toggle, int player)
        {
            if (!toggle || __instance == null)
                return;

            DhhUpgradeOrchestrator.TryHandleToggle(__instance, player);
        }
    }
}

