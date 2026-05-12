#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Items;
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
                PatchItemManagerGetPurchasedItems(harmony);
                PatchLevelGeneratorItemSetup(harmony);
                PatchShopInitialize(harmony);
                PatchShopItemsCollection(harmony);
                PatchUpgradeStandSelection(harmony);

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

        private static void PatchUpgradeStandSelection(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(UpgradeStand), nameof(UpgradeStand.GetWeightedUpgradeExcluding));

            if (original == null)
                return;

            var prefix = new HarmonyMethod(typeof(DHHShopVanillaPoolModule), nameof(UpgradeStand_GetWeightedUpgradeExcluding_Prefix));
            harmony.Patch(original, prefix: prefix);
        }

        private static void PatchLevelGeneratorItemSetup(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(LevelGenerator), nameof(LevelGenerator.ItemSetup));
            if (original == null)
                return;

            var postfix = new HarmonyMethod(typeof(DHHShopVanillaPoolModule), nameof(LevelGenerator_ItemSetup_Postfix));
            harmony.Patch(original, postfix: postfix);
        }

        private static void PatchItemManagerGetPurchasedItems(Harmony harmony)
        {
            var original = AccessTools.Method(typeof(ItemManager), nameof(ItemManager.GetPurchasedItems));
            if (original == null)
                return;

            var prefix = new HarmonyMethod(typeof(DHHShopVanillaPoolModule), nameof(ItemManager_GetPurchasedItems_Prefix));
            harmony.Patch(original, prefix: prefix);
        }

        private static bool BlockOriginalShopMethod_Prefix()
        {
            return false;
        }

        private static bool UpgradeStand_GetWeightedUpgradeExcluding_Prefix(
            Item excludeItem,
            Dictionary<string, int> displayedCounts,
            Dictionary<string, int> selectedDuringReroll,
            ref Item __result)
        {
            var mode = NormalizePoolMode(FeatureFlags.DHHUpgradesShopPoolMode);
            __result = GetWeightedUpgradeExcludingWithDhhMode(excludeItem, displayedCounts, selectedDuringReroll, mode)!;
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
            if (__instance == null)
                return;

            var beforeHeadCharger = __instance.potentialItems?.Count(IsHeadChargerItem) ?? 0;
            var beforeUpgrades = __instance.potentialItemUpgrades?.Count(IsDhhUpgradeItem) ?? 0;

            var potentialItems = __instance.potentialItems;
            if (potentialItems != null)
                ApplyPoolMode(potentialItems, IsHeadChargerItem, FeatureFlags.HeadChargerShopPoolMode);

            var potentialUpgrades = __instance.potentialItemUpgrades;
            if (potentialUpgrades != null)
                ApplyPoolMode(potentialUpgrades, IsDhhUpgradeItem, FeatureFlags.DHHUpgradesShopPoolMode);

            if (ShouldLogShopDebug())
            {
                var afterHeadCharger = __instance.potentialItems?.Count(IsHeadChargerItem) ?? 0;
                var afterUpgrades = __instance.potentialItemUpgrades?.Count(IsDhhUpgradeItem) ?? 0;
                _log?.LogInfo($"[Fix:Shop] Pool mode context mode={(SemiFunc.IsMultiplayer() ? "MP" : "SP")} headCharger before={beforeHeadCharger} after={afterHeadCharger} mode={FeatureFlags.HeadChargerShopPoolMode} upgrades before={beforeUpgrades} after={afterUpgrades} mode={FeatureFlags.DHHUpgradesShopPoolMode}");
            }
        }

        private static void LevelGenerator_ItemSetup_Postfix()
        {
            EnsureDhhItemsInVanillaDictionary();
            ValidateDhhPrefabRefsForVanillaSpawn();
        }

        private static void ItemManager_GetPurchasedItems_Prefix()
        {
            EnsureDhhItemsInVanillaDictionary();
            ValidateDhhPrefabRefsForVanillaSpawn();
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

            if (ShouldLogShopDebug() && added > 0)
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

        private static void ApplyPoolMode(List<Item> list, Predicate<Item> matcher, string mode)
        {
            if (list == null || matcher == null)
                return;

            mode = NormalizePoolMode(mode);
            if (string.Equals(mode, FeatureFlags.ShopPoolModes.Default, StringComparison.OrdinalIgnoreCase))
            {
                ApplyDefaultPoolMode(list, matcher);
                return;
            }

            var originalMatches = list.Where(item => item != null && matcher(item)).ToList();
            if (originalMatches.Count == 0)
                return;

            var originalCount = originalMatches.Count;
            foreach (var item in originalMatches)
            {
                list.Remove(item);
            }

            var addedCopies = 0;
            if (string.Equals(mode, FeatureFlags.ShopPoolModes.Reduced, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in originalMatches.GroupBy(item => item.GetInstanceID()).Select(group => group.First()))
                {
                    list.Add(item);
                    addedCopies++;
                }
            }

            if (ShouldLogShopDebug())
            {
                var runtimeMode = SemiFunc.IsMultiplayer() ? "MP" : "SP";
                _log?.LogInfo($"[Fix:Shop] ApplyPoolMode mode={runtimeMode} matcher={matcher.Method.Name} original={originalCount} removed={originalCount} poolMode={mode} addedCopies={addedCopies} final={list.Count(item => item != null && matcher(item))}");
            }

            list.Shuffle<Item>();
        }

        private static void ApplyDefaultPoolMode(List<Item> list, Predicate<Item> matcher)
        {
            var originalMatches = list.Where(item => item != null && matcher(item)).ToList();
            if (originalMatches.Count == 0)
                return;

            var balancedCopies = GetBalancedPoolCopyCount(list, matcher);
            var groups = originalMatches
                .GroupBy(item => item.GetInstanceID())
                .Select(group => group.ToList())
                .ToList();

            var originalCount = originalMatches.Count;
            var targetCount = groups.Sum(group => Math.Min(group.Count, balancedCopies));
            if (targetCount >= originalCount)
                return;

            foreach (var item in originalMatches)
            {
                list.Remove(item);
            }

            var addedCopies = 0;
            foreach (var group in groups)
            {
                var copies = Math.Min(group.Count, balancedCopies);
                for (var i = 0; i < copies; i++)
                {
                    list.Add(group[0]);
                    addedCopies++;
                }
            }

            if (ShouldLogShopDebug())
            {
                var runtimeMode = SemiFunc.IsMultiplayer() ? "MP" : "SP";
                _log?.LogInfo($"[Fix:Shop] ApplyDefaultPoolMode mode={runtimeMode} matcher={matcher.Method.Name} original={originalCount} balancedCopies={balancedCopies} addedCopies={addedCopies} final={list.Count(item => item != null && matcher(item))}");
            }

            list.Shuffle<Item>();
        }

        private static int GetBalancedPoolCopyCount(List<Item> list, Predicate<Item> matcher)
        {
            var counts = list
                .Where(item => item != null && !matcher(item))
                .GroupBy(item => item.GetInstanceID())
                .Select(group => group.Count())
                .Where(count => count > 0)
                .OrderBy(count => count)
                .ToList();

            if (counts.Count == 0)
                return 1;

            return Math.Max(1, counts[counts.Count / 2]);
        }

        private static string NormalizePoolMode(string mode)
        {
            if (string.Equals(mode, FeatureFlags.ShopPoolModes.Disabled, StringComparison.OrdinalIgnoreCase))
                return FeatureFlags.ShopPoolModes.Disabled;

            if (string.Equals(mode, FeatureFlags.ShopPoolModes.Reduced, StringComparison.OrdinalIgnoreCase))
                return FeatureFlags.ShopPoolModes.Reduced;

            return FeatureFlags.ShopPoolModes.Default;
        }

        private static Item? GetWeightedUpgradeExcludingWithDhhMode(
            Item excludeItem,
            Dictionary<string, int>? displayedCounts,
            Dictionary<string, int>? selectedDuringReroll,
            string mode)
        {
            var stats = StatsManager.instance;
            if (stats?.itemDictionary == null)
                return null;

            var shop = ShopManager.instance;
            if (shop == null)
                return null;

            var candidates = new List<Item>();
            var currency = SemiFunc.StatGetRunCurrency();
            var allItems = stats.itemDictionary.Values
                .Where(item => item != null)
                .GroupBy(item => item.GetInstanceID())
                .Select(group => group.First())
                .ToList();
            var balancedDhhUpgradeMaxAmount = GetBalancedUpgradeStandMaxAmount(allItems);

            foreach (var item in allItems)
            {
                if (item.itemType != SemiFunc.itemType.item_upgrade || item == excludeItem)
                    continue;

                var isDhhUpgrade = IsDhhUpgradeItem(item);
                if (isDhhUpgrade && string.Equals(mode, FeatureFlags.ShopPoolModes.Disabled, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsUpgradeStandCandidateAllowed(item, isDhhUpgrade, displayedCounts, selectedDuringReroll, currency, shop, mode, balancedDhhUpgradeMaxAmount))
                    continue;

                candidates.Add(item);
            }

            return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private static bool IsUpgradeStandCandidateAllowed(
            Item item,
            bool isDhhUpgrade,
            Dictionary<string, int>? displayedCounts,
            Dictionary<string, int>? selectedDuringReroll,
            int currency,
            ShopManager shop,
            string mode,
            int balancedDhhUpgradeMaxAmount)
        {
            var name = item.name;
            var purchased = StatsManager.instance.GetItemsUpgradesPurchased(name);
            var displayed = displayedCounts != null && displayedCounts.TryGetValue(name, out var displayedValue) ? displayedValue : 0;
            var selected = selectedDuringReroll != null && selectedDuringReroll.TryGetValue(name, out var selectedValue) ? selectedValue : 0;
            var totalVisibleOrBought = purchased + displayed + selected;
            var maxAllowedInShop = GetEffectiveUpgradeStandMaxAmount(item, isDhhUpgrade, mode, balancedDhhUpgradeMaxAmount);

            if (maxAllowedInShop > 0 && totalVisibleOrBought >= maxAllowedInShop)
                return false;

            if (item.maxPurchase && StatsManager.instance.GetItemsUpgradesPurchasedTotal(name) >= item.maxPurchaseAmount)
                return false;

            if (item.minPlayerCount > 1 && GameDirector.instance.PlayerList.Count < item.minPlayerCount)
                return false;

            var value = shop.UpgradeValueGet(item.value.valueMax / 1000f * 4f, item);
            return value <= currency || UnityEngine.Random.Range(0, 4) == 0;
        }

        private static int GetEffectiveUpgradeStandMaxAmount(Item item, bool isDhhUpgrade, string mode, int balancedDhhUpgradeMaxAmount)
        {
            if (!isDhhUpgrade)
                return item.maxAmountInShop;

            if (string.Equals(mode, FeatureFlags.ShopPoolModes.Reduced, StringComparison.OrdinalIgnoreCase))
                return 1;

            if (string.Equals(mode, FeatureFlags.ShopPoolModes.Default, StringComparison.OrdinalIgnoreCase))
                return Math.Min(item.maxAmountInShop <= 0 ? balancedDhhUpgradeMaxAmount : item.maxAmountInShop, balancedDhhUpgradeMaxAmount);

            return item.maxAmountInShop;
        }

        private static int GetBalancedUpgradeStandMaxAmount(List<Item> allItems)
        {
            var maxAmounts = allItems
                .Where(item => item != null && item.itemType == SemiFunc.itemType.item_upgrade && !IsDhhUpgradeItem(item) && item.maxAmountInShop > 0)
                .Select(item => item.maxAmountInShop)
                .OrderBy(amount => amount)
                .ToList();

            if (maxAmounts.Count == 0)
                return 1;

            return Math.Max(1, maxAmounts[maxAmounts.Count / 2]);
        }

        private static bool IsHeadChargerItem(Item item)
        {
            var prefab = TryGetPrefab(item);
            if (prefab != null && (prefab.GetComponent<DHHItemHeadCharger>() != null || prefab.GetComponentInChildren<DHHItemHeadCharger>(true) != null))
                return true;

            var key = GetDhhItemKey(item);
            return key.Contains("head charger") || key.Contains("head charge");
        }

        private static bool IsDhhUpgradeItem(Item item)
        {
            var key = GetDhhItemKey(item);
            return key.Contains("upgrade dhh charge") || key.Contains("upgrade dhh power");
        }

        private static string GetDhhItemKey(Item item)
        {
            if (item == null)
                return string.Empty;

            var prefabName = item.prefab != null ? item.prefab.PrefabName : string.Empty;
            return $"{item.name} {item.itemName} {prefabName}".ToLowerInvariant();
        }

        private static UnityEngine.GameObject? TryGetPrefab(Item item)
        {
            if (item?.prefab == null || RunManager.instance == null)
                return null;

            try
            {
                return item.prefab.Prefab;
            }
            catch
            {
                return null;
            }
        }

        private static void LogWarningOnce(string key, string message)
        {
            if (LoggedWarnings.Add(key))
                _log?.LogWarning(message);
        }

        private static bool ShouldLogShopDebug()
        {
            return FeatureFlags.DebugLogging;
        }
    }
}
