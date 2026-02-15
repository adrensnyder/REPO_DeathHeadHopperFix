#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    [HarmonyPatch(typeof(SpectateCamera), "PlayerSwitch")]
    internal static class SpectateDeadPlayersModule
    {
        private const string ModuleId = "DeathHeadHopperFix.Spectate.DeadPlayers";

        private static readonly FieldInfo? s_playerIsDisabledField =
            AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly MethodInfo? s_shouldSkipMethod =
            AccessTools.Method(typeof(SpectateDeadPlayersModule), nameof(ShouldSkipSpectateTarget),
                new[] { typeof(PlayerAvatar) });
        private static readonly FieldInfo? s_playerNameField =
            AccessTools.Field(typeof(PlayerAvatar), "playerName");
        private static readonly FieldInfo? s_playerDeathHeadPhysGrabObjectField =
            AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
        private static readonly FieldInfo? s_physGrabObjectCenterPointField =
            AccessTools.Field(typeof(PhysGrabObject), "centerPoint");
        private static readonly FieldInfo? s_playerAvatarSpectatePointField =
            AccessTools.Field(typeof(PlayerAvatar), "spectatePoint");
        private static readonly FieldInfo? s_spectateCurrentPlayerListIndexField =
            AccessTools.Field(typeof(SpectateCamera), "currentPlayerListIndex");
        private static readonly FieldInfo? s_spectatePlayerField =
            AccessTools.Field(typeof(SpectateCamera), "player");
        private static readonly FieldInfo? s_spectatePlayerOverrideField =
            AccessTools.Field(typeof(SpectateCamera), "playerOverride");
        private static readonly FieldInfo? s_spectateNormalTransformPivotField =
            AccessTools.Field(typeof(SpectateCamera), "normalTransformPivot");
        private static readonly FieldInfo? s_spectateNormalTransformDistanceField =
            AccessTools.Field(typeof(SpectateCamera), "normalTransformDistance");
        private static readonly FieldInfo? s_spectateNormalPreviousPositionField =
            AccessTools.Field(typeof(SpectateCamera), "normalPreviousPosition");
        private static readonly FieldInfo? s_spectateNormalAimHorizontalField =
            AccessTools.Field(typeof(SpectateCamera), "normalAimHorizontal");
        private static readonly FieldInfo? s_spectateNormalAimVerticalField =
            AccessTools.Field(typeof(SpectateCamera), "normalAimVertical");
        private static readonly FieldInfo? s_spectateNormalMaxDistanceField =
            AccessTools.Field(typeof(SpectateCamera), "normalMaxDistance");
        private static readonly MethodInfo? s_cameraTeleportImpulseMethod =
            AccessTools.Method(typeof(SpectateCamera), "CameraTeleportImpulse");
        private static PlayerAvatar? s_stateNormalPatchedPlayer;
        private static Transform? s_stateNormalOriginalSpectatePoint;
        private static Transform? s_stateNormalOrbitProxy;

        [HarmonyPrefix]
        private static bool PlayerSwitchPrefix(SpectateCamera __instance, bool _next)
        {
            if (ShouldBlockPlayerSwitchForLastChance())
            {
                return false;
            }

            if (__instance == null)
            {
                return true;
            }

            if (!IsDeadPlayersSpectateEnabledNow())
            {
                return true;
            }

            var playerList = GameDirector.instance?.PlayerList;
            if (playerList == null || playerList.Count == 0 || s_playerIsDisabledField == null)
            {
                return true;
            }

            var allDisabled = true;
            foreach (var p in playerList)
            {
                if (p == null)
                {
                    continue;
                }

                if (s_playerIsDisabledField.GetValue(p) is bool disabled && !disabled)
                {
                    allDisabled = false;
                    break;
                }
            }

            if (!allDisabled)
            {
                return true;
            }

            var handled = TryPlayerSwitchIncludingDisabled(__instance, playerList, _next);
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.AllDisabledSwitch", 120))
            {
                Debug.Log($"[SpectateDeadPlayers] Prefix all-disabled path handled={handled} count={playerList.Count} next={_next}");
            }

            // If everyone is disabled and feature is enabled, never execute vanilla early-return path.
            return false;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> PlayerSwitchTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (s_playerIsDisabledField == null || s_shouldSkipMethod == null)
            {
                return instructions;
            }

            var codes = instructions.ToList();
            var replaced = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo field && field == s_playerIsDisabledField)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, s_shouldSkipMethod);
                    replaced++;
                }
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.Transpiler", 600))
            {
                Debug.Log($"[SpectateDeadPlayers] PlayerSwitch transpiler replacements={replaced}");
            }

            return codes;
        }

        [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
        [HarmonyPrefix]
        private static void StateNormalPrefix(SpectateCamera __instance)
        {
            if (__instance == null || !IsDeadPlayersSpectateEnabledNow() || s_playerIsDisabledField == null)
            {
                return;
            }

            var currentPlayer = s_spectatePlayerField?.GetValue(__instance) as PlayerAvatar;
            if (currentPlayer == null)
            {
                return;
            }

            if (ReferenceEquals(currentPlayer, PlayerAvatar.instance))
            {
                return;
            }

            if (s_playerIsDisabledField.GetValue(currentPlayer) is not bool disabled || !disabled)
            {
                return;
            }

            if (!TryGetDeathHeadAnchor(currentPlayer, out var anchor))
            {
                return;
            }

            // Replace the source spectate point for this frame with a proxy on the target DeathHead.
            // This keeps vanilla/DHH camera math intact (distance, smoothing, collisions, etc.).
            if (s_playerAvatarSpectatePointField != null)
            {
                var original = s_playerAvatarSpectatePointField.GetValue(currentPlayer) as Transform;
                if (original != null)
                {
                    var proxy = EnsureStateNormalOrbitProxy();
                    if (proxy == null)
                    {
                        return;
                    }

                    // Keep vanilla spectate framing by preserving the original spectatePoint
                    // offset relative to the player transform, but move the anchor to the head.
                    var offset = Vector3.zero;
                    if (currentPlayer.transform != null)
                    {
                        offset = original.position - currentPlayer.transform.position;
                    }

                    proxy.position = anchor + offset;
                    proxy.rotation = original.rotation;

                    s_stateNormalPatchedPlayer = currentPlayer;
                    s_stateNormalOriginalSpectatePoint = original;
                    s_playerAvatarSpectatePointField.SetValue(currentPlayer, proxy);
                }
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.Anchor", 120))
            {
                Debug.Log($"[SpectateDeadPlayers] Orbit source moved to DeathHead center for {GetPlayerName(currentPlayer)}");
            }
        }

        [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
        [HarmonyPostfix]
        private static void StateNormalPostfix(SpectateCamera __instance)
        {
            if (s_playerAvatarSpectatePointField == null || s_stateNormalPatchedPlayer == null)
            {
                HandleLastChanceStateNormalPostfix(__instance);
                return;
            }

            if (s_stateNormalOriginalSpectatePoint != null)
            {
                s_playerAvatarSpectatePointField.SetValue(s_stateNormalPatchedPlayer, s_stateNormalOriginalSpectatePoint);
            }

            s_stateNormalPatchedPlayer = null;
            s_stateNormalOriginalSpectatePoint = null;
            HandleLastChanceStateNormalPostfix(__instance);
        }

        [HarmonyPatch(typeof(SpectateCamera), "UpdateState")]
        [HarmonyPrefix]
        private static bool UpdateStatePrefix(SpectateCamera __instance, SpectateCamera.State _state)
        {
            if (!FeatureFlags.LastChangeMode || __instance == null)
            {
                return true;
            }

            if (_state != SpectateCamera.State.Head)
            {
                return true;
            }

            // During LastChance keep vanilla Head state disabled, even if disabled flags flicker.
            if (LastChanceTimerController.IsActive)
            {
                return false;
            }

            // Fallback: if all players are disabled outside active timer setup, keep old behavior.
            return !LastChanceSpectateHelper.AllPlayersDisabled();
        }

        private static bool ShouldSkipSpectateTarget(PlayerAvatar player)
        {
            if (player == null)
            {
                return true;
            }

            if (IsDeadPlayersSpectateEnabledNow())
            {
                return false;
            }

            if (s_playerIsDisabledField == null)
            {
                return true;
            }

            var skip = s_playerIsDisabledField.GetValue(player) is bool disabled && disabled;
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.ShouldSkip", 300))
            {
                var targetName = GetPlayerName(player);
                Debug.Log($"[SpectateDeadPlayers] ShouldSkip target={targetName} skip={skip}");
            }
            return skip;
        }

        private static bool IsDeadPlayersSpectateEnabledNow()
        {
            return FeatureFlags.SpectateDeadPlayers;
        }

        private static bool TryPlayerSwitchIncludingDisabled(SpectateCamera spectate, IList<PlayerAvatar> players, bool next)
        {
            if (players.Count == 0 ||
                s_spectateCurrentPlayerListIndexField == null ||
                s_spectatePlayerField == null ||
                s_spectatePlayerOverrideField == null ||
                s_spectateNormalTransformPivotField == null ||
                s_spectateNormalTransformDistanceField == null ||
                s_spectateNormalAimHorizontalField == null ||
                s_spectateNormalAimVerticalField == null ||
                s_spectateNormalMaxDistanceField == null)
            {
                return false;
            }

            var currentPlayer = s_spectatePlayerField.GetValue(spectate) as PlayerAvatar;
            var playerOverride = s_spectatePlayerOverrideField.GetValue(spectate) as PlayerAvatar;
            var normalPivot = s_spectateNormalTransformPivotField.GetValue(spectate) as Transform;
            var normalDistance = s_spectateNormalTransformDistanceField.GetValue(spectate) as Transform;
            if (normalPivot == null || normalDistance == null)
            {
                return false;
            }

            var idxObj = s_spectateCurrentPlayerListIndexField.GetValue(spectate);
            var idx = idxObj is int n ? n : 0;
            var count = players.Count;

            for (var i = 0; i < count; i++)
            {
                idx = next ? (idx + 1) % count : (idx - 1 + count) % count;
                var candidate = players[idx];
                if (candidate == null)
                {
                    continue;
                }

                if (playerOverride != null && candidate != playerOverride)
                {
                    continue;
                }

                playerOverride = null;
                if (currentPlayer == candidate || candidate.spectatePoint == null)
                {
                    continue;
                }

                s_spectatePlayerOverrideField.SetValue(spectate, null);
                s_spectateCurrentPlayerListIndexField.SetValue(spectate, idx);
                s_spectatePlayerField.SetValue(spectate, candidate);

                normalPivot.position = candidate.spectatePoint.position;
                var aimHorizontal = candidate.transform.eulerAngles.y;
                s_spectateNormalAimHorizontalField.SetValue(spectate, aimHorizontal);
                s_spectateNormalAimVerticalField.SetValue(spectate, 0f);
                normalPivot.rotation = Quaternion.Euler(0f, aimHorizontal, 0f);
                normalPivot.localRotation = Quaternion.Euler(normalPivot.localRotation.eulerAngles.x, normalPivot.localRotation.eulerAngles.y, 0f);
                normalDistance.localPosition = new Vector3(0f, 0f, -2f);
                spectate.transform.position = normalDistance.position;
                spectate.transform.rotation = normalDistance.rotation;

                if (SemiFunc.IsMultiplayer())
                {
                    SemiFunc.HUDSpectateSetName(GetPlayerName(candidate));
                }

                SemiFunc.LightManagerSetCullTargetTransform(candidate.transform);
                s_cameraTeleportImpulseMethod?.Invoke(spectate, null);
                s_spectateNormalMaxDistanceField.SetValue(spectate, 3f);
                PlayerController.instance?.playerAvatarScript?.localCamera?.Teleported();
                return true;
            }

            s_spectatePlayerOverrideField.SetValue(spectate, null);
            return false;
        }

        private static string GetPlayerName(PlayerAvatar? player)
        {
            if (player == null || s_playerNameField == null)
            {
                return "unknown";
            }

            return s_playerNameField.GetValue(player) as string ?? "unknown";
        }

        private static bool TryGetDeathHeadAnchor(PlayerAvatar player, out Vector3 anchor)
        {
            anchor = default;
            var deathHead = player.playerDeathHead;
            if (deathHead == null)
            {
                return false;
            }

            anchor = deathHead.transform.position;
            var physGrabObject = s_playerDeathHeadPhysGrabObjectField?.GetValue(deathHead) as PhysGrabObject;
            if (physGrabObject != null)
            {
                var centerObj = s_physGrabObjectCenterPointField?.GetValue(physGrabObject);
                if (centerObj is Vector3 center)
                {
                    anchor = center;
                }
            }

            return true;
        }

        private static Transform? EnsureStateNormalOrbitProxy()
        {
            if (s_stateNormalOrbitProxy != null)
            {
                return s_stateNormalOrbitProxy;
            }

            var go = GameObject.Find("DHHFix.SpectateDeadPlayers.OrbitProxy");
            if (go == null)
            {
                go = new GameObject("DHHFix.SpectateDeadPlayers.OrbitProxy");
                UnityEngine.Object.DontDestroyOnLoad(go);
            }

            s_stateNormalOrbitProxy = go.transform;
            return s_stateNormalOrbitProxy;
        }

        private static void HandleLastChanceStateNormalPostfix(SpectateCamera __instance)
        {
            if (!FeatureFlags.LastChangeMode)
            {
                return;
            }

            if (!LastChanceTimerController.IsActive)
            {
                LastChanceSpectateHelper.ResetForceState();
                return;
            }

            if (!LastChanceSpectateHelper.AllPlayersDisabled())
            {
                LastChanceSpectateHelper.ResetForceState();
                return;
            }

            if (LastChanceSpectateHelper.ShouldForceLocalDeathHeadSpectate())
            {
                if (__instance != null)
                {
                    LastChanceSpectateHelper.EnsureSpectatePlayerLocal(__instance);
                }
                LastChanceSpectateHelper.ForceDeathHeadSpectateIfPossible();
            }

            LastChanceSpectateHelper.DebugLogState(__instance);
        }

        private static bool ShouldBlockPlayerSwitchForLastChance()
        {
            if (!FeatureFlags.LastChangeMode)
            {
                return false;
            }

            if (LastChanceSpectateHelper.IsManualSwitchInputDown())
            {
                return false;
            }

            if (!LastChanceTimerController.IsActive)
            {
                return false;
            }

            if (!LastChanceSpectateHelper.AllPlayersDisabled())
            {
                return false;
            }

            return true;
        }
    }
}

