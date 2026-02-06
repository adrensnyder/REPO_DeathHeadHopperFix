#nullable enable

using System.Text;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
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
                var roomsText = record.RoomsAway >= 0 ? record.RoomsAway.ToString() : "n/a";

                message.AppendFormat(" {0}=d{1},h{2},r{3}", playerName, distanceText, heightText, roomsText);
            }

            Debug.Log(message.ToString());
        }
    }
}
