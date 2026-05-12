#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopper.Managers;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Runtime
{
    internal static class StatsModule
    {
        private static bool _statsHooksApplied;

        internal static void ApplyHooks(Harmony harmony)
        {
            if (harmony == null || _statsHooksApplied)
                return;

            PatchSemiFuncStatGetItemsPurchasedIfPossible(harmony);
            PatchStatsManagerGetItemsUpgradesPurchasedIfPossible(harmony);
            PatchStatsManagerItemPurchaseIfPossible(harmony);

            _statsHooksApplied = true;
        }

        internal static void EnsureStatsManagerKey(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            var stats = StatsManager.instance;
            if (stats == null)
                return;

            EnsureKey(stats.itemsPurchased, itemName);
            EnsureKey(stats.itemsPurchasedTotal, itemName);
            EnsureKey(stats.itemsUpgradesPurchased, itemName);
        }

        internal static void EnsureStatsEntriesForItem(UnityEngine.Object itemObj)
        {
            try
            {
                if (itemObj == null)
                    return;

                var stats = StatsManager.instance;
                if (stats == null)
                    return;

                EnsureKey(stats.itemsPurchased, itemObj.name);
                EnsureKey(stats.itemsPurchasedTotal, itemObj.name);
                EnsureKey(stats.itemsUpgradesPurchased, itemObj.name);
            }
            catch
            {
                // Legacy stats dictionaries can differ across versions; skip init for missing fields.
            }
        }

        private static void PatchSemiFuncStatGetItemsPurchasedIfPossible(Harmony harmony)
        {
            var m = AccessTools.Method(typeof(SemiFunc), nameof(SemiFunc.StatGetItemsPurchased), new[] { typeof(string) });
            if (m == null)
                return;

            var miPrefix = typeof(StatsModule).GetMethod(nameof(SemiFunc_StatGetItemsPurchased_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (miPrefix == null)
                return;

            harmony.Patch(m, prefix: new HarmonyMethod(miPrefix));
        }

        private static bool SemiFunc_StatGetItemsPurchased_Prefix(string itemName, ref int __result)
        {
            try
            {
                var stats = StatsManager.instance;
                if (stats == null)
                    return true;

                EnsureKey(stats.itemsPurchased, itemName);
                __result = stats.itemsPurchased[itemName];
                return false;
            }
            catch
            {
                // Keep vanilla result path if reflective lookup fails.
            }

            return true;
        }

        private static void PatchStatsManagerGetItemsUpgradesPurchasedIfPossible(Harmony harmony)
        {
            var mGet = AccessTools.Method(typeof(StatsManager), nameof(StatsManager.GetItemsUpgradesPurchased), new[] { typeof(string) });
            if (mGet == null)
                return;

            var miPrefix = typeof(StatsModule).GetMethod(nameof(StatsManager_GetItemsUpgradesPurchased_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (miPrefix == null)
                return;

            harmony.Patch(mGet, prefix: new HarmonyMethod(miPrefix));
        }

        private static bool StatsManager_GetItemsUpgradesPurchased_Prefix(string itemName, ref int __result)
        {
            try
            {
                var stats = StatsManager.instance;
                if (stats == null)
                    return true;

                EnsureKey(stats.itemsUpgradesPurchased, itemName);
                __result = stats.itemsUpgradesPurchased[itemName];
                return false;
            }
            catch
            {
                // Keep vanilla result path if reflective lookup fails.
            }

            return true;
        }

        private static void PatchStatsManagerItemPurchaseIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var mPurchase = AccessTools.Method(typeof(StatsManager), nameof(StatsManager.ItemPurchase), new[] { typeof(string) });
            if (mPurchase == null)
                return;

            var miPrefix = typeof(StatsModule).GetMethod(nameof(StatsManager_ItemPurchase_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (miPrefix == null)
                return;

            harmony.Patch(mPurchase, prefix: new HarmonyMethod(miPrefix));
        }

        private static bool StatsManager_ItemPurchase_Prefix(string itemName)
        {
            EnsureStatsManagerKey(itemName);
            return true;
        }

        private static void EnsureKey(IDictionary<string, int> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(key))
                return;

            if (!dictionary.ContainsKey(key))
                dictionary[key] = 0;
        }
    }
}

