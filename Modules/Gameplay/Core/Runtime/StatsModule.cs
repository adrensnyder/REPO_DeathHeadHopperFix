#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopper.DeathHead;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Managers;
using HarmonyLib;
using REPOLib.Modules;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Runtime
{
    internal static class StatsModule
    {
        private const string HeadChargeUpgradeId = "HeadCharge";
        private const string HeadPowerUpgradeId = "HeadPower";

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

        internal static void RegisterDhhRepolibUpgrade(Item itemObj)
        {
            try
            {
                if (itemObj == null)
                    return;

                if (!TryResolveDhhUpgrade(itemObj, out var upgradeId, out var isChargeUpgrade))
                    return;

                if (Upgrades.TryGetUpgrade(upgradeId, out _))
                {
                    EnsureDhhUpgradeKey(upgradeId);
                    return;
                }

                Upgrades.RegisterUpgrade(
                    upgradeId,
                    itemObj,
                    null,
                    (avatar, level) => ApplyLocalDhhUpgradeEffect(avatar, level, isChargeUpgrade));

                EnsureDhhUpgradeKey(upgradeId);
            }
            catch
            {
                // Registration is best-effort. The item itself remains usable even if REPOLib metadata cannot be wired.
            }
        }

        internal static bool TryIncreaseDhhUpgrade(string playerId, bool isChargeUpgrade, out int level)
        {
            level = 0;
            if (string.IsNullOrWhiteSpace(playerId))
                return false;

            var upgradeId = isChargeUpgrade ? HeadChargeUpgradeId : HeadPowerUpgradeId;
            if (!Upgrades.TryGetUpgrade(upgradeId, out var upgrade))
                return false;

            EnsureDhhUpgradeKey(upgradeId);
            level = upgrade.AddLevel(playerId);
            return true;
        }

        internal static int GetDhhUpgradeLevel(string playerId, bool isChargeUpgrade)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return 0;

            var upgradeId = isChargeUpgrade ? HeadChargeUpgradeId : HeadPowerUpgradeId;
            if (!Upgrades.TryGetUpgrade(upgradeId, out var upgrade))
                return 0;

            EnsureDhhUpgradeKey(upgradeId);
            return upgrade.GetLevel(playerId);
        }

        internal static void SeedDhhUpgradeKeys()
        {
            EnsureDhhUpgradeKey(HeadChargeUpgradeId);
            EnsureDhhUpgradeKey(HeadPowerUpgradeId);
        }

        private static void EnsureDhhUpgradeKey(string upgradeId)
        {
            var stats = StatsManager.instance;
            if (stats == null || stats.dictionaryOfDictionaries == null)
                return;

            if (!Upgrades.TryGetUpgrade(upgradeId, out var upgrade))
                return;

            var key = "playerUpgrade" + upgradeId;
            stats.dictionaryOfDictionaries[key] = upgrade.PlayerDictionary;
        }

        private static bool TryResolveDhhUpgrade(Item itemObj, out string upgradeId, out bool isChargeUpgrade)
        {
            upgradeId = string.Empty;
            isChargeUpgrade = false;

            var name = itemObj?.itemName ?? itemObj?.name ?? string.Empty;
            if (name.IndexOf("upgrade dhh charge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("head charge", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                upgradeId = HeadChargeUpgradeId;
                isChargeUpgrade = true;
                return true;
            }

            if (name.IndexOf("upgrade dhh power", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("head power", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                upgradeId = HeadPowerUpgradeId;
                return true;
            }

            return false;
        }

        private static void ApplyLocalDhhUpgradeEffect(PlayerAvatar avatar, int level, bool isChargeUpgrade)
        {
            var localAvatar = PlayerAvatar.instance;
            if (avatar == null || localAvatar == null)
                return;

            if (!string.Equals(avatar.steamID, localAvatar.steamID, StringComparison.Ordinal))
                return;

            if (isChargeUpgrade)
            {
                DHHAbilityManager.instance?.EquipAbilities();
                return;
            }

            var playerDeathHead = localAvatar.playerDeathHead;
            AbilityEnergyHandler? abilityEnergyHandler = null;
            if (playerDeathHead != null)
            {
                var component = playerDeathHead.GetComponent<DeathHeadController>();
                abilityEnergyHandler = component != null ? component.abilityEnergyHandler : null;
            }

            if (abilityEnergyHandler != null)
            {
                abilityEnergyHandler.IncreaseEnergy(abilityEnergyHandler.energyIncrease);
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

