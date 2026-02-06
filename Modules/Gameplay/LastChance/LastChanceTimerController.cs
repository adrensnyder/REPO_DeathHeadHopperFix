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
        
        private readonly struct DynamicTimerInputs
        {
            internal DynamicTimerInputs(
                int requiredPlayers,
                int levelNumber,
                float farthestDistanceMeters,
                int playersBelowTruckThreshold,
                float totalBelowTruckMeters,
                int longestShortestRoomPath)
            {
                RequiredPlayers = requiredPlayers;
                LevelNumber = levelNumber;
                FarthestDistanceMeters = farthestDistanceMeters;
                PlayersBelowTruckThreshold = playersBelowTruckThreshold;
                TotalBelowTruckMeters = totalBelowTruckMeters;
                LongestShortestRoomPath = longestShortestRoomPath;
            }

            internal int RequiredPlayers { get; }
            internal int LevelNumber { get; }
            internal float FarthestDistanceMeters { get; }
            internal int PlayersBelowTruckThreshold { get; }
            internal float TotalBelowTruckMeters { get; }
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
            var reducedAddedSeconds = ApplyDiminishing(rawAddedSeconds);
            var finalSeconds = Mathf.Clamp(baseSeconds + reducedAddedSeconds, 30f, maxSeconds);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.DynamicTimer", 30))
            {
                Debug.Log(
                    $"[LastChance] DynamicTimer: base={baseSeconds:F1}s level={inputs.LevelNumber} required={inputs.RequiredPlayers} " +
                    $"farthest={inputs.FarthestDistanceMeters:F1}m belowPlayers={inputs.PlayersBelowTruckThreshold} belowMeters={inputs.TotalBelowTruckMeters:F2} " +
                    $"maxRoomPath={inputs.LongestShortestRoomPath} rawAdd={rawAddedSeconds:F1}s reducedAdd={reducedAddedSeconds:F1}s final={finalSeconds:F1}s cap={maxSeconds:F1}s");
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
            var records = PlayerTruckDistanceHelper.GetDistancesFromTruck();
            if (records.Length == 0)
            {
                return new DynamicTimerInputs(
                    requiredPlayers,
                    levelNumber,
                    0f,
                    0,
                    0f,
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
                farthestDistanceMeters,
                playersBelowTruckThreshold,
                totalBelowTruckMeters,
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
            return Mathf.Max(0f, added);
        }

        private static float ApplyDiminishing(float rawAddedSeconds)
        {
            if (rawAddedSeconds <= 0f)
            {
                return 0f;
            }

            var start = Mathf.Max(0f, FeatureFlags.LastChanceDynamicDiminishStartSeconds);
            if (rawAddedSeconds <= start)
            {
                return rawAddedSeconds;
            }

            var reduction = Mathf.Clamp01(FeatureFlags.LastChanceDynamicDiminishReduction);
            if (reduction <= 0f)
            {
                return rawAddedSeconds;
            }

            var range = Mathf.Max(1f, FeatureFlags.LastChanceDynamicDiminishRangeSeconds);
            var overflow = rawAddedSeconds - start;
            var compressedOverflow = (overflow * range) / (overflow + range);
            var reducedOverflow = Mathf.Lerp(overflow, compressedOverflow, reduction);
            return start + reducedOverflow;
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


