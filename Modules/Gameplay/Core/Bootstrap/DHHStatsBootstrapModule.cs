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
            PatchStatsManagerLoadGameIfPossible(harmony);
            PatchDhhStatsManagerStartIfNeeded(harmony);
            PatchRepolibStatsManagerRunStartIfNeeded(harmony);
            PatchDhhAbilityManagerStartIfPossible(harmony);
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
            EnsureDhhStatsBridge("LoadGame-Prefix");
        }

        private static void StatsManager_LoadGame_Postfix()
        {
            EnsureDhhStatsBridge("LoadGame-Postfix");

            try
            {
                DHHAbilityManager.instance?.EquipAbilities();
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix:Stats] Failed to refresh DHH abilities after load: {ex.Message}");
            }
        }

        private static void EnsureDhhStatsBridge(string reason)
        {
            var stats = StatsManager.instance;
            if (stats == null)
                return;

            if (stats.upgradesInfo != null)
            {
                EnsureDhhUpgradeInfo(stats, HeadChargeKey, "Head Charge");
                EnsureDhhUpgradeInfo(stats, HeadPowerKey, "Head Power");
            }

            var dictionaryOfDictionariesField = AccessTools.Field(typeof(StatsManager), nameof(StatsManager.dictionaryOfDictionaries));
            if (dictionaryOfDictionariesField == null)
            {
                _log?.LogWarning($"[Fix:Stats] Skipped DHH stats bridge during {reason} because runtime StatsManager no longer exposes dictionaryOfDictionaries.");
                return;
            }

            var allDictionaries = stats.dictionaryOfDictionaries;
            if (allDictionaries == null)
                return;

            EnsureDhhDictionaryBridge(allDictionaries, HeadChargeKey, () => EnsureDhhStatsManager().playerUpgradeHeadCharge, reason);
            EnsureDhhDictionaryBridge(allDictionaries, HeadPowerKey, () => EnsureDhhStatsManager().playerUpgradeHeadPower, reason);
        }

        private static void EnsureDhhDictionaryBridge(System.Collections.Generic.IDictionary<string, System.Collections.Generic.Dictionary<string, int>> allDictionaries, string key, Func<System.Collections.Generic.Dictionary<string, int>> targetFactory, string reason)
        {
            if (allDictionaries == null || targetFactory == null)
                return;

            var target = targetFactory();
            if (target == null)
                return;

            if (allDictionaries.TryGetValue(key, out var existing) && existing != null && !ReferenceEquals(existing, target))
                CopyDictionaryValues(existing, target);

            allDictionaries[key] = target;

            if (FeatureFlags.DebugLogging)
                _log?.LogInfo($"[Fix:Stats] Bridged '{key}' during {reason}.");
        }

        private static DHHStatsManager EnsureDhhStatsManager()
        {
            if (DHHStatsManager.instance != null)
                return DHHStatsManager.instance;

            var host = StatsManager.instance != null ? StatsManager.instance.gameObject : null;
            var gameObject = host != null ? host : new GameObject("DHHStatsManager");
            var component = gameObject.GetComponent<DHHStatsManager>();
            if (component != null)
                return component;

            return gameObject.AddComponent<DHHStatsManager>();
        }

        private static void CopyDictionaryValues(System.Collections.Generic.Dictionary<string, int> source, System.Collections.Generic.Dictionary<string, int> target)
        {
            if (source == null || target == null || ReferenceEquals(source, target))
                return;

            foreach (var pair in source)
                target[pair.Key] = pair.Value;
        }

        private static void PatchDhhStatsManagerStartIfNeeded(Harmony harmony)
        {
            if (harmony == null)
                return;

            if (AccessTools.Field(typeof(StatsManager), nameof(StatsManager.dictionaryOfDictionaries)) != null)
                return;

            var original = AccessTools.Method(typeof(DHHStatsManager), nameof(DHHStatsManager.Start));
            if (original == null)
                return;

            var prefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(DHHStatsManager_Start_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static bool DHHStatsManager_Start_Prefix()
        {
            _log?.LogWarning("[Fix:Stats] DHHStatsManager.Start skipped because the runtime StatsManager no longer exposes dictionaryOfDictionaries.");
            EnsureDhhStatsLabels();
            return false;
        }

        private static void PatchRepolibStatsManagerRunStartIfNeeded(Harmony harmony)
        {
            if (harmony == null)
                return;

            if (AccessTools.Field(typeof(StatsManager), nameof(StatsManager.dictionaryOfDictionaries)) != null)
                return;

            var repolibPatchType = AccessTools.TypeByName("REPOLib.Patches.StatsManagerPatch");
            var original = repolibPatchType != null ? AccessTools.Method(repolibPatchType, "RunStartStatsPatch") : null;
            if (original == null)
                return;

            var prefix = typeof(DHHStatsBootstrapModule).GetMethod(nameof(StatsManagerPatch_RunStartStatsPatch_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static bool StatsManagerPatch_RunStartStatsPatch_Prefix()
        {
            _log?.LogWarning("[Fix:Stats] REPOLib StatsManager.RunStartStats upgrade path skipped because the runtime StatsManager no longer exposes dictionaryOfDictionaries.");
            EnsureDhhStatsLabels();
            return false;
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
