#nullable enable

using DeathHeadHopper.Helpers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    [HarmonyPatch(typeof(SpectateCamera), nameof(SpectateCamera.LateUpdate))]
    internal static class SpectateRawInputDebugPatch
    {
        private static SpectateCamera? s_pendingCamera;
        private static PlayerAvatar? s_pendingPlayer;
        private static bool s_pendingNext;
        private static bool s_pendingPrevious;
        private static int s_pendingFrame = -1;

        [HarmonyPrefix]
        private static void Prefix(SpectateCamera __instance)
        {
            ClearPendingInput();

            if (__instance == null || !DHHFunc.LocalDeathHeadActive())
                return;

            var next = SemiFunc.InputDown(InputKey.SpectateNext);
            var previous = SemiFunc.InputDown(InputKey.SpectatePrevious);
            if (!next && !previous)
                return;

            s_pendingCamera = __instance;
            s_pendingPlayer = __instance.player;
            s_pendingNext = next;
            s_pendingPrevious = previous;
            s_pendingFrame = Time.frameCount;

            if (FeatureFlags.DebugLogging)
            {
                Debug.Log(
                    $"[Fix:SpectateDebug] Raw spectate input detected " +
                    $"next={next}, previous={previous}, state={__instance.currentState}, " +
                    $"player={Describe(__instance.player)}");
            }
        }

        private static string Describe(PlayerAvatar? avatar)
        {
            if (avatar == null)
                return "null";

            var viewId = avatar.photonView != null ? avatar.photonView.ViewID.ToString() : "local";
            return $"{avatar.name}(id={avatar.GetInstanceID()},view={viewId},disabled={avatar.isDisabled},dead={avatar.deadSet})";
        }

        [HarmonyPostfix]
        private static void Postfix(SpectateCamera __instance)
        {
            if (__instance == null || s_pendingCamera != __instance || s_pendingFrame != Time.frameCount)
                return;

            var originalPlayer = s_pendingPlayer;
            var next = s_pendingNext;
            var previous = s_pendingPrevious;
            ClearPendingInput();

            if (__instance.currentState != SpectateCamera.State.Normal ||
                MenuManager.instance?.currentMenuPage != null ||
                !ReferenceEquals(__instance.player, originalPlayer))
            {
                return;
            }

            // StateNormal can run before this LateUpdate and miss the spectate
            // edge. Retry it once after vanilla has finished its camera update.
            if (next)
                __instance.PlayerSwitch(true);
            else if (previous)
                __instance.PlayerSwitch(false);

            if (FeatureFlags.DebugLogging)
            {
                Debug.Log(
                    $"[Fix:SpectateDebug] Deferred PlayerSwitch executed next={next}, " +
                    $"previous={previous}, player={Describe(__instance.player)}");
            }
        }

        private static void ClearPendingInput()
        {
            s_pendingCamera = null;
            s_pendingPlayer = null;
            s_pendingNext = false;
            s_pendingPrevious = false;
            s_pendingFrame = -1;
        }
    }

    [HarmonyPatch(typeof(SpectateCamera), nameof(SpectateCamera.StateHead))]
    internal static class SpectateHeadInputDebugPatch
    {
        [HarmonyPrefix]
        private static void Prefix(SpectateCamera __instance)
        {
            if (__instance == null || !ShouldLog(__instance))
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

            var viewId = avatar.photonView != null ? avatar.photonView.ViewID.ToString() : "local";
            return $"{avatar.name}(id={avatar.GetInstanceID()},view={viewId},disabled={avatar.isDisabled},dead={avatar.deadSet})";
        }
    }

    [HarmonyPatch(typeof(SpectateCamera), "PlayerSwitch")]
    internal static class SpectatePlayerSwitchDebugPatch
    {
        private const int DetailedCallsPerWindow = 3;
        private const int LogWindowFrames = 600;
        private static SpectateCamera? s_logWindowCamera;
        private static int s_logWindowStartFrame = -1;
        private static int s_detailedCallsInWindow;
        private static int s_suppressedCallsInWindow;

        private struct PlayerSwitchLogState
        {
            internal PlayerAvatar? PreviousPlayer;
            internal bool EmitDetails;
        }

        [HarmonyPrefix]
        private static void Prefix(SpectateCamera __instance, bool _next, out PlayerSwitchLogState __state)
        {
            __state = new PlayerSwitchLogState
            {
                PreviousPlayer = __instance?.player,
                EmitDetails = false
            };
            if (__instance == null || !ShouldLog(__instance))
                return;
            if (!TryAcquireDetailedLogSlot(__instance))
                return;

            __state.EmitDetails = true;

            Debug.Log(
                $"[Fix:SpectateDebug] PlayerSwitch entered next={_next}, " +
                $"state={__instance.currentState}, player={Describe(__instance.player)}");
        }

        [HarmonyPostfix]
        private static void Postfix(SpectateCamera __instance, bool _next, PlayerSwitchLogState __state)
        {
            if (__instance == null || !__state.EmitDetails)
                return;

            var target = __instance.player;
            var activeAlternatives = CountActiveAlternatives(target);
            var changed = !ReferenceEquals(__state.PreviousPlayer, target);
            Debug.Log(
                $"[Fix:SpectateDebug] PlayerSwitch exited next={_next}, " +
                $"state={__instance.currentState}, player={Describe(target)}, " +
                $"activeAlternatives={activeAlternatives}, " +
                $"result={(changed ? "target changed successfully" : "target unchanged")}, " +
                $"reason={(changed ? "none" : GetUnchangedTargetReason(target, activeAlternatives))}");
        }

        private static bool TryAcquireDetailedLogSlot(SpectateCamera spectate)
        {
            var frame = Time.frameCount;
            var newWindow = !ReferenceEquals(s_logWindowCamera, spectate) ||
                            s_logWindowStartFrame < 0 ||
                            frame - s_logWindowStartFrame >= LogWindowFrames;
            if (newWindow)
            {
                if (s_suppressedCallsInWindow > 0)
                {
                    Debug.Log(
                        $"[Fix:SpectateDebug] PlayerSwitch diagnostics suppressed " +
                        $"calls={s_suppressedCallsInWindow} windowFrames={LogWindowFrames}.");
                }

                s_logWindowCamera = spectate;
                s_logWindowStartFrame = frame;
                s_detailedCallsInWindow = 0;
                s_suppressedCallsInWindow = 0;
            }

            if (s_detailedCallsInWindow >= DetailedCallsPerWindow)
            {
                s_suppressedCallsInWindow++;
                return false;
            }

            s_detailedCallsInWindow++;
            return true;
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

            var viewId = avatar.photonView != null ? avatar.photonView.ViewID.ToString() : "local";
            return $"{avatar.name}(id={avatar.GetInstanceID()},view={viewId},disabled={avatar.isDisabled},dead={avatar.deadSet})";
        }

        private static int CountActiveAlternatives(PlayerAvatar? current)
        {
            var players = GameDirector.instance?.PlayerList;
            if (players == null)
                return 0;

            var count = 0;
            foreach (var player in players)
            {
                if (player != null && !ReferenceEquals(player, current) && !player.isDisabled)
                    count++;
            }

            return count;
        }

        private static string GetUnchangedTargetReason(PlayerAvatar? current, int activeAlternatives)
        {
            if (activeAlternatives == 0)
                return "no active alternative players";

            if (current != null && current.isDisabled)
                return "active alternatives exist but current disabled target was retained";

            return "target unchanged despite active alternatives";
        }
    }
}
