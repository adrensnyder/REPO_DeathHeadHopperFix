#nullable enable

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class PlayerStateExtractionHelper
    {
        private static readonly FieldInfo? s_playerNameField = AccessTools.Field(typeof(PlayerAvatar), "playerName");
        private static readonly FieldInfo? s_playerDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo? s_playerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo? s_playerRoomVolumeCheckField = AccessTools.Field(typeof(PlayerAvatar), "RoomVolumeCheck");
        private static readonly FieldInfo? s_playerSteamIdShortField = AccessTools.Field(typeof(PlayerAvatar), "steamIDshort");
        private static readonly FieldInfo? s_visualColorField = AccessTools.Field(typeof(PlayerAvatarVisuals), "color");
        private static FieldInfo? s_roomVolumeCheckInTruckField;
        private static FieldInfo? s_deathHeadInTruckField;
        private static FieldInfo? s_deathHeadRoomVolumeCheckField;

        internal readonly struct PlayerStateSnapshot
        {
            internal PlayerStateSnapshot(
                int actorNumber,
                int steamIdShort,
                string name,
                Color color,
                bool isAlive,
                bool isDead,
                bool isInTruck,
                bool isSurrendered,
                int sourceOrder)
            {
                ActorNumber = actorNumber;
                SteamIdShort = steamIdShort;
                Name = name;
                Color = color;
                IsAlive = isAlive;
                IsDead = isDead;
                IsInTruck = isInTruck;
                IsSurrendered = isSurrendered;
                SourceOrder = sourceOrder;
            }

            internal int ActorNumber { get; }
            internal int SteamIdShort { get; }
            internal string Name { get; }
            internal Color Color { get; }
            internal bool IsAlive { get; }
            internal bool IsDead { get; }
            internal bool IsInTruck { get; }
            internal bool IsSurrendered { get; }
            internal int SourceOrder { get; }
        }

        internal static List<PlayerStateSnapshot> GetPlayersStateSnapshot()
        {
            var snapshots = new List<PlayerStateSnapshot>();
            var director = GameDirector.instance;
            if (director == null || director.PlayerList == null || director.PlayerList.Count == 0)
            {
                return snapshots;
            }

            for (var i = 0; i < director.PlayerList.Count; i++)
            {
                var player = director.PlayerList[i];
                if (player == null)
                {
                    continue;
                }

                var actorNumber = player.photonView?.Owner?.ActorNumber ?? 0;
                var steamIdShort = GetSteamIdShort(player);
                var name = GetPlayerName(player);
                var color = GetPlayerColor(player);
                var deadSet = IsDeadSet(player);
                var disabled = IsDisabled(player);
                var isDead = deadSet || disabled;
                var isAlive = !isDead;
                var isInTruck = IsPlayerInTruck(player, disabled);
                var isSurrendered = LastChanceInteropBridge.IsPlayerSurrenderedForData(player);

                snapshots.Add(
                    new PlayerStateSnapshot(
                        actorNumber,
                        steamIdShort,
                        name,
                        color,
                        isAlive,
                        isDead,
                        isInTruck,
                        isSurrendered,
                        i));
            }

            snapshots.Sort(CompareSnapshotOrder);
            return snapshots;
        }

        internal static List<PlayerStateSnapshot> GetPlayersStillInLastChance()
        {
            var allPlayers = GetPlayersStateSnapshot();
            var activePlayers = new List<PlayerStateSnapshot>(allPlayers.Count);
            for (var i = 0; i < allPlayers.Count; i++)
            {
                var snapshot = allPlayers[i];
                if (!snapshot.IsSurrendered)
                {
                    activePlayers.Add(snapshot);
                }
            }

            return activePlayers;
        }

        private static int CompareSnapshotOrder(PlayerStateSnapshot left, PlayerStateSnapshot right)
        {
            if (left.ActorNumber > 0 && right.ActorNumber > 0)
            {
                return left.ActorNumber.CompareTo(right.ActorNumber);
            }

            return left.SourceOrder.CompareTo(right.SourceOrder);
        }

        private static string GetPlayerName(PlayerAvatar player)
        {
            if (s_playerNameField != null &&
                s_playerNameField.GetValue(player) is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return "unknown";
        }

        private static Color GetPlayerColor(PlayerAvatar player)
        {
            var visuals = player.playerAvatarVisuals;
            if (visuals == null)
            {
                return Color.black;
            }

            if (s_visualColorField != null && s_visualColorField.GetValue(visuals) is Color color)
            {
                return color;
            }

            return Color.black;
        }

        private static bool IsDeadSet(PlayerAvatar player)
        {
            return s_playerDeadSetField != null &&
                   s_playerDeadSetField.GetValue(player) is bool deadSet &&
                   deadSet;
        }

        private static bool IsDisabled(PlayerAvatar player)
        {
            return s_playerIsDisabledField != null &&
                   s_playerIsDisabledField.GetValue(player) is bool isDisabled &&
                   isDisabled;
        }

        private static bool IsPlayerInTruck(PlayerAvatar player, bool isDisabled)
        {
            if (!isDisabled)
            {
                var roomVolumeCheck = GetRoomVolumeCheck(player);
                return roomVolumeCheck != null && IsRoomVolumeInTruck(roomVolumeCheck);
            }

            var deathHead = player.playerDeathHead;
            if (deathHead == null)
            {
                return false;
            }

            var roomVolume = GetDeathHeadRoomVolumeCheck(deathHead);
            if (roomVolume != null)
            {
                return IsRoomVolumeInTruck(roomVolume);
            }

            var inTruckField = GetDeathHeadInTruckField(deathHead.GetType());
            return inTruckField != null &&
                   inTruckField.GetValue(deathHead) is bool inTruck &&
                   inTruck;
        }

        private static object? GetRoomVolumeCheck(PlayerAvatar player)
        {
            if (s_playerRoomVolumeCheckField == null)
            {
                return null;
            }

            return s_playerRoomVolumeCheckField.GetValue(player);
        }

        private static bool IsRoomVolumeInTruck(object roomVolumeCheck)
        {
            if (roomVolumeCheck == null)
            {
                return false;
            }

            var field = s_roomVolumeCheckInTruckField;
            if (field == null || field.DeclaringType != roomVolumeCheck.GetType())
            {
                field = AccessTools.Field(roomVolumeCheck.GetType(), "inTruck");
                s_roomVolumeCheckInTruckField = field;
            }

            return field != null && field.GetValue(roomVolumeCheck) is bool inTruck && inTruck;
        }

        private static FieldInfo? GetDeathHeadInTruckField(System.Type deathHeadType)
        {
            var field = s_deathHeadInTruckField;
            if (field == null || field.DeclaringType != deathHeadType)
            {
                field = AccessTools.Field(deathHeadType, "inTruck");
                s_deathHeadInTruckField = field;
            }

            return field;
        }

        private static object? GetDeathHeadRoomVolumeCheck(PlayerDeathHead deathHead)
        {
            var field = s_deathHeadRoomVolumeCheckField;
            if (field == null || field.DeclaringType != deathHead.GetType())
            {
                field = AccessTools.Field(deathHead.GetType(), "roomVolumeCheck");
                s_deathHeadRoomVolumeCheckField = field;
            }

            return field?.GetValue(deathHead);
        }

        private static int GetSteamIdShort(PlayerAvatar player)
        {
            return s_playerSteamIdShortField != null &&
                   s_playerSteamIdShortField.GetValue(player) is int steamIdShort
                ? steamIdShort
                : 0;
        }
    }
}
