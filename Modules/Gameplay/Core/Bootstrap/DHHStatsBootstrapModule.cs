#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;
using HarmonyLib;

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
            PatchDhhStatsManagerUpgradeHooksIfPossible(harmony);
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
            StatsModule.SeedDhhUpgradeKeys();
            EnsureDhhStatsManagerDisabled();

            if (__instance != null)
                __instance.enabled = false;
        }

        private static void PatchDhhStatsManagerUpgradeHooksIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var getCharge = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.GetHeadChargeUpgrade));
            var getPower = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.GetHeadPowerUpgrade));
            var upgradeCharge = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.UpgradeHeadCharge));
            var upgradePower = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.UpgradeHeadPower));

            var getChargePrefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_GetHeadChargeUpgrade_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var getPowerPrefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_GetHeadPowerUpgrade_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var upgradeChargePrefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_UpgradeHeadCharge_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var upgradePowerPrefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_UpgradeHeadPower_Prefix), BindingFlags.Static | BindingFlags.NonPublic);

            if (getCharge != null && getChargePrefix != null)
                harmony.Patch(getCharge, prefix: new HarmonyMethod(getChargePrefix));
            if (getPower != null && getPowerPrefix != null)
                harmony.Patch(getPower, prefix: new HarmonyMethod(getPowerPrefix));
            if (upgradeCharge != null && upgradeChargePrefix != null)
                harmony.Patch(upgradeCharge, prefix: new HarmonyMethod(upgradeChargePrefix));
            if (upgradePower != null && upgradePowerPrefix != null)
                harmony.Patch(upgradePower, prefix: new HarmonyMethod(upgradePowerPrefix));
        }

        private static bool DHHStatsManager_GetHeadChargeUpgrade_Prefix(string playerId, ref int __result)
        {
            __result = StatsModule.GetDhhUpgradeLevel(playerId, isChargeUpgrade: true);
            return false;
        }

        private static bool DHHStatsManager_GetHeadPowerUpgrade_Prefix(string playerId, ref int __result)
        {
            __result = StatsModule.GetDhhUpgradeLevel(playerId, isChargeUpgrade: false);
            return false;
        }

        private static bool DHHStatsManager_UpgradeHeadCharge_Prefix(string playerId, ref int __result)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                __result = StatsModule.GetDhhUpgradeLevel(playerId, isChargeUpgrade: true);
                return false;
            }

            if (StatsModule.TryIncreaseDhhUpgrade(playerId, isChargeUpgrade: true, out var level))
                __result = level;

            return false;
        }

        private static bool DHHStatsManager_UpgradeHeadPower_Prefix(string playerId, ref int __result)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                __result = StatsModule.GetDhhUpgradeLevel(playerId, isChargeUpgrade: false);
                return false;
            }

            if (StatsModule.TryIncreaseDhhUpgrade(playerId, isChargeUpgrade: false, out var level))
                __result = level;

            return false;
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
            StatsModule.SeedDhhUpgradeKeys();
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
            StatsModule.SeedDhhUpgradeKeys();
            EnsureDhhStatsManagerDisabled();
        }

        private static void StatsManager_LoadGame_Postfix()
        {
            EnsureDhhStatsLabels();
            StatsModule.SeedDhhUpgradeKeys();
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
