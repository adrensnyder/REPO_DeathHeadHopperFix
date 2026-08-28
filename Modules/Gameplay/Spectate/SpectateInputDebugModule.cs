#nullable enable

using DeathHeadHopper.Helpers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    [HarmonyPatch(typeof(SpectateCamera), nameof(SpectateCamera.StateHead))]
    internal static class SpectateHeadInputDebugPatch
    {
        [HarmonyPrefix]
        private static void Prefix(SpectateCamera __instance)
        {
            if (!ShouldLog(__instance))
                return;

            var next = SemiFunc.InputDown(InputKey.SpectateNext);
            var previous = SemiFunc.InputDown(InputKey.SpectatePrevious);
            if (!next && !previous)
                return;

            var target = __instance.player;
            Debug.Log(
                $"[Fix:SpectateDebug] StateHead received spectate input " +
                $"next={next}, previous={previous}, player={Describe(target)}, " +
                $"state={__instance.currentState}. No vanilla PlayerSwitch call is expected from StateHead.");
        }

        private static bool ShouldLog(SpectateCamera? spectate)
        {
            return FeatureFlags.DebugLogging &&
                   spectate != null &&
                   spectate.currentState == SpectateCamera.State.Head &&
                   DHHFunc.LocalDeathHeadActive();
        }

        private static string Describe(PlayerAvatar? avatar)
        {
            if (avatar == null)
                return "null";

            return $"{avatar.name}(disabled={avatar.isDisabled},dead={avatar.deadSet})";
        }
    }

    [HarmonyPatch(typeof(SpectateCamera), "PlayerSwitch")]
    internal static class SpectatePlayerSwitchDebugPatch
    {
        [HarmonyPrefix]
        private static void Prefix(SpectateCamera __instance, bool _next)
        {
            if (!ShouldLog(__instance))
                return;

            Debug.Log(
                $"[Fix:SpectateDebug] PlayerSwitch entered next={_next}, " +
                $"state={__instance.currentState}, player={Describe(__instance.player)}");
        }

        [HarmonyPostfix]
        private static void Postfix(SpectateCamera __instance, bool _next)
        {
            if (!ShouldLog(__instance))
                return;

            Debug.Log(
                $"[Fix:SpectateDebug] PlayerSwitch exited next={_next}, " +
                $"state={__instance.currentState}, player={Describe(__instance.player)}");
        }

        private static bool ShouldLog(SpectateCamera? spectate)
        {
            return FeatureFlags.DebugLogging &&
                   spectate != null &&
                   DHHFunc.LocalDeathHeadActive();
        }

        private static string Describe(PlayerAvatar? avatar)
        {
            if (avatar == null)
                return "null";

            return $"{avatar.name}(disabled={avatar.isDisabled},dead={avatar.deadSet})";
        }
    }
}
