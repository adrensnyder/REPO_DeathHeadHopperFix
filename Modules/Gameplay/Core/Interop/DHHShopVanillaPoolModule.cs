#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;
using DeathHeadHopper.Managers;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    internal static class DHHShopVanillaPoolModule
    {
        private static bool _hooksApplied;
        private static ManualLogSource? _log;
        private static readonly HashSet<string> LoggedWarnings = new(StringComparer.OrdinalIgnoreCase);

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            if (harmony == null || _hooksApplied)
                return;

            try
            {
                Patch(harmony, AccessTools.Method(typeof(DHHPunManager), nameof(DHHPunManager.LoadShopAtticShelves)));
                Patch(harmony, AccessTools.Method(typeof(DHHPunManager), nameof(DHHPunManager.LoadShopAtticShelvesRPC)));
                Patch(harmony, AccessTools.Method(typeof(DHHShopManager), nameof(DHHShopManager.LoadShopAtticShelves)));
                Patch(harmony, AccessTools.Method(typeof(DHHShopManager), nameof(DHHShopManager.LoadItems)));
                Patch(harmony, AccessTools.Method(typeof(DHHShopManager), nameof(DHHShopManager.ShopPopulateItemVolumes), new[] { typeof(PunManager) }));
                PatchShopInitialize(harmony);
                PatchShopItemsCollection(harmony);

                _hooksApplied = true;
                _log?.LogInfo("[Fix:Shop] Original DeathHeadHopper custom shop flow disabled; vanilla shop pool orchestrator enabled.");
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix:Shop] Failed to disable original DeathHeadHopper custom shop flow: {ex.Message}");
            }
        }

        private static void Patch(Harmony harmony, MethodInfo? original)
        {
            if (original == null)
                return;

            var prefix = new HarmonyMethod(typeof(DHHShopVanillaPoolModule), nameof(BlockOriginalShopMethod_Prefix));
            harmony.Patch(original, prefix: prefix);
        }

        private static void PatchShopInitialize(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(ShopManager), nameof(ShopManager.ShopInitialize));
            if (original == null)
                return;

            var prefix = new HarmonyMethod(typeof(DHHShopVanillaPoolModule), nameof(ShopManager_ShopInitialize_Prefix));
            harmony.Patch(original, prefix: prefix);
        }

        private static void PatchShopItemsCollection(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(ShopManager), nameof(ShopManager.GetAllItemsFromStatsManager));
            if (original == null)
                return;

            var postfix = new HarmonyMethod(typeof(DHHShopVanillaPoolModule), nameof(ShopManager_GetAllItemsFromStatsManager_Postfix));
            harmony.Patch(original, postfix: postfix);
        }

        private static bool BlockOriginalShopMethod_Prefix()
        {
            return false;
        }

        private static void ShopManager_ShopInitialize_Prefix()
        {
            if (!SemiFunc.RunIsShop())
                return;

            EnsureDhhItemsInVanillaDictionary();
            ValidateDhhPrefabRefsForVanillaSpawn();
        }

        private static void ShopManager_GetAllItemsFromStatsManager_Postfix(ShopManager __instance)
        {
            if (__instance == null || SemiFunc.IsNotMasterClient())
                return;

            ApplyRelativeWeight(__instance.potentialItems, IsHeadChargerItem, FeatureFlags.HeadChargerShopWeightPercent);
            ApplyRelativeWeight(__instance.potentialItemUpgrades, IsDhhUpgradeItem, FeatureFlags.DHHUpgradesShopWeightPercent);

            if (FeatureFlags.DebugLogging)
            {
                var headEntries = __instance.potentialItems.Count(IsHeadChargerItem);
                var upgradeEntries = __instance.potentialItemUpgrades.Count(IsDhhUpgradeItem);
                _log?.LogInfo($"[Fix:Shop] DHH weighted entries headCharger={headEntries} upgrades={upgradeEntries} headWeight={FeatureFlags.HeadChargerShopWeightPercent}% upgradeWeight={FeatureFlags.DHHUpgradesShopWeightPercent}%");
            }
        }

        internal static void EnsureDhhItemsInVanillaDictionary()
        {
            var statsManager = StatsManager.instance;
            if (statsManager?.itemDictionary == null)
                return;

            var shopItems = DHHAssetManager.shopItems;
            if (shopItems == null || shopItems.Count == 0)
                return;

            int added = 0;
            foreach (var item in shopItems.Values.Where(x => x != null).GroupBy(x => x.GetInstanceID()).Select(x => x.First()))
            {
                NormalizeDhhShopItemForVanillaLists(item);

                var key = item.name;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (statsManager.itemDictionary.TryGetValue(key, out var existing))
                {
                    if (existing != item)
                        LogWarningOnce($"dictionary-conflict:{key}", $"[Fix:Shop] StatsManager.itemDictionary already contains key '{key}' for a different item. Keeping vanilla entry.");
                    continue;
                }

                if (statsManager.itemDictionary.Values.Any(existingItem => existingItem == item))
                    continue;

                statsManager.itemDictionary.Add(key, item);
                StatsModule.EnsureStatsEntriesForItem(item);
                added++;
            }

            if (FeatureFlags.DebugLogging && added > 0)
                _log?.LogInfo($"[Fix:Shop] Added {added} DeathHeadHopper item(s) to the vanilla shop dictionary.");
        }

        internal static void ValidateDhhPrefabRefsForVanillaSpawn()
        {
            var shopItems = DHHAssetManager.shopItems;
            if (shopItems == null || shopItems.Count == 0)
                return;

            foreach (var item in shopItems.Values.Where(x => x != null).GroupBy(x => x.GetInstanceID()).Select(x => x.First()))
            {
                NormalizeDhhShopItemForVanillaLists(item);

                var key = string.IsNullOrWhiteSpace(item.name) ? item.itemName : item.name;
                if (item.prefab == null)
                {
                    LogWarningOnce($"prefab-null:{key}", $"[Fix:Shop] DHH item '{key}' has no PrefabRef and cannot spawn through the vanilla shop.");
                    continue;
                }

                if (!item.prefab.IsValid())
                {
                    LogWarningOnce($"prefab-invalid:{key}", $"[Fix:Shop] DHH item '{key}' has an invalid PrefabRef resource path.");
                    continue;
                }

                if (SemiFunc.IsMultiplayer() && string.IsNullOrWhiteSpace(item.prefab.ResourcePath))
                    LogWarningOnce($"prefab-resourcepath:{key}", $"[Fix:Shop] DHH item '{key}' has an empty multiplayer ResourcePath.");

                if (!SemiFunc.IsMultiplayer() && RunManager.instance != null && item.prefab.Prefab == null)
                    LogWarningOnce($"prefab-singleplayer:{key}", $"[Fix:Shop] DHH item '{key}' could not resolve its singleplayer prefab.");
            }
        }

        private static void NormalizeDhhShopItemForVanillaLists(Item item)
        {
            if (item == null)
                return;

            if (IsHeadChargerItem(item))
            {
                item.itemType = SemiFunc.itemType.tool;
                item.itemSecretShopType = SemiFunc.itemSecretShopType.none;
                return;
            }

            if (IsDhhUpgradeItem(item))
            {
                item.itemType = SemiFunc.itemType.item_upgrade;
                item.itemSecretShopType = SemiFunc.itemSecretShopType.none;
            }
        }

        private static void ApplyRelativeWeight(List<Item> list, Predicate<Item> matcher, int percent)
        {
            if (list == null || matcher == null || percent == 100)
                return;

            var originalMatches = list.Where(item => item != null && matcher(item)).ToList();
            if (originalMatches.Count == 0)
                return;

            foreach (var item in originalMatches)
            {
                list.Remove(item);
            }

            var wholeCopies = Math.Max(0, percent / 100);
            var fractionalChance = Math.Max(0, percent % 100);
            foreach (var item in originalMatches)
            {
                for (var i = 0; i < wholeCopies; i++)
                    list.Add(item);

                if (fractionalChance > 0 && UnityEngine.Random.Range(0, 100) < fractionalChance)
                    list.Add(item);
            }

            list.Shuffle<Item>();
        }

        private static bool IsHeadChargerItem(Item item)
        {
            return GetDhhItemKey(item).Contains("head charger");
        }

        private static bool IsDhhUpgradeItem(Item item)
        {
            var key = GetDhhItemKey(item);
            return key.Contains("upgrade dhh charge") || key.Contains("upgrade dhh power");
        }

        private static string GetDhhItemKey(Item item)
        {
            return item == null ? string.Empty : $"{item.name} {item.itemName}".ToLowerInvariant();
        }

        private static void LogWarningOnce(string key, string message)
        {
            if (LoggedWarnings.Add(key))
                _log?.LogWarning(message);
        }
    }
}
