#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
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

            PatchDhhAssetManagerIfPossible(harmony, asm);
            PatchDhhShopManagerIfPossible(harmony, asm);
            PatchRunManagerAwakeIfPossible(harmony);
            PatchPhotonDefaultPoolIfPossible(harmony);
        }

        private static void PatchDhhAssetManagerIfPossible(Harmony harmony, Assembly asm)
        {
            var tAssetMgr = asm.GetType("DeathHeadHopper.Managers.DHHAssetManager", throwOnError: false);
            if (tAssetMgr == null)
                return;

            var mLoadAssets = AccessTools.Method(tAssetMgr, "LoadAssets");
            if (mLoadAssets == null)
                return;

            var prefix = typeof(PrefabModule).GetMethod(nameof(DHHAssetManager_LoadAssets_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mLoadAssets, prefix: new HarmonyMethod(prefix));
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

                SetStaticFieldIfExists(
                    "DeathHeadHopper.Managers.DHHAssetManager",
                    "headPhaseMaterial",
                    bundle.LoadAsset<Material>("Assets/DeathHeadHopper/Materials/Head Phase.mat"));

                LoadItemsCompatible(bundle);

                InvokeStaticIfExists("DeathHeadHopper.Managers.DHHAssetManager", "LoadAbilities", bundle);
                InvokeStaticIfExists("DeathHeadHopper.Managers.DHHAssetManager", "LoadChargeAssets", bundle);

                SetStaticFieldIfExists(
                    "DeathHeadHopper.Managers.DHHShopManager",
                    "shopAtticShelvesPrefab",
                    bundle.LoadAsset<GameObject>("Assets/DeathHeadHopper/Shop Attic Shelves.prefab"));

                SetStaticFieldIfExists(
                    "DeathHeadHopper.Managers.DHHUIManager",
                    "abilityUIPrefab",
                    bundle.LoadAsset<GameObject>("Assets/DeathHeadHopper/Ability UI.prefab"));

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
            var tAssetMgr = AccessTools.TypeByName("DeathHeadHopper.Managers.DHHAssetManager");
            if (tAssetMgr == null)
            {
                _log?.LogError("[Fix] DHHAssetManager type not found.");
                return;
            }

            var fShopItems = AccessTools.Field(tAssetMgr, "shopItems");
            if (fShopItems == null)
            {
                _log?.LogError("[Fix] DHHAssetManager.shopItems field not found.");
                return;
            }

            if (fShopItems.GetValue(null) is not IDictionary shopItemsDict)
            {
                _log?.LogError("[Fix] DHHAssetManager.shopItems is not an IDictionary.");
                return;
            }

            var tItem = AccessTools.TypeByName("Item");
            if (tItem == null)
            {
                _log?.LogError("[Fix] Game type Item not found.");
                return;
            }

            var fItemPrefab = AccessTools.Field(tItem, "prefab");
            if (fItemPrefab == null)
            {
                _log?.LogError("[Fix] Field Item.prefab not found.");
                return;
            }

            var mSetPrefab = AccessTools.Method(fItemPrefab.FieldType, "SetPrefab", new[] { typeof(GameObject), typeof(string) });

            var assetNames = bundle.GetAllAssetNames()
                .Where(x => x.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                            x.IndexOf("/items/", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            _log?.LogInfo($"[Fix] Found {assetNames.Count} item assets in bundle.");

            foreach (var itemAssetPath in assetNames)
            {
                UnityEngine.Object? itemObj = null;
                try
                {
                    itemObj = bundle.LoadAsset(itemAssetPath, tItem);
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

                var prefabRefObj = fItemPrefab.GetValue(itemObj) ?? Activator.CreateInstance(fItemPrefab.FieldType);
                if (prefabRefObj == null)
                {
                    _log?.LogError("[Fix] Failed to create PrefabRef instance.");
                    continue;
                }

                fItemPrefab.SetValue(itemObj, prefabRefObj);
                mSetPrefab?.Invoke(prefabRefObj, new object?[] { prefab, prefabPath });

                var key = itemObj.name;
                var assetKey = ItemHelpers.GetItemAssetName(itemObj) ?? key;

                CachePrefabEntry(prefabPath, prefab);

                var prefabFileName = Path.GetFileName(prefabPath);
                if (!string.IsNullOrEmpty(prefabFileName))
                    CachePrefabEntry(prefabFileName, prefab);

                if (!string.IsNullOrWhiteSpace(assetKey))
                {
                    CachePrefabEntry(assetKey, prefab);
                    CachePrefabEntry($"Items/{assetKey}", prefab);
                }

                CacheShopItemKey(shopItemsDict, assetKey, itemObj);
                CacheShopItemKey(shopItemsDict, key, itemObj);

                TryRegisterItemWithRepolib(itemObj);
                StatsModule.EnsureStatsEntriesForItem(itemObj);

                _log?.LogInfo($"[Fix] Loaded item '{key}' from '{itemAssetPath}' and bound prefab '{prefabPath}'.");
            }
        }

        private static void PatchDhhShopManagerIfPossible(Harmony harmony, Assembly asm)
        {
            var tShopMgr = asm.GetType("DeathHeadHopper.Managers.DHHShopManager", throwOnError: false);
            if (tShopMgr == null)
                return;

            var mShopLoadItems = AccessTools.Method(tShopMgr, "LoadItems");
            if (mShopLoadItems == null)
                return;

            var prefix = typeof(PrefabModule).GetMethod(nameof(DHHShopManager_LoadItems_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mShopLoadItems, prefix: new HarmonyMethod(prefix));
        }

        private static bool DHHShopManager_LoadItems_Prefix()
        {
            try
            {
                var shopManager = ReflectionHelper.GetStaticInstanceByName("ShopManager");
                if (shopManager == null)
                    return false;

                var tAssetMgr = AccessTools.TypeByName("DeathHeadHopper.Managers.DHHAssetManager");
                var shopItemsDict = tAssetMgr != null ? AccessTools.Field(tAssetMgr, "shopItems")?.GetValue(null) as IDictionary : null;
                if (shopItemsDict == null)
                    return false;

                var tDhhShopMgr = AccessTools.TypeByName("DeathHeadHopper.Managers.DHHShopManager");
                var fPotential = tDhhShopMgr != null ? AccessTools.Field(tDhhShopMgr, "potentialItems") : null;
                if (fPotential?.GetValue(null) is not IList dhhPotential)
                    return false;

                var fUpgrades = AccessTools.Field(shopManager.GetType(), "potentialItemUpgrades");
                var fItems = AccessTools.Field(shopManager.GetType(), "potentialItems");

                var upgradesList = fUpgrades?.GetValue(shopManager) as IList;
                var itemsList = fItems?.GetValue(shopManager) as IList;

                dhhPotential.Clear();

                void CollectAndRemove(IList? source)
                {
                    if (source == null)
                        return;

                    var toRemove = new List<object>();
                    foreach (var it in source)
                    {
                        if (it is UnityEngine.Object uo)
                        {
                            // Keep original DHH behavior strict: match only by itemAssetName, not display/object name.
                            var itemKey = TryGetStrictItemAssetName(uo);
                            if (!string.IsNullOrWhiteSpace(itemKey) && ShopItemsDictContains(shopItemsDict, itemKey))
                            {
                                dhhPotential.Add(it);
                                toRemove.Add(it);
                                _log?.LogInfo($"[Fix] DHHShopManager selecting '{itemKey}' into dhhPotential");
                            }
                        }
                    }

                    foreach (var entry in toRemove)
                        source.Remove(entry);
                }

                CollectAndRemove(upgradesList);
                CollectAndRemove(itemsList);

                _log?.LogInfo($"[Fix] DHHShopManager potential list size {dhhPotential.Count} (upgrades left={upgradesList?.Count ?? 0}, items left={itemsList?.Count ?? 0})");
            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }

            return false;
        }

        private static void PatchRunManagerAwakeIfPossible(Harmony harmony)
        {
            var tRunManager = AccessTools.TypeByName("RunManager");
            if (tRunManager == null)
                return;

            var mAwake = AccessTools.Method(tRunManager, "Awake");
            if (mAwake == null)
                return;

            var pi = Harmony.GetPatchInfo(mAwake);
            if (pi != null && pi.Postfixes.Any(p => p.owner == harmony.Id))
                return;

            var postfix = typeof(PrefabModule).GetMethod(nameof(RunManager_Awake_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(mAwake, postfix: new HarmonyMethod(postfix));
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

            var tRunManager = AccessTools.TypeByName("RunManager");
            if (tRunManager == null)
                return;

            var runMgr = ReflectionHelper.GetStaticInstanceValue(tRunManager, "instance");
            if (runMgr == null)
                return;

            var fPool = AccessTools.Field(tRunManager, "singleplayerPool");
            var poolObj = fPool?.GetValue(runMgr);
            if (poolObj is not IDictionary<string, GameObject> pool)
                return;

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
            var tDefaultPool = AccessTools.TypeByName("Photon.Pun.DefaultPool");
            if (tDefaultPool == null)
                return;

            var mInstantiate = AccessTools.Method(tDefaultPool, "Instantiate", new[] { typeof(string), typeof(UnityEngine.Vector3), typeof(UnityEngine.Quaternion) });
            if (mInstantiate == null)
                return;

            var prefix = typeof(PrefabModule).GetMethod(nameof(DefaultPool_Instantiate_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mInstantiate, prefix: new HarmonyMethod(prefix));
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
                    // ignore
                }
            }

            return false;
        }

        private static string NormalizePrefabKey(string? key)
        {
            var trimmed = key?.Trim();
            return string.IsNullOrEmpty(trimmed) ? string.Empty : trimmed!.ToLowerInvariant();
        }

        private static void CacheShopItemKey(IDictionary dict, string? key, UnityEngine.Object value)
        {
            if (dict == null || value == null || string.IsNullOrWhiteSpace(key))
                return;

            if (dict.Contains(key))
                dict[key] = value;
            else
                dict.Add(key, value);

            var normalized = NormalizePrefabKey(key);
            if (!string.IsNullOrEmpty(normalized))
            {
                if (dict.Contains(normalized))
                    dict[normalized] = value;
                else
                    dict.Add(normalized, value);
            }
        }

        private static bool ShopItemsDictContains(IDictionary dict, string? key)
        {
            if (dict == null || string.IsNullOrWhiteSpace(key))
                return false;
            if (dict.Contains(key))
                return true;
            var normalized = NormalizePrefabKey(key);
            return dict.Contains(normalized);
        }

        private static string? TryGetStrictItemAssetName(UnityEngine.Object itemObj)
        {
            try
            {
                var type = itemObj.GetType();
                var prop = type.GetProperty("itemAssetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(string))
                    return (string?)prop.GetValue(itemObj, null);

                var field = type.GetField("itemAssetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(string))
                    return (string?)field.GetValue(itemObj);
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static void TryRegisterItemWithRepolib(UnityEngine.Object itemObj)
        {
            try
            {
                var tItems = AccessTools.TypeByName("REPOLib.Modules.Items");
                if (tItems == null)
                    return;

                var m = tItems.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(x =>
                        x.Name == "RegisterItem" &&
                        x.GetParameters().Length == 1 &&
                        x.GetParameters()[0].ParameterType.Name == "Item");

                m?.Invoke(null, new object?[] { itemObj });
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix] REPOLib RegisterItem failed: {ex.Message}");
            }
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
                // ignore
            }

            return false;
        }

        private static void SetStaticFieldIfExists(string typeName, string fieldName, object? value)
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null)
                return;

            var f = AccessTools.Field(t, fieldName);
            if (f == null)
                return;

            f.SetValue(null, value);
        }

        private static void InvokeStaticIfExists(string typeName, string methodName, object arg0)
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null)
                return;

            MethodInfo? m = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x =>
                {
                    if (x.Name != methodName)
                        return false;
                    var ps = x.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(arg0);
                });

            if (m == null)
            {
                m = t.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(x =>
                    {
                        if (x.Name != methodName)
                            return false;
                        var ps = x.GetParameters();
                        return ps.Length == 1 && ps[0].ParameterType.Name == arg0.GetType().Name;
                    });
            }

            m?.Invoke(null, new[] { arg0 });
        }
    }
}
