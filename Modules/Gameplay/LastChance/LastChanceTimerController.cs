#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
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

        private enum LastChanceIndicatorMode
        {
            None = 0,
            Direction = 1,
            Map = 2,
            All = 3
        }

        private enum IndicatorKind
        {
            Direction = 1,
            Map = 2
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
        private static float s_mapCooldownUntil;
        private static float s_mapActiveUntil;
        private static bool s_mapActive;
        private static bool s_indicatorForcedMap;
        private static float s_mapUiRetryAt;
        private static GameObject? s_indicatorDirectionObject;
        private static LineRenderer? s_indicatorDirectionLine;
        private static float s_indicatorNextPathRefreshAt;
        private static Camera? s_mapIndicatorCamera;
        private static RenderTexture? s_mapIndicatorRenderTexture;

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
        private static readonly FieldInfo? s_mapToolActiveField = AccessTools.Field(typeof(MapToolController), "Active");
        private static readonly FieldInfo? s_mapToolHideLerpField = AccessTools.Field(typeof(MapToolController), "HideLerp");
        
        private readonly struct DynamicTimerInputs
        {
            internal DynamicTimerInputs(
                int requiredPlayers,
                int levelNumber,
                int aliveSearchMonsters,
                float farthestDistanceMeters,
                int playersBelowTruckThreshold,
                float totalBelowTruckMeters,
                float monstersAddedSeconds,
                int longestShortestRoomPath)
            {
                RequiredPlayers = requiredPlayers;
                LevelNumber = levelNumber;
                AliveSearchMonsters = aliveSearchMonsters;
                FarthestDistanceMeters = farthestDistanceMeters;
                PlayersBelowTruckThreshold = playersBelowTruckThreshold;
                TotalBelowTruckMeters = totalBelowTruckMeters;
                MonstersAddedSeconds = monstersAddedSeconds;
                LongestShortestRoomPath = longestShortestRoomPath;
            }

            internal int RequiredPlayers { get; }
            internal int LevelNumber { get; }
            internal int AliveSearchMonsters { get; }
            internal float FarthestDistanceMeters { get; }
            internal int PlayersBelowTruckThreshold { get; }
            internal float TotalBelowTruckMeters { get; }
            internal float MonstersAddedSeconds { get; }
            internal int LongestShortestRoomPath { get; }
        }

        internal static bool IsActive => s_active;

        internal static void OnLevelLoaded()
        {
            ClearSurrenderState();

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
            UpdateSurrenderInput();
            UpdateIndicators(maxPlayers);

            if (CheckSurrenderFailure(maxPlayers))
            {
                return;
            }

            DebugTruckState();

            if (AllHeadsInTruck())
            {
                HandleSuccess();
                return;
            }

            if (s_timerRemaining <= 0f)
            {
                HandleTimeout();
            }
        }

        private static void StartTimer(int maxPlayers)
        {
            s_active = true;
            s_timerRemaining = GetInitialTimerSeconds(maxPlayers);
            s_currencyCaptured = false;
            CaptureBaseCurrency();
            LastChanceSurrenderNetwork.EnsureCreated();
            LastChanceTimerUI.Show(SurrenderHintPrompt);
            s_jumpDistanceLogged = false;

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
            ClearSurrenderState();

            if (!s_active)
            {
                return;
            }

            s_active = false;
            s_currencyCaptured = false;
            s_timerRemaining = 0f;
            LastChanceTimerUI.Hide();
            AllPlayersDeadGuard.ResetVanillaAllPlayersDead();
            LastChanceSpectateHelper.ResetForceState();
            LastChanceSaveDeleteState.ResetAutoDeleteBlock();
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

        private static void UpdateSurrenderInput()
        {
            if (!s_active || !AllPlayersDeadGuard.AllPlayersDisabled())
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

        private static void ClearSurrenderState()
        {
            LastChanceSurrenderedPlayers.Clear();
            s_surrenderHoldTimer = 0f;
            s_localSurrendered = false;
            LastChanceTimerUI.ResetSurrenderHint();
            ClearIndicatorsState();
        }

        private static void UpdateIndicators(int maxPlayers)
        {
            var mode = GetIndicatorMode();
            if (!s_active || mode == LastChanceIndicatorMode.None || !AllPlayersDeadGuard.AllPlayersDisabled())
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.Indicator.Blocked", 5))
                {
                    var rawMode = FeatureFlags.LastChanceIndicators ?? string.Empty;
                    Debug.Log($"[LastChance] Indicator blocked: active={s_active} allDead={AllPlayersDeadGuard.AllPlayersDisabled()} modeRaw='{rawMode}' modeParsed={mode}");
                }
                ClearActiveIndicatorVisuals();
                return;
            }

            var directionEnabled = mode == LastChanceIndicatorMode.Direction || mode == LastChanceIndicatorMode.All;
            var mapEnabled = mode == LastChanceIndicatorMode.Map || mode == LastChanceIndicatorMode.All;

            UpdateSingleIndicator(IndicatorKind.Direction, directionEnabled, InputKey.Tumble, maxPlayers);
            UpdateSingleIndicator(IndicatorKind.Map, mapEnabled, InputKey.Rotate, maxPlayers);
        }

        private static LastChanceIndicatorMode GetIndicatorMode()
        {
            var raw = (FeatureFlags.LastChanceIndicators ?? string.Empty).Trim();
            if (raw.Equals("Direction", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceIndicatorMode.Direction;
            }

            if (raw.Equals("Map", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceIndicatorMode.Map;
            }

            if (raw.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceIndicatorMode.All;
            }

            if (raw.Equals("Both", StringComparison.OrdinalIgnoreCase))
            {
                return LastChanceIndicatorMode.All;
            }

            return LastChanceIndicatorMode.None;
        }

        private static void UpdateSingleIndicator(IndicatorKind kind, bool enabled, InputKey inputKey, int maxPlayers)
        {
            if (!enabled)
            {
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
                return;
            }

            if (!SemiFunc.InputDown(inputKey))
            {
                return;
            }

            TriggerIndicator(kind, maxPlayers);
        }

        private static void TriggerIndicator(IndicatorKind kind, int maxPlayers)
        {
            var duration = Mathf.Clamp(FeatureFlags.LastChanceIndicatorDurationSeconds, 0.5f, 10f);
            var cooldown = Mathf.Clamp(FeatureFlags.LastChanceIndicatorCooldownSeconds, 1f, 30f);
            var activeUntil = Time.time + duration;
            SetIndicatorActive(kind, true);
            SetIndicatorActiveUntil(kind, activeUntil);
            SetIndicatorCooldownUntil(kind, activeUntil + cooldown);
            if (kind == IndicatorKind.Map)
            {
                s_mapUiRetryAt = 0f;
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog($"{IndicatorCooldownLogKey}.Start.{kind}", 2))
            {
                Debug.Log($"[LastChance] Indicator cooldown started: kind={kind} duration={duration:F1}s cooldown={cooldown:F1}s");
            }

            ApplyIndicatorPenalty(kind, maxPlayers);
            TickActiveIndicator(kind);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(IndicatorLogKey, 3))
            {
                var remainingCooldown = Mathf.Max(0f, GetIndicatorCooldownUntil(kind) - Time.time);
                Debug.Log($"[LastChance] Indicator triggered: mode={kind} active={duration:F1}s cooldown={remainingCooldown:F1}s timer={s_timerRemaining:F1}s");
            }
        }

        private static void ApplyIndicatorPenalty(IndicatorKind kind, int maxPlayers)
        {
            var inputs = CollectDynamicTimerInputs(maxPlayers);
            var difficulty = EstimateDifficulty01(inputs);
            float easyPenalty;
            float hardPenalty;

            if (kind == IndicatorKind.Direction)
            {
                easyPenalty = Mathf.Max(0f, FeatureFlags.LastChanceIndicatorDirectionPenaltyEasySeconds);
                hardPenalty = Mathf.Max(0f, FeatureFlags.LastChanceIndicatorDirectionPenaltyHardSeconds);
            }
            else
            {
                easyPenalty = Mathf.Max(0f, FeatureFlags.LastChanceIndicatorMapPenaltyEasySeconds);
                hardPenalty = Mathf.Max(0f, FeatureFlags.LastChanceIndicatorMapPenaltyHardSeconds);
            }

            var maxPenalty = Mathf.Max(easyPenalty, hardPenalty);
            var minPenalty = Mathf.Min(easyPenalty, hardPenalty);
            var penalty = Mathf.Lerp(maxPenalty, minPenalty, difficulty);
            if (penalty <= 0f)
            {
                return;
            }

            s_timerRemaining = Mathf.Max(0f, s_timerRemaining - penalty);
            LastChanceTimerUI.UpdateText(FormatTimerText(s_timerRemaining));
        }

        private static float EstimateDifficulty01(DynamicTimerInputs inputs)
        {
            var levelFactor = Mathf.Clamp01((inputs.LevelNumber - 1f) / 20f);
            var distanceFactor = Mathf.Clamp01(inputs.FarthestDistanceMeters / 180f);
            var roomFactor = Mathf.Clamp01(inputs.LongestShortestRoomPath / 14f);
            var altitudeFactor = Mathf.Clamp01(inputs.TotalBelowTruckMeters / 25f);
            var monsterFactor = Mathf.Clamp01(inputs.AliveSearchMonsters / 10f);
            var weighted = levelFactor * 0.2f + distanceFactor * 0.35f + roomFactor * 0.2f + altitudeFactor * 0.1f + monsterFactor * 0.15f;
            return Mathf.Clamp01(weighted);
        }

        private static void TickActiveIndicator(IndicatorKind kind)
        {
            if (kind == IndicatorKind.Map)
            {
                if (Time.time < s_mapUiRetryAt)
                {
                    return;
                }

                if (Map.Instance != null)
                {
                    Map.Instance.ActiveSet(true);
                    s_indicatorForcedMap = true;

                    var mapTexture = TryGetMapScreenTexture(out var textureDebug);
                    var mapUiShown = LastChanceMapIndicatorUI.Show(mapTexture, FeatureFlags.DebugLogging);
                    if (mapUiShown)
                    {
                        s_mapUiRetryAt = Time.time + 0.5f;
                        if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.Indicator.Map.UI.Shown", 2))
                        {
                            Debug.Log($"[LastChance] Indicator Map UI: shown=True textureInfo={textureDebug}");
                        }
                    }
                    else
                    {
                        s_mapUiRetryAt = Time.time + 0.2f;
                        if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.Indicator.Map.UI.Missing", 1))
                        {
                            Debug.LogWarning($"[LastChance] Indicator Map UI missing texture. info={textureDebug} mapActive={Map.Instance.Active}");
                        }
                    }
                }
                else if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.Indicator.Map.Missing", 5))
                {
                    Debug.LogWarning("[LastChance] Indicator Map: Map.Instance is null.");
                }
                return;
            }

            if (kind == IndicatorKind.Direction)
            {
                EnsureDirectionLine();
                UpdateDirectionPath(force: Time.time >= s_indicatorNextPathRefreshAt);
            }
        }

        private static void DeactivateIndicator(IndicatorKind kind)
        {
            SetIndicatorActive(kind, false);
            if (kind == IndicatorKind.Map && s_indicatorForcedMap && Map.Instance != null)
            {
                var mapToolActive = false;
                if (MapToolController.instance != null)
                {
                    var activeField = AccessTools.Field(MapToolController.instance.GetType(), "Active");
                    if (activeField != null && activeField.GetValue(MapToolController.instance) is bool active)
                    {
                        mapToolActive = active;
                    }
                }
                if (!mapToolActive)
                {
                    Map.Instance.ActiveSet(false);
                }
                s_indicatorForcedMap = false;
                s_mapUiRetryAt = 0f;
            }
            if (kind == IndicatorKind.Map)
            {
                LastChanceMapIndicatorUI.Hide();
            }

            if (kind == IndicatorKind.Direction && s_indicatorDirectionLine != null)
            {
                s_indicatorDirectionLine.positionCount = 0;
                s_indicatorDirectionLine.enabled = false;
            }
        }

        private static bool IsIndicatorActive(IndicatorKind kind)
        {
            return kind == IndicatorKind.Direction ? s_directionActive : s_mapActive;
        }

        private static void SetIndicatorActive(IndicatorKind kind, bool value)
        {
            if (kind == IndicatorKind.Direction)
            {
                s_directionActive = value;
                return;
            }

            s_mapActive = value;
        }

        private static float GetIndicatorActiveUntil(IndicatorKind kind)
        {
            return kind == IndicatorKind.Direction ? s_directionActiveUntil : s_mapActiveUntil;
        }

        private static void SetIndicatorActiveUntil(IndicatorKind kind, float value)
        {
            if (kind == IndicatorKind.Direction)
            {
                s_directionActiveUntil = value;
                return;
            }

            s_mapActiveUntil = value;
        }

        private static float GetIndicatorCooldownUntil(IndicatorKind kind)
        {
            return kind == IndicatorKind.Direction ? s_directionCooldownUntil : s_mapCooldownUntil;
        }

        private static void SetIndicatorCooldownUntil(IndicatorKind kind, float value)
        {
            if (kind == IndicatorKind.Direction)
            {
                s_directionCooldownUntil = value;
                return;
            }

            s_mapCooldownUntil = value;
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
            }

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(3.2f, 3.4f, 0.25f), 0f),
                    new GradientColorKey(new Color(2.0f, 2.2f, 0.18f), 0.65f),
                    new GradientColorKey(new Color(1.3f, 1.4f, 0.12f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.98f, 0.35f),
                    new GradientAlphaKey(0.9f, 1f)
                });
            s_indicatorDirectionLine.colorGradient = gradient;
            s_indicatorDirectionLine.startColor = new Color(3.2f, 3.4f, 0.25f, 1f);
            s_indicatorDirectionLine.endColor = new Color(1.3f, 1.4f, 0.12f, 0.95f);

            var mat = s_indicatorDirectionLine.material;
            if (mat != null)
            {
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(3.8f, 4.0f, 0.3f, 1f));
                }
                else if (mat.HasProperty("_TintColor"))
                {
                    mat.SetColor("_TintColor", new Color(2.5f, 2.7f, 0.25f, 1f));
                }
            }
            s_indicatorNextPathRefreshAt = 0f;
        }

        private static bool TryApplyPhysGrabBeamMaterial(LineRenderer lineRenderer)
        {
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

            var source = physGrabber.physGrabBeam.GetComponent<LineRenderer>();
            if (source == null || source.material == null)
            {
                return false;
            }

            lineRenderer.material = source.material;
            lineRenderer.textureMode = source.textureMode;
            return true;
        }

        private static void UpdateDirectionPath(bool force)
        {
            if (!force)
            {
                return;
            }

            s_indicatorNextPathRefreshAt = Time.time + 0.2f;
            if (s_indicatorDirectionLine == null)
            {
                return;
            }

            if (!TryBuildPathToTruck(out var pathPoints))
            {
                s_indicatorDirectionLine.positionCount = 0;
                return;
            }

            s_indicatorDirectionLine.positionCount = pathPoints.Length;
            s_indicatorDirectionLine.SetPositions(pathPoints);
        }

        private static bool TryBuildPathToTruck(out Vector3[] points)
        {
            points = Array.Empty<Vector3>();
            var localAvatar = PlayerAvatar.instance;
            if (localAvatar == null)
            {
                return false;
            }

            var localPos = GetLocalHeadOrPlayerPosition(localAvatar);
            if (!TryGetTruckPosition(out var truckPos))
            {
                return false;
            }

            if (!TrySampleNavMeshPosition(localPos, 12f, out var from))
            {
                from = localPos;
            }

            if (!TrySampleNavMeshPosition(truckPos, 8f, out var to))
            {
                to = truckPos;
            }

            if (!TryCalculateNavMeshPathCorners(from, to, out var corners) || corners.Length == 0)
            {
                points = new[] { localPos, truckPos };
                return true;
            }

            var results = new List<Vector3>(corners.Length + 1) { localPos };
            for (var i = 0; i < corners.Length; i++)
            {
                results.Add(corners[i] + Vector3.up * 0.05f);
            }

            points = results.ToArray();
            return points.Length >= 2;
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

            var navHit = Activator.CreateInstance(s_navMeshHitType);
            if (navHit == null)
            {
                return false;
            }

            var args = new object[] { source, navHit, maxDistance, -1 };
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

            var path = Activator.CreateInstance(s_navMeshPathType);
            if (path == null)
            {
                return false;
            }

            var args = new object[] { from, to, -1, path };
            if (s_navMeshCalculatePathMethod.Invoke(null, args) is not bool success || !success)
            {
                return false;
            }

            if (s_navMeshPathCornersProperty.GetValue(path) is Vector3[] pathCorners && pathCorners.Length > 0)
            {
                corners = pathCorners;
                return true;
            }

            return false;
        }

        private static void ClearActiveIndicatorVisuals()
        {
            DeactivateIndicator(IndicatorKind.Map);
            DeactivateIndicator(IndicatorKind.Direction);
        }

        private static Texture? TryGetMapScreenTexture(out string debug)
        {
            debug = "none";
            var mapTool = MapToolController.instance;
            if (mapTool == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.Indicator.Map.MapToolMissing", 4))
                {
                    Debug.Log("[LastChance] Indicator Map source: MapToolController.instance is null, using fallback render path.");
                }
                return TryGetFallbackMapTexture(out debug);
            }

            var displayMesh = mapTool.DisplayMesh;
            if (displayMesh == null)
            {
                debug = "DisplayMesh=null";
                return null;
            }

            var mat = displayMesh.material;
            if (mat == null)
            {
                debug = "DisplayMesh.material=null";
                return null;
            }

            if (mat.mainTexture != null)
            {
                debug = $"material={mat.name} texture={mat.mainTexture.name}";
                return mat.mainTexture;
            }

            var shared = displayMesh.sharedMaterial;
            if (shared?.mainTexture != null)
            {
                debug = $"sharedMaterial={shared.name} texture={shared.mainTexture.name}";
                return shared.mainTexture;
            }

            return TryGetFallbackMapTexture(out debug);
        }

        private static Texture? TryGetFallbackMapTexture(out string debug)
        {
            debug = "fallback:none";
            var map = Map.Instance;
            if (map == null)
            {
                debug = "fallback:Map.Instance null";
                return null;
            }

            var root = map.ActiveParent;
            if (root == null)
            {
                root = map.gameObject;
            }
            if (root == null)
            {
                debug = "fallback:root null";
                return null;
            }

            if (s_mapIndicatorRenderTexture == null)
            {
                s_mapIndicatorRenderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
                {
                    name = "DHHFix.LastChanceMapRT"
                };
            }

            if (s_mapIndicatorCamera == null)
            {
                var go = new GameObject("DHHFix.LastChanceMapCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                s_mapIndicatorCamera = go.AddComponent<Camera>();
                s_mapIndicatorCamera.enabled = false;
                s_mapIndicatorCamera.orthographic = true;
                s_mapIndicatorCamera.clearFlags = CameraClearFlags.SolidColor;
                s_mapIndicatorCamera.backgroundColor = new Color(0.02f, 0.12f, 0.06f, 1f);
                s_mapIndicatorCamera.allowHDR = false;
                s_mapIndicatorCamera.allowMSAA = false;
                s_mapIndicatorCamera.nearClipPlane = 0.1f;
                s_mapIndicatorCamera.farClipPlane = 1000f;
                s_mapIndicatorCamera.cullingMask = ~0;
            }

            Bounds bounds;
            if (!TryGetMapBounds(root, out bounds, out var rendererCount))
            {
                var center = map.OverLayerParent != null ? map.OverLayerParent.position : root.transform.position;
                bounds = new Bounds(center, new Vector3(30f, 10f, 30f));
                rendererCount = 0;
            }

            var maxPlanar = Mathf.Max(bounds.size.x, bounds.size.z);
            var height = Mathf.Max(20f, maxPlanar + 10f);
            var camPos = bounds.center + Vector3.up * height;
            s_mapIndicatorCamera.transform.position = camPos;
            s_mapIndicatorCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            s_mapIndicatorCamera.orthographicSize = Mathf.Max(8f, maxPlanar * 0.6f);
            s_mapIndicatorCamera.targetTexture = s_mapIndicatorRenderTexture;
            s_mapIndicatorCamera.Render();

            debug = $"fallback:rt={s_mapIndicatorRenderTexture.width}x{s_mapIndicatorRenderTexture.height} renderers={rendererCount} root='{root.name}' activeSelf={root.activeSelf} activeInHierarchy={root.activeInHierarchy} bounds={bounds.size.x:F1}x{bounds.size.z:F1}";
            return s_mapIndicatorRenderTexture;
        }

        private static bool TryGetMapBounds(GameObject root, out Bounds bounds, out int rendererCount)
        {
            bounds = default;
            rendererCount = 0;
            if (root == null)
            {
                return false;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }
                rendererCount++;

                if (bounds.size == Vector3.zero)
                {
                    bounds = r.bounds;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return bounds.size != Vector3.zero;
        }

        private static void ClearIndicatorsState()
        {
            s_directionCooldownUntil = 0f;
            s_directionActiveUntil = 0f;
            s_directionActive = false;
            s_mapCooldownUntil = 0f;
            s_mapActiveUntil = 0f;
            s_mapActive = false;
            s_mapUiRetryAt = 0f;
            s_indicatorNextPathRefreshAt = 0f;
            ClearActiveIndicatorVisuals();
        }

        private static void DebugTruckState()
        {
            if (!FeatureFlags.DebugLogging || !FeatureFlags.LastChangeMode || !FeatureFlags.BatteryJumpEnabled)
            {
                return;
            }

            if (!AllPlayersDeadGuard.AllPlayersDisabled())
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
                return Mathf.Clamp(baseSeconds, 30f, maxSeconds);
            }

            var inputs = CollectDynamicTimerInputs(maxPlayers);
            var rawAddedSeconds = CalculateRawAddedSeconds(inputs);
            var levelCurveMultiplier = GetLevelCurveMultiplier(inputs.LevelNumber);
            var levelScaledAddedSeconds = rawAddedSeconds * levelCurveMultiplier;
            var reducedAddedSeconds = ApplyDiminishing(levelScaledAddedSeconds);
            var finalSeconds = Mathf.Clamp(baseSeconds + reducedAddedSeconds, 30f, maxSeconds);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.DynamicTimer", 30))
            {
                Debug.Log(
                    $"[LastChance] DynamicTimer: base={baseSeconds:F1}s level={inputs.LevelNumber} required={inputs.RequiredPlayers} " +
                    $"farthest={inputs.FarthestDistanceMeters:F1}m belowPlayers={inputs.PlayersBelowTruckThreshold} belowMeters={inputs.TotalBelowTruckMeters:F2} " +
                    $"aliveMonsters={inputs.AliveSearchMonsters} monstersAdd={inputs.MonstersAddedSeconds:F1}s " +
                    $"maxRoomPath={inputs.LongestShortestRoomPath} rawAdd={rawAddedSeconds:F1}s levelCurve={levelCurveMultiplier:F3} " +
                    $"scaledAdd={levelScaledAddedSeconds:F1}s reducedAdd={reducedAddedSeconds:F1}s final={finalSeconds:F1}s cap={maxSeconds:F1}s");
            }

            return finalSeconds;
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
            var monstersAddedSeconds = CalculateMonsterAddedSeconds(aliveSearchMonsters);
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
                    monstersAddedSeconds,
                    0);
            }

            var selected = SelectRequiredPlayers(records, requiredPlayers);
            var belowThreshold = Mathf.Min(0f, FeatureFlags.LastChanceBelowTruckThresholdMeters);
            var farthestDistanceMeters = 0f;
            var playersBelowTruckThreshold = 0;
            var totalBelowTruckMeters = 0f;
            var longestShortestRoomPath = 0;

            for (var i = 0; i < selected.Count; i++)
            {
                var record = selected[i];
                if (record.HasValidPath && record.NavMeshDistance >= 0f)
                {
                    farthestDistanceMeters = Mathf.Max(farthestDistanceMeters, record.NavMeshDistance);
                }

                if (record.ShortestRoomPathToTruck >= 0)
                {
                    longestShortestRoomPath = Mathf.Max(longestShortestRoomPath, record.ShortestRoomPathToTruck);
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
                farthestDistanceMeters,
                playersBelowTruckThreshold,
                totalBelowTruckMeters,
                monstersAddedSeconds,
                longestShortestRoomPath);
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

            sorted.Sort((left, right) => ScoreTimerDifficulty(left).CompareTo(ScoreTimerDifficulty(right)));
            if (FeatureFlags.LastChanceTimerUseHardestRequiredPlayers)
            {
                sorted.Reverse();
            }
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
            added += inputs.LevelNumber * FeatureFlags.LastChanceTimerPerLevelSeconds;
            added += inputs.FarthestDistanceMeters * FeatureFlags.LastChanceTimerPerFarthestMeterSeconds;
            added += inputs.PlayersBelowTruckThreshold * FeatureFlags.LastChanceTimerPerBelowTruckPlayerSeconds;
            added += inputs.TotalBelowTruckMeters * FeatureFlags.LastChanceTimerPerBelowTruckMeterSeconds;
            added += inputs.LongestShortestRoomPath * FeatureFlags.LastChanceTimerPerRoomStepSeconds;
            added += inputs.MonstersAddedSeconds;
            return Mathf.Max(0f, added);
        }

        private static float CalculateMonsterAddedSeconds(int aliveSearchMonsters)
        {
            if (!FeatureFlags.LastChanceMonstersSearchEnabled || aliveSearchMonsters <= 0)
            {
                return 0f;
            }

            var raw = aliveSearchMonsters * Mathf.Max(0f, FeatureFlags.LastChanceTimerPerMonsterSeconds);
            var start = Mathf.Max(0f, FeatureFlags.LastChanceTimerMonsterDiminishStart);
            var range = Mathf.Max(1f, FeatureFlags.LastChanceTimerMonsterDiminishRange);
            var reduction = Mathf.Clamp01(FeatureFlags.LastChanceTimerMonsterDiminishReduction);
            return ApplyDiminishing(raw, start, range, reduction);
        }

        private static float ApplyDiminishing(float rawAddedSeconds)
        {
            var start = Mathf.Max(0f, FeatureFlags.LastChanceDynamicDiminishStartSeconds);
            var range = Mathf.Max(1f, FeatureFlags.LastChanceDynamicDiminishRangeSeconds);
            var reduction = Mathf.Clamp01(FeatureFlags.LastChanceDynamicDiminishReduction);
            return ApplyDiminishing(rawAddedSeconds, start, range, reduction);
        }

        private static float ApplyDiminishing(float rawAddedSeconds, float start, float range, float reduction)
        {
            if (rawAddedSeconds <= 0f)
            {
                return 0f;
            }

            if (rawAddedSeconds <= start || reduction <= 0f)
            {
                return rawAddedSeconds;
            }

            var overflow = rawAddedSeconds - start;
            var compressedOverflow = (overflow * range) / (overflow + range);
            var reducedOverflow = Mathf.Lerp(overflow, compressedOverflow, reduction);
            return start + reducedOverflow;
        }

        private static float GetLevelCurveMultiplier(int levelNumber)
        {
            if (!FeatureFlags.LastChanceLevelCurveEnabled)
            {
                return 1f;
            }

            var minMultiplier = Mathf.Clamp(FeatureFlags.LastChanceLevelCurveMinMultiplier, 0.01f, 1f);
            var maxMultiplier = Mathf.Clamp(FeatureFlags.LastChanceLevelCurveMaxMultiplier, minMultiplier, 3f);
            var exponent = Mathf.Clamp(FeatureFlags.LastChanceLevelCurveExponent, 0.1f, 5f);
            var fullGrowthLevel = Mathf.Max(2, FeatureFlags.LastChanceLevelCurveFullGrowthLevel);

            var normalized = Mathf.Clamp01((Mathf.Max(1, levelNumber) - 1f) / (fullGrowthLevel - 1f));
            var curved = Mathf.Pow(normalized, exponent);
            return Mathf.Lerp(minMultiplier, maxMultiplier, curved);
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


