#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using HarmonyLib;
using REPOLib.Modules;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Bootstrap
{
    internal static class DHHStatsBootstrapModule
    {
        private const string HeadChargeKey = "playerUpgradeHeadCharge";
        private const string HeadPowerKey = "playerUpgradeHeadPower";

        private static ManualLogSource? _log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;

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
            EnsureRepolibUpgradeDictionaries();
            EnsureDhhStatsManagerDisabled();

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

        private static bool DHHStatsManager_Upgrade_Prefix(DHHStatsManager __instance, string playerId)
        {
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: false);
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: true);
            return true;
        }

        private static bool DHHStatsManager_UpdateHeadChargeStat_Prefix(DHHStatsManager __instance, string playerId, ref int __state)
        {
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: true);
            __instance.playerUpgradeHeadCharge.TryGetValue(playerId, out __state);
            return true;
        }

        private static void DHHStatsManager_UpdateHeadChargeStat_Postfix(string playerId, int value, int __state)
        {
            DhhUpgradeOrchestrator.PlayAuthorizedLocalFeedback(playerId, value, __state);
        }

        private static bool DHHStatsManager_UpdateHeadPowerStat_Prefix(DHHStatsManager __instance, string playerId, ref int __state)
        {
            EnsureDhhPlayerUpgradeKey(__instance, playerId, isChargeUpgrade: false);
            __instance.playerUpgradeHeadPower.TryGetValue(playerId, out __state);
            return true;
        }

        private static void DHHStatsManager_UpdateHeadPowerStat_Postfix(string playerId, int value, int __state)
        {
            DhhUpgradeOrchestrator.PlayAuthorizedLocalFeedback(playerId, value, __state);
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
            EnsureRepolibUpgradeDictionaries();
            EnsureDhhStatsManagerDisabled();
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
            EnsureRepolibUpgradeDictionaries();
            EnsureDhhStatsManagerDisabled();
        }

        private static void StatsManager_LoadGame_Postfix()
        {
            EnsureDhhStatsLabels();
            EnsureDhhUpgradeDictionaries();
            EnsureRepolibUpgradeDictionaries();
            EnsureDhhStatsManagerDisabled();

            try
            {
                DHHAbilityManager.instance?.EquipAbilities();
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix:Stats] Failed to refresh DHH abilities after load: {ex.Message}");
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

        private static void EnsureRepolibUpgradeDictionaries()
        {
            var dhhStats = DHHStatsManager.instance;
            if (dhhStats == null)
                return;

            BindRepolibUpgrade("HeadCharge", dhhStats.playerUpgradeHeadCharge, "charge");
            BindRepolibUpgrade("HeadPower", dhhStats.playerUpgradeHeadPower, "power");
        }

        private static void BindRepolibUpgrade(string upgradeId, System.Collections.Generic.Dictionary<string, int> dictionary, string label)
        {
            var upgrade = Upgrades.GetUpgrade(upgradeId);
            if (upgrade == null)
                return;

            upgrade.PlayerDictionary = dictionary;

            if (FeatureFlags.DebugLogging)
                _log?.LogInfo($"[Fix:Stats] Bound REPOLib upgrade '{upgradeId}' to DHH {label} dictionary.");
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
}
