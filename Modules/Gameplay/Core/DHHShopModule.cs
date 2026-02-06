#nullable enable

using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    internal static class DHHShopModule
    {
        private static bool _hooksApplied;
        private static ManualLogSource? _log;
        private static FieldInfo? _itemVolumesField;
        private static FieldInfo? _potentialItemsField;
        private static MethodInfo? _spawnShopItemMethod;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            if (harmony == null || _hooksApplied)
                return;

            var shopManagerType = asm.GetType("DeathHeadHopper.Managers.DHHShopManager", throwOnError: false);
            if (shopManagerType == null)
                return;

            _itemVolumesField ??= AccessTools.Field(shopManagerType, "itemVolumes");
            _potentialItemsField ??= AccessTools.Field(shopManagerType, "potentialItems");
            _spawnShopItemMethod ??= AccessTools.Method(typeof(PunManager), "SpawnShopItem", new[] { typeof(ItemVolume), typeof(List<Item>), typeof(int).MakeByRefType(), typeof(bool) });

            var populateMethod = AccessTools.Method(shopManagerType, "ShopPopulateItemVolumes", new[] { typeof(PunManager) });
            if (populateMethod != null)
            {
                var prefix = typeof(DHHShopModule).GetMethod(nameof(ShopPopulateItemVolumes_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix != null)
                {
                    harmony.Patch(populateMethod, prefix: new HarmonyMethod(prefix));
                }
            }

            _hooksApplied = true;
        }

        private static bool ShopPopulateItemVolumes_Prefix(object punManager)
        {
            if (SemiFunc.IsNotMasterClient())
            {
                return false;
            }

            var volumes = _itemVolumesField?.GetValue(null) as IList<ItemVolume>;
            var items = _potentialItemsField?.GetValue(null) as List<Item>;
            if (volumes == null || items == null)
            {
                return true;
            }

            if (_spawnShopItemMethod == null)
            {
                return true;
            }

            int spawned = 0;
            int limit = FeatureFlags.DHHShopMaxItems < 0 ? int.MaxValue : FeatureFlags.DHHShopMaxItems;
            float slotChance = Mathf.Clamp01(FeatureFlags.DHHShopSpawnChance);
            float itemChance = Mathf.Clamp01(FeatureFlags.ShopItemsSpawnChance);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHShop.Populate", 30))
            {
                _log?.LogInfo($"[Fix:DHHShop] Populate volumes={volumes.Count} items={items.Count} limit={limit} slotChance={slotChance:F3} itemChance={itemChance:F3}");
            }

            foreach (var volume in volumes)
            {
                if (volume == null)
                {
                    continue;
                }

                if (spawned >= limit)
                {
                    break;
                }

                var slotRoll = 1f;
                if (slotChance < 1f)
                {
                    slotRoll = UnityEngine.Random.value;
                    if (slotRoll > slotChance)
                    {
                        if (FeatureFlags.DebugLogging)
                            _log?.LogInfo($"[Fix:DHHShop] Slot skip '{volume.name}' slotRoll={slotRoll:F3} slotChance={slotChance:F3} spawned={spawned}");
                        continue;
                    }
                }

                // English addition: extra gate that checks ShopItemsSpawnChance once a slot has been selected.
                var itemRoll = 1f;
                if (itemChance < 1f)
                {
                    itemRoll = UnityEngine.Random.value;
                    if (itemRoll > itemChance)
                    {
                        if (FeatureFlags.DebugLogging)
                            _log?.LogInfo($"[Fix:DHHShop] Item skip '{volume.name}' itemRoll={itemRoll:F3} itemChance={itemChance:F3} spawned={spawned}");
                        continue;
                    }
                }

                if (FeatureFlags.DebugLogging)
                {
                    _log?.LogInfo($"[Fix:DHHShop] Spawn '{volume.name}' slotRoll={slotRoll:F3} itemRoll={itemRoll:F3} slotChance={slotChance:F3} itemChance={itemChance:F3} spawned={spawned}");
                }

                var args = new object?[] { volume, items, spawned, false };
                var result = _spawnShopItemMethod.Invoke(punManager, args);
                spawned = (int)args[2]!;
                if (limit != int.MaxValue && spawned >= limit)
                {
                    break;
                }
            }

            return false;
        }
    }
}




