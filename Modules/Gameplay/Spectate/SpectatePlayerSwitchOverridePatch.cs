#nullable enable

using System.Collections.Generic;
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

            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0 ||
                __instance.normalTransformPivot == null || __instance.normalTransformDistance == null)
            {
                return false;
            }

            var current = __instance.player;
            var index = FindCurrentPlayerIndex(players, current, __instance.currentPlayerListIndex);
            var count = players.Count;

            for (var i = 0; i < count; i++)
            {
                index = _next ? (index + 1) % count : (index - 1 + count) % count;
                var candidate = players[index];
                if (!IsEligibleCandidate(candidate, current))
                {
                    continue;
                }

                // A stale local/remote avatar reference can make the list entry
                // look different even though it represents the current target.
                // Stop before changing any camera state in that case.
                if (IsSamePlayer(candidate, current))
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

        private static int FindCurrentPlayerIndex(IList<PlayerAvatar> players, PlayerAvatar? current, int fallback)
        {
            if (current != null)
            {
                for (var i = 0; i < players.Count; i++)
                {
                    if (IsSamePlayer(players[i], current))
                    {
                        return i;
                    }
                }
            }

            return fallback >= 0 && fallback < players.Count ? fallback : 0;
        }

        private static bool IsEligibleCandidate(PlayerAvatar? candidate, PlayerAvatar? current)
        {
            if (candidate == null || candidate.spectatePoint == null)
            {
                return false;
            }

            // The local DeathHead must remain reachable so the player can always
            // return to their own spectate target after viewing living players.
            if (candidate == PlayerAvatar.instance ||
                (candidate.photonView != null && candidate.photonView.IsMine))
            {
                return true;
            }

            return FeatureFlags.AllowSpectatingDeathHeads ||
                   (!candidate.isDisabled && !candidate.deadSet);
        }

        private static bool IsSamePlayer(PlayerAvatar? first, PlayerAvatar? second)
        {
            if (first == null || second == null)
                return false;

            if (ReferenceEquals(first, second))
                return true;

            var firstView = first.photonView;
            var secondView = second.photonView;
            return firstView != null && secondView != null &&
                   firstView.ViewID != 0 && firstView.ViewID == secondView.ViewID;
        }
    }
}
