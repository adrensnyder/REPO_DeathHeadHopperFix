#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class PlayerTruckDistanceHelper
    {
        private const float HeightCacheTtlSeconds = 2f;

        [Flags]
        internal enum DistanceQueryFields
        {
            None = 0,
            Height = 1 << 0,
            NavMeshDistance = 1 << 1,
            RoomPath = 1 << 2,
            All = Height | NavMeshDistance | RoomPath
        }

        internal readonly struct PlayerTruckDistance
        {
            internal PlayerTruckDistance(PlayerAvatar playerAvatar, float navMeshDistance, float heightDelta, int shortestRoomPathToTruck, int totalMapRooms, bool hasValidPath)
            {
                PlayerAvatar = playerAvatar;
                NavMeshDistance = navMeshDistance;
                HeightDelta = heightDelta;
                ShortestRoomPathToTruck = shortestRoomPathToTruck;
                TotalMapRooms = totalMapRooms;
                HasValidPath = hasValidPath;
            }

            internal PlayerAvatar PlayerAvatar { get; }
            internal float NavMeshDistance { get; }
            internal float HeightDelta { get; }
            internal int ShortestRoomPathToTruck { get; }
            internal int TotalMapRooms { get; }
            internal bool HasValidPath { get; }
        }

        private sealed class CachedPlayerDistance
        {
            internal float NavMeshDistance = -1f;
            internal float HeightDelta;
            internal int ShortestRoomPathToTruck = -1;
            internal int TotalMapRooms = -1;
            internal bool HasValidPath;
            internal int RoomHash;
            internal float HeightUpdatedAt = float.NegativeInfinity;
            internal int LevelStamp;
            internal Vector3 LastKnownWorldPosition;
        }

        private static readonly Type? s_levelGeneratorType = AccessTools.TypeByName("LevelGenerator");
        private static readonly FieldInfo? s_levelGeneratorInstanceField = s_levelGeneratorType == null ? null : AccessTools.Field(s_levelGeneratorType, "Instance");
        private static readonly FieldInfo? s_levelPathTruckField = s_levelGeneratorType == null ? null : AccessTools.Field(s_levelGeneratorType, "LevelPathTruck");
        private static readonly FieldInfo? s_levelPathPointsField = s_levelGeneratorType == null ? null : AccessTools.Field(s_levelGeneratorType, "LevelPathPoints");
        private static readonly Type? s_levelPointType = AccessTools.TypeByName("LevelPoint");
        private static readonly FieldInfo? s_levelPointTruckField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "Truck");
        private static readonly FieldInfo? s_levelPointRoomField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "Room");
        private static readonly FieldInfo? s_levelPointConnectedPointsField = s_levelPointType == null ? null : AccessTools.Field(s_levelPointType, "ConnectedPoints");
        private static readonly Type? s_roomVolumeType = AccessTools.TypeByName("RoomVolume");
        private static readonly FieldInfo? s_roomVolumeTruckField = s_roomVolumeType == null ? null : AccessTools.Field(s_roomVolumeType, "Truck");
        private static readonly FieldInfo? s_playerLastNavmeshField = AccessTools.Field(typeof(PlayerAvatar), "LastNavmeshPosition");
        private static readonly FieldInfo? s_playerRoomVolumeCheckField = AccessTools.Field(typeof(PlayerAvatar), "RoomVolumeCheck");
        private static readonly Type? s_playerDeathHeadType = AccessTools.TypeByName("PlayerDeathHead");
        private static readonly FieldInfo? s_playerDeathHeadRoomVolumeCheckField = s_playerDeathHeadType == null ? null : AccessTools.Field(s_playerDeathHeadType, "roomVolumeCheck");
        private static readonly FieldInfo? s_playerDeathHeadPhysGrabObjectField = s_playerDeathHeadType == null ? null : AccessTools.Field(s_playerDeathHeadType, "physGrabObject");
        private static readonly Type? s_physGrabObjectType = AccessTools.TypeByName("PhysGrabObject");
        private static readonly FieldInfo? s_physGrabObjectCenterPointField = s_physGrabObjectType == null ? null : AccessTools.Field(s_physGrabObjectType, "centerPoint");
        private static readonly Type? s_roomVolumeCheckType = AccessTools.TypeByName("RoomVolumeCheck");
        private static readonly FieldInfo? s_roomVolumeCheckCurrentRoomsField = s_roomVolumeCheckType == null ? null : AccessTools.Field(s_roomVolumeCheckType, "CurrentRooms");
        private static readonly Type? s_navMeshType = AccessTools.TypeByName("UnityEngine.AI.NavMesh");
        private static readonly Type? s_navMeshHitType = AccessTools.TypeByName("UnityEngine.AI.NavMeshHit");
        private static readonly Type? s_navMeshPathType = AccessTools.TypeByName("UnityEngine.AI.NavMeshPath");
        private static readonly MethodInfo? s_navMeshSamplePositionMethod = s_navMeshType?.GetMethod(
            "SamplePosition",
            BindingFlags.Static | BindingFlags.Public,
            null,
            s_navMeshHitType == null
                ? null
                : new[] { typeof(Vector3), s_navMeshHitType.MakeByRefType(), typeof(float), typeof(int) },
            null);
        private static readonly PropertyInfo? s_navMeshHitPositionProperty = s_navMeshHitType?.GetProperty("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo? s_navMeshHitPositionField = s_navMeshHitType?.GetField("position", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo? s_navMeshCalculatePathMethod = s_navMeshType?.GetMethod(
            "CalculatePath",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector3), typeof(Vector3), typeof(int), s_navMeshPathType },
            null);
        private static readonly PropertyInfo? s_navMeshPathCornersProperty = s_navMeshPathType?.GetProperty("corners");

        private static object? s_cachedGraphLevelGenerator;
        private static int s_cachedGraphPointCount;
        private static Dictionary<object, HashSet<object>>? s_cachedRoomGraph;
        private static readonly Dictionary<int, CachedPlayerDistance> s_playerCache = new();
        private static readonly Dictionary<int, RemotePlayerHint> s_remoteHints = new();
        private static object? s_cachedLevelGeneratorForPlayers;
        private static Vector3 s_cachedTruckPosition;
        private static bool s_hasCachedTruckPosition;
        private static int s_cachedLevelPointsCount;
        private static bool s_activationProfilingEnabled;
        private static ActivationProfileStats s_activationProfileStats;

        private readonly struct RemotePlayerHint
        {
            internal RemotePlayerHint(int roomHash, float heightDelta, int levelStamp, float updatedAt)
            {
                RoomHash = roomHash;
                HeightDelta = heightDelta;
                LevelStamp = levelStamp;
                UpdatedAt = updatedAt;
            }

            internal int RoomHash { get; }
            internal float HeightDelta { get; }
            internal int LevelStamp { get; }
            internal float UpdatedAt { get; }
        }

        private struct ActivationProfileStats
        {
            internal int Calls;
            internal int PlayersProcessed;
            internal int NavRefreshCount;
            internal int RoomRefreshCount;
            internal int RemoteHintUsedCount;
            internal float TotalMs;
            internal float SetupMs;
            internal float LoopMs;
            internal float MaxCallMs;
        }

        internal static void PrimeDistancesCache()
        {
            _ = GetDistancesFromTruck(DistanceQueryFields.NavMeshDistance | DistanceQueryFields.RoomPath);
        }

        internal static PlayerTruckDistance[] GetDistancesFromTruck()
        {
            return GetDistancesFromTruck(DistanceQueryFields.All, null, false);
        }

        internal static PlayerTruckDistance[] GetDistancesFromTruck(
            DistanceQueryFields fields,
            ICollection<PlayerAvatar>? players = null,
            bool forceRefresh = false)
        {
            try
            {
            var profileEnabled = FeatureFlags.DebugLogging;
            var profileStart = profileEnabled ? Time.realtimeSinceStartup : 0f;
            var profileAfterSetup = profileStart;
            var profileLoopStart = profileStart;
            var navRefreshCount = 0;
            var roomRefreshCount = 0;
            var remoteHintUsedCount = 0;
            var processedPlayers = 0;

            if (fields == DistanceQueryFields.None || s_levelGeneratorInstanceField == null)
            {
                return Array.Empty<PlayerTruckDistance>();
            }

            var levelGenerator = s_levelGeneratorInstanceField.GetValue(null);
            if (levelGenerator == null)
            {
                return Array.Empty<PlayerTruckDistance>();
            }

            var allLevelPoints = GetAllLevelPoints(levelGenerator);
            if (!TryGetTruckTarget(levelGenerator, allLevelPoints, out var truckPosition, out var truckPoint))
            {
                return Array.Empty<PlayerTruckDistance>();
            }

            var director = GameDirector.instance;
            if (director?.PlayerList == null || director.PlayerList.Count == 0)
            {
                return Array.Empty<PlayerTruckDistance>();
            }

            var levelPointsCount = allLevelPoints?.Count ?? 0;
            if (!ReferenceEquals(s_cachedLevelGeneratorForPlayers, levelGenerator) ||
                !s_hasCachedTruckPosition ||
                Vector3.SqrMagnitude(s_cachedTruckPosition - truckPosition) > 0.0001f ||
                s_cachedLevelPointsCount != levelPointsCount)
            {
                s_playerCache.Clear();
                s_remoteHints.Clear();
                s_cachedLevelGeneratorForPlayers = levelGenerator;
                s_cachedTruckPosition = truckPosition;
                s_cachedLevelPointsCount = levelPointsCount;
                s_hasCachedTruckPosition = true;
            }

            HashSet<int>? allowedKeys = null;
            if (players != null)
            {
                allowedKeys = new HashSet<int>();
                foreach (var player in players)
                {
                    if (player == null)
                    {
                        continue;
                    }

                    allowedKeys.Add(GetPlayerKey(player));
                }

                if (allowedKeys.Count == 0)
                {
                    return Array.Empty<PlayerTruckDistance>();
                }
            }

            var needsRoomPath = (fields & DistanceQueryFields.RoomPath) != 0;
            var needsNavPath = (fields & DistanceQueryFields.NavMeshDistance) != 0;
            var needsHeight = (fields & DistanceQueryFields.Height) != 0;
            var roomGraph = (needsRoomPath || needsNavPath)
                ? GetOrBuildRoomGraph(levelGenerator, allLevelPoints)
                : null;
            var totalMapRooms = roomGraph != null && roomGraph.Count > 0 ? roomGraph.Count : -1;
            var levelStamp = RunManager.instance != null ? RunManager.instance.levelsCompleted : 0;
            if (profileEnabled)
            {
                profileAfterSetup = Time.realtimeSinceStartup;
                profileLoopStart = profileAfterSetup;
            }

            var distances = new List<PlayerTruckDistance>(director.PlayerList.Count);
            foreach (var player in director.PlayerList)
            {
                if (player == null)
                {
                    continue;
                }

                var playerKey = GetPlayerKey(player);
                if (allowedKeys != null && !allowedKeys.Contains(playerKey))
                {
                    continue;
                }

                if (!s_playerCache.TryGetValue(playerKey, out var cached))
                {
                    cached = new CachedPlayerDistance();
                    s_playerCache[playerKey] = cached;
                }

                var worldPosition = GetPlayerWorldPosition(player);
                cached.LastKnownWorldPosition = worldPosition;
                var actorNumber = player.photonView?.Owner?.ActorNumber ?? 0;
                s_remoteHints.TryGetValue(actorNumber, out var remoteHint);
                var shouldUseRemoteHint =
                    SemiFunc.IsMasterClientOrSingleplayer() &&
                    SemiFunc.IsMultiplayer() &&
                    actorNumber > 0 &&
                    PhotonNetwork.LocalPlayer != null &&
                    actorNumber != PhotonNetwork.LocalPlayer.ActorNumber &&
                    remoteHint.LevelStamp == levelStamp;

                List<object>? playerRooms = null;
                var roomHash = cached.RoomHash;
                if (shouldUseRemoteHint)
                {
                    roomHash = remoteHint.RoomHash;
                    remoteHintUsedCount++;
                }
                else if (needsRoomPath || needsNavPath)
                {
                    playerRooms = GetPlayerRooms(player);
                    roomHash = ComputeRoomsHash(playerRooms);
                }

                var roomChanged = roomHash != cached.RoomHash;
                var levelChanged = cached.LevelStamp != levelStamp;

                if (needsHeight)
                {
                    var heightAge = Time.unscaledTime - cached.HeightUpdatedAt;
                    if (forceRefresh || levelChanged || heightAge < 0f || heightAge > HeightCacheTtlSeconds)
                    {
                        if (shouldUseRemoteHint && (Time.unscaledTime - remoteHint.UpdatedAt) <= HeightCacheTtlSeconds)
                        {
                            cached.HeightDelta = remoteHint.HeightDelta;
                            cached.HeightUpdatedAt = remoteHint.UpdatedAt;
                        }
                        else
                        {
                            cached.HeightDelta = worldPosition.y - truckPosition.y;
                            cached.HeightUpdatedAt = Time.unscaledTime;
                        }
                    }
                }

                if (needsNavPath && (forceRefresh || levelChanged || roomChanged))
                {
                    var navDistance = -1f;
                    var hasPath = TryGetPlayerNavMeshPosition(player, worldPosition, out var navMeshStart) &&
                                  TryCalculatePathDistance(navMeshStart, truckPosition, out navDistance);
                    cached.NavMeshDistance = hasPath ? navDistance : -1f;
                    cached.HasValidPath = hasPath;
                    navRefreshCount++;
                }

                if (needsRoomPath && (forceRefresh || levelChanged || roomChanged))
                {
                    playerRooms ??= GetPlayerRooms(player);
                    cached.ShortestRoomPathToTruck = ResolveShortestRoomPathToTruck(playerRooms ?? new List<object>(), truckPoint, roomGraph);
                    roomRefreshCount++;
                }

                if (needsRoomPath || needsNavPath)
                {
                    cached.TotalMapRooms = totalMapRooms;
                }

                cached.RoomHash = roomHash;
                cached.LevelStamp = levelStamp;

                distances.Add(new PlayerTruckDistance(
                    player,
                    cached.NavMeshDistance,
                    cached.HeightDelta,
                    cached.ShortestRoomPathToTruck,
                    cached.TotalMapRooms,
                    cached.HasValidPath));
                processedPlayers++;
            }

            if (profileEnabled && s_activationProfilingEnabled)
            {
                var profileEnd = Time.realtimeSinceStartup;
                var totalMs = (profileEnd - profileStart) * 1000f;
                var setupMs = (profileAfterSetup - profileStart) * 1000f;
                var loopMs = (profileEnd - profileLoopStart) * 1000f;
                s_activationProfileStats.Calls++;
                s_activationProfileStats.PlayersProcessed += processedPlayers;
                s_activationProfileStats.NavRefreshCount += navRefreshCount;
                s_activationProfileStats.RoomRefreshCount += roomRefreshCount;
                s_activationProfileStats.RemoteHintUsedCount += remoteHintUsedCount;
                s_activationProfileStats.TotalMs += totalMs;
                s_activationProfileStats.SetupMs += setupMs;
                s_activationProfileStats.LoopMs += loopMs;
                s_activationProfileStats.MaxCallMs = Mathf.Max(s_activationProfileStats.MaxCallMs, totalMs);
            }

            return distances.ToArray();
            }
            catch (Exception ex)
            {
                LogReflectionHotPathException("GetDistancesFromTruck", ex);
                return Array.Empty<PlayerTruckDistance>();
            }
        }

        internal static void ApplyRemotePlayerHint(int actorNumber, int roomHash, float heightDelta, int levelStamp)
        {
            if (actorNumber <= 0)
            {
                return;
            }

            s_remoteHints[actorNumber] = new RemotePlayerHint(roomHash, heightDelta, levelStamp, Time.unscaledTime);
        }

        internal static bool TryBuildLocalPlayerTruckHint(out int roomHash, out float heightDelta, out int levelStamp)
        {
            roomHash = 0;
            heightDelta = 0f;
            levelStamp = RunManager.instance != null ? RunManager.instance.levelsCompleted : 0;

            try
            {
                if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null || s_levelGeneratorInstanceField == null)
                {
                    return false;
                }

                var levelGenerator = s_levelGeneratorInstanceField.GetValue(null);
                if (levelGenerator == null)
                {
                    return false;
                }

                var allLevelPoints = GetAllLevelPoints(levelGenerator);
                if (!TryGetTruckTarget(levelGenerator, allLevelPoints, out var truckPosition, out _))
                {
                    return false;
                }

                var director = GameDirector.instance;
                if (director?.PlayerList == null || director.PlayerList.Count == 0)
                {
                    return false;
                }

                var localActor = PhotonNetwork.LocalPlayer.ActorNumber;
                PlayerAvatar? localPlayer = null;
                for (var i = 0; i < director.PlayerList.Count; i++)
                {
                    var candidate = director.PlayerList[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if ((candidate.photonView?.Owner?.ActorNumber ?? 0) == localActor)
                    {
                        localPlayer = candidate;
                        break;
                    }
                }

                if (localPlayer == null)
                {
                    return false;
                }

                var rooms = GetPlayerRooms(localPlayer);
                roomHash = ComputeRoomsHash(rooms);
                var position = GetPlayerWorldPosition(localPlayer);
                heightDelta = position.y - truckPosition.y;
                return true;
            }
            catch (Exception ex)
            {
                LogReflectionHotPathException("TryBuildLocalPlayerTruckHint", ex);
                return false;
            }
        }

        private static void LogReflectionHotPathException(string context, Exception ex)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            var key = "LastChance.Reflection.PlayerTruckDistance." + context;
            if (!LogLimiter.ShouldLog(key, 600))
            {
                return;
            }

            Debug.LogWarning($"[LastChance] Reflection hot-path failed in {context}: {ex.GetType().Name}: {ex.Message}");
        }

        internal static void BeginActivationProfiling()
        {
            s_activationProfileStats = default;
            s_activationProfilingEnabled = true;
        }

        internal static string EndActivationProfilingSummary()
        {
            s_activationProfilingEnabled = false;
            return
                $"calls={s_activationProfileStats.Calls} total={s_activationProfileStats.TotalMs:F1}ms setup={s_activationProfileStats.SetupMs:F1}ms loop={s_activationProfileStats.LoopMs:F1}ms maxCall={s_activationProfileStats.MaxCallMs:F1}ms players={s_activationProfileStats.PlayersProcessed} navRefresh={s_activationProfileStats.NavRefreshCount} roomRefresh={s_activationProfileStats.RoomRefreshCount} remoteHints={s_activationProfileStats.RemoteHintUsedCount}";
        }

        private static int GetPlayerKey(PlayerAvatar player)
        {
            var actor = player.photonView?.Owner?.ActorNumber ?? 0;
            if (actor != 0)
            {
                return actor;
            }

            return player.GetInstanceID();
        }

        private static int ComputeRoomsHash(List<object> rooms)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < rooms.Count; i++)
                {
                    hash = (hash * 31) + RuntimeHelpers.GetHashCode(rooms[i]);
                }

                return hash;
            }
        }

        private static Dictionary<object, HashSet<object>> GetOrBuildRoomGraph(object levelGenerator, List<object>? allLevelPoints)
        {
            var pointCount = allLevelPoints?.Count ?? 0;
            if (s_cachedRoomGraph != null &&
                ReferenceEquals(levelGenerator, s_cachedGraphLevelGenerator) &&
                s_cachedGraphPointCount == pointCount)
            {
                return s_cachedRoomGraph;
            }

            var graph = BuildRoomGraph(allLevelPoints);
            s_cachedGraphLevelGenerator = levelGenerator;
            s_cachedGraphPointCount = pointCount;
            s_cachedRoomGraph = graph;
            return graph;
        }

        private static bool TryGetTruckTarget(object levelGenerator, List<object>? allLevelPoints, out Vector3 truckPosition, out object? truckPoint)
        {
            truckPosition = Vector3.zero;
            truckPoint = null;
            if (s_levelPathTruckField != null)
            {
                var candidate = s_levelPathTruckField.GetValue(levelGenerator);
                if (TryGetLevelPointPosition(candidate, out truckPosition))
                {
                    truckPoint = candidate;
                    return true;
                }
            }

            if (allLevelPoints == null)
            {
                return false;
            }

            foreach (var point in allLevelPoints)
            {
                if (point == null)
                {
                    continue;
                }

                if (!TryIsTruckPoint(point))
                {
                    continue;
                }

                if (TryGetLevelPointPosition(point, out truckPosition))
                {
                    truckPoint = point;
                    return true;
                }
            }

            return false;
        }

        private static List<object>? GetAllLevelPoints(object levelGenerator)
        {
            if (s_levelPathPointsField == null)
            {
                return null;
            }

            var points = s_levelPathPointsField.GetValue(levelGenerator) as IEnumerable;
            if (points == null)
            {
                return null;
            }

            var list = new List<object>();
            foreach (var point in points)
            {
                if (point != null)
                {
                    list.Add(point);
                }
            }

            return list.Count > 0 ? list : null;
        }

        private static bool TryIsTruckPoint(object point)
        {
            if (point == null)
            {
                return false;
            }

            if (s_levelPointTruckField != null &&
                s_levelPointTruckField.GetValue(point) is bool flag &&
                flag)
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

        private static int ResolveShortestRoomPathToTruck(List<object> playerRooms, object? truckPoint, Dictionary<object, HashSet<object>>? roomGraph)
        {
            if (truckPoint == null || roomGraph == null || roomGraph.Count == 0)
            {
                return -1;
            }

            var truckRoom = GetLevelPointRoom(truckPoint);
            if (truckRoom == null || !roomGraph.ContainsKey(truckRoom))
            {
                return -1;
            }

            if (playerRooms.Count == 0)
            {
                return -1;
            }

            var visited = new HashSet<object>();
            var queue = new Queue<(object room, int distance)>();
            foreach (var room in playerRooms)
            {
                if (room == null || !roomGraph.ContainsKey(room) || !visited.Add(room))
                {
                    continue;
                }

                queue.Enqueue((room, 0));
            }

            while (queue.Count > 0)
            {
                var (room, depth) = queue.Dequeue();
                if (ReferenceEquals(room, truckRoom))
                {
                    return depth;
                }

                if (!roomGraph.TryGetValue(room, out var neighbors))
                {
                    continue;
                }

                foreach (var neighbor in neighbors)
                {
                    if (neighbor == null || !visited.Add(neighbor))
                    {
                        continue;
                    }

                    queue.Enqueue((neighbor, depth + 1));
                }
            }

            return -1;
        }

        private static List<object> GetPlayerRooms(PlayerAvatar player)
        {
            var results = new List<object>();
            if (player == null || s_playerRoomVolumeCheckField == null || s_roomVolumeCheckCurrentRoomsField == null)
            {
                return results;
            }

            object? roomCheck = null;
            var deathHead = player.playerDeathHead;
            if (deathHead != null && s_playerDeathHeadRoomVolumeCheckField != null)
            {
                roomCheck = s_playerDeathHeadRoomVolumeCheckField.GetValue(deathHead);
            }

            if (roomCheck == null)
            {
                roomCheck = s_playerRoomVolumeCheckField.GetValue(player);
            }

            if (roomCheck == null)
            {
                return results;
            }

            var currentRooms = s_roomVolumeCheckCurrentRoomsField.GetValue(roomCheck) as IEnumerable;
            if (currentRooms == null)
            {
                return results;
            }

            foreach (var room in currentRooms)
            {
                if (room != null)
                {
                    results.Add(room);
                }
            }

            return results;
        }

        private static Dictionary<object, HashSet<object>> BuildRoomGraph(List<object>? allLevelPoints)
        {
            var graph = new Dictionary<object, HashSet<object>>();
            if (allLevelPoints == null || s_levelPointRoomField == null)
            {
                return graph;
            }

            foreach (var point in allLevelPoints)
            {
                if (point == null)
                {
                    continue;
                }

                var room = GetLevelPointRoom(point);
                if (room == null)
                {
                    continue;
                }

                if (!graph.ContainsKey(room))
                {
                    graph[room] = new HashSet<object>();
                }

                var connected = GetConnectedPoints(point);
                if (connected == null)
                {
                    continue;
                }

                foreach (var neighborPoint in connected)
                {
                    if (neighborPoint == null)
                    {
                        continue;
                    }

                    var neighborRoom = GetLevelPointRoom(neighborPoint);
                    if (neighborRoom == null)
                    {
                        continue;
                    }

                    if (!graph.ContainsKey(neighborRoom))
                    {
                        graph[neighborRoom] = new HashSet<object>();
                    }

                    if (!ReferenceEquals(room, neighborRoom))
                    {
                        graph[room].Add(neighborRoom);
                        graph[neighborRoom].Add(room);
                    }
                }
            }

            return graph;
        }

        private static object? GetLevelPointRoom(object levelPoint)
        {
            if (levelPoint == null || s_levelPointRoomField == null)
            {
                return null;
            }

            return s_levelPointRoomField.GetValue(levelPoint);
        }

        private static IEnumerable<object>? GetConnectedPoints(object levelPoint)
        {
            if (levelPoint == null || s_levelPointConnectedPointsField == null)
            {
                return null;
            }

            if (s_levelPointConnectedPointsField.GetValue(levelPoint) is IEnumerable connected)
            {
                var list = new List<object>();
                foreach (var point in connected)
                {
                    if (point != null)
                    {
                        list.Add(point);
                    }
                }

                return list.Count > 0 ? list : null;
            }

            return null;
        }

        private static bool TryGetLevelPointPosition(object? levelPoint, out Vector3 position)
        {
            position = Vector3.zero;
            if (levelPoint == null)
            {
                return false;
            }

            if (levelPoint is Component component && component != null)
            {
                position = component.transform.position;
                return true;
            }

            var transformProperty = levelPoint.GetType().GetProperty("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (transformProperty != null && transformProperty.GetValue(levelPoint) is Transform transform)
            {
                position = transform.position;
                return true;
            }

            return false;
        }

        private static Vector3 GetPlayerWorldPosition(PlayerAvatar player)
        {
            if (player == null)
            {
                return Vector3.zero;
            }

            var deathHead = player.playerDeathHead;
            if (deathHead != null)
            {
                return deathHead.transform.position;
            }

            if (player.playerTransform != null)
            {
                return player.playerTransform.position;
            }

            return player.transform.position;
        }

        private static bool TryGetPlayerNavMeshPosition(PlayerAvatar player, Vector3 worldPosition, out Vector3 navMeshPosition)
        {
            navMeshPosition = Vector3.zero;
            if (player == null)
            {
                return false;
            }

            if (TrySamplePosition(worldPosition, 8f, out var sampledPosition))
            {
                navMeshPosition = sampledPosition;
                return true;
            }

            // Death head often moves above the navmesh; prefer multiple probes around its physics center
            // before falling back to PlayerAvatar.LastNavmeshPosition (which may remain at death location).
            var deathHead = player.playerDeathHead;
            if (deathHead != null)
            {
                var headCenter = deathHead.transform.position;
                if (s_playerDeathHeadPhysGrabObjectField != null &&
                    s_physGrabObjectCenterPointField != null)
                {
                    var physGrabObject = s_playerDeathHeadPhysGrabObjectField.GetValue(deathHead);
                    if (physGrabObject != null &&
                        s_physGrabObjectCenterPointField.GetValue(physGrabObject) is Vector3 centerPoint)
                    {
                        headCenter = centerPoint;
                    }
                }

                if (TrySamplePosition(headCenter, 12f, out sampledPosition) ||
                    TrySamplePosition(headCenter - Vector3.up * 0.5f, 18f, out sampledPosition) ||
                    TrySamplePosition(headCenter, 30f, out sampledPosition))
                {
                    navMeshPosition = sampledPosition;
                    return true;
                }

                // When the death head is active and no navmesh point can be resolved nearby,
                // avoid using stale avatar navmesh position from the corpse location.
                return false;
            }

            if (s_playerLastNavmeshField != null &&
                s_playerLastNavmeshField.GetValue(player) is Vector3 position &&
                position != Vector3.zero)
            {
                navMeshPosition = position;
                return true;
            }

            return false;
        }

        private static bool TrySamplePosition(Vector3 worldPosition, float maxDistance, out Vector3 navMeshPosition)
        {
            navMeshPosition = Vector3.zero;
            if (s_navMeshType == null || s_navMeshHitType == null || s_navMeshSamplePositionMethod == null)
            {
                return false;
            }

            var navHit = Activator.CreateInstance(s_navMeshHitType);
            if (navHit == null)
            {
                return false;
            }

            var args = new object?[] { worldPosition, navHit, maxDistance, -1 };
            if (s_navMeshSamplePositionMethod.Invoke(null, args) is not bool success || !success)
            {
                return false;
            }

            if (!TryGetNavMeshHitPosition(args[1], out var hitPosition))
            {
                return false;
            }

            navMeshPosition = hitPosition;
            return true;
        }

        private static bool TryGetNavMeshHitPosition(object? navHitBoxed, out Vector3 position)
        {
            position = Vector3.zero;
            if (navHitBoxed == null)
            {
                return false;
            }

            if (s_navMeshHitPositionProperty != null &&
                s_navMeshHitPositionProperty.GetValue(navHitBoxed) is Vector3 propPosition)
            {
                position = propPosition;
                return true;
            }

            if (s_navMeshHitPositionField != null &&
                s_navMeshHitPositionField.GetValue(navHitBoxed) is Vector3 fieldPosition)
            {
                position = fieldPosition;
                return true;
            }

            return false;
        }

        private static bool TryCalculatePathDistance(Vector3 from, Vector3 to, out float navMeshDistance)
        {
            navMeshDistance = 0f;
            if (s_navMeshType == null || s_navMeshPathType == null || s_navMeshCalculatePathMethod == null || s_navMeshPathCornersProperty == null)
            {
                return false;
            }

            var path = Activator.CreateInstance(s_navMeshPathType);
            if (path == null)
            {
                return false;
            }

            var args = new object?[] { from, to, -1, path };
            if (s_navMeshCalculatePathMethod.Invoke(null, args) is not bool success || !success)
            {
                return false;
            }

            if (s_navMeshPathCornersProperty.GetValue(path) is not Vector3[] corners || corners.Length == 0)
            {
                navMeshDistance = Vector3.Distance(from, to);
                return true;
            }

            var previous = from;
            var totalDistance = 0f;
            foreach (var corner in corners)
            {
                totalDistance += Vector3.Distance(previous, corner);
                previous = corner;
            }

            navMeshDistance = totalDistance;
            return true;
        }
    }
}
