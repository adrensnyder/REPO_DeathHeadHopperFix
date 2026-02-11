#nullable enable

using System.Text;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Diagnostics
{
    internal static class LastChanceTruckDistanceLogger
    {
        private const string LogKey = "LastChance.TruckDistances";

        internal static void LogDistances()
        {
            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            if (!LogLimiter.ShouldLog(LogKey, 30))
            {
                return;
            }

            var distances = PlayerTruckDistanceHelper.GetDistancesFromTruck();
            if (distances.Length == 0)
            {
                return;
            }

            var message = new StringBuilder("[LastChance] TruckDistances:");
            foreach (var record in distances)
            {
                var playerName = record.PlayerAvatar != null ? record.PlayerAvatar.GetType().Name : "null";
                var distanceText = record.HasValidPath ? $"{record.NavMeshDistance:F1}m" : "n/a";
                var heightText = record.HeightDelta >= 0
                    ? $"+{record.HeightDelta:F2}m"
                    : $"{record.HeightDelta:F2}m";
                var shortestRoomsText = record.ShortestRoomPathToTruck >= 0 ? record.ShortestRoomPathToTruck.ToString() : "n/a";
                var totalMapRoomsText = record.TotalMapRooms > 0 ? record.TotalMapRooms.ToString() : "n/a";

                message.AppendFormat(
                    " {0}=distance:{1},height:{2},shortestRoomPathToTruck:{3},totalMapRooms:{4}",
                    playerName,
                    distanceText,
                    heightText,
                    shortestRoomsText,
                    totalMapRoomsText);
            }

            Debug.Log(message.ToString());
        }
    }
}

