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
            internal PlayerTruckDistance(PlayerAvatar playerAvatar, float navMeshDistance, float heightDelta, int roomsAway, bool hasValidPath)
            {
                PlayerAvatar = playerAvatar;
                NavMeshDistance = navMeshDistance;
                HeightDelta = heightDelta;
                RoomsAway = roomsAway;
                HasValidPath = hasValidPath;
            }

            internal PlayerAvatar PlayerAvatar { get; }
            internal float NavMeshDistance { get; }
            internal float HeightDelta { get; }
            internal int RoomsAway { get; }
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
        private static readonly FieldInfo? s_navMeshHitPositionField = s_navMeshHitType == null ? null : AccessTools.Field(s_navMeshHitType, "position");
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
                var roomsAway = ResolveRoomsAway(player, truckPoint, allLevelPoints);

                distances.Add(new PlayerTruckDistance(
                    player,
                    hasPath ? navDistance : -1f,
                    heightDelta,
                    roomsAway,
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

        private static int ResolveRoomsAway(PlayerAvatar player, object? truckPoint, List<object>? allLevelPoints)
        {
            if (truckPoint == null || allLevelPoints == null)
            {
                return -1;
            }

            var playerPoints = GetPlayerLevelPoints(player, allLevelPoints);
            if (playerPoints.Count == 0)
            {
                return -1;
            }

            var minDistance = int.MaxValue;
            foreach (var point in playerPoints)
            {
                var distance = CalculateRoomDistance(point, truckPoint);
                if (!distance.HasValue)
                {
                    continue;
                }

                if (distance.Value < minDistance)
                {
                    minDistance = distance.Value;
                    if (minDistance == 0)
                    {
                        break;
                    }
                }
            }

            return minDistance == int.MaxValue ? -1 : minDistance;
        }

        private static List<object> GetPlayerLevelPoints(PlayerAvatar player, List<object> allLevelPoints)
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

            var seen = new HashSet<object>();
            foreach (var room in currentRooms)
            {
                if (room == null)
                {
                    continue;
                }

                foreach (var levelPoint in allLevelPoints)
                {
                    if (levelPoint == null)
                    {
                        continue;
                    }

                    if (LevelPointRoomMatches(levelPoint, room) && seen.Add(levelPoint))
                    {
                        results.Add(levelPoint);
                    }
                }
            }

            return results;
        }

        private static bool LevelPointRoomMatches(object levelPoint, object room)
        {
            if (levelPoint == null || room == null || s_levelPointRoomField == null)
            {
                return false;
            }

            var matchRoom = s_levelPointRoomField.GetValue(levelPoint);
            return ReferenceEquals(matchRoom, room);
        }

        private static int? CalculateRoomDistance(object startPoint, object targetPoint)
        {
            if (startPoint == null || targetPoint == null || s_levelPointConnectedPointsField == null)
            {
                return null;
            }

            if (ReferenceEquals(startPoint, targetPoint))
            {
                return 0;
            }

            var visited = new HashSet<object> { startPoint };
            var queue = new Queue<(object point, int distance)>();
            queue.Enqueue((startPoint, 0));

            while (queue.Count > 0)
            {
                var (current, depth) = queue.Dequeue();
                var neighbors = GetConnectedPoints(current);
                if (neighbors == null)
                {
                    continue;
                }

                foreach (var neighbor in neighbors)
                {
                    if (neighbor == null || visited.Contains(neighbor))
                    {
                        continue;
                    }

                    if (ReferenceEquals(neighbor, targetPoint))
                    {
                        return depth + 1;
                    }

                    visited.Add(neighbor);
                    queue.Enqueue((neighbor, depth + 1));
                }
            }

            return null;
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

            if (TrySamplePosition(worldPosition, out var sampledPosition))
            {
                navMeshPosition = sampledPosition;
                return true;
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

        private static bool TrySamplePosition(Vector3 worldPosition, out Vector3 navMeshPosition)
        {
            navMeshPosition = Vector3.zero;
            if (s_navMeshType == null || s_navMeshHitType == null || s_navMeshSamplePositionMethod == null || s_navMeshHitPositionField == null)
            {
                return false;
            }

            var navHit = Activator.CreateInstance(s_navMeshHitType);
            if (navHit == null)
            {
                return false;
            }

            var args = new object?[] { worldPosition, navHit, 6f, -1 };
            if (s_navMeshSamplePositionMethod.Invoke(null, args) is not bool success || !success)
            {
                return false;
            }

            if (s_navMeshHitPositionField.GetValue(args[1]) is not Vector3 hitPosition)
            {
                return false;
            }

            navMeshPosition = hitPosition;
            return true;
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
