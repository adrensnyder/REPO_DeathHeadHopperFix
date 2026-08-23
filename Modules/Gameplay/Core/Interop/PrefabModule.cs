#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DeathHeadHopper.Managers;
using DeathHeadHopper.Items;
using HarmonyLib;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;
using DeathHeadHopperFix.Modules.Utilities;
using Photon.Pun;
using REPOLib.Modules;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    internal static class PrefabModule
    {
        private static readonly Dictionary<string, GameObject> PendingPool = new(StringComparer.OrdinalIgnoreCase);
        private static AssetBundle? _dhhBundle;
        private static ManualLogSource? _log;
        private static readonly HashSet<string> _knownPrefabKeys = new(StringComparer.OrdinalIgnoreCase);

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            if (harmony == null || asm == null)
                return;

            PatchDhhAssetManagerIfPossible(harmony);
            PatchRunManagerAwakeIfPossible(harmony);
            PatchMultiplayerPoolIfPossible(harmony);
            PatchPhotonDefaultPoolIfPossible(harmony);
        }

        private static void PatchDhhAssetManagerIfPossible(Harmony harmony)
        {
            var mLoadAssets = AccessTools.Method(typeof(DHHAssetManager), nameof(DHHAssetManager.LoadAssets));
            if (mLoadAssets == null)
                return;

            harmony.Patch(mLoadAssets, prefix: new HarmonyMethod(typeof(PrefabModule), nameof(DHHAssetManager_LoadAssets_Prefix)));
        }

        private static bool DHHAssetManager_LoadAssets_Prefix()
        {
            try
            {
                var bundlePath = Path.Combine(BepInEx.Paths.PluginPath, "Cronchy-DeathHeadHopper", "deathheadhopper");
                if (!File.Exists(bundlePath))
                {
                    _log?.LogError($"AssetBundle not found at: {bundlePath}");
                    return false;
                }

                AssetBundle? bundle = _dhhBundle;
                if (bundle != null)
                {
                    _log?.LogInfo("[Fix] Reusing already loaded AssetBundle instance.");
                }
                else
                {
                    var knownAssetPath = "Assets/DeathHeadHopper/Materials/Head Phase.mat";
                    foreach (var loaded in AssetBundle.GetAllLoadedAssetBundles())
                    {
                        if (loaded == null)
                            continue;

                        if (string.Equals(loaded.name, "deathheadhopper", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(loaded.name, Path.GetFileName(bundlePath), StringComparison.OrdinalIgnoreCase) ||
                            loaded.Contains(knownAssetPath) ||
                            LooksLikeDhhBundle(loaded))
                        {
                            bundle = loaded;
                            break;
                        }
                    }

                    if (bundle != null)
                    {
                        _log?.LogInfo("[Fix] Found already loaded DeathHeadHopper AssetBundle.");
                    }
                    else
                    {
                        _log?.LogInfo($"[Fix] Loading AssetBundle from: {bundlePath}");
                        bundle = AssetBundle.LoadFromFile(bundlePath);
                        if (bundle == null)
                        {
                            _log?.LogError("[Fix] AssetBundle.LoadFromFile returned null.");
                            return false;
                        }
                    }

                    if (bundle == null)
                    {
                        _log?.LogError("[Fix] AssetBundle not available.");
                        return false;
                    }

                    _dhhBundle = bundle;
                }

                DHHAssetManager.headPhaseMaterial = bundle.LoadAsset<Material>("Assets/DeathHeadHopper/Materials/Head Phase.mat");

                LoadItemsCompatible(bundle);

                DHHAssetManager.LoadAbilities(bundle);
                DHHAssetManager.LoadChargeAssets(bundle);

                DHHUIManager.abilityUIPrefab = bundle.LoadAsset<GameObject>("Assets/DeathHeadHopper/Ability UI.prefab");

                _log?.LogInfo("[Fix] LoadAssets compatible flow completed.");

            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }

            return false;
        }

        private static void LoadItemsCompatible(AssetBundle bundle)
        {
            var shopItemsDict = DHHAssetManager.shopItems;
            shopItemsDict.Clear();

            var assetNames = bundle.GetAllAssetNames()
                .Where(x => x.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                            x.IndexOf("/items/", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            _log?.LogInfo($"[Fix] Found {assetNames.Count} item assets in bundle.");

            foreach (var itemAssetPath in assetNames)
            {
                Item? itemObj = null;
                try
                {
                    itemObj = bundle.LoadAsset<Item>(itemAssetPath);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"[Fix] Exception loading item asset '{itemAssetPath}': {ex}");
                }

                if (itemObj == null)
                {
                    _log?.LogError($"[Fix] Failed to load item asset: {itemAssetPath}");
                    continue;
                }

                var prefabPath = itemAssetPath.Replace(".asset", ".prefab");
                var prefab = bundle.LoadAsset<GameObject>(prefabPath);
                if (prefab == null)
                {
                    _log?.LogError($"[Fix] Failed to load item prefab: {prefabPath}");
                    continue;
                }

                itemObj.prefab ??= new PrefabRef();
                itemObj.prefab.SetPrefab(prefab, prefabPath);

                var key = itemObj.name;

                CachePrefabEntry(prefabPath, prefab);

                var prefabFileName = Path.GetFileName(prefabPath);
                if (!string.IsNullOrEmpty(prefabFileName))
                    CachePrefabEntry(prefabFileName, prefab);

                CachePrefabEntry(key, prefab);
                CachePrefabEntry($"Items/{key}", prefab);

                CacheShopItemKey(shopItemsDict, key, itemObj);

                DhhUpgradeOrchestrator.DisableLegacyToggleListeners(prefab);
                TryRegisterItemWithRepolib(itemObj, prefab);
                TryRegisterUpgradeWithRepolib(itemObj, prefab);
                EnsureStatsItemDictionaryEntry(itemObj);
                StatsModule.EnsureStatsEntriesForItem(itemObj);

                _log?.LogInfo($"[Fix] Loaded item '{key}' from '{itemAssetPath}' and bound prefab '{prefabPath}'.");
            }
        }

        private static void PatchRunManagerAwakeIfPossible(Harmony harmony)
        {
            var mAwake = AccessTools.Method(typeof(RunManager), nameof(RunManager.Awake));
            if (mAwake == null)
                return;

            var pi = Harmony.GetPatchInfo(mAwake);
            if (pi != null && pi.Postfixes.Any(p => p.owner == harmony.Id))
                return;

            harmony.Patch(mAwake, postfix: new HarmonyMethod(typeof(PrefabModule), nameof(RunManager_Awake_Postfix)));
        }

        private static void RunManager_Awake_Postfix()
        {
            try
            {
                TryInjectPendingPool();
            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }
        }

        private static void TryInjectPendingPool()
        {
            if (PendingPool.Count == 0)
                return;

            var runMgr = RunManager.instance;
            if (runMgr == null)
                return;

            // The current game snapshot exposes RunManager.singleplayerPool as Dictionary<string, UnityEngine.Object>.
            Dictionary<string, UnityEngine.Object> pool = runMgr.singleplayerPool;

            int added = 0;
            foreach (var kv in PendingPool.ToList())
            {
                if (!pool.ContainsKey(kv.Key) && kv.Value != null)
                {
                    pool[kv.Key] = kv.Value;
                    added++;
                }

                _log?.LogDebug($"[Fix] RunManager cache already has '{kv.Key}'? {pool.ContainsKey(kv.Key)}");
            }

            if (added > 0)
                _log?.LogInfo($"[Fix] Injected {added} prefabs into RunManager.singleplayerPool.");
        }

        private static void PatchPhotonDefaultPoolIfPossible(Harmony harmony)
        {
            var mInstantiate = AccessTools.Method(typeof(DefaultPool), nameof(DefaultPool.Instantiate), new[] { typeof(string), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion) });
            if (mInstantiate == null)
                return;

            harmony.Patch(mInstantiate, prefix: new HarmonyMethod(typeof(PrefabModule), nameof(DefaultPool_Instantiate_Prefix)));
        }

        private static void PatchMultiplayerPoolIfPossible(Harmony harmony)
        {
            var mInstantiate = AccessTools.Method(typeof(MultiplayerPool), nameof(MultiplayerPool.Instantiate), new[] { typeof(string), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion) });
            if (mInstantiate == null)
                return;

            harmony.Patch(mInstantiate, prefix: new HarmonyMethod(typeof(PrefabModule), nameof(MultiplayerPool_Instantiate_Prefix)));
        }

        private static bool MultiplayerPool_Instantiate_Prefix(MultiplayerPool __instance, string prefabId, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, ref GameObject __result)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return true;

            if (!TryGetPendingPrefab(prefabId, out var prefab, out var normalized) || prefab == null)
                return true;

            if (__instance != null && !__instance.ResourceCache.ContainsKey(prefabId))
                __instance.ResourceCache[prefabId] = prefab;

            __result = UnityEngine.Object.Instantiate(prefab, position, rotation);
            __result.SetActive(false);
            _log?.LogInfo($"[Fix] MultiplayerPool cached prefab '{prefabId}' (normalized '{normalized}')");
            return false;
        }

        private static bool DefaultPool_Instantiate_Prefix(string prefabId, UnityEngine.Vector3 position, UnityEngine.Quaternion rotation, ref GameObject __result)
        {
            var normalizedId = NormalizePrefabKey(prefabId);
            if (string.IsNullOrEmpty(normalizedId))
                return true;

            if (TryGetPendingPrefab(prefabId, out var prefab, out var normalized) && prefab != null)
            {
                __result = UnityEngine.Object.Instantiate(prefab, position, rotation);
                __result.SetActive(false);
                _log?.LogInfo($"[Fix] DefaultPool cached prefab '{prefabId}' (normalized '{normalized}')");
                return false;
            }

            if (TryLoadPrefabFromBundle(prefabId, out prefab) && prefab != null)
            {
                __result = UnityEngine.Object.Instantiate(prefab, position, rotation);
                __result.SetActive(false);
                _log?.LogInfo($"[Fix] DefaultPool loaded prefab '{prefabId}' from bundle.");
                return false;
            }

            if (FeatureFlags.DebugLogging && IsKnownModPrefab(prefabId, normalizedId))
                _log?.LogWarning($"[Fix] DefaultPool missing cached prefab '{prefabId}' (normalized '{normalizedId}')");
            return true;
        }

        private static bool TryGetPendingPrefab(string prefabId, out GameObject? prefab, out string normalized)
        {
            prefab = null;
            normalized = NormalizePrefabKey(prefabId);
            if (string.IsNullOrEmpty(normalized))
                return false;

            if (PendingPool.TryGetValue(prefabId, out prefab))
                return true;

            if (!string.Equals(prefabId, normalized, StringComparison.Ordinal) && PendingPool.TryGetValue(normalized, out prefab))
                return true;

            return false;
        }

        private static void CachePrefabEntry(string? key, GameObject? prefab)
        {
            if (prefab == null || string.IsNullOrWhiteSpace(key))
                return;

            var actualKey = key!;
            var actualPrefab = prefab!;

            PendingPool[actualKey] = actualPrefab;
            var normalized = NormalizePrefabKey(key);
            if (!string.Equals(normalized, actualKey, StringComparison.Ordinal))
                PendingPool[normalized] = actualPrefab;

            _log?.LogInfo($"[Fix] Cached prefab '{actualKey}' as normalized '{normalized}'");
            AddKnownPrefabKey(actualKey);
            AddKnownPrefabKey(normalized);
        }

        private static void AddKnownPrefabKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            _knownPrefabKeys.Add(key!);
        }

        private static bool ContainsKnownPrefabKey(string? key)
        {
            return !string.IsNullOrEmpty(key) && _knownPrefabKeys.Contains(key!);
        }

        private static bool IsKnownModPrefab(string? prefabId, string normalizedId)
        {
            return ContainsKnownPrefabKey(prefabId) || ContainsKnownPrefabKey(normalizedId);
        }

        private static bool TryLoadPrefabFromBundle(string prefabId, out GameObject? prefab)
        {
            prefab = null;
            if (_dhhBundle == null)
                return false;

            var candidates = new List<string?>
            {
                prefabId,
                prefabId?.TrimStart('/')
            };

            var normalized = NormalizePrefabKey(prefabId);
            if (!string.IsNullOrEmpty(normalized))
                candidates.Add(normalized);

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;

                try
                {
                    var loaded = _dhhBundle.LoadAsset<GameObject>(candidate);
                    if (loaded != null)
                    {
                        prefab = loaded;
                        if (!string.IsNullOrWhiteSpace(prefabId))
                        {
                            CachePrefabEntry(prefabId, prefab);
                        }
                        return true;
                    }
                }
                catch
                {
                    // Continue probing remaining candidate names in the bundle.
                }
            }

            return false;
        }

        private static string NormalizePrefabKey(string? key)
        {
            var trimmed = key?.Trim();
            return string.IsNullOrEmpty(trimmed) ? string.Empty : trimmed!.ToLowerInvariant();
        }

        private static void CacheShopItemKey(IDictionary<string, Item> dict, string? key, Item value)
        {
            if (value == null || string.IsNullOrWhiteSpace(key))
                return;

            var actualKey = key!;
            if (dict.ContainsKey(actualKey))
                dict[actualKey] = value;
            else
                dict.Add(actualKey, value);
        }

        private static void EnsureStatsItemDictionaryEntry(Item itemObj)
        {
            if (itemObj == null)
                return;

            var stats = StatsManager.instance;
            if (stats == null || stats.itemDictionary == null)
                return;

            var key = itemObj.name;
            if (string.IsNullOrWhiteSpace(key))
                return;

            if (stats.itemDictionary.TryGetValue(key, out var existing) && existing == itemObj)
                return;

            stats.itemDictionary[key] = itemObj;
        }

        private static void TryRegisterItemWithRepolib(Item itemObj, GameObject prefab)
        {
            try
            {
                var itemAttributes = prefab != null ? prefab.GetComponent<ItemAttributes>() ?? prefab.GetComponentInChildren<ItemAttributes>() : null;
                if (itemAttributes == null)
                {
                    _log?.LogWarning($"[Fix] REPOLib RegisterItem skipped for '{itemObj.name}': prefab has no ItemAttributes.");
                    return;
                }

                if (itemAttributes.item == null)
                    itemAttributes.item = itemObj;

                Items.RegisterItem(itemAttributes);
            }
            catch (Exception ex)
            {
                if (LogLimiter.ShouldLog($"Fix:REPOLib.RegisterItem:{itemObj?.name}", 600))
                    _log?.LogWarning($"[Fix] REPOLib RegisterItem failed for '{itemObj?.name}': {ex.Message}");
            }
        }

        private static void TryRegisterUpgradeWithRepolib(Item itemObj, GameObject prefab)
        {
            if (itemObj == null || prefab == null)
                return;

            try
            {
                var hasChargeUpgrade = DhhUpgradeOrchestrator.HasChargeUpgrade(prefab);
                var hasPowerUpgrade = DhhUpgradeOrchestrator.HasPowerUpgrade(prefab);

                if (!hasChargeUpgrade && !hasPowerUpgrade)
                    return;

                if (hasChargeUpgrade)
                    RegisterOrBindUpgrade("HeadCharge", itemObj, isChargeUpgrade: true);

                if (hasPowerUpgrade)
                    RegisterOrBindUpgrade("HeadPower", itemObj, isChargeUpgrade: false);
            }
            catch (Exception ex)
            {
                if (LogLimiter.ShouldLog($"Fix:REPOLib.RegisterUpgrade:{itemObj?.name}", 600))
                    _log?.LogWarning($"[Fix] REPOLib RegisterUpgrade failed for '{itemObj?.name}': {ex.Message}");
            }
        }

        private static void RegisterOrBindUpgrade(string upgradeId, Item itemObj, bool isChargeUpgrade)
        {
            var upgrade = Upgrades.GetUpgrade(upgradeId);
            if (upgrade == null)
            {
                upgrade = Upgrades.RegisterUpgrade(upgradeId, itemObj, null, null);
                if (upgrade == null)
                    return;
            }

            var dhhStats = DHHStatsManager.instance;
            if (dhhStats == null)
                return;

            upgrade.PlayerDictionary = isChargeUpgrade
                ? dhhStats.playerUpgradeHeadCharge
                : dhhStats.playerUpgradeHeadPower;
        }

        private static bool LooksLikeDhhBundle(AssetBundle bundle)
        {
            try
            {
                foreach (var assetName in bundle.GetAllAssetNames())
                {
                    if (assetName.IndexOf("deathheadhopper", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    if (assetName.IndexOf("head phase", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch
            {
                // Asset enumeration may fail on invalid bundles; treat as non-matching bundle.
            }

            return false;
        }
    }
}

