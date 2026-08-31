#nullable enable

using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DeathHeadHopper.DeathHead;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using Photon.Pun;
using REPOLib.Modules;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    internal static class DhhRepolibUpgradeBridge
    {
        internal const string HeadChargeUpgradeId = "HeadCharge";
        internal const string HeadPowerUpgradeId = "HeadPower";

        private const string HeadChargeStatsKey = "playerUpgradeHeadCharge";
        private const string HeadPowerStatsKey = "playerUpgradeHeadPower";
        private const string TracePrefix = "[Fix:DHHUpgrade]";

        private static ManualLogSource? _log;
        private static PlayerUpgrade? _headChargeUpgrade;
        private static PlayerUpgrade? _headPowerUpgrade;
        private static readonly HashSet<string> CollisionLogs = new(StringComparer.Ordinal);

        [ThreadStatic]
        private static List<SetLevelFrame>? _setLevelFrames;

        [ThreadStatic]
        private static List<ApplyTransitionFrame>? _applyTransitionFrames;

        internal enum UpgradeKind
        {
            HeadCharge,
            HeadPower
        }

        internal enum TransitionOrigin
        {
            SetLevel,
            DirectApplyUpgrade
        }

        internal sealed class SetLevelFrame
        {
            internal SetLevelFrame(PlayerUpgrade upgrade, string steamId, int previousLevel)
            {
                Upgrade = upgrade;
                SteamId = steamId;
                PreviousLevel = previousLevel;
            }

            internal PlayerUpgrade Upgrade { get; }
            internal string SteamId { get; }
            internal int PreviousLevel { get; }
            internal bool ApplyCaptured { get; set; }
        }

        internal sealed class ApplyTransitionFrame
        {
            internal ApplyTransitionFrame(
                PlayerUpgrade upgrade,
                UpgradeKind kind,
                string steamId,
                int previousLevel,
                int newLevel,
                TransitionOrigin origin)
            {
                Upgrade = upgrade;
                Kind = kind;
                SteamId = steamId;
                PreviousLevel = previousLevel;
                NewLevel = newLevel;
                Origin = origin;
            }

            internal PlayerUpgrade Upgrade { get; }
            internal UpgradeKind Kind { get; }
            internal string SteamId { get; }
            internal int PreviousLevel { get; }
            internal int NewLevel { get; }
            internal TransitionOrigin Origin { get; }
        }

        internal static void Initialize(ManualLogSource? log)
        {
            _log ??= log;
        }

        internal static bool RegisterOrBindUpgrade(string upgradeId, Item item, bool isChargeUpgrade)
        {
            if (item == null)
                return false;

            var kind = isChargeUpgrade ? UpgradeKind.HeadCharge : UpgradeKind.HeadPower;
            if (!string.Equals(upgradeId, GetUpgradeId(kind), StringComparison.Ordinal))
            {
                _log?.LogError($"{TracePrefix} Registration rejected for unexpected upgrade id '{upgradeId}' ({kind}).");
                return false;
            }

            var owned = GetOwnedUpgrade(kind);
            var existing = Upgrades.GetUpgrade(upgradeId);

            if (owned == null)
            {
                if (existing != null)
                {
                    LogRegistrationCollision(upgradeId, "a registration already exists before DHHFix registered its callbacks");
                    return false;
                }

                owned = isChargeUpgrade
                    ? Upgrades.RegisterUpgrade(upgradeId, item, HeadChargeStartAction, HeadChargeUpgradeAction)
                    : Upgrades.RegisterUpgrade(upgradeId, item, HeadPowerStartAction, HeadPowerUpgradeAction);

                if (owned == null)
                {
                    _log?.LogError($"{TracePrefix} Failed to register REPOLib upgrade '{upgradeId}' with DHHFix callbacks.");
                    return false;
                }

                SetOwnedUpgrade(kind, owned);
                DebugLog($"registration upgrade={upgradeId} result=created item='{item.name}'");
            }
            else
            {
                if (existing == null || !ReferenceEquals(existing, owned))
                {
                    LogRegistrationCollision(upgradeId, "the REPOLib registration no longer matches the instance owned by DHHFix");
                    return false;
                }

                DebugLog($"registration upgrade={upgradeId} result=owned-existing item='{item.name}'");
            }

            var dhhStats = DHHStatsManager.instance;
            if (dhhStats == null)
            {
                DebugLog($"binding upgrade={upgradeId} result=deferred reason=dhh-stats-unavailable");
                return true;
            }

            BindUpgrade(kind, owned, dhhStats, "registration");
            return true;
        }

        internal static bool TryGetOwnedUpgrade(string upgradeId, out PlayerUpgrade? upgrade, out string failureReason)
        {
            upgrade = null;
            failureReason = string.Empty;

            UpgradeKind kind;
            if (string.Equals(upgradeId, HeadChargeUpgradeId, StringComparison.Ordinal))
                kind = UpgradeKind.HeadCharge;
            else if (string.Equals(upgradeId, HeadPowerUpgradeId, StringComparison.Ordinal))
                kind = UpgradeKind.HeadPower;
            else
            {
                failureReason = $"unexpected-upgrade-id:{upgradeId}";
                return false;
            }

            var owned = GetOwnedUpgrade(kind);
            if (owned == null)
            {
                failureReason = "dhhfix-registration-unavailable";
                return false;
            }

            var registered = Upgrades.GetUpgrade(upgradeId);
            if (registered == null)
            {
                failureReason = "repolib-registration-missing";
                return false;
            }

            if (!ReferenceEquals(registered, owned))
            {
                failureReason = "repolib-registration-not-owned-by-dhhfix";
                LogRegistrationCollision(upgradeId, "the active REPOLib registration does not match the instance owned by DHHFix");
                return false;
            }

            upgrade = owned;
            return true;
        }

        internal static void LogWarning(string message)
        {
            try
            {
                _log?.LogWarning($"{TracePrefix} {message}");
            }
            catch
            {
                // Logging must never alter upgrade ownership or synchronization behavior.
            }
        }

        internal static void LogError(string message)
        {
            try
            {
                _log?.LogError($"{TracePrefix} {message}");
            }
            catch
            {
                // Logging must never alter upgrade ownership or synchronization behavior.
            }
        }

        internal static void BindRegisteredUpgrades(DHHStatsManager? dhhStats, string context)
        {
            if (dhhStats == null)
            {
                DebugLog($"binding context={context} result=deferred reason=dhh-stats-unavailable");
                return;
            }

            if (_headChargeUpgrade != null)
                BindUpgrade(UpgradeKind.HeadCharge, _headChargeUpgrade, dhhStats, context);
            else
                DebugLog($"binding upgrade={HeadChargeUpgradeId} context={context} result=deferred reason=registration-unavailable");

            if (_headPowerUpgrade != null)
                BindUpgrade(UpgradeKind.HeadPower, _headPowerUpgrade, dhhStats, context);
            else
                DebugLog($"binding upgrade={HeadPowerUpgradeId} context={context} result=deferred reason=registration-unavailable");
        }

        internal static void VerifyDictionaryInvariant(string context, bool warnOnMismatch = true)
        {
            var dhhStats = DHHStatsManager.instance;
            var statsManager = StatsManager.instance;
            if (dhhStats == null || statsManager?.dictionaryOfDictionaries == null)
            {
                DebugLog(
                    $"dictionary-invariant context={context} result=pending " +
                    $"dhhStatsAvailable={dhhStats != null} statsManagerAvailable={statsManager != null}");
                return;
            }

            VerifyDictionaryInvariant(
                context,
                UpgradeKind.HeadCharge,
                HeadChargeStatsKey,
                dhhStats.playerUpgradeHeadCharge,
                _headChargeUpgrade,
                warnOnMismatch);

            VerifyDictionaryInvariant(
                context,
                UpgradeKind.HeadPower,
                HeadPowerStatsKey,
                dhhStats.playerUpgradeHeadPower,
                _headPowerUpgrade,
                warnOnMismatch);
        }

        internal static SetLevelFrame? BeginSetLevel(PlayerUpgrade? upgrade, string steamId)
        {
            if (upgrade == null || string.IsNullOrWhiteSpace(steamId) || !TryGetUpgradeKind(upgrade, out _))
                return null;

            var frame = new SetLevelFrame(upgrade, steamId, upgrade.GetLevel(steamId));
            (_setLevelFrames ??= new List<SetLevelFrame>()).Add(frame);

            if (FeatureFlags.DebugLogging)
            {
                DebugLog(
                    $"set-level-capture upgrade={upgrade.UpgradeId} target={steamId} " +
                    $"old={frame.PreviousLevel} role={GetRuntimeRole()}");
            }

            return frame;
        }

        internal static void EndSetLevel(SetLevelFrame? frame)
        {
            RemoveFrame(_setLevelFrames, frame, "SetLevel");
        }

        internal static ApplyTransitionFrame? BeginApplyUpgrade(PlayerUpgrade? upgrade, string steamId, int newLevel)
        {
            if (upgrade == null || string.IsNullOrWhiteSpace(steamId) || !TryGetUpgradeKind(upgrade, out var kind))
                return null;

            var setLevelFrame = FindMatchingSetLevelFrame(upgrade, steamId);
            var previousLevel = setLevelFrame?.PreviousLevel ?? upgrade.GetLevel(steamId);
            var origin = setLevelFrame != null ? TransitionOrigin.SetLevel : TransitionOrigin.DirectApplyUpgrade;
            if (setLevelFrame != null)
                setLevelFrame.ApplyCaptured = true;
            var frame = new ApplyTransitionFrame(upgrade, kind, steamId, previousLevel, newLevel, origin);
            (_applyTransitionFrames ??= new List<ApplyTransitionFrame>()).Add(frame);

            if (FeatureFlags.DebugLogging)
            {
                DebugLog(
                    $"transition upgrade={upgrade.UpgradeId} target={steamId} old={previousLevel} new={newLevel} " +
                    $"type={ClassifyTransition(previousLevel, newLevel)} origin={origin} role={GetRuntimeRole()} " +
                    $"avatarAvailable={TryResolvePlayerAvatar(steamId) != null}");
            }

            return frame;
        }

        internal static void EndApplyUpgrade(ApplyTransitionFrame? frame)
        {
            RemoveFrame(_applyTransitionFrames, frame, "ApplyUpgrade");
        }

        internal static bool IsManagedCompatibilityUpdate(UpgradeKind kind, string steamId, int level)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                return false;

            var frames = _applyTransitionFrames;
            if (frames == null)
                return false;

            for (var index = frames.Count - 1; index >= 0; index--)
            {
                var candidate = frames[index];
                if (candidate.Kind == kind &&
                    string.Equals(candidate.SteamId, steamId, StringComparison.Ordinal) &&
                    candidate.NewLevel == level)
                {
                    return true;
                }
            }

            return false;
        }

        private static void HeadChargeStartAction(PlayerAvatar playerAvatar, int level)
        {
            if (!IsLocalTarget(playerAvatar))
            {
                DebugLog(
                    $"start-action upgrade={HeadChargeUpgradeId} target={playerAvatar?.steamID ?? "<null>"} level={level} " +
                    "decision=skip reason=non-local-target");
                return;
            }

            var abilityManager = DHHAbilityManager.instance;
            if (abilityManager == null)
            {
                DebugLog(
                    $"start-action upgrade={HeadChargeUpgradeId} target={playerAvatar.steamID} level={level} " +
                    "decision=skip reason=ability-manager-unavailable");
                return;
            }

            try
            {
                abilityManager.EquipAbilities();
                DebugLog(
                    $"start-action upgrade={HeadChargeUpgradeId} target={playerAvatar.steamID} level={level} " +
                    "decision=refresh-charge");
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"{TracePrefix} Charge start refresh failed for '{playerAvatar.steamID}': {ex.Message}");
            }
        }

        private static void HeadPowerStartAction(PlayerAvatar playerAvatar, int level)
        {
            DebugLog(
                $"start-action upgrade={HeadPowerUpgradeId} target={playerAvatar?.steamID ?? "<null>"} level={level} " +
                "decision=skip-power-purchase-effect reason=restore-start-action");
        }

        private static void HeadChargeUpgradeAction(PlayerAvatar playerAvatar, int level)
        {
            if (!TryGetCurrentTransition(_headChargeUpgrade, UpgradeKind.HeadCharge, playerAvatar, level, out var transition))
                return;

            if (transition.NewLevel == transition.PreviousLevel)
            {
                DebugLog(
                    $"callback upgrade={HeadChargeUpgradeId} target={transition.SteamId} old={transition.PreviousLevel} " +
                    $"new={transition.NewLevel} decision=skip reason=same-value");
                return;
            }

            if (!IsLocalTarget(playerAvatar))
            {
                DebugLog(
                    $"callback upgrade={HeadChargeUpgradeId} target={transition.SteamId} old={transition.PreviousLevel} " +
                    $"new={transition.NewLevel} decision=skip-local-event reason=non-local-target");
                return;
            }

            var dhhStats = DHHStatsManager.instance;
            if (dhhStats == null)
            {
                _log?.LogWarning(
                    $"{TracePrefix} Charge transition {transition.PreviousLevel}->{transition.NewLevel} for '{transition.SteamId}' " +
                    "could not refresh DHH local state because DHHStatsManager is unavailable.");
                return;
            }

            try
            {
                dhhStats.UpdateHeadChargeStat(transition.SteamId, transition.NewLevel);
                DhhUpgradeOrchestrator.PlayAuthorizedLocalFeedback(
                    transition.SteamId,
                    transition.NewLevel,
                    transition.PreviousLevel);

                DebugLog(
                    $"callback upgrade={HeadChargeUpgradeId} target={transition.SteamId} old={transition.PreviousLevel} " +
                    $"new={transition.NewLevel} decision=apply-charge-refresh");
            }
            catch (Exception ex)
            {
                _log?.LogWarning(
                    $"{TracePrefix} Charge transition {transition.PreviousLevel}->{transition.NewLevel} for '{transition.SteamId}' failed: {ex.Message}");
            }
        }

        private static void HeadPowerUpgradeAction(PlayerAvatar playerAvatar, int level)
        {
            if (!TryGetCurrentTransition(_headPowerUpgrade, UpgradeKind.HeadPower, playerAvatar, level, out var transition))
                return;

            if (transition.NewLevel <= transition.PreviousLevel)
            {
                DebugLog(
                    $"callback upgrade={HeadPowerUpgradeId} target={transition.SteamId} old={transition.PreviousLevel} " +
                    $"new={transition.NewLevel} decision=skip-power-purchase-effect " +
                    $"reason={(transition.NewLevel == transition.PreviousLevel ? "same-value" : "decrease")}");
                return;
            }

            if (!IsLocalTarget(playerAvatar))
            {
                DebugLog(
                    $"callback upgrade={HeadPowerUpgradeId} target={transition.SteamId} old={transition.PreviousLevel} " +
                    $"new={transition.NewLevel} decision=skip-local-power-effect reason=non-local-target");
                return;
            }

            var dhhStats = DHHStatsManager.instance;
            if (dhhStats == null)
            {
                _log?.LogWarning(
                    $"{TracePrefix} Power transition {transition.PreviousLevel}->{transition.NewLevel} for '{transition.SteamId}' " +
                    "could not apply the DHH local effect because DHHStatsManager is unavailable.");
                return;
            }

            AbilityEnergyHandler? energyHandlerBefore = null;
            float? energyBefore = null;
            if (FeatureFlags.DebugLogging)
            {
                energyHandlerBefore = TryGetEnergyHandler(playerAvatar);
                energyBefore = energyHandlerBefore?.Energy;
            }

            try
            {
                dhhStats.UpdateHeadPowerStat(transition.SteamId, transition.NewLevel);
                DhhUpgradeOrchestrator.PlayAuthorizedLocalFeedback(
                    transition.SteamId,
                    transition.NewLevel,
                    transition.PreviousLevel);

                if (FeatureFlags.DebugLogging)
                {
                    var energyHandlerAfter = TryGetEnergyHandler(playerAvatar);
                    var energyAfter = energyHandlerAfter?.Energy;
                    DebugLog(
                        $"callback upgrade={HeadPowerUpgradeId} target={transition.SteamId} old={transition.PreviousLevel} " +
                        $"new={transition.NewLevel} decision=apply-power-effect " +
                        $"energyHandlerBefore={energyHandlerBefore != null} energyBefore={FormatEnergy(energyBefore)} " +
                        $"energyHandlerAfter={energyHandlerAfter != null} energyAfter={FormatEnergy(energyAfter)}");
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning(
                    $"{TracePrefix} Power transition {transition.PreviousLevel}->{transition.NewLevel} for '{transition.SteamId}' failed: {ex.Message}");
            }
        }

        private static bool TryGetCurrentTransition(
            PlayerUpgrade? expectedUpgrade,
            UpgradeKind expectedKind,
            PlayerAvatar? playerAvatar,
            int callbackLevel,
            out ApplyTransitionFrame transition)
        {
            transition = null!;
            if (expectedUpgrade == null || playerAvatar == null || string.IsNullOrWhiteSpace(playerAvatar.steamID))
            {
                if (LogLimiter.ShouldLog($"Fix:DHHUpgrade:MissingManagedContext:{GetUpgradeId(expectedKind)}", 600))
                {
                    _log?.LogWarning($"{TracePrefix} REPOLib callback for {GetUpgradeId(expectedKind)} has no managed upgrade/player context.");
                }
                return false;
            }

            var frames = _applyTransitionFrames;
            if (frames != null)
            {
                for (var index = frames.Count - 1; index >= 0; index--)
                {
                    var candidate = frames[index];
                    if (ReferenceEquals(candidate.Upgrade, expectedUpgrade) &&
                        candidate.Kind == expectedKind &&
                        string.Equals(candidate.SteamId, playerAvatar.steamID, StringComparison.Ordinal) &&
                        candidate.NewLevel == callbackLevel)
                    {
                        transition = candidate;
                        return true;
                    }
                }
            }

            if (LogLimiter.ShouldLog($"Fix:DHHUpgrade:MissingTransition:{GetUpgradeId(expectedKind)}:{playerAvatar.steamID}", 600))
            {
                _log?.LogWarning(
                    $"{TracePrefix} REPOLib callback for {GetUpgradeId(expectedKind)} target='{playerAvatar.steamID}' level={callbackLevel} " +
                    "arrived without the expected ApplyUpgrade transition context; purchase effects were skipped.");
            }
            return false;
        }

        private static void BindUpgrade(UpgradeKind kind, PlayerUpgrade upgrade, DHHStatsManager dhhStats, string context)
        {
            var dictionary = kind == UpgradeKind.HeadCharge
                ? dhhStats.playerUpgradeHeadCharge
                : dhhStats.playerUpgradeHeadPower;

            upgrade.PlayerDictionary = dictionary;
            DebugLog(
                $"binding upgrade={upgrade.UpgradeId} context={context} result=bound " +
                $"dictionaryEntries={dictionary.Count}");
        }

        private static void VerifyDictionaryInvariant(
            string context,
            UpgradeKind kind,
            string statsKey,
            Dictionary<string, int> dhhDictionary,
            PlayerUpgrade? upgrade,
            bool warnOnMismatch)
        {
            if (upgrade == null)
            {
                DebugLog(
                    $"dictionary-invariant upgrade={GetUpgradeId(kind)} context={context} result=pending " +
                    "reason=registration-unavailable");
                return;
            }

            var dictionaries = StatsManager.instance.dictionaryOfDictionaries;
            var hasStatsDictionary = dictionaries.TryGetValue(statsKey, out var statsDictionary);
            var statsMatches = hasStatsDictionary && ReferenceEquals(dhhDictionary, statsDictionary);
            var repolibMatches = ReferenceEquals(dhhDictionary, upgrade.PlayerDictionary);

            if (!statsMatches || !repolibMatches)
            {
                var message =
                    $"dictionary-invariant upgrade={upgrade.UpgradeId} context={context} result=mismatch " +
                    $"statsKeyPresent={hasStatsDictionary} dhhEqualsStats={statsMatches} dhhEqualsREPOLib={repolibMatches}";

                if (warnOnMismatch)
                {
                    var limiterKey = $"Fix:DHHUpgrade:DictionaryInvariant:{upgrade.UpgradeId}:{context}";
                    if (LogLimiter.ShouldLog(limiterKey, 600))
                        LogWarning(message);
                }
                else
                {
                    DebugLog(message);
                }
                return;
            }

            DebugLog(
                $"dictionary-invariant upgrade={upgrade.UpgradeId} context={context} result=ok " +
                "dhhEqualsStats=True dhhEqualsREPOLib=True");
        }

        private static bool TryGetUpgradeKind(PlayerUpgrade upgrade, out UpgradeKind kind)
        {
            if (_headChargeUpgrade != null && ReferenceEquals(upgrade, _headChargeUpgrade))
            {
                kind = UpgradeKind.HeadCharge;
                return true;
            }

            if (_headPowerUpgrade != null && ReferenceEquals(upgrade, _headPowerUpgrade))
            {
                kind = UpgradeKind.HeadPower;
                return true;
            }

            kind = default;
            return false;
        }

        private static PlayerUpgrade? GetOwnedUpgrade(UpgradeKind kind)
        {
            return kind == UpgradeKind.HeadCharge ? _headChargeUpgrade : _headPowerUpgrade;
        }

        private static void SetOwnedUpgrade(UpgradeKind kind, PlayerUpgrade upgrade)
        {
            if (kind == UpgradeKind.HeadCharge)
                _headChargeUpgrade = upgrade;
            else
                _headPowerUpgrade = upgrade;
        }

        private static string GetUpgradeId(UpgradeKind kind)
        {
            return kind == UpgradeKind.HeadCharge ? HeadChargeUpgradeId : HeadPowerUpgradeId;
        }

        private static SetLevelFrame? FindMatchingSetLevelFrame(PlayerUpgrade upgrade, string steamId)
        {
            var frames = _setLevelFrames;
            if (frames == null)
                return null;

            for (var index = frames.Count - 1; index >= 0; index--)
            {
                var candidate = frames[index];
                if (!candidate.ApplyCaptured &&
                    ReferenceEquals(candidate.Upgrade, upgrade) &&
                    string.Equals(candidate.SteamId, steamId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void RemoveFrame<T>(List<T>? frames, T? frame, string operation) where T : class
        {
            if (frames == null || frame == null)
                return;

            for (var index = frames.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(frames[index], frame))
                    continue;

                if (index != frames.Count - 1)
                {
                    _log?.LogWarning(
                        $"{TracePrefix} {operation} transition context completed out of order; the exact frame was removed defensively.");
                }

                frames.RemoveAt(index);
                return;
            }

            _log?.LogWarning($"{TracePrefix} {operation} transition context cleanup could not find its frame.");
        }

        private static bool IsLocalTarget(PlayerAvatar? playerAvatar)
        {
            var local = PlayerAvatar.instance;
            return playerAvatar != null &&
                   local != null &&
                   !string.IsNullOrWhiteSpace(playerAvatar.steamID) &&
                   string.Equals(local.steamID, playerAvatar.steamID, StringComparison.Ordinal);
        }

        private static PlayerAvatar? TryResolvePlayerAvatar(string steamId)
        {
            try
            {
                return SemiFunc.PlayerAvatarGetFromSteamID(steamId);
            }
            catch
            {
                return null;
            }
        }

        private static AbilityEnergyHandler? TryGetEnergyHandler(PlayerAvatar? playerAvatar)
        {
            try
            {
                var deathHead = playerAvatar?.playerDeathHead;
                var controller = deathHead?.GetComponent<DeathHeadController>();
                return controller?.abilityEnergyHandler;
            }
            catch
            {
                return null;
            }
        }

        private static string ClassifyTransition(int previousLevel, int newLevel)
        {
            if (newLevel > previousLevel)
                return "increase";
            if (newLevel < previousLevel)
                return "decrease";
            return "same";
        }

        internal static string GetRuntimeRole()
        {
            if (!GameManager.Multiplayer())
                return "singleplayer";

            return PhotonNetwork.IsMasterClient ? "master" : "client";
        }

        private static string FormatEnergy(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "n/a";
        }

        private static void LogRegistrationCollision(string upgradeId, string reason)
        {
            if (!CollisionLogs.Add(upgradeId))
                return;

            _log?.LogError(
                $"{TracePrefix} REPOLib upgrade '{upgradeId}' is not owned by DHHFix: {reason}. " +
                "DHHFix will not replace or bind the foreign registration.");
        }

        internal static void DebugLog(string message)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            try
            {
                _log?.LogInfo($"{TracePrefix} {message}");
            }
            catch
            {
                // Debug diagnostics are observational and must remain behavior-neutral.
            }
        }
    }

    [HarmonyPatch(typeof(PlayerUpgrade), nameof(PlayerUpgrade.SetLevel), typeof(string), typeof(int))]
    internal static class DhhRepolibSetLevelTransitionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(PlayerUpgrade __instance, string steamId, out DhhRepolibUpgradeBridge.SetLevelFrame? __state)
        {
            __state = null;
            try
            {
                __state = DhhRepolibUpgradeBridge.BeginSetLevel(__instance, steamId);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"SetLevel transition capture failed for upgrade='{__instance?.UpgradeId ?? "<null>"}', " +
                    $"target='{steamId}': {ex.Message}. REPOLib SetLevel will continue without DHH compatibility effects.");
            }
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, DhhRepolibUpgradeBridge.SetLevelFrame? __state)
        {
            try
            {
                DhhRepolibUpgradeBridge.EndSetLevel(__state);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"SetLevel transition cleanup failed: {ex.Message}. The original REPOLib result was preserved.");
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerUpgrade), "ApplyUpgrade", typeof(string), typeof(int))]
    internal static class DhhRepolibApplyUpgradeTransitionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            PlayerUpgrade __instance,
            string steamId,
            int level,
            out DhhRepolibUpgradeBridge.ApplyTransitionFrame? __state)
        {
            __state = null;
            try
            {
                __state = DhhRepolibUpgradeBridge.BeginApplyUpgrade(__instance, steamId, level);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"ApplyUpgrade transition capture failed for upgrade='{__instance?.UpgradeId ?? "<null>"}', " +
                    $"target='{steamId}', level={level}: {ex.Message}. REPOLib ApplyUpgrade will continue without DHH compatibility effects.");
            }
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, DhhRepolibUpgradeBridge.ApplyTransitionFrame? __state)
        {
            try
            {
                DhhRepolibUpgradeBridge.EndApplyUpgrade(__state);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"ApplyUpgrade transition cleanup failed: {ex.Message}. The original REPOLib result was preserved.");
            }
            return __exception;
        }
    }
}
