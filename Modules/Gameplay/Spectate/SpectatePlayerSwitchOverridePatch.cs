#nullable enable

using DeathHeadHopper.Helpers;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    [HarmonyPatch(typeof(SpectateCamera), "PlayerSwitch")]
    internal static class SpectatePlayerSwitchOverridePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SpectateCamera __instance, bool _next)
        {
            if (__instance == null || !DHHFunc.LocalDeathHeadActive())
            {
                return true;
            }

            if (!FeatureFlags.AllowSpectatingDeathHeads)
            {
                // Vanilla is sufficient while a living target exists. When all
                // alternatives are dead, stop the host-only DHH fallback as well.
                return HasLivingAlternative(__instance) ? true : false;
            }

            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0 ||
                __instance.normalTransformPivot == null || __instance.normalTransformDistance == null)
            {
                return false;
            }

            var current = __instance.player;
            var index = __instance.currentPlayerListIndex;
            var count = players.Count;

            for (var i = 0; i < count; i++)
            {
                index = _next ? (index + 1) % count : (index - 1 + count) % count;
                var candidate = players[index];
                if (!IsEligibleCandidate(candidate, current))
                {
                    continue;
                }

                __instance.playerOverride = null;
                __instance.currentPlayerListIndex = index;
                __instance.player = candidate;
                __instance.normalTransformPivot.position = candidate.spectatePoint!.position;
                __instance.normalAimHorizontal = candidate.transform.eulerAngles.y;
                __instance.normalAimVertical = 0f;
                __instance.normalTransformPivot.rotation = Quaternion.Euler(
                    __instance.normalAimVertical,
                    __instance.normalAimHorizontal,
                    0f);
                __instance.normalTransformPivot.localRotation = Quaternion.Euler(
                    __instance.normalTransformPivot.localRotation.eulerAngles.x,
                    __instance.normalTransformPivot.localRotation.eulerAngles.y,
                    0f);
                __instance.normalTransformDistance.localPosition = new Vector3(0f, 0f, -2f);
                __instance.transform.position = __instance.normalTransformDistance.position;
                __instance.transform.rotation = __instance.normalTransformDistance.rotation;

                if (SemiFunc.IsMultiplayer())
                {
                    SemiFunc.HUDSpectateSetName(candidate.playerName);
                }

                SemiFunc.LightManagerSetCullTargetTransform(candidate.transform);
                __instance.CameraTeleportImpulse();
                __instance.normalMaxDistance = 3f;
                PlayerController.instance?.playerAvatarScript?.localCamera?.Teleported();
                return false;
            }

            // Do not fall back to the DHH/vanilla implementation when no local
            // candidate exists; that path is not consistent between host and clients.
            __instance.playerOverride = null;
            return false;
        }

        private static bool IsEligibleCandidate(PlayerAvatar? candidate, PlayerAvatar? current)
        {
            if (candidate == null || candidate == current || candidate.spectatePoint == null)
            {
                return false;
            }

            if (FeatureFlags.AllowSpectatingDeathHeads)
            {
                return true;
            }

            // The local DeathHead must remain reachable so the player can always
            // return to their own spectate target after viewing living players.
            if (candidate == PlayerAvatar.instance)
            {
                return true;
            }

            return !candidate.isDisabled && !candidate.deadSet;
        }

        private static bool HasLivingAlternative(SpectateCamera spectate)
        {
            var players = GameDirector.instance?.PlayerList;
            if (players == null)
            {
                return false;
            }

            foreach (var player in players)
            {
                if (player != null && player != spectate.player &&
                    !player.isDisabled && !player.deadSet && player.spectatePoint != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
