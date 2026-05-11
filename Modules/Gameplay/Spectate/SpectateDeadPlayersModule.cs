#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private static PlayerAvatar? s_stateNormalPatchedPlayer;
        private static Transform? s_stateNormalOriginalSpectatePoint;
        private static Transform? s_stateNormalOrbitProxy;

        [HarmonyPrefix]
        private static bool PlayerSwitchPrefix(SpectateCamera __instance, bool _next)
        {
            if (ShouldBlockJumpDrivenPlayerSwitch(_next))
            {
                return false;
            }

            if (ShouldBlockPlayerSwitchForLastChance())
            {
                return false;
            }

            if (__instance == null)
            {
                return true;
            }

            var playerList = GameDirector.instance?.PlayerList;
            if (playerList == null || playerList.Count == 0)
            {
                return true;
            }

            if (IsDeadPlayersSpectateEnabledNow())
            {
                return HandleDeadPlayersSpectateSwitch(__instance, playerList, _next);
            }

            return HandleVanillaEquivalentPlayerSwitch(__instance, playerList, _next);
        }

        private static bool HandleDeadPlayersSpectateSwitch(SpectateCamera spectate, IList<PlayerAvatar> playerList, bool next)
        {
            var allDisabled = true;
            foreach (var p in playerList)
            {
                if (p == null)
                {
                    continue;
                }

                if (!p.isDisabled)
                {
                    allDisabled = false;
                    break;
                }
            }

            if (!allDisabled)
            {
                return true;
            }

            var handled = TryPlayerSwitch(spectate, playerList, next, includeDisabled: true);
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.AllDisabledSwitch", 120))
            {
                Debug.Log($"[SpectateDeadPlayers] Prefix all-disabled path handled={handled} count={playerList.Count} next={next}");
            }

            // If everyone is disabled and feature is enabled, never execute vanilla early-return path.
            return false;
        }

        private static bool HandleVanillaEquivalentPlayerSwitch(SpectateCamera spectate, IList<PlayerAvatar> playerList, bool next)
        {
            if (playerList.All(p => p == null || p.isDisabled))
            {
                return false;
            }

            if (TryPlayerSwitch(spectate, playerList, next, includeDisabled: false))
            {
                return false;
            }

            return true;
        }

        private static bool ShouldBlockJumpDrivenPlayerSwitch(bool next)
        {
            return next
                && SemiFunc.InputDown(InputKey.Jump)
                && !SemiFunc.InputDown(InputKey.SpectateNext);
        }

        [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
        [HarmonyPrefix]
        private static void StateNormalPrefix(SpectateCamera __instance)
        {
            if (__instance == null || !IsDeadPlayersSpectateEnabledNow())
            {
                return;
            }

            var currentPlayer = __instance.player;
            if (currentPlayer == null)
            {
                return;
            }

            if (ReferenceEquals(currentPlayer, PlayerAvatar.instance))
            {
                return;
            }

            if (!currentPlayer.isDisabled)
            {
                return;
            }

            if (!TryGetDeathHeadAnchor(currentPlayer, out var anchor))
            {
                return;
            }

            // Replace the source spectate point for this frame with a proxy on the target DeathHead.
            // This keeps vanilla/DHH camera math intact (distance, smoothing, collisions, etc.).
            var original = currentPlayer.spectatePoint;
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
                currentPlayer.spectatePoint = proxy;
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
            if (s_stateNormalPatchedPlayer == null)
            {
                HandleLastChanceStateNormalPostfix(__instance);
                return;
            }

            if (s_stateNormalOriginalSpectatePoint != null)
            {
                s_stateNormalPatchedPlayer.spectatePoint = s_stateNormalOriginalSpectatePoint;
            }

            s_stateNormalPatchedPlayer = null;
            s_stateNormalOriginalSpectatePoint = null;
            HandleLastChanceStateNormalPostfix(__instance);
        }

        [HarmonyPatch(typeof(SpectateCamera), "UpdateState")]
        [HarmonyPrefix]
        private static bool UpdateStatePrefix(SpectateCamera __instance, SpectateCamera.State _state)
        {
            if (!LastChanceInteropBridge.IsLastChanceModeEnabled() || __instance == null)
            {
                return true;
            }

            if (_state != SpectateCamera.State.Head)
            {
                return true;
            }

            // During LastChance keep vanilla Head state disabled, even if disabled flags flicker.
            if (LastChanceInteropBridge.IsLastChanceActive())
            {
                return false;
            }

            // Fallback: if all players are disabled outside active timer setup, keep old behavior.
            return !LastChanceInteropBridge.AllPlayersDisabled();
        }

        private static bool IsDeadPlayersSpectateEnabledNow()
        {
            if (!LastChanceInteropBridge.IsSpectateDeadPlayersEnabled())
            {
                return false;
            }

            var mode = LastChanceInteropBridge.GetSpectateDeadPlayersMode().Trim();
            if (mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (mode.Equals("LastChanceOnly", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceInteropBridge.IsLastChanceModeEnabled() &&
                       LastChanceInteropBridge.IsLastChanceActive() &&
                       IsLocalPlayerDeadOrDisabled();
            }

            return true;
        }

        private static bool TryPlayerSwitch(SpectateCamera spectate, IList<PlayerAvatar> players, bool next, bool includeDisabled)
        {
            if (players.Count == 0)
            {
                return false;
            }

            var currentPlayer = spectate.player;
            var playerOverride = spectate.playerOverride;
            var normalPivot = spectate.normalTransformPivot;
            var normalDistance = spectate.normalTransformDistance;
            if (normalPivot == null || normalDistance == null)
            {
                return false;
            }

            var idx = spectate.currentPlayerListIndex;
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
                if (currentPlayer == candidate || candidate.spectatePoint == null || (!includeDisabled && candidate.isDisabled))
                {
                    continue;
                }

                spectate.playerOverride = null;
                spectate.currentPlayerListIndex = idx;
                spectate.player = candidate;

                normalPivot.position = candidate.spectatePoint.position;
                var aimHorizontal = candidate.transform.eulerAngles.y;
                spectate.normalAimHorizontal = aimHorizontal;
                spectate.normalAimVertical = 0f;
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
                spectate.CameraTeleportImpulse();
                spectate.normalMaxDistance = 3f;
                PlayerController.instance?.playerAvatarScript?.localCamera?.Teleported();
                return true;
            }

            spectate.playerOverride = null;
            return false;
        }

        private static string GetPlayerName(PlayerAvatar? player)
        {
            if (player == null)
            {
                return "unknown";
            }

            return player.playerName ?? "unknown";
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
            var physGrabObject = deathHead.physGrabObject;
            if (physGrabObject != null)
            {
                anchor = physGrabObject.centerPoint;
            }

            return true;
        }

        private static bool IsLocalPlayerDeadOrDisabled()
        {
            var local = PlayerAvatar.instance;
            if (local == null)
            {
                return false;
            }

            if (local.isDisabled)
            {
                return true;
            }

            if (local.deadSet)
            {
                return true;
            }

            return false;
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
            if (!LastChanceInteropBridge.IsLastChanceModeEnabled())
            {
                return;
            }

            if (!LastChanceInteropBridge.IsLastChanceActive())
            {
                LastChanceInteropBridge.ResetSpectateForceState();
                return;
            }

            if (!LastChanceInteropBridge.AllPlayersDisabled())
            {
                LastChanceInteropBridge.ResetSpectateForceState();
                return;
            }

            if (LastChanceInteropBridge.ShouldForceLocalDeathHeadSpectate())
            {
                if (__instance != null)
                {
                    LastChanceInteropBridge.EnsureSpectatePlayerLocal(__instance);
                }
                LastChanceInteropBridge.ForceDeathHeadSpectateIfPossible();
            }

            LastChanceInteropBridge.DebugLogState(__instance);
        }

        private static bool ShouldBlockPlayerSwitchForLastChance()
        {
            if (!LastChanceInteropBridge.IsLastChanceModeEnabled())
            {
                return false;
            }

            if (LastChanceInteropBridge.IsManualSwitchInputDown())
            {
                return false;
            }

            if (!LastChanceInteropBridge.IsLastChanceActive())
            {
                return false;
            }

            if (!LastChanceInteropBridge.AllPlayersDisabled())
            {
                return false;
            }

            return true;
        }
    }
}

