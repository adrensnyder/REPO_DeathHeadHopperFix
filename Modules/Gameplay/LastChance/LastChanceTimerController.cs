#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.UI;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    [HarmonyPatch(typeof(RunManager), "Update")]
    internal static class RunManagerUpdateLastChanceTimerPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            LastChanceTimerController.Tick();
        }
    }

    internal static class LastChanceTimerController
    {
        private const string LogKey = "LastChance.Timer";
        private const string TimerSecondAudioFileName = "TimerSecond.mp3";
        private const string TimerWarningAudioPrimaryFileName = "TimeWarning.mp3";
        private const string TimerWarningAudioFallbackFileName = "TimerWarning.mp3";
        private static float s_timerRemaining;
        private static bool s_active;
        private static int s_baseCurrency;
        private static bool s_currencyCaptured;
        private static readonly Color TimerColor = new(1f, 0.85f, 0.1f, 1f);
        private static readonly Color FlashColor = new(1f, 0.2f, 0.2f, 1f);
        private const string SurrenderHintPrompt = "Hold Crouch to surrender";
        private const string SurrenderCountdownFormat = "Surrender in {0}s";
        private const string SurrenderedHintText = "Surrendered <3";
        private const string LocalSurrenderedHintText = "You surrendered <3";
        private const string IndicatorLogKey = "LastChance.Indicator";
        private const string IndicatorCooldownLogKey = "LastChance.Indicator.Cooldown";
        private static readonly Vector2 DirectionLineScrollSpeed = new(4f, 0f);
        private const float DirectionLineHeightOffset = 0.2f;
        private const float DirectionPathRefreshSeconds = 0.4f;
        private const float DirectionPathMovementThresholdSqr = 0.64f; // 0.8m
        private const float DirectionIndicatorHoldSeconds = 1f;

        private enum LastChanceIndicatorMode
        {
            None = 0,
            Direction = 1
        }

        private enum IndicatorKind
        {
            Direction = 1
        }

        private static readonly FieldInfo? RunManagerRunStartedField =
            AccessTools.Field(typeof(RunManager), "runStarted");
        private static readonly FieldInfo? RunManagerRestartingField =
            AccessTools.Field(typeof(RunManager), "restarting");
        private static readonly FieldInfo? RunManagerPreviousRunLevelField =
            AccessTools.Field(typeof(RunManager), "previousRunLevel");
        private static readonly FieldInfo? PlayerAvatarIsDisabledField =
            AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo? PlayerAvatarRoomVolumeCheckField =
            AccessTools.Field(typeof(PlayerAvatar), "RoomVolumeCheck");
        private static readonly FieldInfo? PlayerAvatarNameField =
            AccessTools.Field(typeof(PlayerAvatar), "playerName");
        private static readonly FieldInfo? SpectateCameraSpectatePlayerField =
            AccessTools.Field(typeof(SpectateCamera), "spectatePlayer");
        private static FieldInfo? s_roomVolumeCheckInTruckField;
        private static FieldInfo? s_deathHeadInTruckField;
        private static FieldInfo? s_deathHeadRoomVolumeCheckField;
        private static readonly FieldInfo? RoundDirectorAllExtractionField =
            AccessTools.Field(typeof(RoundDirector), "allExtractionPointsCompleted");
        private static float SurrenderHoldDuration => Mathf.Clamp(FeatureFlags.LastChanceSurrenderSeconds, 2f, 10f);
        private static readonly HashSet<int> LastChanceSurrenderedPlayers = new();
        private static float s_surrenderHoldTimer;
        private static bool s_localSurrendered;
        private static bool s_jumpDistanceLogged;
        private static float s_directionCooldownUntil;
        private static float s_directionActiveUntil;
        private static bool s_directionActive;
        private static float s_directionHoldTimer;
        private static bool s_indicatorNoneLoggedThisCycle;
        private static GameObject? s_indicatorDirectionObject;
        private static LineRenderer? s_indicatorDirectionLine;
        private static Material? s_indicatorDirectionMaterial;
        private static float s_indicatorNextPathRefreshAt;
        private static object? s_reusableNavMeshHitBoxed;
        private static object? s_reusableNavMeshPath;
        private static Vector3 s_lastDirectionPathFrom;
        private static Vector3 s_lastDirectionPathTo;
        private static bool s_hasLastDirectionPathSample;
        private static AudioSource? s_timerSecondAudioSource;
        private static AudioClip? s_timerSecondAudioClip;
        private static bool s_timerSecondAudioLoadAttempted;
        private static int s_lastTimerSecondAudioPlayed = -1;
        private static AudioSource? s_timerWarningAudioSource;
        private static AudioClip? s_timerWarningAudioClip;
        private static bool s_timerWarningAudioLoadAttempted;
        private static int s_lastTimerWarningAudioPlayed = -1;
        private static int s_lastNetworkTimerBroadcastSecond = -1;
        private static bool s_timerSyncedFromHost;
        private static DynamicTimerInputs s_cachedDynamicTimerInputs;
        private static bool s_hasCachedDynamicTimerInputs;

        private static readonly Type? s_levelGeneratorType = AccessTools.TypeByName("LevelGenerator");
        private static readonly FieldInfo? s_levelGeneratorInstanceField = s_levelGeneratorType == null ? null : AccessTools.Field(s_levelGeneratorType, "Instance");
        private static readonly FieldInfo? s_levelPathTruckField = s_levelGeneratorType == null ? null : AccessTools.Field(s_levelGeneratorType, "LevelPathTruck");
        private static readonly FieldInfo? s_levelPathPointsField = s_levelGeneratorType == null ? null : AccessTools.Field(s_levelGeneratorType, "LevelPathPoints");
        private static readonly Type? s_levelPointType = AccessTools.TypeByName("LevelPoint");
        private static readonly FieldInfo? s_levelPointTruckField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "Truck");
        private static readonly Type? s_navMeshType = AccessTools.TypeByName("UnityEngine.AI.NavMesh");
        private static readonly Type? s_navMeshHitType = AccessTools.TypeByName("UnityEngine.AI.NavMeshHit");
        private static readonly Type? s_navMeshPathType = AccessTools.TypeByName("UnityEngine.AI.NavMeshPath");
        private static readonly MethodInfo? s_navMeshSamplePositionMethod = s_navMeshType?.GetMethod(
            "SamplePosition",
            BindingFlags.Static | BindingFlags.Public,
            null,
            s_navMeshHitType == null ? null : new[] { typeof(Vector3), s_navMeshHitType.MakeByRefType(), typeof(float), typeof(int) },
            null);
        private static readonly PropertyInfo? s_navMeshHitPositionProperty = s_navMeshHitType?.GetProperty("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_navMeshHitPositionField = s_navMeshHitType?.GetField("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo? s_navMeshCalculatePathMethod = s_navMeshType?.GetMethod(
            "CalculatePath",
            BindingFlags.Static | BindingFlags.Public,
            null,
            s_navMeshPathType == null ? null : new[] { typeof(Vector3), typeof(Vector3), typeof(int), s_navMeshPathType },
            null);
        private static readonly PropertyInfo? s_navMeshPathCornersProperty = s_navMeshPathType?.GetProperty("corners", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_playerAvatarLastNavmeshPositionField = AccessTools.Field(typeof(PlayerAvatar), "LastNavmeshPosition");
        
        private readonly struct DynamicTimerInputs
        {
            internal DynamicTimerInputs(
                int requiredPlayers,
                int levelNumber,
                int aliveSearchMonsters,
                float totalDistanceMeters,
                int playersBelowTruckThreshold,
                float totalBelowTruckMeters,
                int totalShortestRoomPathSteps)
            {
                RequiredPlayers = requiredPlayers;
                LevelNumber = levelNumber;
                AliveSearchMonsters = aliveSearchMonsters;
                TotalDistanceMeters = totalDistanceMeters;
                PlayersBelowTruckThreshold = playersBelowTruckThreshold;
                TotalBelowTruckMeters = totalBelowTruckMeters;
                TotalShortestRoomPathSteps = totalShortestRoomPathSteps;
            }

            internal int RequiredPlayers { get; }
            internal int LevelNumber { get; }
            internal int AliveSearchMonsters { get; }
            internal float TotalDistanceMeters { get; }
            internal int PlayersBelowTruckThreshold { get; }
            internal float TotalBelowTruckMeters { get; }
            internal int TotalShortestRoomPathSteps { get; }
        }

        internal static bool IsActive => s_active;
        internal static bool IsDirectionIndicatorUiVisible =>
            s_active &&
            AllPlayersDeadGuard.AllPlayersDisabled() &&
            GetIndicatorMode() == LastChanceIndicatorMode.Direction;
        internal static float GetDirectionIndicatorPenaltySecondsPreview()
        {
            if (!IsDirectionIndicatorUiVisible)
            {
                return 0f;
            }

            var maxPlayers = GetRunPlayerCount();
            if (maxPlayers <= 0)
            {
                return 0f;
            }

            return CalculateIndicatorPenaltySeconds(maxPlayers);
        }

        internal static void OnLevelLoaded()
        {
            ClearSurrenderState();
            ClearCachedDynamicTimerInputs();

            if (!s_active)
            {
                LastChanceTimerUI.Hide();
                AllPlayersDeadGuard.ResetVanillaAllPlayersDead();
                LastChanceSaveDeleteState.ResetAutoDeleteBlock();
                LastChanceSpectateHelper.ResetForceState();
                return;
            }

            s_active = false;
            s_currencyCaptured = false;
            s_timerRemaining = 0f;
            s_timerSyncedFromHost = false;
            LastChanceTimerUI.Hide();
            AllPlayersDeadGuard.ResetVanillaAllPlayersDead();
            LastChanceSaveDeleteState.ResetAutoDeleteBlock();
            LastChanceSpectateHelper.ResetForceState();
        }

        internal static void Tick()
        {
            if (!FeatureFlags.LastChangeMode)
            {
                ResetState();
                return;
            }

            if (!IsValidRunContext())
            {
                ResetState();
                return;
            }

            var allDead = AllPlayersDeadGuard.AllPlayersDisabled();
            if (!allDead)
            {
                ResetState();
                return;
            }

            var maxPlayers = GetRunPlayerCount();
            if (maxPlayers <= 0)
            {
                ResetState();
                return;
            }

            if (!s_active)
            {
                StartTimer(maxPlayers);
            }

            UpdateTimer();
            UpdateSurrenderInput(allDead);
            UpdateIndicators(maxPlayers, allDead);
            UpdatePlayersStatusUi(maxPlayers);

            if (CheckSurrenderFailure(maxPlayers))
            {
                return;
            }

            DebugTruckState(allDead);

            if (AllHeadsInTruck())
            {
                HandleSuccess();
                return;
            }

            if (s_timerRemaining <= 0f)
            {
                if (SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClient() && !s_timerSyncedFromHost)
                {
                    return;
                }
                HandleTimeout();
            }
        }

        private static void StartTimer(int maxPlayers)
        {
            s_active = true;
            if (SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClient())
            {
                s_timerRemaining = Mathf.Max(30f, GetConfiguredSeconds());
                s_timerSyncedFromHost = false;
            }
            else
            {
                s_timerRemaining = GetInitialTimerSeconds(maxPlayers);
                s_timerSyncedFromHost = true;
            }
            s_lastTimerSecondAudioPlayed = -1;
            s_lastTimerWarningAudioPlayed = -1;
            s_lastNetworkTimerBroadcastSecond = -1;
            s_currencyCaptured = false;
            s_indicatorNoneLoggedThisCycle = false;
            CaptureBaseCurrency();
            LastChanceSurrenderNetwork.EnsureCreated();
            LastChanceTimerUI.Show(SurrenderHintPrompt);
            s_jumpDistanceLogged = false;
            BroadcastTimerStateIfHost(force: true);

            if (FeatureFlags.DebugLogging)
            {
                LastChanceTruckDistanceLogger.LogDistances();
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(LogKey, 30))
            {
                Debug.Log($"[LastChance] Timer started: {s_timerRemaining:F1}s");
            }
        }

        private static void UpdateTimer()
        {
            s_timerRemaining = Mathf.Max(0f, s_timerRemaining - Time.deltaTime);
            BroadcastTimerStateIfHost(force: false);
            TryPlayLastChanceTimerWarnings();
            TryPlayLastChanceTimerSecondTick();
            LastChanceTimerUI.UpdateText(FormatTimerText(s_timerRemaining));
        }

        private static void HandleTimeout()
        {
            FailLastChance("[LastChance] Timer expired; resuming vanilla all-dead flow.");
        }

        private static void HandleSuccess()
        {
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(LogKey, 30))
            {
                Debug.Log("[LastChance] All heads in truck; sending to shop.");
            }

            LastChanceTimerUI.Hide();
            s_active = false;
            s_timerSyncedFromHost = false;
            StopTimerSecondAudio();
            BroadcastTimerStateIfHost(force: true);

            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                return;
            }

            var runMgr = RunManager.instance;
            if (runMgr == null || IsRestarting(runMgr))
            {
                return;
            }

            CaptureBaseCurrency();
            var bonus = Mathf.Max(0, FeatureFlags.LastChanceConsolationMoney);
            var newCurrency = s_baseCurrency + bonus;
            SemiFunc.StatSetRunCurrency(newCurrency);

            if (RunManagerPreviousRunLevelField != null)
            {
                RunManagerPreviousRunLevelField.SetValue(runMgr, runMgr.levelCurrent);
            }

            runMgr.ChangeLevel(false, false, RunManager.ChangeLevelType.Shop);
        }

        private static void CaptureBaseCurrency()
        {
            if (s_currencyCaptured)
            {
                return;
            }

            s_baseCurrency = SemiFunc.StatGetRunCurrency();
            s_currencyCaptured = true;
        }

        private static void ResetState()
        {
            if (!HasRuntimeStateToReset())
            {
                return;
            }

            ClearSurrenderState();
            ClearCachedDynamicTimerInputs();

            if (!s_active)
            {
                return;
            }

            s_active = false;
            s_currencyCaptured = false;
            s_timerRemaining = 0f;
            s_timerSyncedFromHost = false;
            StopTimerSecondAudio();
            LastChanceTimerUI.Hide();
            AllPlayersDeadGuard.ResetVanillaAllPlayersDead();
            LastChanceSpectateHelper.ResetForceState();
            LastChanceSaveDeleteState.ResetAutoDeleteBlock();
            BroadcastTimerStateIfHost(force: true);
        }

        private static bool HasRuntimeStateToReset()
        {
            if (s_active)
            {
                return true;
            }

            if (s_currencyCaptured || s_timerRemaining > 0f)
            {
                return true;
            }

            if (LastChanceSurrenderedPlayers.Count > 0 || s_surrenderHoldTimer > 0f || s_localSurrendered)
            {
                return true;
            }

            if (s_directionActive || s_directionActiveUntil > 0f || s_directionCooldownUntil > 0f)
            {
                return true;
            }

            return false;
        }

        private static bool IsValidRunContext()
        {
            if (!RunManager.instance)
            {
                return false;
            }

            if (SemiFunc.RunIsArena() || SemiFunc.RunIsLobby() || SemiFunc.RunIsShop() || SemiFunc.RunIsLobbyMenu() || SemiFunc.RunIsTutorial())
            {
                return false;
            }

            if (GameDirector.instance == null)
            {
                return false;
            }

            var runMgr = RunManager.instance;
            if (runMgr == null)
            {
                return false;
            }

            return IsRunStarted(runMgr) && GameDirector.instance.currentState == GameDirector.gameState.Main;
        }

        private static bool AllHeadsInTruck()
        {
            var director = GameDirector.instance;
            if (director == null || director.PlayerList == null || director.PlayerList.Count == 0)
            {
                return false;
            }

            int totalPlayers = 0;
            int headsInTruck = 0;

            foreach (var player in director.PlayerList)
            {
                if (player == null)
                {
                    continue;
                }

                totalPlayers++;

                if (IsPlayerSurrendered(player))
                {
                    continue;
                }

                if (!IsPlayerDisabled(player))
                {
                    var roomVolumeCheck = GetRoomVolumeCheck(player);
                    if (roomVolumeCheck == null || !IsRoomVolumeInTruck(roomVolumeCheck))
                    {
                        return false;
                    }
                    continue;
                }

                var deathHead = player.playerDeathHead;
                if (deathHead == null)
                {
                    return false;
                }

                var inTruck = GetDeathHeadInTruckStatus(deathHead);
                if (inTruck.HasValue && inTruck.Value)
                {
                    headsInTruck++;
                }
            }

            var maxPlayers = totalPlayers;
            var required = GetLastChanceNeededPlayers(maxPlayers);
            if (required <= 0)
            {
                return false;
            }

            return headsInTruck >= required;
        }

        private static int GetLastChanceNeededPlayers(int maxPlayers)
        {
            if (maxPlayers <= 0)
            {
                return 0;
            }

            var allowedMissing = Math.Max(0, Math.Min(FeatureFlags.LastChanceMissingPlayers, Math.Max(0, maxPlayers - 1)));
            var required = maxPlayers - allowedMissing;
            return Math.Max(1, required);
        }

        private static int GetLastChanceCanSurrender(int maxPlayers)
        {
            if (maxPlayers <= 0)
            {
                return 0;
            }

            var needed = GetLastChanceNeededPlayers(maxPlayers);
            return Math.Max(0, maxPlayers - needed);
        }

        private static int GetRunPlayerCount()
        {
            var director = GameDirector.instance;
            if (director == null || director.PlayerList == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var player in director.PlayerList)
            {
                if (player != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool CheckSurrenderFailure(int maxPlayers)
        {
            if (maxPlayers <= 0)
            {
                return false;
            }

            var surrendered = LastChanceSurrenderedPlayers.Count;
            var allowedToSurrender = GetLastChanceCanSurrender(maxPlayers);
            if (surrendered <= allowedToSurrender)
            {
                return false;
            }

            FailLastChance($"[LastChance] Too many surrendered ({surrendered}) > allowed ({allowedToSurrender}); resuming vanilla all-dead flow.");
            return true;
        }

        private static void FailLastChance(string reason)
        {
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(LogKey, 30))
            {
                Debug.Log(reason);
            }

            LastChanceTimerUI.Hide();
            s_timerRemaining = 0f;
            s_active = false;
            s_timerSyncedFromHost = false;
            StopTimerSecondAudio();
            BroadcastTimerStateIfHost(force: true);

            LastChanceSaveDeleteState.AllowAutoDelete();
            AllPlayersDeadGuard.AllowVanillaAllPlayersDead();
            ClearIndicatorsState();

            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                return;
            }

            var runMgr = RunManager.instance;
            if (runMgr == null || IsRestarting(runMgr))
            {
                return;
            }

            runMgr.ChangeLevel(false, true, RunManager.ChangeLevelType.Normal);
        }

        internal static void ApplyNetworkTimerState(bool active, float secondsRemaining)
        {
            if (!SemiFunc.IsMultiplayer() || SemiFunc.IsMasterClient())
            {
                return;
            }

            s_active = active;
            s_timerSyncedFromHost = active;
            s_timerRemaining = Mathf.Max(0f, secondsRemaining);
            s_lastNetworkTimerBroadcastSecond = Mathf.CeilToInt(s_timerRemaining);

            if (active)
            {
                LastChanceTimerUI.Show(SurrenderHintPrompt);
                LastChanceTimerUI.UpdateText(FormatTimerText(s_timerRemaining));
                return;
            }

            LastChanceTimerUI.Hide();
            StopTimerSecondAudio();
        }

        private static void BroadcastTimerStateIfHost(bool force)
        {
            if (!SemiFunc.IsMultiplayer() || !SemiFunc.IsMasterClient())
            {
                return;
            }

            var wholeSeconds = Mathf.CeilToInt(s_timerRemaining);
            if (!force && wholeSeconds == s_lastNetworkTimerBroadcastSecond)
            {
                return;
            }

            s_lastNetworkTimerBroadcastSecond = wholeSeconds;
            LastChanceSurrenderNetwork.NotifyTimerState(s_active, s_timerRemaining);
        }

        private static void UpdateSurrenderInput(bool allDead)
        {
            if (!s_active || !allDead)
            {
                ResetLocalSurrenderAttempt();
                return;
            }

            if (s_localSurrendered)
            {
                LastChanceTimerUI.SetSurrenderHintText(SurrenderedHintText);
                return;
            }

            if (!SemiFunc.InputHold(InputKey.Crouch))
            {
                ResetLocalSurrenderAttempt();
                return;
            }

            if (s_surrenderHoldTimer <= 0f)
            {
                TryLogTruckDistancesForJump();
            }

            s_surrenderHoldTimer += Time.deltaTime;
            var remaining = SurrenderHoldDuration - s_surrenderHoldTimer;
            if (remaining > 0f)
            {
                var secs = Mathf.CeilToInt(remaining);
                LastChanceTimerUI.SetSurrenderHintText(string.Format(SurrenderCountdownFormat, secs));
                return;
            }

            HandleLocalSurrender();
        }

        private static void HandleLocalSurrender()
        {
            if (s_localSurrendered)
            {
                return;
            }

            s_localSurrendered = true;
            s_surrenderHoldTimer = SurrenderHoldDuration;
            LastChanceTimerUI.SetSurrenderHintText(LocalSurrenderedHintText);

            var actorNumber = GetLocalActorNumber();
            RegisterSurrenderedActor(actorNumber, true);
        }

        private static void ResetLocalSurrenderAttempt()
        {
            if (s_surrenderHoldTimer > 0f && !s_localSurrendered)
            {
                s_surrenderHoldTimer = 0f;
                LastChanceTimerUI.ResetSurrenderHint();
            }
            s_jumpDistanceLogged = false;
        }

        private static void TryLogTruckDistancesForJump()
        {
            if (!FeatureFlags.LastChangeMode || !FeatureFlags.DebugLogging || s_jumpDistanceLogged)
            {
                return;
            }

            LastChanceTruckDistanceLogger.LogDistances();
            s_jumpDistanceLogged = true;
        }

        private static int GetLocalActorNumber()
        {
            var localAvatar = PlayerAvatar.instance;
            if (localAvatar?.photonView != null)
            {
                var owner = localAvatar.photonView.Owner;
                if (owner != null)
                {
                    return owner.ActorNumber;
                }
            }

            return PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
        }

        private static int GetPlayerActorNumber(PlayerAvatar player)
        {
            if (player?.photonView != null)
            {
                var owner = player.photonView.Owner;
                if (owner != null)
                {
                    return owner.ActorNumber;
                }
            }

            return PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
        }

        private static bool IsPlayerSurrendered(PlayerAvatar player)
        {
            var actorNumber = GetPlayerActorNumber(player);
            return LastChanceSurrenderedPlayers.Contains(actorNumber);
        }

        private static void RegisterSurrenderedActor(int actorNumber, bool broadcast)
        {
            if (actorNumber < 0)
            {
                return;
            }

            if (!LastChanceSurrenderedPlayers.Add(actorNumber))
            {
                return;
            }

            if (broadcast)
            {
                LastChanceSurrenderNetwork.NotifyLocalSurrender(actorNumber);
            }
        }

        internal static void RegisterRemoteSurrender(int actorNumber)
        {
            RegisterSurrenderedActor(actorNumber, false);
        }

        private static void UpdatePlayersStatusUi(int maxPlayers)
        {
            if (!s_active)
            {
                return;
            }

            var snapshots = PlayerStateExtractionHelper.GetPlayersStateSnapshot();
            var required = GetLastChanceNeededPlayers(maxPlayers);
            LastChanceTimerUI.UpdatePlayerStates(snapshots, required);
        }

        internal static bool IsPlayerSurrenderedForData(PlayerAvatar? player)
        {
            return player != null && IsPlayerSurrendered(player);
        }

        private static void ClearSurrenderState()
        {
            LastChanceSurrenderedPlayers.Clear();
            s_surrenderHoldTimer = 0f;
            s_localSurrendered = false;
            StopTimerSecondAudio();
            LastChanceTimerUI.ResetSurrenderHint();
            ClearIndicatorsState();
        }

        private static void TryPlayLastChanceTimerSecondTick()
        {
            if (!s_active)
                return;

            var wholeSeconds = Mathf.CeilToInt(s_timerRemaining);
            if (wholeSeconds > 10 || wholeSeconds <= 0)
                return;
            if (wholeSeconds == s_lastTimerSecondAudioPlayed)
                return;

            if (!TryEnsureTimerSecondAudioReady())
                return;
            if (s_timerSecondAudioSource == null || s_timerSecondAudioClip == null)
                return;

            s_timerSecondAudioSource.PlayOneShot(s_timerSecondAudioClip);
            s_lastTimerSecondAudioPlayed = wholeSeconds;
        }

        private static void TryPlayLastChanceTimerWarnings()
        {
            if (!s_active)
            {
                return;
            }

            var wholeSeconds = Mathf.CeilToInt(s_timerRemaining);
            var shouldPlay = wholeSeconds == 60 || wholeSeconds == 30;
            if (!shouldPlay || wholeSeconds == s_lastTimerWarningAudioPlayed)
            {
                return;
            }

            if (!TryEnsureTimerWarningAudioReady())
            {
                return;
            }

            if (s_timerWarningAudioSource == null || s_timerWarningAudioClip == null)
            {
                return;
            }

            // 1:00 at normal speed, 0:30 at +50% speed.
            s_timerWarningAudioSource.pitch = wholeSeconds == 30 ? 1.5f : 1f;
            s_timerWarningAudioSource.PlayOneShot(s_timerWarningAudioClip);
            s_lastTimerWarningAudioPlayed = wholeSeconds;
        }

        private static bool TryEnsureTimerSecondAudioReady()
        {
            if (s_timerSecondAudioClip == null && !s_timerSecondAudioLoadAttempted)
            {
                s_timerSecondAudioLoadAttempted = true;
                if (!AudioAssetLoader.TryLoadAudioClip(
                        TimerSecondAudioFileName,
                        AudioAssetLoader.GetDefaultAssetsDirectory(),
                        out var clip,
                        out var resolvedPath) || clip == null)
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.TimerSecond.LoadFail", 30))
                    {
                        var baseDir = AudioAssetLoader.GetDefaultAssetsDirectory();
                        Debug.LogWarning($"[LastChance] Failed to load timer tick audio. file={TimerSecondAudioFileName} baseDir={baseDir}");
                    }

                    return false;
                }

                s_timerSecondAudioClip = clip;
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.TimerSecond.Loaded", 30))
                {
                    Debug.Log($"[LastChance] Loaded timer tick audio from: {resolvedPath}");
                }
            }

            if (s_timerSecondAudioClip == null)
                return false;

            if (s_timerSecondAudioSource == null)
            {
                var go = new GameObject("DHHFix.LastChanceTimerSecondAudio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                src.volume = 1f;
                s_timerSecondAudioSource = src;
            }

            return s_timerSecondAudioSource != null;
        }

        private static bool TryEnsureTimerWarningAudioReady()
        {
            if (s_timerWarningAudioClip == null && !s_timerWarningAudioLoadAttempted)
            {
                s_timerWarningAudioLoadAttempted = true;
                if (!TryLoadTimerWarningClip(out var clip, out var resolvedPath) || clip == null)
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.TimerWarning.LoadFail", 30))
                    {
                        var baseDir = AudioAssetLoader.GetDefaultAssetsDirectory();
                        Debug.LogWarning(
                            $"[LastChance] Failed to load timer warning audio. files={TimerWarningAudioPrimaryFileName},{TimerWarningAudioFallbackFileName} baseDir={baseDir}");
                    }

                    return false;
                }

                s_timerWarningAudioClip = clip;
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.TimerWarning.Loaded", 30))
                {
                    Debug.Log($"[LastChance] Loaded timer warning audio from: {resolvedPath}");
                }
            }

            if (s_timerWarningAudioClip == null)
            {
                return false;
            }

            if (s_timerWarningAudioSource == null)
            {
                var go = new GameObject("DHHFix.LastChanceTimerWarningAudio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                src.volume = 1f;
                src.pitch = 1f;
                s_timerWarningAudioSource = src;
            }

            return s_timerWarningAudioSource != null;
        }

        private static bool TryLoadTimerWarningClip(out AudioClip? clip, out string resolvedPath)
        {
            clip = null;
            resolvedPath = string.Empty;

            if (AudioAssetLoader.TryLoadAudioClip(
                TimerWarningAudioPrimaryFileName,
                AudioAssetLoader.GetDefaultAssetsDirectory(),
                out clip,
                out resolvedPath))
            {
                return true;
            }

            return AudioAssetLoader.TryLoadAudioClip(
                TimerWarningAudioFallbackFileName,
                AudioAssetLoader.GetDefaultAssetsDirectory(),
                out clip,
                out resolvedPath);
        }

        private static void StopTimerSecondAudio()
        {
            s_lastTimerSecondAudioPlayed = -1;
            if (s_timerSecondAudioSource != null)
            {
                s_timerSecondAudioSource.Stop();
            }

            s_lastTimerWarningAudioPlayed = -1;
            if (s_timerWarningAudioSource != null)
            {
                s_timerWarningAudioSource.Stop();
                s_timerWarningAudioSource.pitch = 1f;
            }
        }

        private static void UpdateIndicators(int maxPlayers, bool allDead)
        {
            var mode = GetIndicatorMode();
            if (!s_active || !allDead)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.Indicator.Blocked", 5))
                {
                    var rawMode = FeatureFlags.LastChanceIndicators ?? string.Empty;
                    Debug.Log($"[LastChance] Indicator blocked: active={s_active} allDead={allDead} modeRaw='{rawMode}' modeParsed={mode}");
                }
                ClearActiveIndicatorVisuals();
                AbilityModule.RefreshDirectionSlotVisuals();
                return;
            }

            if (mode == LastChanceIndicatorMode.None)
            {
                if (!s_indicatorNoneLoggedThisCycle && FeatureFlags.DebugLogging)
                {
                    var rawMode = FeatureFlags.LastChanceIndicators ?? string.Empty;
                    Debug.Log($"[LastChance] Indicator disabled for this cycle: modeRaw='{rawMode}' modeParsed={mode}");
                    s_indicatorNoneLoggedThisCycle = true;
                }
                ClearActiveIndicatorVisuals();
                AbilityModule.RefreshDirectionSlotVisuals();
                return;
            }

            var directionEnabled = mode == LastChanceIndicatorMode.Direction;
            UpdateSingleIndicator(IndicatorKind.Direction, directionEnabled, InputKey.Inventory2, maxPlayers);
            AbilityModule.RefreshDirectionSlotVisuals();
        }

        private static LastChanceIndicatorMode GetIndicatorMode()
        {
            var raw = (FeatureFlags.LastChanceIndicators ?? string.Empty).Trim();
            if (raw.Equals("Direction", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceIndicatorMode.Direction;
            }

            if (raw.Equals("Indicator", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceIndicatorMode.Direction;
            }

            return LastChanceIndicatorMode.None;
        }

        private static void UpdateSingleIndicator(IndicatorKind kind, bool enabled, InputKey inputKey, int maxPlayers)
        {
            if (!enabled)
            {
                ResetIndicatorHold();
                DeactivateIndicator(kind);
                return;
            }

            if (IsIndicatorActive(kind))
            {
                if (Time.time >= GetIndicatorActiveUntil(kind))
                {
                    DeactivateIndicator(kind);
                }
                else
                {
                    TickActiveIndicator(kind);
                }
            }

            if (IsIndicatorActive(kind) || Time.time < GetIndicatorCooldownUntil(kind))
            {
                ResetIndicatorHold();
                return;
            }

            var holdSeconds = DirectionIndicatorHoldSeconds;
            if (!SemiFunc.InputHold(inputKey))
            {
                ResetIndicatorHold();
                return;
            }

            s_directionHoldTimer = Mathf.Min(holdSeconds, s_directionHoldTimer + Time.deltaTime);
            AbilityModule.SetDirectionSlotActivationProgress(Mathf.Clamp01(s_directionHoldTimer / holdSeconds));
            if (s_directionHoldTimer < holdSeconds)
            {
                return;
            }

            ResetIndicatorHold();
            TriggerIndicator(kind, maxPlayers);
        }

        private static void ResetIndicatorHold()
        {
            s_directionHoldTimer = 0f;
            AbilityModule.SetDirectionSlotActivationProgress(0f);
        }

        private static void TriggerIndicator(IndicatorKind kind, int maxPlayers)
        {
            var duration = Mathf.Clamp(FeatureFlags.LastChanceIndicatorDirectionDurationSeconds, 0.5f, 20f);
            var cooldown = Mathf.Clamp(FeatureFlags.LastChanceIndicatorDirectionCooldownSeconds, 1f, 60f);
            var activeUntil = Time.time + duration;
            SetIndicatorActive(kind, true);
            SetIndicatorActiveUntil(kind, activeUntil);
            SetIndicatorCooldownUntil(kind, activeUntil + cooldown);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog($"{IndicatorCooldownLogKey}.Start.{kind}", 2))
            {
                Debug.Log($"[LastChance] Indicator cooldown started: kind={kind} duration={duration:F1}s cooldown={cooldown:F1}s");
            }

            ApplyIndicatorPenalty(kind, maxPlayers);
            TickActiveIndicator(kind);
            if (kind == IndicatorKind.Direction)
            {
                var uiLockSeconds = Mathf.Max(0f, GetIndicatorCooldownUntil(kind) - Time.time);
                AbilityModule.TriggerDirectionSlotCooldown(uiLockSeconds);
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(IndicatorLogKey, 3))
            {
                var remainingCooldown = Mathf.Max(0f, GetIndicatorCooldownUntil(kind) - Time.time);
                Debug.Log($"[LastChance] Indicator triggered: mode={kind} active={duration:F1}s cooldown={remainingCooldown:F1}s timer={s_timerRemaining:F1}s");
            }
        }

        private static void ApplyIndicatorPenalty(IndicatorKind kind, int maxPlayers)
        {
            if (SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClient())
            {
                return;
            }

            var penalty = CalculateIndicatorPenaltySeconds(maxPlayers);
            if (penalty <= 0f)
            {
                return;
            }

            s_timerRemaining = Mathf.Max(0f, s_timerRemaining - penalty);
            BroadcastTimerStateIfHost(force: true);
            LastChanceTimerUI.UpdateText(FormatTimerText(s_timerRemaining));
        }

        private static float CalculateIndicatorPenaltySeconds(int maxPlayers)
        {
            var inputs = GetDynamicTimerInputsForRuntime(maxPlayers);
            var difficulty = EstimateDifficulty01(inputs);
            var easyPenalty = Mathf.Max(0f, FeatureFlags.LastChanceIndicatorDirectionPenaltyEasySeconds);
            var hardPenalty = Mathf.Max(0f, FeatureFlags.LastChanceIndicatorDirectionPenaltyHardSeconds);

            var maxPenalty = Mathf.Max(easyPenalty, hardPenalty);
            var minPenalty = Mathf.Min(easyPenalty, hardPenalty);
            return Mathf.Lerp(maxPenalty, minPenalty, difficulty);
        }

        private static float EstimateDifficulty01(DynamicTimerInputs inputs)
        {
            var levelFactor = Mathf.Clamp01((inputs.LevelNumber - 1f) / 20f);
            var distanceFactor = Mathf.Clamp01(inputs.TotalDistanceMeters / 300f);
            var roomFactor = Mathf.Clamp01(inputs.TotalShortestRoomPathSteps / 28f);
            var altitudeFactor = Mathf.Clamp01(inputs.TotalBelowTruckMeters / 25f);
            var monsterFactor = Mathf.Clamp01(inputs.AliveSearchMonsters / 10f);
            var weighted = levelFactor * 0.2f + distanceFactor * 0.35f + roomFactor * 0.2f + altitudeFactor * 0.1f + monsterFactor * 0.15f;
            return Mathf.Clamp01(weighted);
        }

        private static void TickActiveIndicator(IndicatorKind kind)
        {
            EnsureDirectionLine();
            AnimateDirectionLineMaterial();
            UpdateDirectionPath(force: Time.time >= s_indicatorNextPathRefreshAt);
        }

        private static void DeactivateIndicator(IndicatorKind kind)
        {
            SetIndicatorActive(kind, false);
            if (s_indicatorDirectionLine != null)
            {
                s_indicatorDirectionLine.positionCount = 0;
                s_indicatorDirectionLine.enabled = false;
            }
        }

        private static bool IsIndicatorActive(IndicatorKind kind)
        {
            return s_directionActive;
        }

        private static void SetIndicatorActive(IndicatorKind kind, bool value)
        {
            s_directionActive = value;
        }

        private static float GetIndicatorActiveUntil(IndicatorKind kind)
        {
            return s_directionActiveUntil;
        }

        private static void SetIndicatorActiveUntil(IndicatorKind kind, float value)
        {
            s_directionActiveUntil = value;
        }

        private static float GetIndicatorCooldownUntil(IndicatorKind kind)
        {
            return s_directionCooldownUntil;
        }

        private static void SetIndicatorCooldownUntil(IndicatorKind kind, float value)
        {
            s_directionCooldownUntil = value;
        }

        private static void EnsureDirectionLine()
        {
            if (s_indicatorDirectionLine != null)
            {
                s_indicatorDirectionLine.enabled = true;
                return;
            }

            s_indicatorDirectionObject = new GameObject("DHHFix.LastChanceDirectionIndicator");
            UnityEngine.Object.DontDestroyOnLoad(s_indicatorDirectionObject);
            s_indicatorDirectionLine = s_indicatorDirectionObject.AddComponent<LineRenderer>();
            s_indicatorDirectionLine.useWorldSpace = true;
            s_indicatorDirectionLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s_indicatorDirectionLine.receiveShadows = false;
            s_indicatorDirectionLine.textureMode = LineTextureMode.Tile;
            s_indicatorDirectionLine.alignment = LineAlignment.View;
            s_indicatorDirectionLine.widthCurve = AnimationCurve.EaseInOut(0f, 0.09f, 1f, 0.05f);
            s_indicatorDirectionLine.positionCount = 0;
            s_indicatorDirectionLine.enabled = true;

            if (!TryApplyPhysGrabBeamMaterial(s_indicatorDirectionLine))
            {
                s_indicatorDirectionLine.material = new Material(Shader.Find("Sprites/Default"));
                s_indicatorDirectionMaterial = s_indicatorDirectionLine.material;
            }
            ConfigureDirectionLineFromPhysGrabBeam();
            s_indicatorNextPathRefreshAt = 0f;
        }

        private static bool TryApplyPhysGrabBeamMaterial(LineRenderer lineRenderer)
        {
            if (!TryGetPhysGrabBeamSource(out var source))
            {
                return false;
            }
            if (source == null)
            {
                return false;
            }

            lineRenderer.material = source.material;
            lineRenderer.textureMode = source.textureMode;
            s_indicatorDirectionMaterial = lineRenderer.material;
            return true;
        }

        private static void ConfigureDirectionLineFromPhysGrabBeam()
        {
            if (s_indicatorDirectionLine == null || !TryGetPhysGrabBeamSource(out var source))
            {
                return;
            }
            if (source == null)
            {
                return;
            }

            s_indicatorDirectionLine.alignment = source.alignment;
            s_indicatorDirectionLine.textureMode = source.textureMode;
            s_indicatorDirectionLine.widthMultiplier = source.widthMultiplier;
            s_indicatorDirectionLine.widthCurve = source.widthCurve;
            s_indicatorDirectionLine.colorGradient = source.colorGradient;
            s_indicatorDirectionLine.startColor = source.startColor;
            s_indicatorDirectionLine.endColor = source.endColor;
            s_indicatorDirectionLine.numCornerVertices = source.numCornerVertices;
            s_indicatorDirectionLine.numCapVertices = source.numCapVertices;
            s_indicatorDirectionLine.generateLightingData = source.generateLightingData;
            s_indicatorDirectionLine.material = source.material;
            s_indicatorDirectionMaterial = s_indicatorDirectionLine.material;
        }

        private static void AnimateDirectionLineMaterial()
        {
            if (s_indicatorDirectionMaterial == null)
            {
                return;
            }

            s_indicatorDirectionMaterial.mainTextureScale = Vector2.one;
            s_indicatorDirectionMaterial.mainTextureOffset = Time.time * DirectionLineScrollSpeed;
        }

        private static bool TryGetPhysGrabBeamSource(out LineRenderer? source)
        {
            source = null;
            var avatar = PlayerAvatar.instance;
            if (avatar == null)
            {
                return false;
            }

            var physGrabber = avatar.GetComponent<PhysGrabber>();
            if (physGrabber == null || physGrabber.physGrabBeam == null)
            {
                return false;
            }

            source = physGrabber.physGrabBeam.GetComponent<LineRenderer>();
            if (source == null || source.material == null)
            {
                return false;
            }
            return true;
        }

        private static void UpdateDirectionPath(bool force)
        {
            if (!force)
            {
                return;
            }

            s_indicatorNextPathRefreshAt = Time.time + DirectionPathRefreshSeconds;
            if (s_indicatorDirectionLine == null)
            {
                return;
            }

            if (!TryBuildPathToTruck(out var pathPoints, out var navFrom, out var navTo))
            {
                s_indicatorDirectionLine.positionCount = 0;
                return;
            }

            if (s_hasLastDirectionPathSample &&
                (navFrom - s_lastDirectionPathFrom).sqrMagnitude <= DirectionPathMovementThresholdSqr &&
                (navTo - s_lastDirectionPathTo).sqrMagnitude <= DirectionPathMovementThresholdSqr)
            {
                return;
            }

            s_hasLastDirectionPathSample = true;
            s_lastDirectionPathFrom = navFrom;
            s_lastDirectionPathTo = navTo;

            s_indicatorDirectionLine.positionCount = pathPoints.Count;
            for (var i = 0; i < pathPoints.Count; i++)
            {
                s_indicatorDirectionLine.SetPosition(i, pathPoints[pathPoints.Count - 1 - i]);
            }
        }

        private static bool TryBuildPathToTruck(out List<Vector3> points, out Vector3 navFrom, out Vector3 navTo)
        {
            points = new List<Vector3>(2);
            navFrom = Vector3.zero;
            navTo = Vector3.zero;
            var localAvatar = PlayerAvatar.instance;
            if (localAvatar == null)
            {
                return false;
            }

            var localPosBase = GetLocalHeadOrPlayerPosition(localAvatar);
            if (!TryGetTruckPosition(out var truckPosBase))
            {
                return false;
            }
            var localPos = localPosBase + Vector3.up * DirectionLineHeightOffset;
            var truckPos = truckPosBase + Vector3.up * DirectionLineHeightOffset;

            if (!TrySampleNavMeshPosition(localPosBase, 12f, out var from))
            {
                from = localPosBase;
            }

            if (!TrySampleNavMeshPosition(truckPosBase, 8f, out var to))
            {
                to = truckPosBase;
            }

            navFrom = from;
            navTo = to;

            if (!TryCalculateNavMeshPathCorners(from, to, out var corners) || corners.Length == 0)
            {
                points.Add(localPos);
                points.Add(truckPos);
                return true;
            }

            points = new List<Vector3>(corners.Length + 1) { localPos };
            for (var i = 0; i < corners.Length; i++)
            {
                points.Add(corners[i] + Vector3.up * DirectionLineHeightOffset);
            }

            return points.Count >= 2;
        }

        private static Vector3 GetLocalHeadOrPlayerPosition(PlayerAvatar avatar)
        {
            if (avatar.playerDeathHead != null)
            {
                return avatar.playerDeathHead.transform.position;
            }

            if (avatar.playerTransform != null)
            {
                return avatar.playerTransform.position;
            }

            return avatar.transform.position;
        }

        private static bool TryGetTruckPosition(out Vector3 truckPosition)
        {
            truckPosition = Vector3.zero;
            if (s_levelGeneratorInstanceField == null)
            {
                return false;
            }

            var levelGenerator = s_levelGeneratorInstanceField.GetValue(null);
            if (levelGenerator == null)
            {
                return false;
            }

            if (s_levelPathTruckField != null)
            {
                var candidate = s_levelPathTruckField.GetValue(levelGenerator);
                if (TryGetTransformPosition(candidate, out truckPosition))
                {
                    return true;
                }
            }

            if (s_levelPathPointsField?.GetValue(levelGenerator) is not System.Collections.IEnumerable points || s_levelPointTruckField == null)
            {
                return false;
            }

            foreach (var point in points)
            {
                if (point == null)
                {
                    continue;
                }

                if (s_levelPointTruckField.GetValue(point) is bool isTruck && isTruck && TryGetTransformPosition(point, out truckPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetTransformPosition(object obj, out Vector3 position)
        {
            position = Vector3.zero;
            if (obj == null)
            {
                return false;
            }

            if (obj is Component comp && comp != null)
            {
                position = comp.transform.position;
                return true;
            }

            var transformProperty = obj.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (transformProperty?.GetValue(obj) is Transform transform)
            {
                position = transform.position;
                return true;
            }

            return false;
        }

        private static bool TrySampleNavMeshPosition(Vector3 source, float maxDistance, out Vector3 sampledPosition)
        {
            sampledPosition = Vector3.zero;
            if (s_navMeshHitType == null || s_navMeshSamplePositionMethod == null)
            {
                return false;
            }

            s_reusableNavMeshHitBoxed ??= Activator.CreateInstance(s_navMeshHitType);
            if (s_reusableNavMeshHitBoxed == null)
            {
                return false;
            }

            var args = new object[] { source, s_reusableNavMeshHitBoxed, maxDistance, -1 };
            if (s_navMeshSamplePositionMethod.Invoke(null, args) is not bool success || !success)
            {
                return false;
            }

            if (TryGetNavHitPosition(args[1], out var hit))
            {
                sampledPosition = hit;
                return true;
            }

            return false;
        }

        private static bool TryGetNavHitPosition(object navHit, out Vector3 position)
        {
            position = Vector3.zero;
            if (navHit == null)
            {
                return false;
            }

            if (s_navMeshHitPositionProperty != null && s_navMeshHitPositionProperty.GetValue(navHit) is Vector3 propPos)
            {
                position = propPos;
                return true;
            }

            if (s_navMeshHitPositionField != null && s_navMeshHitPositionField.GetValue(navHit) is Vector3 fieldPos)
            {
                position = fieldPos;
                return true;
            }

            return false;
        }

        private static bool TryCalculateNavMeshPathCorners(Vector3 from, Vector3 to, out Vector3[] corners)
        {
            corners = Array.Empty<Vector3>();
            if (s_navMeshPathType == null || s_navMeshCalculatePathMethod == null || s_navMeshPathCornersProperty == null)
            {
                return false;
            }

            s_reusableNavMeshPath ??= Activator.CreateInstance(s_navMeshPathType);
            if (s_reusableNavMeshPath == null)
            {
                return false;
            }

            var args = new object[] { from, to, -1, s_reusableNavMeshPath };
            if (s_navMeshCalculatePathMethod.Invoke(null, args) is not bool success || !success)
            {
                return false;
            }

            if (s_navMeshPathCornersProperty.GetValue(s_reusableNavMeshPath) is Vector3[] pathCorners && pathCorners.Length > 0)
            {
                corners = pathCorners;
                return true;
            }

            return false;
        }

        private static void ClearActiveIndicatorVisuals()
        {
            DeactivateIndicator(IndicatorKind.Direction);
            AbilityModule.RefreshDirectionSlotVisuals();
        }

        private static void ClearIndicatorsState()
        {
            s_indicatorNoneLoggedThisCycle = false;
            s_directionHoldTimer = 0f;
            s_directionCooldownUntil = 0f;
            s_directionActiveUntil = 0f;
            s_directionActive = false;
            s_indicatorNextPathRefreshAt = 0f;
            s_hasLastDirectionPathSample = false;
            AbilityModule.SetDirectionSlotActivationProgress(0f);
            ClearActiveIndicatorVisuals();
        }


        private static void DebugTruckState(bool allDead)
        {
            if (!FeatureFlags.DebugLogging || !FeatureFlags.LastChangeMode || !FeatureFlags.BatteryJumpEnabled)
            {
                return;
            }

            if (!allDead)
            {
                return;
            }

            if (!LogLimiter.ShouldLog("LastChance.TruckState", 30))
            {
                return;
            }

            var director = GameDirector.instance;
            if (director == null || director.PlayerList == null || director.PlayerList.Count == 0)
            {
                return;
            }

            var message = "[LastChance] TruckState:";
            var extractionDone = RoundDirectorAllExtractionField != null &&
                RoundDirector.instance != null &&
                RoundDirectorAllExtractionField.GetValue(RoundDirector.instance) is bool extraction &&
                extraction;
            message += $" extractionDone={extractionDone}";
            foreach (var player in director.PlayerList)
            {
                if (player == null)
                {
                    message += " [null player]";
                    continue;
                }

                var name = GetPlayerName(player);
                var disabled = IsPlayerDisabled(player);
                if (!disabled)
                {
                    var rvc = GetRoomVolumeCheck(player);
                    var inTruck = rvc != null && IsRoomVolumeInTruck(rvc);
                    message += $" {name}(alive,inTruck={inTruck})";
                    continue;
                }

                var deathHead = player.playerDeathHead;
                var dhRoom = deathHead != null ? GetDeathHeadRoomVolumeCheck(deathHead) : null;
                var dhRoomInTruck = dhRoom != null && IsRoomVolumeInTruck(dhRoom);
                var inTruckField = deathHead != null ? GetDeathHeadInTruckField(deathHead.GetType()) : null;
                var dhInTruck = false;
                if (deathHead != null && inTruckField != null && inTruckField.GetValue(deathHead) is bool b)
                {
                    dhInTruck = b;
                }
                message += $" {name}(deadHead,roomInTruck={dhRoomInTruck},inTruck={dhInTruck})";
            }

            Debug.Log(message);
        }

        private static string GetPlayerName(PlayerAvatar player)
        {
            if (PlayerAvatarNameField != null &&
                PlayerAvatarNameField.GetValue(player) is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return player.GetType().Name;
        }

        private static bool IsRunStarted(RunManager runMgr)
        {
            if (RunManagerRunStartedField == null)
            {
                return false;
            }

            return RunManagerRunStartedField.GetValue(runMgr) is bool started && started;
        }

        private static bool IsRestarting(RunManager runMgr)
        {
            if (RunManagerRestartingField == null)
            {
                return false;
            }

            return RunManagerRestartingField.GetValue(runMgr) is bool restarting && restarting;
        }

        private static bool IsPlayerDisabled(PlayerAvatar player)
        {
            if (PlayerAvatarIsDisabledField == null)
            {
                return false;
            }

            return PlayerAvatarIsDisabledField.GetValue(player) is bool disabled && disabled;
        }

        private static object? GetRoomVolumeCheck(PlayerAvatar player)
        {
            if (PlayerAvatarRoomVolumeCheckField == null)
            {
                return null;
            }

            return PlayerAvatarRoomVolumeCheckField.GetValue(player);
        }

        private static bool IsRoomVolumeInTruck(object roomVolumeCheck)
        {
            if (roomVolumeCheck == null)
            {
                return false;
            }

            var field = s_roomVolumeCheckInTruckField;
            if (field == null || field.DeclaringType != roomVolumeCheck.GetType())
            {
                field = AccessTools.Field(roomVolumeCheck.GetType(), "inTruck");
                s_roomVolumeCheckInTruckField = field;
            }

            return field != null && field.GetValue(roomVolumeCheck) is bool inTruck && inTruck;
        }

        private static FieldInfo? GetDeathHeadInTruckField(Type deathHeadType)
        {
            var field = s_deathHeadInTruckField;
            if (field == null || field.DeclaringType != deathHeadType)
            {
                field = AccessTools.Field(deathHeadType, "inTruck");
                s_deathHeadInTruckField = field;
            }

            return field;
        }

        private static bool? GetDeathHeadInTruckStatus(PlayerDeathHead deathHead)
        {
            if (deathHead == null)
            {
                return null;
            }

            var roomVolume = GetDeathHeadRoomVolumeCheck(deathHead);
            if (roomVolume != null)
            {
                return IsRoomVolumeInTruck(roomVolume);
            }

            var inTruckField = GetDeathHeadInTruckField(deathHead.GetType());
            if (inTruckField != null && inTruckField.GetValue(deathHead) is bool inTruck)
            {
                return inTruck;
            }

            return null;
        }

        private static object? GetDeathHeadRoomVolumeCheck(PlayerDeathHead deathHead)
        {
            var field = s_deathHeadRoomVolumeCheckField;
            if (field == null || field.DeclaringType != deathHead.GetType())
            {
                field = AccessTools.Field(deathHead.GetType(), "roomVolumeCheck");
                s_deathHeadRoomVolumeCheckField = field;
            }

            return field?.GetValue(deathHead);
        }

        private static float GetConfiguredSeconds()
        {
            var seconds = Mathf.Clamp(FeatureFlags.LastChanceTimerSeconds, 30, 600);
            var step = Mathf.RoundToInt(seconds / 30f) * 30;
            return Mathf.Clamp(step, 30, 600);
        }

        private static float GetInitialTimerSeconds(int maxPlayers)
        {
            var baseSeconds = GetConfiguredSeconds();
            var maxSeconds = GetDynamicTimerCapSeconds();
            if (!FeatureFlags.LastChanceDynamicTimerEnabled)
            {
                ClearCachedDynamicTimerInputs();
                return Mathf.Clamp(baseSeconds, 30f, maxSeconds);
            }

            var inputs = CollectDynamicTimerInputs(maxPlayers);
            CacheDynamicTimerInputs(inputs);
            var rawAddedSeconds = CalculateRawAddedSeconds(inputs);
            var growthCapSeconds = GetLevelGrowthCapSeconds(baseSeconds, maxSeconds, inputs.LevelNumber);
            var finalSeconds = Mathf.Clamp(baseSeconds + rawAddedSeconds, 30f, growthCapSeconds);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.DynamicTimer", 30))
            {
                Debug.Log(
                    $"[LastChance] DynamicTimer: base={baseSeconds:F1}s level={inputs.LevelNumber} required={inputs.RequiredPlayers} " +
                    $"totalDistance={inputs.TotalDistanceMeters:F1}m belowPlayers={inputs.PlayersBelowTruckThreshold} belowMeters={inputs.TotalBelowTruckMeters:F2} " +
                    $"aliveMonsters={inputs.AliveSearchMonsters} totalRoomSteps={inputs.TotalShortestRoomPathSteps} rawAdd={rawAddedSeconds:F1}s " +
                    $"levelCap={growthCapSeconds:F1}s final={finalSeconds:F1}s hardCap={maxSeconds:F1}s");
            }

            return finalSeconds;
        }

        private static DynamicTimerInputs GetDynamicTimerInputsForRuntime(int maxPlayers)
        {
            if (s_hasCachedDynamicTimerInputs)
            {
                return s_cachedDynamicTimerInputs;
            }

            var inputs = CollectDynamicTimerInputs(maxPlayers);
            CacheDynamicTimerInputs(inputs);
            return inputs;
        }

        private static void CacheDynamicTimerInputs(DynamicTimerInputs inputs)
        {
            s_cachedDynamicTimerInputs = inputs;
            s_hasCachedDynamicTimerInputs = true;
        }

        private static void ClearCachedDynamicTimerInputs()
        {
            s_cachedDynamicTimerInputs = default;
            s_hasCachedDynamicTimerInputs = false;
        }

        private static float GetDynamicTimerCapSeconds()
        {
            var capMinutes = Mathf.Clamp(FeatureFlags.LastChanceDynamicMaxMinutes, 5, 20);
            return capMinutes * 60f;
        }

        private static DynamicTimerInputs CollectDynamicTimerInputs(int maxPlayers)
        {
            var requiredPlayers = Mathf.Max(1, GetLastChanceNeededPlayers(maxPlayers));
            var levelNumber = GetCurrentLevelNumber();
            var aliveSearchMonsters = LastChanceMonstersSearchModule.GetAliveSearchMonsterCount();
            var records = PlayerTruckDistanceHelper.GetDistancesFromTruck();
            if (records.Length == 0)
            {
                return new DynamicTimerInputs(
                    requiredPlayers,
                    levelNumber,
                    aliveSearchMonsters,
                    0f,
                    0,
                    0f,
                    0);
            }

            var selected = SelectRequiredPlayers(records, requiredPlayers);
            var belowThreshold = Mathf.Min(0f, FeatureFlags.LastChanceBelowTruckThresholdMeters);
            var totalDistanceMeters = 0f;
            var playersBelowTruckThreshold = 0;
            var totalBelowTruckMeters = 0f;
            var totalShortestRoomPathSteps = 0;

            for (var i = 0; i < selected.Count; i++)
            {
                var record = selected[i];
                if (record.HasValidPath && record.NavMeshDistance >= 0f)
                {
                    totalDistanceMeters += record.NavMeshDistance;
                }

                if (record.ShortestRoomPathToTruck >= 0)
                {
                    totalShortestRoomPathSteps += record.ShortestRoomPathToTruck;
                }

                if (record.HeightDelta <= belowThreshold)
                {
                    playersBelowTruckThreshold++;
                    totalBelowTruckMeters += Mathf.Max(0f, belowThreshold - record.HeightDelta);
                }
            }

            return new DynamicTimerInputs(
                requiredPlayers,
                levelNumber,
                aliveSearchMonsters,
                totalDistanceMeters,
                playersBelowTruckThreshold,
                totalBelowTruckMeters,
                totalShortestRoomPathSteps);
        }

        private static List<PlayerTruckDistanceHelper.PlayerTruckDistance> SelectRequiredPlayers(
            PlayerTruckDistanceHelper.PlayerTruckDistance[] records,
            int requiredPlayers)
        {
            var sorted = new List<PlayerTruckDistanceHelper.PlayerTruckDistance>(records.Length);
            for (var i = 0; i < records.Length; i++)
            {
                sorted.Add(records[i]);
            }

            sorted.Sort((left, right) => ScoreTimerDifficulty(right).CompareTo(ScoreTimerDifficulty(left)));
            var take = Mathf.Clamp(requiredPlayers, 1, sorted.Count);
            if (take >= sorted.Count)
            {
                return sorted;
            }

            var trimmed = new List<PlayerTruckDistanceHelper.PlayerTruckDistance>(take);
            for (var i = 0; i < take; i++)
            {
                trimmed.Add(sorted[i]);
            }

            return trimmed;
        }

        private static float ScoreTimerDifficulty(PlayerTruckDistanceHelper.PlayerTruckDistance record)
        {
            if (record.HasValidPath && record.NavMeshDistance >= 0f)
            {
                return record.NavMeshDistance;
            }

            if (record.ShortestRoomPathToTruck >= 0)
            {
                return record.ShortestRoomPathToTruck * 15f;
            }

            return 99999f;
        }

        private static int GetCurrentLevelNumber()
        {
            var runMgr = RunManager.instance;
            if (runMgr == null)
            {
                return 1;
            }

            try
            {
                return Mathf.Max(1, Convert.ToInt32(runMgr.levelCurrent));
            }
            catch
            {
                return 1;
            }
        }

        private static float CalculateRawAddedSeconds(DynamicTimerInputs inputs)
        {
            var added = 0f;
            added += inputs.RequiredPlayers * FeatureFlags.LastChanceTimerPerRequiredPlayerSeconds;
            added += inputs.TotalDistanceMeters * FeatureFlags.LastChanceTimerPerFarthestMeterSeconds;
            added += inputs.PlayersBelowTruckThreshold * FeatureFlags.LastChanceTimerPerBelowTruckPlayerSeconds;
            added += inputs.TotalBelowTruckMeters * FeatureFlags.LastChanceTimerPerBelowTruckMeterSeconds;
            added += inputs.TotalShortestRoomPathSteps * FeatureFlags.LastChanceTimerPerRoomStepSeconds;
            var monsterSeconds = inputs.AliveSearchMonsters * Mathf.Max(0f, FeatureFlags.LastChanceTimerPerMonsterSeconds);
            if (FeatureFlags.LastChanceMonstersSearchEnabled)
            {
                // MonstersSearch ON makes disabled players valid targets: add extra pressure on timer.
                monsterSeconds *= 1.5f;
            }
            added += monsterSeconds;
            added *= CalculateLevelContributionMultiplier(inputs);
            return Mathf.Max(0f, added);
        }

        private static float CalculateLevelContributionMultiplier(DynamicTimerInputs inputs)
        {
            var perLevelPercent = Mathf.Max(0f, FeatureFlags.LastChanceTimerPerLevelSeconds) * 0.01f;
            var levelMultiplier = 1f + (Mathf.Max(1, inputs.LevelNumber) - 1f) * perLevelPercent;
            var roomBase = Mathf.Max(1f, inputs.RequiredPlayers * 14f);
            var roomFactor = Mathf.Clamp01(inputs.TotalShortestRoomPathSteps / roomBase);
            var monsterFactor = Mathf.Clamp01(inputs.AliveSearchMonsters / 10f);
            var roomWeight = Mathf.Max(0f, FeatureFlags.LastChanceLevelContextRoomWeight);
            var monsterWeight = Mathf.Max(0f, FeatureFlags.LastChanceLevelContextMonsterWeight);
            var contextMultiplier = 1f + (roomFactor * roomWeight) + (monsterFactor * monsterWeight);

            return Mathf.Max(1f, levelMultiplier * contextMultiplier);
        }

        private static float GetLevelGrowthCapSeconds(float baseSeconds, float maxSeconds, int levelNumber)
        {
            if (maxSeconds <= baseSeconds)
            {
                return baseSeconds;
            }

            var maxAtLevel = Mathf.Max(2, FeatureFlags.LastChanceDynamicMaxMinutesAtLevel);
            var normalized = Mathf.Clamp01((Mathf.Max(1, levelNumber) - 1f) / (maxAtLevel - 1f));
            return Mathf.Lerp(baseSeconds, maxSeconds, normalized);
        }

        private static string FormatTimerText(float secondsRemaining)
        {
            var seconds = Mathf.CeilToInt(secondsRemaining);
            var minutes = seconds / 60;
            var secs = seconds % 60;
            var color = seconds <= 30 ? FlashColor : TimerColor;
            var colorHex = ColorUtility.ToHtmlStringRGB(color);
            return $"<color=#{colorHex}><b>LAST CHANCE</b>  {minutes:0}:{secs:00}</color>";
        }
    }

}



