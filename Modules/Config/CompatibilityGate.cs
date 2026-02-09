#nullable enable

using System;
using System.Collections.Generic;
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
        private static CompatibilityGate? s_instance;
        private static bool s_hostApprovedLastChanceCluster = true;
        private static bool s_receivedHostDecision;
        private static bool s_lastAppliedRuntimeDisable;
        private readonly HashSet<int> _playersWithFix = new();

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
            _playersWithFix.Clear();
            _playersWithFix.Add(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0);
            s_receivedHostDecision = PhotonNetwork.IsMasterClient;
            s_hostApprovedLastChanceCluster = PhotonNetwork.IsMasterClient;

            AnnounceLocalFixPresence();
            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public override void OnLeftRoom()
        {
            _playersWithFix.Clear();
            s_receivedHostDecision = false;
            s_hostApprovedLastChanceCluster = true;
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
                _playersWithFix.Remove(otherPlayer.ActorNumber);
            }

            EvaluateHostApprovalAndBroadcast(forceBroadcast: true);
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            _playersWithFix.Clear();
            _playersWithFix.Add(PhotonNetwork.LocalPlayer?.ActorNumber ?? 0);
            s_receivedHostDecision = PhotonNetwork.IsMasterClient;
            s_hostApprovedLastChanceCluster = PhotonNetwork.IsMasterClient;

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
                if (!PhotonNetwork.IsMasterClient || photonEvent.CustomData is not int actorNumber)
                {
                    return;
                }

                _playersWithFix.Add(actorNumber);
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

            var changed = !s_receivedHostDecision || s_hostApprovedLastChanceCluster != allowed;
            s_receivedHostDecision = true;
            s_hostApprovedLastChanceCluster = allowed;
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

            PhotonNetwork.RaiseEvent(ClientFixPresenceEventCode, actor, options, SendOptions.SendReliable);
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

            var actor = PhotonNetwork.LocalPlayer?.ActorNumber ?? 0;
            if (actor > 0)
            {
                _playersWithFix.Add(actor);
            }

            var allPlayersHaveFix = true;
            var players = PhotonNetwork.PlayerList;
            for (var i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || !_playersWithFix.Contains(player.ActorNumber))
                {
                    allPlayersHaveFix = false;
                    break;
                }
            }

            var changed = s_hostApprovedLastChanceCluster != allPlayersHaveFix || !s_receivedHostDecision;
            s_hostApprovedLastChanceCluster = allPlayersHaveFix;
            s_receivedHostDecision = true;
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
                new object[] { s_hostApprovedLastChanceCluster },
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
            }
            else
            {
                ConfigManager.ClearHostRuntimeOverride(LastChanceModeKey);
            }
        }
    }
}
