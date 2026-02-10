#nullable enable

using System;
using System.Collections.Generic;
using BepInEx;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Config
{
    internal enum ModFeatureGate
    {
        LastChanceCluster = 1
    }

    internal sealed class CompatibilityGate : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private const byte ClientFixPresenceEventCode = 84;
        private const byte HostGateStateEventCode = 85;
        private const string LastChanceModeKey = nameof(FeatureFlags.LastChangeMode);
        private const string UnknownVersion = "unknown";
        private static CompatibilityGate? s_instance;
        private static bool s_hostApprovedLastChanceCluster = true;
        private static bool s_receivedHostDecision;
        private static bool s_lastAppliedRuntimeDisable;
        private static string s_lastHostDecisionReason = string.Empty;
        private static string s_lastLoggedIncompatibilityReason = string.Empty;
        private readonly Dictionary<int, string> _playersWithFixVersion = new();

        internal static event Action? HostApprovalChanged;

        internal static void EnsureCreated()
        {
            if (s_instance != null)
            {
                return;
            }

            var go = new GameObject("DHHFix.CompatibilityGate");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<CompatibilityGate>();
        }

        internal static bool IsFeatureUsable(ModFeatureGate feature)
        {
            if (feature != ModFeatureGate.LastChanceCluster)
            {
                return true;
            }

            if (!PhotonNetwork.InRoom || !SemiFunc.IsMultiplayer())
            {
                return true;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                return s_hostApprovedLastChanceCluster;
            }

            return s_receivedHostDecision && s_hostApprovedLastChanceCluster;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            PhotonNetwork.AddCallbackTarget(this);
            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public override void OnJoinedRoom()
        {
            _playersWithFixVersion.Clear();
            RegisterLocalPlayerFixVersion();
            s_receivedHostDecision = PhotonNetwork.IsMasterClient;
            s_hostApprovedLastChanceCluster = PhotonNetwork.IsMasterClient;
            s_lastHostDecisionReason = string.Empty;
            s_lastLoggedIncompatibilityReason = string.Empty;

            AnnounceLocalFixPresence();
            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public override void OnLeftRoom()
        {
            _playersWithFixVersion.Clear();
            s_receivedHostDecision = false;
            s_hostApprovedLastChanceCluster = true;
            s_lastHostDecisionReason = string.Empty;
            s_lastLoggedIncompatibilityReason = string.Empty;
            ApplyRuntimeHostOverrides();
            HostApprovalChanged?.Invoke();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (otherPlayer != null)
            {
                _playersWithFixVersion.Remove(otherPlayer.ActorNumber);
            }

            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            _playersWithFixVersion.Clear();
            RegisterLocalPlayerFixVersion();
            s_receivedHostDecision = PhotonNetwork.IsMasterClient;
            s_hostApprovedLastChanceCluster = PhotonNetwork.IsMasterClient;
            s_lastHostDecisionReason = string.Empty;
            s_lastLoggedIncompatibilityReason = string.Empty;

            AnnounceLocalFixPresence();
            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent == null)
            {
                return;
            }

            if (photonEvent.Code == ClientFixPresenceEventCode)
            {
                if (!PhotonNetwork.IsMasterClient)
                {
                    return;
                }

                var hasPayload = TryParseClientPresencePayload(photonEvent.CustomData, out var actorNumber, out var reportedVersion);
                if (!hasPayload || actorNumber <= 0)
                {
                    return;
                }

                _playersWithFixVersion[actorNumber] = reportedVersion;
                EvaluateHostApprovalAndBroadcast(forceBroadcast: false);
                return;
            }

            if (photonEvent.Code != HostGateStateEventCode)
            {
                return;
            }

            if (photonEvent.CustomData is not object[] payload || payload.Length < 1 || payload[0] is not bool allowed)
            {
                return;
            }

            var reason = payload.Length >= 2 && payload[1] is string rawReason ? rawReason : string.Empty;
            var changed = !s_receivedHostDecision || s_hostApprovedLastChanceCluster != allowed;
            s_receivedHostDecision = true;
            s_hostApprovedLastChanceCluster = allowed;
            s_lastHostDecisionReason = reason;
            if (!allowed)
            {
                EmitIncompatibilityWarning(reason);
            }
            if (changed)
            {
                HostApprovalChanged?.Invoke();
            }
        }

        private static void AnnounceLocalFixPresence()
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            var actor = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
            if (actor <= 0)
            {
                return;
            }

            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.MasterClient
            };

            PhotonNetwork.RaiseEvent(
                ClientFixPresenceEventCode,
                new object[] { actor, GetLocalFixVersion() },
                options,
                SendOptions.SendReliable);
        }

        private void EvaluateHostApprovalAndBroadcast(bool forceBroadcast)
        {
            if (!PhotonNetwork.InRoom || !SemiFunc.IsMultiplayer())
            {
                s_hostApprovedLastChanceCluster = true;
                s_receivedHostDecision = PhotonNetwork.IsMasterClient;
                ApplyRuntimeHostOverrides();
                HostApprovalChanged?.Invoke();
                return;
            }

            if (!PhotonNetwork.IsMasterClient)
            {
                ApplyRuntimeHostOverrides();
                HostApprovalChanged?.Invoke();
                return;
            }

            RegisterLocalPlayerFixVersion();

            var localVersion = GetLocalFixVersion();
            var missingPlayers = new List<string>();
            var mismatchPlayers = new List<string>();
            var allPlayersCompatible = true;
            var players = PhotonNetwork.PlayerList;
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null)
                {
                    continue;
                }

                if (!_playersWithFixVersion.TryGetValue(player.ActorNumber, out var remoteVersion))
                {
                    allPlayersCompatible = false;
                    missingPlayers.Add(FormatPlayerTag(player));
                    continue;
                }

                if (!string.Equals(remoteVersion, localVersion, StringComparison.Ordinal))
                {
                    allPlayersCompatible = false;
                    mismatchPlayers.Add($"{FormatPlayerTag(player)}={remoteVersion}");
                }
            }

            var reason = BuildIncompatibilityReason(localVersion, missingPlayers, mismatchPlayers);
            var changed = s_hostApprovedLastChanceCluster != allPlayersCompatible ||
                          !s_receivedHostDecision ||
                          !string.Equals(s_lastHostDecisionReason, reason, StringComparison.Ordinal);
            s_hostApprovedLastChanceCluster = allPlayersCompatible;
            s_receivedHostDecision = true;
            s_lastHostDecisionReason = reason;
            ApplyRuntimeHostOverrides();

            if (changed || forceBroadcast)
            {
                BroadcastHostApproval();
                HostApprovalChanged?.Invoke();
            }
        }

        private static void BroadcastHostApproval()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            {
                return;
            }

            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.All
            };

            PhotonNetwork.RaiseEvent(
                HostGateStateEventCode,
                new object[] { s_hostApprovedLastChanceCluster, s_lastHostDecisionReason },
                options,
                SendOptions.SendReliable);
        }

        private static void ApplyRuntimeHostOverrides()
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            var shouldDisable = !s_hostApprovedLastChanceCluster;
            if (s_lastAppliedRuntimeDisable == shouldDisable)
            {
                return;
            }

            s_lastAppliedRuntimeDisable = shouldDisable;
            if (shouldDisable)
            {
                ConfigManager.SetHostRuntimeOverride(LastChanceModeKey, bool.FalseString);
                EmitIncompatibilityWarning(s_lastHostDecisionReason);
            }
            else
            {
                ConfigManager.ClearHostRuntimeOverride(LastChanceModeKey);
                s_lastLoggedIncompatibilityReason = string.Empty;
            }
        }

        private static void EmitIncompatibilityWarning(string reason)
        {
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Unknown incompatibility reason." : reason.Trim();
            if (string.Equals(s_lastLoggedIncompatibilityReason, normalizedReason, StringComparison.Ordinal))
            {
                return;
            }

            s_lastLoggedIncompatibilityReason = normalizedReason;
            Debug.LogWarning($"[LastChance] LastChange disabled due to incompatibility: {normalizedReason}");
        }

        private static bool TryParseClientPresencePayload(object? customData, out int actorNumber, out string version)
        {
            actorNumber = 0;
            version = UnknownVersion;

            if (customData is int legacyActor)
            {
                actorNumber = legacyActor;
                return true;
            }

            if (customData is object[] payload &&
                payload.Length >= 2 &&
                payload[0] is int actor &&
                payload[1] is string payloadVersion)
            {
                actorNumber = actor;
                version = NormalizeVersion(payloadVersion);
                return true;
            }

            return false;
        }

        private void RegisterLocalPlayerFixVersion()
        {
            var actor = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
            if (actor <= 0)
            {
                return;
            }

            _playersWithFixVersion[actor] = GetLocalFixVersion();
        }

        private static string GetLocalFixVersion()
        {
            return NormalizeVersion(GetPluginVersionRaw());
        }

        private static string NormalizeVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return UnknownVersion;
            }

            return value!.Trim();
        }

        private static string? GetPluginVersionRaw()
        {
            var attr = (BepInPlugin?)Attribute.GetCustomAttribute(typeof(Plugin), typeof(BepInPlugin));
            return attr?.Version?.ToString();
        }

        private static string BuildIncompatibilityReason(
            string localVersion,
            List<string> missingPlayers,
            List<string> mismatchPlayers)
        {
            var reasonParts = new List<string>();
            if (missingPlayers.Count > 0)
            {
                reasonParts.Add("missing fix presence from: " + string.Join(", ", missingPlayers));
            }

            if (mismatchPlayers.Count > 0)
            {
                reasonParts.Add($"version mismatch (host={localVersion}): " + string.Join(", ", mismatchPlayers));
            }

            return reasonParts.Count == 0 ? string.Empty : string.Join(" | ", reasonParts);
        }

        private static string FormatPlayerTag(Player player)
        {
            var name = string.IsNullOrWhiteSpace(player.NickName) ? "unknown" : player.NickName.Trim();
            return $"{name}#{player.ActorNumber}";
        }
    }
}
