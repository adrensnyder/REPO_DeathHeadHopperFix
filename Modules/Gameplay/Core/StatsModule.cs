#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
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

            var tStats = AccessTools.TypeByName("StatsManager");
            if (tStats == null)
                return;

            var inst = ReflectionHelper.GetStaticInstanceValue(tStats, "instance");
            if (inst == null)
                return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in new[] { "itemsPurchased", "itemsPurchasedTotal", "itemsUpgradesPurchased", "itemsUpgradesPurchasedTotal" })
            {
                var fDict = tStats.GetField(field, flags);
                if (fDict?.GetValue(inst) is IDictionary dict && !dict.Contains(itemName))
                {
                    dict[itemName] = 0;
                }
            }
        }

        internal static void EnsureStatsEntriesForItem(UnityEngine.Object itemObj)
        {
            try
            {
                if (itemObj == null)
                    return;

                var tStats = AccessTools.TypeByName("StatsManager");
                if (tStats == null)
                    return;

                var inst = ReflectionHelper.GetStaticInstanceValue(tStats, "instance");
                if (inst == null)
                    return;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                var fPurchased = tStats.GetField("itemsPurchased", flags);
                var fPurchasedTotal = tStats.GetField("itemsPurchasedTotal", flags);
                var fUpgrades = tStats.GetField("itemsUpgradesPurchased", flags);
                var fUpgradesTotal = tStats.GetField("itemsUpgradesPurchasedTotal", flags);

                if (fPurchased?.GetValue(inst) is IDictionary<string, int> dict1 && !dict1.ContainsKey(itemObj.name))
                    dict1[itemObj.name] = 0;

                if (fPurchasedTotal?.GetValue(inst) is IDictionary<string, int> dict2 && !dict2.ContainsKey(itemObj.name))
                    dict2[itemObj.name] = 0;

                if (fUpgrades?.GetValue(inst) is IDictionary<string, int> dict3 && !dict3.ContainsKey(itemObj.name))
                    dict3[itemObj.name] = 0;

                if (fUpgradesTotal?.GetValue(inst) is IDictionary<string, int> dict4 && !dict4.ContainsKey(itemObj.name))
                    dict4[itemObj.name] = 0;
            }
            catch
            {
                // ignore
            }
        }

        private static void PatchSemiFuncStatGetItemsPurchasedIfPossible(Harmony harmony)
        {
            var tSemi = AccessTools.TypeByName("SemiFunc");
            if (tSemi == null)
                return;

            var m = AccessTools.Method(tSemi, "StatGetItemsPurchased", new[] { typeof(string) });
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
                var tStats = AccessTools.TypeByName("StatsManager");
                if (tStats == null)
                    return true;

                var inst = ReflectionHelper.GetStaticInstanceValue(tStats, "instance");
                if (inst == null)
                    return true;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var f = tStats.GetField("itemsPurchased", flags);
                if (f?.GetValue(inst) is IDictionary<string, int> dict)
                {
                    if (!dict.ContainsKey(itemName))
                        dict[itemName] = 0;
                    __result = dict[itemName];
                    return false;
                }
            }
            catch
            {
                // ignore
            }

            return true;
        }

        private static void PatchStatsManagerGetItemsUpgradesPurchasedIfPossible(Harmony harmony)
        {
            var tStats = AccessTools.TypeByName("StatsManager");
            if (tStats == null)
                return;

            var mGet = AccessTools.Method(tStats, "GetItemsUpgradesPurchased", new[] { typeof(string) });
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
                var tStats = AccessTools.TypeByName("StatsManager");
                if (tStats == null)
                    return true;

                var inst = ReflectionHelper.GetStaticInstanceValue(tStats, "instance");
                if (inst == null)
                    return true;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var f = tStats.GetField("itemsUpgradesPurchased", flags);
                if (f?.GetValue(inst) is IDictionary<string, int> dict)
                {
                    if (!dict.TryGetValue(itemName, out var value))
                    {
                        dict[itemName] = 0;
                        __result = 0;
                    }
                    else
                    {
                        __result = value;
                    }
                    return false;
                }
            }
            catch
            {
                // ignore
            }

            return true;
        }

        private static void PatchStatsManagerItemPurchaseIfPossible(Harmony harmony)
        {
            if (harmony == null)
                return;

            var tStats = AccessTools.TypeByName("StatsManager");
            if (tStats == null)
                return;

            var mPurchase = AccessTools.Method(tStats, "ItemPurchase", new[] { typeof(string) });
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
    }
}
