#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using DeathHeadHopperFix.Modules.Gameplay.Core.Interop;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Bootstrap
{
    internal static class DHHStatsBootstrapModule
    {
        private const string HeadChargeKey = "playerUpgradeHeadCharge";
        private const string HeadPowerKey = "playerUpgradeHeadPower";

        private static ManualLogSource? _log;

        private readonly struct DhhUpdatePatchState
        {
            internal DhhUpdatePatchState(int previousLevel, bool managedRepolibTransition)
            {
                PreviousLevel = previousLevel;
                ManagedRepolibTransition = managedRepolibTransition;
            }

            internal int PreviousLevel { get; }
            internal bool ManagedRepolibTransition { get; }
        }

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            DhhRepolibUpgradeBridge.Initialize(log);

            PatchDhhStatsManagerAwakeIfPossible(harmony);
            PatchDhhStatsManagerUpgradeMethodsIfPossible(harmony);
            PatchDhhStatsManagerUpdateMethodsIfPossible(harmony);
            PatchStatsManagerAwakeIfPossible(harmony);
            PatchStatsManagerLoadGameIfPossible(harmony);
        }

        private static void EnsureDhhStatsLabels()
        {
            var stats = StatsManager.instance;
            if (stats == null)
                return;

            if (stats.upgradesInfo != null)
            {
                EnsureDhhUpgradeInfo(stats, HeadChargeKey, "Head Charge");
                EnsureDhhUpgradeInfo(stats, HeadPowerKey, "Head Power");
            }
        }

        private static void PatchDhhStatsManagerAwakeIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var original = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.Awake));
            if (original == null)
                return;

            var postfix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_Awake_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        private static void DHHStatsManager_Awake_Postfix(DHHStatsManager __instance)
        {
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries("DHHStatsManager.Awake");
            EnsureDhhStatsManagerDisabled();
            DhhRepolibUpgradeBridge.VerifyDictionaryInvariant("DHHStatsManager.Awake");

            if (__instance != null)
                __instance.enabled = false;
        }

        private static void PatchDhhStatsManagerUpgradeMethodsIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var upgradeCharge = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.UpgradeHeadCharge));
            var upgradePower = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.UpgradeHeadPower));
            if (upgradeCharge == null && upgradePower == null)
                return;

            var prefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_Upgrade_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            if (upgradeCharge != null)
            {
                var patch = new HarmonyMethod(prefix) { priority = Priority.First };
                harmony.Patch(upgradeCharge, prefix: patch);
            }
            if (upgradePower != null)
            {
                var patch = new HarmonyMethod(prefix) { priority = Priority.First };
                harmony.Patch(upgradePower, prefix: patch);
            }
        }

        private static void PatchDhhStatsManagerUpdateMethodsIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var updateCharge = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.UpdateHeadChargeStat));
            var updatePower = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.UpdateHeadPowerStat));
            if (updateCharge == null && updatePower == null)
                return;

            var chargePrefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_UpdateHeadChargeStat_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var chargePostfix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_UpdateHeadChargeStat_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var powerPrefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_UpdateHeadPowerStat_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var powerPostfix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_UpdateHeadPowerStat_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (chargePrefix == null || chargePostfix == null || powerPrefix == null || powerPostfix == null)
                return;

            if (updateCharge != null)
            {
                var prefixPatch = new HarmonyMethod(chargePrefix) { priority = Priority.First };
                var postfixPatch = new HarmonyMethod(chargePostfix);
                harmony.Patch(updateCharge, prefix: prefixPatch, postfix: postfixPatch);
            }
            if (updatePower != null)
            {
                var prefixPatch = new HarmonyMethod(powerPrefix) { priority = Priority.First };
                var postfixPatch = new HarmonyMethod(powerPostfix);
                harmony.Patch(updatePower, prefix: prefixPatch, postfix: postfixPatch);
            }
        }

        private static bool DHHStatsManager_Upgrade_Prefix(DHHStatsManager __instance, string playerId, MethodBase __originalMethod)
        {
            // Original DHH indexes both dictionaries directly. Keep zero seeding so unknown legacy callers
            // cannot turn a still-supported public UpgradeHead* entry point into a KeyNotFoundException.
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: false);
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: true);

            var entry = __originalMethod?.Name ?? "<unknown>";
            var isChargeUpgrade = string.Equals(entry, nameof(DHHStatsManager.UpgradeHeadCharge), StringComparison.Ordinal);
            var upgradeId = isChargeUpgrade
                ? DhhRepolibUpgradeBridge.HeadChargeUpgradeId
                : DhhRepolibUpgradeBridge.HeadPowerUpgradeId;
            var dictionary = isChargeUpgrade
                ? __instance?.playerUpgradeHeadCharge
                : __instance?.playerUpgradeHeadPower;
            var currentLevel = 0;
            dictionary?.TryGetValue(playerId, out currentLevel);

            DhhRepolibUpgradeBridge.DebugLog(
                $"legacy-upgrade entry={entry} upgrade={upgradeId} target={playerId} " +
                $"currentLevel={currentLevel} role={DhhRepolibUpgradeBridge.GetRuntimeRole()} " +
                "classification=residual-dhh-or-external decision=allow-compatible");
            return true;
        }

        private static bool DHHStatsManager_UpdateHeadChargeStat_Prefix(
            DHHStatsManager __instance,
            string playerId,
            int value,
            ref DhhUpdatePatchState __state)
        {
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: true);
            __instance.playerUpgradeHeadCharge.TryGetValue(playerId, out var previousLevel);

            var managed = DhhRepolibUpgradeBridge.IsManagedCompatibilityUpdate(
                DhhRepolibUpgradeBridge.UpgradeKind.HeadCharge,
                playerId,
                value);
            __state = new DhhUpdatePatchState(previousLevel, managed);

            DhhRepolibUpgradeBridge.DebugLog(
                $"compat-update upgrade={DhhRepolibUpgradeBridge.HeadChargeUpgradeId} target={playerId} " +
                $"oldSnapshot={previousLevel} new={value} source={(managed ? "managed-repolib" : "legacy-or-external")}");
            return true;
        }

        private static void DHHStatsManager_UpdateHeadChargeStat_Postfix(
            string playerId,
            int value,
            DhhUpdatePatchState __state)
        {
            if (!__state.ManagedRepolibTransition)
                DhhUpgradeOrchestrator.PlayAuthorizedLocalFeedback(playerId, value, __state.PreviousLevel);
        }

        private static bool DHHStatsManager_UpdateHeadPowerStat_Prefix(
            DHHStatsManager __instance,
            string playerId,
            int value,
            ref DhhUpdatePatchState __state)
        {
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: false);
            __instance.playerUpgradeHeadPower.TryGetValue(playerId, out var previousLevel);

            var managed = DhhRepolibUpgradeBridge.IsManagedCompatibilityUpdate(
                DhhRepolibUpgradeBridge.UpgradeKind.HeadPower,
                playerId,
                value);
            __state = new DhhUpdatePatchState(previousLevel, managed);

            DhhRepolibUpgradeBridge.DebugLog(
                $"compat-update upgrade={DhhRepolibUpgradeBridge.HeadPowerUpgradeId} target={playerId} " +
                $"oldSnapshot={previousLevel} new={value} source={(managed ? "managed-repolib" : "legacy-or-external")}");
            return true;
        }

        private static void DHHStatsManager_UpdateHeadPowerStat_Postfix(
            string playerId,
            int value,
            DhhUpdatePatchState __state)
        {
            if (!__state.ManagedRepolibTransition)
                DhhUpgradeOrchestrator.PlayAuthorizedLocalFeedback(playerId, value, __state.PreviousLevel);
        }

        private static void PatchStatsManagerAwakeIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var original = AccessTools.Method(typeof(StatsManager), nameof(StatsManager.Awake));
            if (original == null)
                return;

            var postfix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(StatsManager_Awake_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        private static void StatsManager_Awake_Postfix()
        {
            EnsureDhhStatsLabels();
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries("StatsManager.Awake");
            EnsureDhhStatsManagerDisabled();
            DhhRepolibUpgradeBridge.VerifyDictionaryInvariant("StatsManager.Awake");
        }

        private static void PatchStatsManagerLoadGameIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var original = AccessTools.Method(typeof(StatsManager), nameof(StatsManager.LoadGame));
            if (original == null)
                return;

            var prefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(StatsManager_LoadGame_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var postfix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(StatsManager_LoadGame_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null || postfix == null)
                return;

            harmony.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
        }

        private static void StatsManager_LoadGame_Prefix()
        {
            EnsureDhhStatsLabels();
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries("StatsManager.LoadGame.Prefix");
            EnsureDhhStatsManagerDisabled();
            DhhRepolibUpgradeBridge.VerifyDictionaryInvariant("StatsManager.LoadGame.Prefix");
        }

        private static void StatsManager_LoadGame_Postfix()
        {
            EnsureDhhStatsLabels();
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries("StatsManager.LoadGame.Postfix");
            EnsureDhhStatsManagerDisabled();
            DhhRepolibUpgradeBridge.VerifyDictionaryInvariant("StatsManager.LoadGame.Postfix");

            try
            {
                DHHAbilityManager.instance?.EquipAbilities();
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix:Stats] Failed to refresh DHH abilities after load: {ex.Message}");
            }
        }

        internal static void PrepareForFullDictionarySync(string context)
        {
            var dictionariesBefore = StatsManager.instance?.dictionaryOfDictionaries;
            var headChargeKeyPresentBefore = dictionariesBefore?.ContainsKey(HeadChargeKey) == true;
            var headPowerKeyPresentBefore = dictionariesBefore?.ContainsKey(HeadPowerKey) == true;

            try
            {
                DhhRepolibUpgradeBridge.VerifyDictionaryInvariant($"{context}.PreBind", warnOnMismatch: false);
                DhhRepolibUpgradeBridge.DebugLog(
                    $"full-sync context={context} stage=pre-bind " +
                    $"headChargeKeyPresent={headChargeKeyPresentBefore} " +
                    $"headPowerKeyPresent={headPowerKeyPresentBefore}");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Full-sync pre-bind diagnostics failed for context={context}: {ex.Message}. Binding will continue.");
            }

            EnsureDhhStatsLabels();
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries(context);
            EnsureDhhStatsManagerDisabled();

            var dictionariesAfter = StatsManager.instance?.dictionaryOfDictionaries;
            try
            {
                DhhRepolibUpgradeBridge.VerifyDictionaryInvariant($"{context}.PostBind");
                DhhRepolibUpgradeBridge.DebugLog(
                    $"full-sync context={context} stage=post-bind " +
                    $"headChargeKeyPresent={dictionariesAfter?.ContainsKey(HeadChargeKey) == true} " +
                    $"headPowerKeyPresent={dictionariesAfter?.ContainsKey(HeadPowerKey) == true}");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Full-sync post-bind diagnostics failed for context={context}: {ex.Message}. Binding was preserved.");
            }
        }

        internal static void CompleteFullDictionarySyncReceive(Hashtable? data, bool finalChunk)
        {
            var containsCharge = data?.ContainsKey(HeadChargeKey) == true;
            var containsPower = data?.ContainsKey(HeadPowerKey) == true;

            DhhRepolibUpgradeBridge.DebugLog(
                $"full-sync context=PunManager.ReceiveSyncData.Postfix stage=received-chunk " +
                $"containsHeadCharge={containsCharge} containsHeadPower={containsPower} finalChunk={finalChunk} " +
                "classification=non-purchase");

            if (!finalChunk)
                return;

            // ReceiveSyncData mutates existing dictionary instances in place. Re-assert and verify ownership
            // after the final chunk, but never invoke a REPOLib mutation or DHH Power purchase effect here.
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries("PunManager.ReceiveSyncData.Postfix.Final");
            EnsureDhhStatsManagerDisabled();
            DhhRepolibUpgradeBridge.VerifyDictionaryInvariant("PunManager.ReceiveSyncData.Postfix.Final");
            RefreshChargeAfterNonPurchaseSync();

            DhhRepolibUpgradeBridge.DebugLog(
                "full-sync context=PunManager.ReceiveSyncData.Postfix.Final stage=complete " +
                "chargeDecision=refresh-current-state powerDecision=level-only " +
                "powerPurchaseEffect=skipped itemConsumption=skipped");
        }

        private static void RefreshChargeAfterNonPurchaseSync()
        {
            try
            {
                var manager = DHHAbilityManager.instance;
                if (manager == null)
                {
                    DhhRepolibUpgradeBridge.DebugLog(
                        "full-sync charge-refresh decision=deferred reason=ability-manager-unavailable");
                    return;
                }

                manager.EquipAbilities();
                DhhRepolibUpgradeBridge.DebugLog(
                    $"full-sync charge-refresh decision=refresh-current-state spotsFetched={manager.spotsFeched}");
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix:Stats] Failed to refresh DHH Charge state after full sync: {ex.Message}");
            }
        }

        private static void EnsureDhhStatsManagerDisabled()
        {
            var stats = DHHStatsManager.instance;
            if (stats == null)
                return;

            if (!stats.enabled)
                return;

            stats.enabled = false;
            if (FeatureFlags.DebugLogging)
                _log?.LogInfo("[Fix:Stats] DHHStatsManager disabled so the Fix can own the compatibility path.");
        }

        private static void EnsureDhhUpgradeDictionaries()
        {
            var stats = StatsManager.instance;
            var dhhStats = DHHStatsManager.instance;
            if (stats == null || dhhStats == null || stats.dictionaryOfDictionaries == null)
                return;

            stats.dictionaryOfDictionaries[HeadChargeKey] = dhhStats.playerUpgradeHeadCharge;
            stats.dictionaryOfDictionaries[HeadPowerKey] = dhhStats.playerUpgradeHeadPower;

            if (FeatureFlags.DebugLogging)
                _log?.LogInfo("[Fix:Stats] Bound DHH upgrade dictionaries into StatsManager.");
        }

        private static void EnsureRepolibUpgradeDictionaries(string context)
        {
            DhhRepolibUpgradeBridge.BindRegisteredUpgrades(DHHStatsManager.instance, context);
        }

        private static void EnsureDhhPlayerUpgradeKey(DHHStatsManager? stats, string playerId, bool isChargeUpgrade)
        {
            if (stats == null || string.IsNullOrWhiteSpace(playerId))
                return;

            var dictionary = isChargeUpgrade
                ? stats.playerUpgradeHeadCharge
                : stats.playerUpgradeHeadPower;

            if (!dictionary.ContainsKey(playerId))
                dictionary[playerId] = 0;
        }

        private static void EnsureDhhUpgradeInfo(StatsManager stats, string key, string displayName)
        {
            if (stats.upgradesInfo == null)
                return;

            if (stats.upgradesInfo.ContainsKey(key))
                return;

            stats.upgradesInfo.Add(key, new StatsManager.UpgradeInfo
            {
                displayName = displayName,
                displayNameLocalized = null
            });

            if (FeatureFlags.DebugLogging)
                _log?.LogInfo($"[Fix:Stats] Added upgrade label '{key}' -> '{displayName}'.");
        }
    }

    [HarmonyPatch(typeof(PunManager), nameof(PunManager.SyncAllDictionaries))]
    internal static class DhhPunManagerSyncAllDictionariesPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try
            {
                DHHStatsBootstrapModule.PrepareForFullDictionarySync("PunManager.SyncAllDictionaries.Prefix");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Full-sync sender binding guard failed: {ex.Message}. The original SyncAllDictionaries call will continue.");
            }
        }
    }

    [HarmonyPatch(
        typeof(PunManager),
        nameof(PunManager.ReceiveSyncData),
        typeof(Hashtable),
        typeof(bool),
        typeof(PhotonMessageInfo))]
    internal static class DhhPunManagerReceiveSyncDataPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Hashtable data, bool finalChunk)
        {
            try
            {
                DHHStatsBootstrapModule.PrepareForFullDictionarySync(
                    finalChunk
                        ? "PunManager.ReceiveSyncData.Prefix.Final"
                        : "PunManager.ReceiveSyncData.Prefix");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Full-sync receiver pre-bind guard failed: {ex.Message}. The original ReceiveSyncData call will continue.");
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Hashtable data, bool finalChunk)
        {
            try
            {
                DHHStatsBootstrapModule.CompleteFullDictionarySyncReceive(data, finalChunk);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Full-sync receiver post-bind guard failed: {ex.Message}. The original ReceiveSyncData result was preserved.");
            }
        }
    }
}
