#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class PlayerTruckDistanceHelper
    {
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

        internal static PlayerTruckDistance[] GetDistancesFromTruck()
        {
            if (s_levelGeneratorInstanceField == null)
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

            var roomGraph = BuildRoomGraph(allLevelPoints);
            var totalMapRooms = roomGraph.Count > 0 ? roomGraph.Count : -1;
            var distances = new List<PlayerTruckDistance>(director.PlayerList.Count);
            foreach (var player in director.PlayerList)
            {
                if (player == null)
                {
                    continue;
                }

                var worldPosition = GetPlayerWorldPosition(player);
                var heightDelta = worldPosition.y - truckPosition.y;
                var navDistance = -1f;
                var hasPath = TryGetPlayerNavMeshPosition(player, worldPosition, out var navMeshStart) &&
                              TryCalculatePathDistance(navMeshStart, truckPosition, out navDistance);
                var shortestRoomPathToTruck = ResolveShortestRoomPathToTruck(player, truckPoint, roomGraph);

                distances.Add(new PlayerTruckDistance(
                    player,
                    hasPath ? navDistance : -1f,
                    heightDelta,
                    shortestRoomPathToTruck,
                    totalMapRooms,
                    hasPath));
            }

            return distances.ToArray();
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

        private static int ResolveShortestRoomPathToTruck(PlayerAvatar player, object? truckPoint, Dictionary<object, HashSet<object>> roomGraph)
        {
            if (truckPoint == null || roomGraph.Count == 0)
            {
                return -1;
            }

            var truckRoom = GetLevelPointRoom(truckPoint);
            if (truckRoom == null || !roomGraph.ContainsKey(truckRoom))
            {
                return -1;
            }

            var playerRooms = GetPlayerRooms(player);
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
                    results.Add(room);
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
                return null;
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
