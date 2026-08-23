#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private static string? s_lastNearClipSnapshotKey;
        private static string? s_lastCameraSnapshotKey;
        private static int s_lastFovRecoveryFrame = -1;

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

            var playerList = GameDirector.instance?.PlayerList;
            if (playerList == null || playerList.Count == 0)
            {
                return true;
            }

            if (IsDeadPlayersSpectateEnabledNow())
            {
                TraceCameraActivation(__instance, "PlayerSwitchPrefix");
                return HandleDeadPlayersSpectateSwitch(__instance, playerList, _next);
            }

            return HandleVanillaEquivalentPlayerSwitch(__instance, playerList, _next);
        }

        private static bool HandleDeadPlayersSpectateSwitch(SpectateCamera spectate, IList<PlayerAvatar> playerList, bool next)
        {
            TraceCameraActivation(spectate, "HandleDeadPlayersSpectateSwitch");

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

            if (!allDisabled && !IsLocalPlayerDeadOrDisabled())
            {
                return true;
            }

            TryPlayerSwitch(spectate, playerList, next, includeDisabled: true);
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
                MaintainDhhFovState(__instance);
                return;
            }

            if (s_stateNormalOriginalSpectatePoint != null)
            {
                s_stateNormalPatchedPlayer.spectatePoint = s_stateNormalOriginalSpectatePoint;
            }

            s_stateNormalPatchedPlayer = null;
            s_stateNormalOriginalSpectatePoint = null;
            HandleLastChanceStateNormalPostfix(__instance);
            MaintainDhhFovState(__instance);
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
                return false;

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

                if (!includeDisabled && playerOverride != null && candidate != playerOverride)
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

            TraceCameraActivation(__instance, "LastChanceStateNormalPostfix");

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

        private static void TraceCameraActivation(SpectateCamera? spectate, string reason)
        {
            if (spectate == null)
            {
                return;
            }

            var snapshot = CaptureCameraSnapshot(spectate, reason);
            MaintainDhhFovState(spectate);
            StartDhhFovRecovery(spectate, snapshot);

            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            if (!LogCameraSnapshot("activation", snapshot))
            {
                return;
            }

            spectate.StartCoroutine(LogCameraFollowUp(spectate, snapshot));
            spectate.StartCoroutine(LogCameraDelayedFollowUp(spectate, snapshot));
        }

        private static void StartDhhFovRecovery(SpectateCamera spectate, CameraSnapshot snapshot)
        {
            if (spectate == null || !ShouldApplyDhhFovRecovery())
            {
                return;
            }

            if (Time.frameCount == s_lastFovRecoveryFrame)
            {
                return;
            }

            s_lastFovRecoveryFrame = Time.frameCount;
            spectate.StartCoroutine(RunDhhFovRecovery(spectate, snapshot.Reason));
        }

        private static void MaintainDhhFovState(SpectateCamera? spectate)
        {
            if (spectate == null || !IsDeadPlayersSpectateEnabledNow() || !ShouldApplyDhhFovRecovery())
            {
                return;
            }

            var targetFov = FeatureFlags.DHHSpectateDefaultFov;
            if (!NeedsDhhFovRestore(spectate, targetFov))
            {
                return;
            }

            ApplyDhhFovState(spectate, targetFov);
        }

        [HarmonyPatch(typeof(SpectateCamera), "LateUpdate")]
        [HarmonyPostfix]
        private static void LateUpdatePostfix(SpectateCamera __instance)
        {
            MaintainDhhFovState(__instance);
        }

        private static IEnumerator RunDhhFovRecovery(SpectateCamera spectate, string reason)
        {
            yield return new WaitForSecondsRealtime(3f);

            if (spectate == null || !IsDeadPlayersSpectateEnabledNow() || !ShouldApplyDhhFovRecovery())
            {
                yield break;
            }

            var targetFov = FeatureFlags.DHHSpectateDefaultFov;
            TryRestoreDhhFov(spectate, targetFov, reason);
        }

        private static bool NeedsDhhFovRestore(SpectateCamera spectate, float targetFov)
        {
            if (targetFov <= 0f)
            {
                return false;
            }

            var mainCamera = Camera.main;
            var cameraZoom = CameraZoom.Instance;
            var currentFov = mainCamera != null ? mainCamera.fieldOfView : (float?)null;
            var zoomDefault = cameraZoom != null ? (float?)cameraZoom.playerZoomDefault : null;
            var zoomCurrent = cameraZoom != null ? (float?)cameraZoom.zoomCurrent : null;
            var zoomNew = cameraZoom != null ? (float?)cameraZoom.zoomNew : null;

            if (!currentFov.HasValue || currentFov.Value <= 0.01f || Mathf.Abs(currentFov.Value - targetFov) > 0.01f)
            {
                return true;
            }

            if (!zoomDefault.HasValue || Mathf.Abs(zoomDefault.Value - targetFov) > 0.01f)
            {
                return true;
            }

            if (!zoomCurrent.HasValue || Mathf.Abs(zoomCurrent.Value - targetFov) > 0.01f)
            {
                return true;
            }

            if (!zoomNew.HasValue || Mathf.Abs(zoomNew.Value - targetFov) > 0.01f)
            {
                return true;
            }

            return Mathf.Abs(spectate.cameraFieldOfView - targetFov) > 0.01f;
        }

        private static bool TryRestoreDhhFov(SpectateCamera spectate, float targetFov, string reason)
        {
            if (targetFov <= 0f)
            {
                return false;
            }

            var mainCamera = Camera.main;
            var spectateCamera = spectate.MainCamera;
            var cameraZoom = CameraZoom.Instance;
            var currentFov = mainCamera != null ? mainCamera.fieldOfView : (float?)null;
            var zoomDefault = cameraZoom != null ? (float?)cameraZoom.playerZoomDefault : null;
            var zoomCurrent = cameraZoom != null ? (float?)cameraZoom.zoomCurrent : null;
            var zoomNew = cameraZoom != null ? (float?)cameraZoom.zoomNew : null;
            var cameraFieldOfView = spectate.cameraFieldOfView;
            var needsRestore = !currentFov.HasValue || currentFov.Value <= 0.01f || Mathf.Abs(currentFov.Value - targetFov) > 0.01f;
            needsRestore |= !zoomDefault.HasValue || Mathf.Abs(zoomDefault.Value - targetFov) > 0.01f;
            needsRestore |= !zoomCurrent.HasValue || Mathf.Abs(zoomCurrent.Value - targetFov) > 0.01f;
            needsRestore |= !zoomNew.HasValue || Mathf.Abs(zoomNew.Value - targetFov) > 0.01f;
            needsRestore |= Mathf.Abs(cameraFieldOfView - targetFov) > 0.01f;

            if (!needsRestore)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.DhhFov.Check3", 30))
                {
                    Debug.Log(
                        "[SpectateDeadPlayers][DhhFov] " +
                        $"phase={reason} t=3s current={(currentFov.HasValue ? currentFov.Value.ToString("0.###") : "n/a")} target={targetFov:0.###} restored=False " +
                        $"default={(zoomDefault.HasValue ? zoomDefault.Value.ToString("0.###") : "n/a")} " +
                        $"currentZoom={(zoomCurrent.HasValue ? zoomCurrent.Value.ToString("0.###") : "n/a")} " +
                        $"newZoom={(zoomNew.HasValue ? zoomNew.Value.ToString("0.###") : "n/a")} " +
                        $"cameraFieldOfView={(cameraFieldOfView.ToString("0.###"))}");
                }

                return false;
            }

            ApplyDhhFovState(spectate, targetFov);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Spectate.DeadPlayers.DhhFov.Restore3", 30))
            {
                Debug.Log(
                    "[SpectateDeadPlayers][DhhFov] " +
                    $"phase={reason} t=3s current={(currentFov.HasValue ? currentFov.Value.ToString("0.###") : "n/a")} target={targetFov:0.###} restored=True " +
                    $"default={(cameraZoom != null ? cameraZoom.playerZoomDefault.ToString("0.###") : "n/a")} " +
                    $"currentZoom={(cameraZoom != null ? cameraZoom.zoomCurrent.ToString("0.###") : "n/a")} " +
                    $"newZoom={(cameraZoom != null ? cameraZoom.zoomNew.ToString("0.###") : "n/a")} " +
                    $"cameraFieldOfView={(spectate.cameraFieldOfView.ToString("0.###"))} " +
                    $"mainCam={(mainCamera != null ? mainCamera.name : "null")} spectateCam={(spectateCamera != null ? spectateCamera.name : "null")}");
            }

            return true;
        }

        private static void ApplyDhhFovState(SpectateCamera spectate, float targetFov)
        {
            if (spectate == null || targetFov <= 0f)
            {
                return;
            }

            var mainCamera = Camera.main;
            var spectateCamera = spectate.MainCamera;
            var topCamera = spectate.TopCamera;
            var cameraZoom = CameraZoom.Instance;

            if (cameraZoom != null)
            {
                cameraZoom.playerZoomDefault = targetFov;
                cameraZoom.zoomPrev = targetFov;
                cameraZoom.zoomCurrent = targetFov;
                cameraZoom.zoomNew = targetFov;

                if (cameraZoom.OverrideActive || cameraZoom.OverrideZoomTimer > 0f)
                {
                    cameraZoom.OverrideZoomSet(targetFov, 0.1f, 3f, 3f, spectate.gameObject, 150);
                }
            }

            if (mainCamera != null)
            {
                mainCamera.fieldOfView = targetFov;
            }

            if (spectateCamera != null)
            {
                spectateCamera.fieldOfView = targetFov;
            }

            if (topCamera != null)
            {
                topCamera.fieldOfView = targetFov;
            }

            spectate.cameraFieldOfView = targetFov;
        }

        private static bool ShouldApplyDhhFovRecovery()
        {
            return FeatureFlags.DHHSpectateDefaultFov > 0f;
        }

        private static IEnumerator LogCameraFollowUp(SpectateCamera spectate, CameraSnapshot baseline)
        {
            yield return null;

            if (spectate == null || !FeatureFlags.DebugLogging)
            {
                yield break;
            }

            var current = CaptureCameraSnapshot(spectate, baseline.Reason);
            if (!current.HasMeaningfulDelta(baseline))
            {
                yield break;
            }

            LogCameraDelta(baseline, current);
        }

        private static IEnumerator LogCameraDelayedFollowUp(SpectateCamera spectate, CameraSnapshot baseline)
        {
            yield return new WaitForSecondsRealtime(3f);

            if (spectate == null || !FeatureFlags.DebugLogging)
            {
                yield break;
            }

            var current = CaptureCameraSnapshot(spectate, baseline.Reason);
            if (!current.HasMeaningfulDelta(baseline))
            {
                yield break;
            }

            if (!LogCameraSnapshot("delayed", current))
            {
                yield break;
            }

            LogCameraDelta(baseline, current);
        }

        private static bool LogCameraSnapshot(string phase, CameraSnapshot snapshot)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return false;
            }

            var key = $"Spectate.DeadPlayers.Camera.{phase}";
            var snapshotKey =
                $"{snapshot.Reason}|{snapshot.LastChanceEnabled}|{snapshot.LastChanceActive}|{snapshot.AllPlayersDisabled}|{snapshot.CurrentPlayerDisabled}|{snapshot.LocalPlayerDisabled}|{snapshot.LocalPlayerDeadSet}|{snapshot.CurrentState}|{snapshot.PlayerName}|{snapshot.OverrideName}|{snapshot.MainCameraName}|{snapshot.SpectateCameraName}|{snapshot.MainNearClip}|{snapshot.SpectateNearClip}|{snapshot.MainFov}|{snapshot.SpectateFov}|{snapshot.MainRectKey}|{snapshot.MainCullingMask}|{snapshot.MainClearFlags}|{snapshot.MainParentName}";
            if (snapshotKey == s_lastCameraSnapshotKey)
            {
                return false;
            }

            s_lastCameraSnapshotKey = snapshotKey;

            if (!LogLimiter.ShouldLog(key, 30))
            {
                return false;
            }

            Debug.Log(
                "[SpectateDeadPlayers][Camera] " +
                $"phase={phase} " +
                $"reason={snapshot.Reason} " +
                $"lastChanceEnabled={snapshot.LastChanceEnabled} " +
                $"lastChanceActive={snapshot.LastChanceActive} " +
                $"allPlayersDisabled={snapshot.AllPlayersDisabled} " +
                $"currentPlayerDisabled={snapshot.CurrentPlayerDisabled} " +
                $"localPlayerDisabled={snapshot.LocalPlayerDisabled} " +
                $"localPlayerDeadSet={snapshot.LocalPlayerDeadSet} " +
                $"state={snapshot.CurrentState} " +
                $"player={snapshot.PlayerName} " +
                $"override={snapshot.OverrideName} " +
                $"mainCam={(snapshot.MainCameraName ?? "null")} " +
                $"mainNear={(snapshot.MainNearClip.HasValue ? snapshot.MainNearClip.Value.ToString("0.###") : "n/a")} " +
                $"mainFov={(snapshot.MainFov.HasValue ? snapshot.MainFov.Value.ToString("0.###") : "n/a")} " +
                $"mainRect={(snapshot.MainRectKey ?? "n/a")} " +
                $"mainMask={snapshot.MainCullingMask} " +
                $"mainClear={snapshot.MainClearFlags} " +
                $"mainParent={(snapshot.MainParentName ?? "null")} " +
                $"spectateCam={(snapshot.SpectateCameraName ?? "null")} " +
                $"spectateNear={(snapshot.SpectateNearClip.HasValue ? snapshot.SpectateNearClip.Value.ToString("0.###") : "n/a")} " +
                $"spectateFov={(snapshot.SpectateFov.HasValue ? snapshot.SpectateFov.Value.ToString("0.###") : "n/a")} " +
                $"fogStart={(snapshot.FogStartDistance.HasValue ? snapshot.FogStartDistance.Value.ToString("0.###") : "n/a")} " +
                $"mainMinusSpectate={(snapshot.MainMinusSpectateNearClip.HasValue ? snapshot.MainMinusSpectateNearClip.Value.ToString("0.###") : "n/a")}");
            return true;
        }

        private static void LogCameraDelta(CameraSnapshot baseline, CameraSnapshot current)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            if (!LogLimiter.ShouldLog("Spectate.DeadPlayers.Camera.Delta", 30))
            {
                return;
            }

            Debug.Log(
                "[SpectateDeadPlayers][CameraDelta] " +
                $"reason={baseline.Reason} " +
                $"mainNear={FormatFloat(baseline.MainNearClip)}->{FormatFloat(current.MainNearClip)} delta={FormatDelta(baseline.MainNearClip, current.MainNearClip)} " +
                $"spectateNear={FormatFloat(baseline.SpectateNearClip)}->{FormatFloat(current.SpectateNearClip)} delta={FormatDelta(baseline.SpectateNearClip, current.SpectateNearClip)} " +
                $"mainFov={FormatFloat(baseline.MainFov)}->{FormatFloat(current.MainFov)} delta={FormatDelta(baseline.MainFov, current.MainFov)} " +
                $"spectateFov={FormatFloat(baseline.SpectateFov)}->{FormatFloat(current.SpectateFov)} delta={FormatDelta(baseline.SpectateFov, current.SpectateFov)} " +
                $"fogStart={FormatFloat(baseline.FogStartDistance)}->{FormatFloat(current.FogStartDistance)} delta={FormatDelta(baseline.FogStartDistance, current.FogStartDistance)} " +
                $"mainMinusSpectate={FormatFloat(baseline.MainMinusSpectateNearClip)}->{FormatFloat(current.MainMinusSpectateNearClip)} delta={FormatDelta(baseline.MainMinusSpectateNearClip, current.MainMinusSpectateNearClip)} " +
                $"mainCam={(baseline.MainCameraName ?? "null")}->{(current.MainCameraName ?? "null")} " +
                $"mainRect={(baseline.MainRectKey ?? "n/a")}->{(current.MainRectKey ?? "n/a")} " +
                $"mainMask={baseline.MainCullingMask}->{current.MainCullingMask} " +
                $"mainClear={baseline.MainClearFlags}->{current.MainClearFlags} " +
                $"mainParent={(baseline.MainParentName ?? "null")}->{(current.MainParentName ?? "null")} " +
                $"spectateCam={(baseline.SpectateCameraName ?? "null")}->{(current.SpectateCameraName ?? "null")}");
        }

        private static CameraSnapshot CaptureCameraSnapshot(SpectateCamera spectate, string reason)
        {
            var mainCamera = Camera.main;
            var spectateCamera = spectate.MainCamera;

            var mainNear = mainCamera != null ? mainCamera.nearClipPlane : (float?)null;
            var spectateNear = spectateCamera != null ? spectateCamera.nearClipPlane : (float?)null;
            var mainRect = mainCamera != null ? mainCamera.rect : (Rect?)null;
            var mainParent = mainCamera != null ? mainCamera.transform.parent : null;

            return new CameraSnapshot
            {
                Reason = reason,
                LastChanceEnabled = LastChanceInteropBridge.IsLastChanceModeEnabled(),
                LastChanceActive = LastChanceInteropBridge.IsLastChanceActive(),
                AllPlayersDisabled = LastChanceInteropBridge.AllPlayersDisabled(),
                CurrentState = spectate.currentState.ToString(),
                PlayerName = GetPlayerName(spectate.player),
                OverrideName = GetPlayerName(spectate.playerOverride),
                CurrentPlayerDisabled = spectate.player != null && spectate.player.isDisabled,
                LocalPlayerDisabled = PlayerAvatar.instance != null && PlayerAvatar.instance.isDisabled,
                LocalPlayerDeadSet = PlayerAvatar.instance != null && PlayerAvatar.instance.deadSet,
                MainCameraName = mainCamera != null ? mainCamera.name : null,
                SpectateCameraName = spectateCamera != null ? spectateCamera.name : null,
                MainNearClip = mainNear,
                SpectateNearClip = spectateNear,
                MainFov = mainCamera != null ? mainCamera.fieldOfView : (float?)null,
                SpectateFov = spectateCamera != null ? spectateCamera.fieldOfView : (float?)null,
                MainRect = mainRect,
                MainRectKey = mainRect.HasValue ? FormatRect(mainRect.Value) : null,
                MainCullingMask = mainCamera != null ? mainCamera.cullingMask : 0,
                MainClearFlags = mainCamera != null ? mainCamera.clearFlags.ToString() : "n/a",
                MainParentName = mainParent != null ? mainParent.name : null,
                FogStartDistance = RenderSettings.fogStartDistance,
                MainMinusSpectateNearClip = mainNear.HasValue && spectateNear.HasValue
                    ? mainNear.Value - spectateNear.Value
                    : (float?)null
            };
        }

        private static string FormatRect(Rect rect)
        {
            return $"({rect.x:0.###},{rect.y:0.###},{rect.width:0.###},{rect.height:0.###})";
        }

        private sealed class CameraSnapshot
        {
            public string Reason = string.Empty;
            public bool LastChanceEnabled;
            public bool LastChanceActive;
            public bool AllPlayersDisabled;
            public string CurrentState = string.Empty;
            public string PlayerName = string.Empty;
            public string OverrideName = string.Empty;
            public bool CurrentPlayerDisabled;
            public bool LocalPlayerDisabled;
            public bool LocalPlayerDeadSet;
            public string? MainCameraName;
            public string? SpectateCameraName;
            public float? MainNearClip;
            public float? SpectateNearClip;
            public float? MainFov;
            public float? SpectateFov;
            public Rect? MainRect;
            public string? MainRectKey;
            public int MainCullingMask;
            public string MainClearFlags = string.Empty;
            public string? MainParentName;
            public float? FogStartDistance;
            public float? MainMinusSpectateNearClip;

            public bool HasMeaningfulDelta(CameraSnapshot other)
            {
                return !NullableFloatEquals(MainNearClip, other.MainNearClip)
                    || !NullableFloatEquals(SpectateNearClip, other.SpectateNearClip)
                    || !NullableFloatEquals(MainFov, other.MainFov)
                    || !NullableRectEquals(MainRect, other.MainRect)
                    || MainCullingMask != other.MainCullingMask
                    || !string.Equals(MainClearFlags, other.MainClearFlags, StringComparison.Ordinal)
                    || !string.Equals(MainParentName, other.MainParentName, StringComparison.Ordinal)
                    || !NullableFloatEquals(FogStartDistance, other.FogStartDistance)
                    || !NullableFloatEquals(MainMinusSpectateNearClip, other.MainMinusSpectateNearClip)
                    || !string.Equals(MainCameraName, other.MainCameraName, StringComparison.Ordinal)
                    || !string.Equals(SpectateCameraName, other.SpectateCameraName, StringComparison.Ordinal);
            }

            private static bool NullableFloatEquals(float? left, float? right)
            {
                if (!left.HasValue && !right.HasValue)
                {
                    return true;
                }

                if (!left.HasValue || !right.HasValue)
                {
                    return false;
                }

                return Mathf.Abs(left.Value - right.Value) < 0.0001f;
            }

            private static bool NullableRectEquals(Rect? left, Rect? right)
            {
                if (!left.HasValue && !right.HasValue)
                {
                    return true;
                }

                if (!left.HasValue || !right.HasValue)
                {
                    return false;
                }

                var l = left.Value;
                var r = right.Value;
                return Mathf.Abs(l.x - r.x) < 0.0001f
                    && Mathf.Abs(l.y - r.y) < 0.0001f
                    && Mathf.Abs(l.width - r.width) < 0.0001f
                    && Mathf.Abs(l.height - r.height) < 0.0001f;
            }
        }

        private static void TraceNearClipActivation(SpectateCamera? spectate, string reason)
        {
            if (!FeatureFlags.DebugLogging || spectate == null)
            {
                return;
            }

            var snapshot = CaptureNearClipSnapshot(spectate, reason);
            if (!LogNearClipSnapshot("activation", snapshot))
            {
                return;
            }

            spectate.StartCoroutine(LogNearClipFollowUp(spectate, snapshot));
            spectate.StartCoroutine(LogNearClipDelayedFollowUp(spectate, snapshot));
        }

        private static IEnumerator LogNearClipFollowUp(SpectateCamera spectate, NearClipSnapshot baseline)
        {
            yield return null;

            if (spectate == null || !FeatureFlags.DebugLogging)
            {
                yield break;
            }

            var current = CaptureNearClipSnapshot(spectate, baseline.Reason);
            if (!current.HasMeaningfulDelta(baseline))
            {
                yield break;
            }

            LogNearClipDelta(baseline, current);
        }

        private static IEnumerator LogNearClipDelayedFollowUp(SpectateCamera spectate, NearClipSnapshot baseline)
        {
            yield return new WaitForSecondsRealtime(3f);

            if (spectate == null || !FeatureFlags.DebugLogging)
            {
                yield break;
            }

            var current = CaptureNearClipSnapshot(spectate, baseline.Reason);
            if (!current.HasMeaningfulDelta(baseline))
            {
                yield break;
            }

            if (!LogNearClipSnapshot("delayed", current))
            {
                yield break;
            }

            LogNearClipDelta(baseline, current);
        }

        private static bool LogNearClipSnapshot(string phase, NearClipSnapshot snapshot)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return false;
            }

            var key = $"Spectate.DeadPlayers.NearClip.{phase}";
            var snapshotKey =
                $"{snapshot.Reason}|{snapshot.LastChanceEnabled}|{snapshot.LastChanceActive}|{snapshot.AllPlayersDisabled}|{snapshot.CurrentPlayerDisabled}|{snapshot.LocalPlayerDisabled}|{snapshot.LocalPlayerDeadSet}|{snapshot.CurrentState}|{snapshot.PlayerName}|{snapshot.OverrideName}|{snapshot.MainCameraName}|{snapshot.SpectateCameraName}|{snapshot.MainNearClip}|{snapshot.SpectateNearClip}|{snapshot.FogStartDistance}|{snapshot.MainMinusSpectateNearClip}";
            if (snapshotKey == s_lastNearClipSnapshotKey)
            {
                return false;
            }

            s_lastNearClipSnapshotKey = snapshotKey;

            if (!LogLimiter.ShouldLog(key, 30))
            {
                return false;
            }

            Debug.Log(
                "[SpectateDeadPlayers][NearClip] " +
                $"phase={phase} " +
                $"reason={snapshot.Reason} " +
                $"lastChanceEnabled={snapshot.LastChanceEnabled} " +
                $"lastChanceActive={snapshot.LastChanceActive} " +
                $"allPlayersDisabled={snapshot.AllPlayersDisabled} " +
                $"currentPlayerDisabled={snapshot.CurrentPlayerDisabled} " +
                $"localPlayerDisabled={snapshot.LocalPlayerDisabled} " +
                $"localPlayerDeadSet={snapshot.LocalPlayerDeadSet} " +
                $"state={snapshot.CurrentState} " +
                $"player={snapshot.PlayerName} " +
                $"override={snapshot.OverrideName} " +
                $"mainCam={(snapshot.MainCameraName ?? "null")} " +
                $"mainNear={(snapshot.MainNearClip.HasValue ? snapshot.MainNearClip.Value.ToString("0.###") : "n/a")} " +
                $"spectateCam={(snapshot.SpectateCameraName ?? "null")} " +
                $"spectateNear={(snapshot.SpectateNearClip.HasValue ? snapshot.SpectateNearClip.Value.ToString("0.###") : "n/a")} " +
                $"fogStart={(snapshot.FogStartDistance.HasValue ? snapshot.FogStartDistance.Value.ToString("0.###") : "n/a")} " +
                $"mainMinusSpectate={(snapshot.MainMinusSpectateNearClip.HasValue ? snapshot.MainMinusSpectateNearClip.Value.ToString("0.###") : "n/a")}");
            return true;
        }

        private static void LogNearClipDelta(NearClipSnapshot baseline, NearClipSnapshot current)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            if (!LogLimiter.ShouldLog("Spectate.DeadPlayers.NearClip.Delta", 30))
            {
                return;
            }

            Debug.Log(
                "[SpectateDeadPlayers][NearClipDelta] " +
                $"reason={baseline.Reason} " +
                $"mainNear={FormatFloat(baseline.MainNearClip)}->{FormatFloat(current.MainNearClip)} delta={FormatDelta(baseline.MainNearClip, current.MainNearClip)} " +
                $"spectateNear={FormatFloat(baseline.SpectateNearClip)}->{FormatFloat(current.SpectateNearClip)} delta={FormatDelta(baseline.SpectateNearClip, current.SpectateNearClip)} " +
                $"fogStart={FormatFloat(baseline.FogStartDistance)}->{FormatFloat(current.FogStartDistance)} delta={FormatDelta(baseline.FogStartDistance, current.FogStartDistance)} " +
                $"mainMinusSpectate={FormatFloat(baseline.MainMinusSpectateNearClip)}->{FormatFloat(current.MainMinusSpectateNearClip)} delta={FormatDelta(baseline.MainMinusSpectateNearClip, current.MainMinusSpectateNearClip)} " +
                $"mainCam={(baseline.MainCameraName ?? "null")}->{(current.MainCameraName ?? "null")} " +
                $"spectateCam={(baseline.SpectateCameraName ?? "null")}->{(current.SpectateCameraName ?? "null")}");
        }

        private static NearClipSnapshot CaptureNearClipSnapshot(SpectateCamera spectate, string reason)
        {
            var mainCamera = Camera.main;
            var spectateCamera = spectate.MainCamera;

            var mainNear = mainCamera != null ? mainCamera.nearClipPlane : (float?)null;
            var spectateNear = spectateCamera != null ? spectateCamera.nearClipPlane : (float?)null;

            return new NearClipSnapshot
            {
                Reason = reason,
                LastChanceEnabled = LastChanceInteropBridge.IsLastChanceModeEnabled(),
                LastChanceActive = LastChanceInteropBridge.IsLastChanceActive(),
                AllPlayersDisabled = LastChanceInteropBridge.AllPlayersDisabled(),
                CurrentState = spectate.currentState.ToString(),
                PlayerName = GetPlayerName(spectate.player),
                OverrideName = GetPlayerName(spectate.playerOverride),
                CurrentPlayerDisabled = spectate.player != null && spectate.player.isDisabled,
                LocalPlayerDisabled = PlayerAvatar.instance != null && PlayerAvatar.instance.isDisabled,
                LocalPlayerDeadSet = PlayerAvatar.instance != null && PlayerAvatar.instance.deadSet,
                MainCameraName = mainCamera != null ? mainCamera.name : null,
                SpectateCameraName = spectateCamera != null ? spectateCamera.name : null,
                MainNearClip = mainNear,
                SpectateNearClip = spectateNear,
                FogStartDistance = RenderSettings.fogStartDistance,
                MainMinusSpectateNearClip = mainNear.HasValue && spectateNear.HasValue
                    ? mainNear.Value - spectateNear.Value
                    : (float?)null
            };
        }

        private static string FormatFloat(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "n/a";
        }

        private static string FormatDelta(float? before, float? after)
        {
            if (!before.HasValue || !after.HasValue)
            {
                return "n/a";
            }

            return (after.Value - before.Value).ToString("0.###");
        }

        private sealed class NearClipSnapshot
        {
            public string Reason = string.Empty;
            public bool LastChanceEnabled;
            public bool LastChanceActive;
            public bool AllPlayersDisabled;
            public string CurrentState = string.Empty;
            public string PlayerName = string.Empty;
            public string OverrideName = string.Empty;
            public bool CurrentPlayerDisabled;
            public bool LocalPlayerDisabled;
            public bool LocalPlayerDeadSet;
            public string? MainCameraName;
            public string? SpectateCameraName;
            public float? MainNearClip;
            public float? SpectateNearClip;
            public float? FogStartDistance;
            public float? MainMinusSpectateNearClip;

            public bool HasMeaningfulDelta(NearClipSnapshot other)
            {
                return !NullableFloatEquals(MainNearClip, other.MainNearClip)
                    || !NullableFloatEquals(SpectateNearClip, other.SpectateNearClip)
                    || !NullableFloatEquals(FogStartDistance, other.FogStartDistance)
                    || !NullableFloatEquals(MainMinusSpectateNearClip, other.MainMinusSpectateNearClip)
                    || !string.Equals(MainCameraName, other.MainCameraName, StringComparison.Ordinal)
                    || !string.Equals(SpectateCameraName, other.SpectateCameraName, StringComparison.Ordinal);
            }

            private static bool NullableFloatEquals(float? left, float? right)
            {
                if (!left.HasValue && !right.HasValue)
                {
                    return true;
                }

                if (!left.HasValue || !right.HasValue)
                {
                    return false;
                }

                return Mathf.Abs(left.Value - right.Value) < 0.0001f;
            }
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

