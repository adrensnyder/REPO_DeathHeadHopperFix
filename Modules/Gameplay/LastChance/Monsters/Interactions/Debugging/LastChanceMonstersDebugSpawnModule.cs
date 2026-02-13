#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions.Debugging
{
    [HarmonyPatch]
    internal static class LastChanceMonstersDebugSpawnModule
    {
        private sealed class CatalogWatchRunner : MonoBehaviour { }
        private sealed class DelayStunRunner : MonoBehaviour { }
        private readonly struct SpawnVerifyResult
        {
            internal SpawnVerifyResult(bool success, EnemyParent? enemyParent)
            {
                Success = success;
                EnemyParent = enemyParent;
            }

            internal bool Success { get; }
            internal EnemyParent? EnemyParent { get; }
        }
        private sealed class RepolibLogProbe : ILogListener
        {
            public void Dispose()
            {
            }

            public void LogEvent(object sender, LogEventArgs eventArgs)
            {
                if (s_repolibSignalReceived)
                {
                    return;
                }

                var source = eventArgs.Source?.SourceName ?? string.Empty;
                if (source.IndexOf("REPOLib", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                var message = eventArgs.Data?.ToString() ?? string.Empty;
                if (message.IndexOf("Adding enemies", StringComparison.OrdinalIgnoreCase) < 0 &&
                    message.IndexOf("Finished loading bundles", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                s_repolibSignalReceived = true;
                Log.LogInfo($"[LastChance][DebugSpawn] REPOLib signal received: '{message}'. Starting catalog watcher.");
                StartCatalogWatcher();
            }
        }

        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Monsters.DebugSpawn");
        private static bool s_spawnDoneForLevel;
        private static bool s_loggedConfiguredAtStartup;
        private static bool s_loggedConfiguredAtModsLoaded;
        private static bool s_loggedAvailableCatalog;
        private static bool s_catalogWatcherStarted;
        private static bool s_logProbeInstalled;
        private static bool s_repolibSignalReceived;
        private static DelayStunRunner? s_delayStunRunner;
        private static readonly RepolibLogProbe s_repolibLogProbe = new();
        private static readonly Regex s_enemyPrefixRegex = new Regex("^Enemy (- )?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly System.Reflection.FieldInfo? s_debugSpawnCloseField = AccessTools.Field(typeof(EnemyDirector), "debugSpawnClose");
        private static readonly System.Reflection.FieldInfo? s_setupDoneField = AccessTools.Field(typeof(EnemyParent), "SetupDone");
        private static readonly System.Reflection.FieldInfo? s_firstSpawnPointUsedField = AccessTools.Field(typeof(EnemyParent), "firstSpawnPointUsed");
        private static readonly Type? s_levelPointType = AccessTools.TypeByName("LevelPoint");
        private static readonly Type? s_roomVolumeType = AccessTools.TypeByName("RoomVolume");
        private static readonly System.Reflection.FieldInfo? s_levelPathPointsField = AccessTools.Field(typeof(LevelGenerator), "LevelPathPoints");
        private static readonly System.Reflection.FieldInfo? s_levelPointTruckField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "Truck");
        private static readonly System.Reflection.FieldInfo? s_levelPointRoomField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "Room");
        private static readonly System.Reflection.FieldInfo? s_levelPointConnectedPointsField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "ConnectedPoints");
        private static readonly System.Reflection.FieldInfo? s_roomVolumeTruckField = s_roomVolumeType == null ? null : AccessTools.Field(s_roomVolumeType, "Truck");
        private static readonly System.Reflection.FieldInfo? s_enemyDirectorSpawnedField = AccessTools.Field(typeof(EnemyDirector), "enemiesSpawned");
        private static readonly PropertyInfo? s_enemyParentSpawnedProperty =
            typeof(EnemyParent).GetProperty("Spawned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo? s_enemyParentSpawnedField =
            typeof(EnemyParent).GetField("Spawned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
            typeof(EnemyParent).GetField("spawned", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly System.Reflection.FieldInfo? s_enemyTeleportedTimerField = AccessTools.Field(typeof(Enemy), "TeleportedTimer");
        private static List<MethodInfo>? s_enemyDirectorSpawnMethods;

        internal static void NotifyPluginAwake()
        {
            if (s_loggedConfiguredAtStartup)
            {
                return;
            }

            s_loggedConfiguredAtStartup = true;
            if (!IsDebugSpawnConfigured())
            {
                return;
            }

            TryLogAvailableEnemyCatalog("startup");
            InstallRepolibLogProbe();
        }

        internal static void NotifyTargetAssemblyLoaded()
        {
            if (s_loggedConfiguredAtModsLoaded)
            {
                return;
            }

            s_loggedConfiguredAtModsLoaded = true;
            if (!IsDebugSpawnConfigured())
            {
                return;
            }

            TryLogAvailableEnemyCatalog("mods-loaded");
            InstallRepolibLogProbe();
        }

        private static void InstallRepolibLogProbe()
        {
            if (s_logProbeInstalled)
            {
                return;
            }

            Logger.Listeners.Add(s_repolibLogProbe);
            s_logProbeInstalled = true;
        }

        [HarmonyPatch(typeof(LevelGenerator), "Awake")]
        [HarmonyPostfix]
        private static void LevelAwakePostfix()
        {
            if (!IsDebugSpawnConfigured())
            {
                return;
            }

            s_spawnDoneForLevel = false;
        }

        [HarmonyPatch(typeof(LevelGenerator), "GenerateDone")]
        [HarmonyPostfix]
        private static void GenerateDonePostfix()
        {
            if (!IsDebugSpawnConfigured())
            {
                return;
            }

            TrySpawnDebugMonsters();
        }

        internal static void ResetRuntimeState()
        {
            s_spawnDoneForLevel = false;
            if (s_delayStunRunner != null)
            {
                UnityEngine.Object.Destroy(s_delayStunRunner.gameObject);
                s_delayStunRunner = null;
            }
        }

        private static void StartCatalogWatcher()
        {
            if (s_catalogWatcherStarted)
            {
                return;
            }

            s_catalogWatcherStarted = true;
            var go = new GameObject("DHHFix_LastChance_DebugCatalogWatcher");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var runner = go.AddComponent<CatalogWatchRunner>();
            runner.StartCoroutine(CatalogWatchCoroutine(runner));
        }

        private static IEnumerator CatalogWatchCoroutine(MonoBehaviour runner)
        {
            var lastCount = -1;
            var stableFor = 0f;
            const float pollInterval = 1f;
            const float stableSecondsRequired = 6f;
            const float maxWaitSeconds = 45f;
            var waited = 0f;

            while (waited < maxWaitSeconds && !s_loggedAvailableCatalog)
            {
                var count = CollectAllKnownSetups().Count;
                if (count == lastCount && count > 0)
                {
                    stableFor += pollInterval;
                }
                else
                {
                    lastCount = count;
                    stableFor = 0f;
                }

                if (stableFor >= stableSecondsRequired)
                {
                    TryLogAvailableEnemyCatalog("catalog-stable");
                    break;
                }

                // Use realtime so this keeps progressing even when menu/game timescale is 0.
                yield return new WaitForSecondsRealtime(pollInterval);
                waited += pollInterval;
            }

            if (!s_loggedAvailableCatalog)
            {
                // Fallback: still log whatever we have after timeout.
                TryLogAvailableEnemyCatalog("catalog-timeout");
            }

            if (runner != null)
            {
                UnityEngine.Object.Destroy(runner.gameObject);
            }
        }

        private static void TrySpawnDebugMonsters()
        {
            if (!IsDebugSpawnConfigured())
            {
                return;
            }

            if (s_spawnDoneForLevel)
            {
                return;
            }

            var requestedNames = ParseRequestedNames();
            if (requestedNames.Count == 0)
            {
                return;
            }

            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                return;
            }

            if (!SemiFunc.RunIsLevel())
            {
                return;
            }

            if (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated)
            {
                return;
            }

            var director = EnemyDirector.instance;
            if (director == null)
            {
                return;
            }

            var setups = CollectSetups(director);
            if (setups.Count == 0)
            {
                return;
            }

            var selected = new List<(string label, EnemySetup setup)>();
            var unknownRequests = new List<string>();

            for (var i = 0; i < requestedNames.Count; i++)
            {
                var requested = requestedNames[i];
                var direct = setups.FirstOrDefault(s => IsNameMatch(requested, NormalizeSetupName(s.name)));
                if (direct == null)
                {
                    unknownRequests.Add(requested);
                    continue;
                }

                selected.Add((requested, direct));
            }

            if (unknownRequests.Count > 0)
            {
                Log.LogWarning($"[LastChance][DebugSpawn] Unknown debug enemy names: {string.Join(", ", unknownRequests)}");
                TryLogAvailableEnemyCatalog("unknown-request");
            }

            if (selected.Count == 0)
            {
                return;
            }

            var spawnCenterPosition = ResolveSpawnCenterPosition();

            s_spawnDoneForLevel = true;
            s_debugSpawnCloseField?.SetValue(director, true);
            try
            {
                for (var i = 0; i < selected.Count; i++)
                {
                    var entry = selected[i];
                    var spawnPos = spawnCenterPosition + GetSpawnOffset(i);
                    Log.LogInfo($"[LastChance][DebugSpawn] Spawning '{entry.label}'...");
                    var beforeIds = CaptureSpawnedEnemyIds(director);
                    SpawnEnemySetup(entry.setup, spawnPos);
                    var verifyResult = TryVerifySpawnSucceeded(director, beforeIds, spawnPos, 8f);

                    if (!verifyResult.Success)
                    {
                        Log.LogWarning($"[LastChance][DebugSpawn] Direct spawn for '{entry.label}' not confirmed. Falling back to EnemyDirector spawn.");
                        beforeIds = CaptureSpawnedEnemyIds(director);
                        var spawnedViaDirector = TrySpawnEnemySetupViaDirector(director, entry.setup, spawnPos);
                        verifyResult = spawnedViaDirector ? TryVerifySpawnSucceeded(director, beforeIds, spawnPos, 8f) : default;
                    }

                    if (!verifyResult.Success || verifyResult.EnemyParent == null)
                    {
                        Log.LogWarning($"[LastChance][DebugSpawn] Spawn failed for '{entry.label}'.");
                        continue;
                    }

                    ApplySpawnDelayStun(verifyResult.EnemyParent);

                    if (TryResolveSpawnMetrics(verifyResult.EnemyParent, out var metersFromLocalPlayer, out var roomSteps))
                    {
                        Log.LogInfo($"[LastChance][DebugSpawn] Spawned '{entry.label}': distance={metersFromLocalPlayer:0.0}m rooms={roomSteps}.");
                    }
                    else
                    {
                        Log.LogInfo($"[LastChance][DebugSpawn] Spawned '{entry.label}': distance=n/a rooms=n/a.");
                    }
                }
            }
            finally
            {
                s_debugSpawnCloseField?.SetValue(director, false);
            }
        }

        private static Vector3 ResolveSpawnCenterPosition()
        {
            if (TryGetFirstPlayableRoomPointPosition(out var position))
            {
                return position;
            }

            var fallback = SemiFunc.LevelPointsGetClosestToLocalPlayer();
            return fallback != null ? fallback.transform.position : Vector3.zero;
        }

        private static bool TryGetFirstPlayableRoomPointPosition(out Vector3 position)
        {
            position = Vector3.zero;
            var generator = LevelGenerator.Instance;
            if (generator == null || s_levelPathPointsField == null)
            {
                return false;
            }

            if (!(s_levelPathPointsField.GetValue(generator) is IEnumerable rawPoints))
            {
                return false;
            }

            var points = new List<object>();
            foreach (var point in rawPoints)
            {
                if (point != null)
                {
                    points.Add(point);
                }
            }

            if (points.Count == 0)
            {
                return false;
            }

            var truckPoints = points.Where(IsTruckPoint).ToList();
            if (truckPoints.Count == 0)
            {
                return false;
            }

            foreach (var truckPoint in truckPoints)
            {
                var connected = GetConnectedPoints(truckPoint);
                if (connected == null)
                {
                    continue;
                }

                foreach (var candidate in connected)
                {
                    if (candidate == null || IsTruckPoint(candidate))
                    {
                        continue;
                    }

                    if (TryGetPointPosition(candidate, out position))
                    {
                        return true;
                    }
                }
            }

            var referenceTruck = truckPoints[0];
            if (!TryGetPointPosition(referenceTruck, out var truckPosition))
            {
                return false;
            }

            var nearest = points
                .Where(p => !IsTruckPoint(p))
                .Select(p => new { Point = p, Distance = TryGetPointPosition(p, out var pos) ? Vector3.Distance(pos, truckPosition) : float.MaxValue })
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (nearest == null || nearest.Distance == float.MaxValue)
            {
                return false;
            }

            return TryGetPointPosition(nearest.Point, out position);
        }

        private static bool IsTruckPoint(object point)
        {
            if (point == null)
            {
                return false;
            }

            if (s_levelPointTruckField != null &&
                s_levelPointTruckField.GetValue(point) is bool truckFlag &&
                truckFlag)
            {
                return true;
            }

            if (s_levelPointRoomField != null && s_roomVolumeTruckField != null)
            {
                var room = s_levelPointRoomField.GetValue(point);
                if (room != null && s_roomVolumeTruckField.GetValue(room) is bool roomTruck && roomTruck)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<object>? GetConnectedPoints(object point)
        {
            if (point == null || s_levelPointConnectedPointsField == null)
            {
                return null;
            }

            if (!(s_levelPointConnectedPointsField.GetValue(point) is IEnumerable connected))
            {
                return null;
            }

            var list = new List<object>();
            foreach (var item in connected)
            {
                if (item != null)
                {
                    list.Add(item);
                }
            }

            return list.Count > 0 ? list : null;
        }

        private static bool TryGetPointPosition(object? point, out Vector3 position)
        {
            position = Vector3.zero;
            if (point == null)
            {
                return false;
            }

            if (point is Component component)
            {
                position = component.transform.position;
                return true;
            }

            var transformProperty = point.GetType().GetProperty("transform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (transformProperty != null && transformProperty.GetValue(point) is Transform transform)
            {
                position = transform.position;
                return true;
            }

            return false;
        }

        private static List<string> ParseRequestedNames()
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(InternalDebugFlags.DebugAutoSpawnMonsterNamesCsv))
            {
                return list;
            }

            var chunks = InternalDebugFlags.DebugAutoSpawnMonsterNamesCsv.Split(',');
            for (var i = 0; i < chunks.Length; i++)
            {
                var raw = chunks[i];
                if (raw == null)
                {
                    continue;
                }

                var token = raw.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    list.Add(token);
                }
            }

            return list;
        }

        private static bool IsDebugSpawnConfigured()
        {
            return InternalDebugFlags.EnableDebugSpawnRuntime &&
                   !string.IsNullOrWhiteSpace(InternalDebugFlags.DebugAutoSpawnMonsterNamesCsv);
        }

        private static void TryLogAvailableEnemyCatalog(string reason)
        {
            if (s_loggedAvailableCatalog)
            {
                return;
            }

            var setups = CollectAllKnownSetups();
            if (setups.Count == 0)
            {
                var shouldWarn =
                    string.Equals(reason, "catalog-timeout", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(reason, "unknown-request", StringComparison.OrdinalIgnoreCase);

                if (shouldWarn)
                {
                    Log.LogWarning($"[LastChance][DebugSpawn] Enemy setup catalog still empty ({reason}).");
                }
                else
                {
                    Log.LogInfo($"[LastChance][DebugSpawn] Enemy setup catalog not ready yet ({reason}).");
                }
                return;
            }

            s_loggedAvailableCatalog = true;
            var entries = setups
                .Select(s => new
                {
                    Setup = s,
                    Name = NormalizeSetupName(s.name)
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .OrderBy(x => x.Name)
                .ToList();

            Log.LogInfo($"[LastChance][DebugSpawn] Available enemy setup names ({reason}) count={entries.Count}:");
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var displayName = BuildDisplayName(e.Name, e.Setup);
                Log.LogInfo($"- {displayName}");
            }
        }

        private static string BuildDisplayName(string normalizedSetupName, EnemySetup setup)
        {
            if (setup == null || setup.spawnObjects == null || setup.spawnObjects.Count == 0)
            {
                return normalizedSetupName;
            }

            var aliases = new HashSet<string>();
            for (var i = 0; i < setup.spawnObjects.Count; i++)
            {
                var prefabRef = setup.spawnObjects[i];
                var prefab = prefabRef?.Prefab;
                if (prefab == null)
                {
                    continue;
                }

                var parent = prefab.GetComponent<EnemyParent>();
                if (parent == null)
                {
                    continue;
                }

                var alias = (parent.enemyName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                // Ignore vanilla placeholder/default values.
                if (string.Equals(alias, "Dinosaur", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                aliases.Add(alias);
            }

            if (aliases.Count == 0)
            {
                return normalizedSetupName;
            }

            return $"{normalizedSetupName} ({string.Join(", ", aliases.OrderBy(x => x))})";
        }

        private static List<EnemySetup> CollectAllKnownSetups()
        {
            var list = new List<EnemySetup>();

            // Global catalog loaded in memory (vanilla + mods) once assets are available.
            var resources = Resources.FindObjectsOfTypeAll<EnemySetup>();
            for (var i = 0; i < resources.Length; i++)
            {
                var setup = resources[i];
                if (setup != null && !list.Contains(setup))
                {
                    list.Add(setup);
                }
            }

            // Current run-level director pools, when present.
            var director = EnemyDirector.instance;
            if (director != null)
            {
                AddRangeUnique(list, director.enemiesDifficulty1);
                AddRangeUnique(list, director.enemiesDifficulty2);
                AddRangeUnique(list, director.enemiesDifficulty3);
            }

            return list;
        }

        private static List<EnemySetup> CollectSetups(EnemyDirector director)
        {
            var list = new List<EnemySetup>();
            AddRangeUnique(list, director.enemiesDifficulty1);
            AddRangeUnique(list, director.enemiesDifficulty2);
            AddRangeUnique(list, director.enemiesDifficulty3);
            return list;
        }

        private static void AddRangeUnique(List<EnemySetup> target, List<EnemySetup>? source)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var setup = source[i];
                if (setup != null && !target.Contains(setup))
                {
                    target.Add(setup);
                }
            }
        }

        private static string NormalizeSetupName(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            return s_enemyPrefixRegex.Replace(raw, string.Empty).Trim().ToLowerInvariant();
        }

        private static bool IsNameMatch(string requested, string candidate)
        {
            if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            return candidate.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   requested.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void SpawnEnemySetup(EnemySetup setup, Vector3 position)
        {
            if (setup == null || setup.spawnObjects == null)
            {
                return;
            }

            for (var i = 0; i < setup.spawnObjects.Count; i++)
            {
                var prefab = setup.spawnObjects[i];
                if (prefab == null)
                {
                    continue;
                }

                GameObject obj;
                if (GameManager.instance.gameMode != 0)
                {
                    obj = PhotonNetwork.InstantiateRoomObject(prefab.ResourcePath, position, Quaternion.identity, 0, null);
                }
                else
                {
                    obj = UnityEngine.Object.Instantiate(prefab.Prefab, position, Quaternion.identity);
                }

                var parent = obj.GetComponent<EnemyParent>();
                if (parent == null)
                {
                    continue;
                }

                s_setupDoneField?.SetValue(parent, true);
                s_firstSpawnPointUsedField?.SetValue(parent, true);

                var enemy = obj.GetComponentInChildren<Enemy>();
                if (enemy != null)
                {
                    enemy.EnemyTeleported(position);
                }
            }
        }

        private static bool TrySpawnEnemySetupViaDirector(EnemyDirector director, EnemySetup setup, Vector3 preferredPosition)
        {
            if (director == null || setup == null)
            {
                return false;
            }

            var candidates = GetEnemyDirectorSpawnMethods();
            if (candidates.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var method = candidates[i];
                if (!TryBuildDirectorSpawnArgs(method, setup, preferredPosition, out var args))
                {
                    continue;
                }

                try
                {
                    var beforeCount = GetSpawnedEnemiesCount(director);
                    var result = method.Invoke(director, args);
                    var afterCount = GetSpawnedEnemiesCount(director);
                    if (result is bool ok && !ok)
                    {
                        continue;
                    }

                    if (result == null && afterCount <= beforeCount)
                    {
                        continue;
                    }

                    return true;
                }
                catch
                {
                    // Try next compatible spawn method.
                }
            }

            return false;
        }

        private static List<MethodInfo> GetEnemyDirectorSpawnMethods()
        {
            if (s_enemyDirectorSpawnMethods != null)
            {
                return s_enemyDirectorSpawnMethods;
            }

            var methods = AccessTools.GetDeclaredMethods(typeof(EnemyDirector))
                .Where(m =>
                    m != null &&
                    !m.IsAbstract &&
                    m.Name.IndexOf("spawn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    m.GetParameters().Any(p => p.ParameterType == typeof(EnemySetup)))
                .OrderByDescending(m => m.Name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenBy(m => m.GetParameters().Length)
                .ToList();

            s_enemyDirectorSpawnMethods = methods;
            return methods;
        }

        private static bool TryBuildDirectorSpawnArgs(MethodInfo method, EnemySetup setup, Vector3 preferredPosition, out object?[] args)
        {
            var parameters = method.GetParameters();
            args = new object?[parameters.Length];
            var usedSetup = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var pt = p.ParameterType;

                if (!usedSetup && pt == typeof(EnemySetup))
                {
                    args[i] = setup;
                    usedSetup = true;
                    continue;
                }

                if (pt == typeof(Vector3))
                {
                    args[i] = preferredPosition;
                    continue;
                }

                if (pt == typeof(Quaternion))
                {
                    args[i] = Quaternion.identity;
                    continue;
                }

                if (pt == typeof(bool))
                {
                    args[i] = false;
                    continue;
                }

                if (pt == typeof(int))
                {
                    args[i] = 0;
                    continue;
                }

                if (pt == typeof(float))
                {
                    args[i] = 0f;
                    continue;
                }

                if (!pt.IsValueType)
                {
                    args[i] = null;
                    continue;
                }

                return false;
            }

            return usedSetup;
        }

        private static int GetSpawnedEnemiesCount(EnemyDirector director)
        {
            if (director == null || s_enemyDirectorSpawnedField == null)
            {
                return -1;
            }

            return s_enemyDirectorSpawnedField.GetValue(director) is IList list ? list.Count : -1;
        }

        private static HashSet<int> CaptureSpawnedEnemyIds(EnemyDirector director)
        {
            var ids = new HashSet<int>();
            AddKnownEnemyIdsFromDirector(ids, director);
            AddKnownEnemyIdsFromScene(ids);

            return ids;
        }

        private static SpawnVerifyResult TryVerifySpawnSucceeded(EnemyDirector director, HashSet<int> beforeIds, Vector3 requestedPosition, float preferredDistance)
        {
            var sawAnyNewSpawned = false;
            var nearestDistance = float.MaxValue;
            EnemyParent? nearestParent = null;
            var candidates = CollectCurrentEnemyParents(director);
            for (var i = 0; i < candidates.Count; i++)
            {
                var parent = candidates[i];
                if (parent == null)
                {
                    continue;
                }

                var id = parent.GetInstanceID();
                if (beforeIds.Contains(id))
                {
                    continue;
                }

                if (!IsEnemyParentSpawned(parent))
                {
                    continue;
                }

                sawAnyNewSpawned = true;
                var distance = Vector3.Distance(parent.transform.position, requestedPosition);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestParent = parent;
                }

                if (distance <= preferredDistance)
                {
                    return new SpawnVerifyResult(success: true, enemyParent: parent);
                }
            }

            if (sawAnyNewSpawned)
            {
                return new SpawnVerifyResult(success: true, enemyParent: nearestParent);
            }

            return default;
        }

        private static List<EnemyParent> CollectCurrentEnemyParents(EnemyDirector director)
        {
            var list = new List<EnemyParent>();
            AddEnemyParentsFromDirector(list, director);

            // Fallback for direct spawns that may not yet be tracked by EnemyDirector.enemiesSpawned.
            var sceneParents = UnityEngine.Object.FindObjectsOfType<EnemyParent>(true);
            for (var i = 0; i < sceneParents.Length; i++)
            {
                var parent = sceneParents[i];
                if (parent != null && !list.Contains(parent))
                {
                    list.Add(parent);
                }
            }

            return list;
        }

        private static void AddKnownEnemyIdsFromDirector(HashSet<int> targetIds, EnemyDirector director)
        {
            if (targetIds == null || director == null || s_enemyDirectorSpawnedField == null)
            {
                return;
            }

            if (!(s_enemyDirectorSpawnedField.GetValue(director) is IList list))
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is EnemyParent parent && parent != null)
                {
                    targetIds.Add(parent.GetInstanceID());
                }
            }
        }

        private static void AddKnownEnemyIdsFromScene(HashSet<int> targetIds)
        {
            if (targetIds == null)
            {
                return;
            }

            var sceneParents = UnityEngine.Object.FindObjectsOfType<EnemyParent>(true);
            for (var i = 0; i < sceneParents.Length; i++)
            {
                var parent = sceneParents[i];
                if (parent != null)
                {
                    targetIds.Add(parent.GetInstanceID());
                }
            }
        }

        private static void AddEnemyParentsFromDirector(List<EnemyParent> target, EnemyDirector director)
        {
            if (target == null || director == null || s_enemyDirectorSpawnedField == null)
            {
                return;
            }

            if (!(s_enemyDirectorSpawnedField.GetValue(director) is IList list))
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is EnemyParent parent && parent != null && !target.Contains(parent))
                {
                    target.Add(parent);
                }
            }
        }

        private static bool IsEnemyParentSpawned(EnemyParent parent)
        {
            if (parent == null)
            {
                return false;
            }

            if (s_enemyParentSpawnedProperty == null)
            {
                if (s_enemyParentSpawnedField == null)
                {
                    return true;
                }

                return s_enemyParentSpawnedField.GetValue(parent) is bool spawnedField && spawnedField;
            }

            return s_enemyParentSpawnedProperty.GetValue(parent) is bool spawnedProp && spawnedProp;
        }

        private static void ApplySpawnDelayStun(EnemyParent enemyParent)
        {
            if (enemyParent == null)
            {
                return;
            }

            var delaySeconds = Mathf.Max(0f, InternalDebugFlags.DebugAutoSpawnDelaySeconds);
            if (delaySeconds <= 0f)
            {
                return;
            }

            var enemy = enemyParent.GetComponentInChildren<Enemy>();
            if (enemy == null)
            {
                return;
            }

            var runner = EnsureDelayStunRunner();
            if (runner == null)
            {
                return;
            }

            runner.StartCoroutine(ApplySpawnDelayStunCoroutine(enemy, delaySeconds));
        }

        private static DelayStunRunner? EnsureDelayStunRunner()
        {
            if (s_delayStunRunner != null)
            {
                return s_delayStunRunner;
            }

            var go = new GameObject("DHHFix_LastChance_DebugDelayStunRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_delayStunRunner = go.AddComponent<DelayStunRunner>();
            return s_delayStunRunner;
        }

        private static IEnumerator ApplySpawnDelayStunCoroutine(Enemy enemy, float delaySeconds)
        {
            if (enemy == null)
            {
                yield break;
            }

            var stateStunned = enemy.GetComponent<EnemyStateStunned>();
            if (stateStunned != null)
            {
                var targetEndTime = Time.time + delaySeconds;
                var setUntil = Time.realtimeSinceStartup + 1.5f;

                // Wait for spawn/teleport phase, then enter stun through native Set(...) path.
                while (enemy != null && Time.realtimeSinceStartup < setUntil)
                {
                    if (GetEnemyTeleportedTimer(enemy) <= 0f)
                    {
                        stateStunned.Set(delaySeconds);
                        enemy.CurrentState = EnemyState.Stunned;
                        enemy.DisableChase(delaySeconds + 0.25f);
                        break;
                    }

                    yield return null;
                }

                if (enemy == null)
                {
                    yield break;
                }

                // Keep stun alive for full delay window in case other spawn logic briefly resets it.
                while (enemy != null && Time.time < targetEndTime)
                {
                    var remaining = targetEndTime - Time.time;
                    if (remaining <= 0f)
                    {
                        break;
                    }

                    if (stateStunned.stunTimer < remaining - 0.05f)
                    {
                        stateStunned.Set(remaining);
                    }

                    enemy.CurrentState = EnemyState.Stunned;
                    enemy.DisableChase(0.35f);
                    yield return null;
                }

                yield break;
            }

            // Fallback for enemies without EnemyStateStunned.
            enemy.Freeze(delaySeconds);
        }

        private static float GetEnemyTeleportedTimer(Enemy enemy)
        {
            if (enemy == null || s_enemyTeleportedTimerField == null)
            {
                return 0f;
            }

            return s_enemyTeleportedTimerField.GetValue(enemy) is float timer ? timer : 0f;
        }

        private static bool TryResolveSpawnMetrics(EnemyParent enemyParent, out float metersFromLocalPlayer, out int roomSteps)
        {
            metersFromLocalPlayer = -1f;
            roomSteps = -1;

            if (enemyParent == null || !TryGetLocalPlayerPosition(out var playerPosition))
            {
                return false;
            }

            metersFromLocalPlayer = Vector3.Distance(playerPosition, enemyParent.transform.position);
            roomSteps = GetRoomStepsBetween(playerPosition, enemyParent.transform.position);
            return true;
        }

        private static bool TryGetLocalPlayerPosition(out Vector3 position)
        {
            position = Vector3.zero;
            var player = PlayerController.instance?.playerAvatarScript;
            if (player == null)
            {
                return false;
            }

            position = player.transform.position;
            return true;
        }

        private static int GetRoomStepsBetween(Vector3 fromPosition, Vector3 toPosition)
        {
            if (LevelGenerator.Instance == null || s_levelPathPointsField == null)
            {
                return -1;
            }

            if (!(s_levelPathPointsField.GetValue(LevelGenerator.Instance) is IEnumerable rawPoints))
            {
                return -1;
            }

            var points = new List<object>();
            foreach (var point in rawPoints)
            {
                if (point != null)
                {
                    points.Add(point);
                }
            }

            if (points.Count == 0)
            {
                return -1;
            }

            var start = GetClosestLevelPoint(points, fromPosition);
            var end = GetClosestLevelPoint(points, toPosition);
            if (start == null || end == null)
            {
                return -1;
            }

            return ComputePointDistanceInSteps(start, end);
        }

        private static object? GetClosestLevelPoint(List<object> points, Vector3 position)
        {
            object? closest = null;
            var best = float.MaxValue;
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (!TryGetPointPosition(point, out var pointPos))
                {
                    continue;
                }

                var d = Vector3.Distance(position, pointPos);
                if (d < best)
                {
                    best = d;
                    closest = point;
                }
            }

            return closest;
        }

        private static int ComputePointDistanceInSteps(object start, object end)
        {
            if (ReferenceEquals(start, end))
            {
                return 0;
            }

            var queue = new Queue<object>();
            var visited = new HashSet<object>();
            var distance = new Dictionary<object, int>();

            queue.Enqueue(start);
            visited.Add(start);
            distance[start] = 0;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!distance.TryGetValue(current, out var currentDistance))
                {
                    continue;
                }

                var connected = GetConnectedPoints(current);
                if (connected == null)
                {
                    continue;
                }

                foreach (var next in connected)
                {
                    if (next == null || visited.Contains(next))
                    {
                        continue;
                    }

                    visited.Add(next);
                    distance[next] = currentDistance + 1;
                    if (ReferenceEquals(next, end))
                    {
                        return currentDistance + 1;
                    }

                    queue.Enqueue(next);
                }
            }

            return -1;
        }

        private static Vector3 GetSpawnOffset(int index)
        {
            const float radius = 2.5f;
            var angle = (Mathf.PI * 2f / 6f) * index;
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }
}

