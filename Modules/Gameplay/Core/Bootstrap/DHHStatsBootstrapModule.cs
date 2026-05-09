#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

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

            PatchStatsManagerAwakeIfPossible(harmony);
            PatchDhhAbilityManagerStartIfPossible(harmony);
            EnsureDhhStatsBootstrapState();
        }

        private static void EnsureDhhStatsManagerExists()
        {
            if (DHHStatsManager.instance != null)
                return;

            if (StatsManager.instance == null || StatsManager.instance.gameObject == null)
                return;

            StatsManager.instance.gameObject.AddComponent<DHHStatsManager>();
            _log?.LogInfo("[Fix:Stats] DHHStatsManager attached to StatsManager before save bootstrap.");
        }

        private static void EnsureDhhStatsBootstrapState()
        {
            EnsureDhhStatsManagerExists();

            var stats = StatsManager.instance;
            var dhhStats = DHHStatsManager.instance;
            if (stats == null || dhhStats == null)
                return;

            if (stats.dictionaryOfDictionaries != null)
            {
                stats.dictionaryOfDictionaries[HeadChargeKey] = dhhStats.playerUpgradeHeadCharge;
                stats.dictionaryOfDictionaries[HeadPowerKey] = dhhStats.playerUpgradeHeadPower;
            }

            if (stats.upgradesInfo != null)
            {
                EnsureDhhUpgradeInfo(stats, HeadChargeKey, "Head Charge");
                EnsureDhhUpgradeInfo(stats, HeadPowerKey, "Head Power");
            }
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
            EnsureDhhStatsBootstrapState();
        }

        private static void PatchDhhAbilityManagerStartIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var original = AccessTools.Method(typeof(DHHAbilityManager), nameof(DHHAbilityManager.Start));
            if (original == null)
                return;

            var postfix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHAbilityManager_Start_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
        }

        private static void DHHAbilityManager_Start_Postfix()
        {
            try
            {
                DHHAbilityManager.instance?.EquipAbilities();
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix:Stats] Failed to seed DHH abilities on start: {ex.Message}");
            }
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
