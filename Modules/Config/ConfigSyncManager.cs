#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Config
{
    internal sealed class ConfigSyncManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private const byte ConfigSyncEventCode = 79;
        private const float RuntimeReconcileIntervalSeconds = 0.75f;
        private const float RuntimeRebroadcastIntervalSeconds = 5f;
        private static ConfigSyncManager? s_instance;
        private float _nextRuntimeReconcileAt;
        private float _nextRuntimeRebroadcastAt;
        private int _lastSnapshotHash;

        internal static void EnsureCreated()
        {
            if (s_instance != null)
            {
                return;
            }

            var go = new GameObject("DHHFix.ConfigSyncManager");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ConfigSyncManager>();
        }

        public override void OnEnable()
        {
            base.OnEnable();
            PhotonNetwork.AddCallbackTarget(this);
            ConfigManager.HostControlledChanged += OnHostControlledChanged;
            _nextRuntimeReconcileAt = 0f;
            _nextRuntimeRebroadcastAt = 0f;
            _lastSnapshotHash = 0;
            TrySendSnapshot();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.RemoveCallbackTarget(this);
            ConfigManager.HostControlledChanged -= OnHostControlledChanged;
        }

        private void OnHostControlledChanged()
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            TrySendSnapshot();
        }

        public override void OnJoinedRoom()
        {
            if (PhotonNetwork.IsMasterClient)
            {
                TrySendSnapshot();
            }
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (!PhotonNetwork.IsMasterClient || newPlayer == null)
            {
                return;
            }

            TrySendSnapshot(new[] { newPlayer.ActorNumber });
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                TrySendSnapshot();
            }
        }

        private void Update()
        {
            if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
            {
                return;
            }

            if (Time.unscaledTime < _nextRuntimeReconcileAt)
            {
                return;
            }

            _nextRuntimeReconcileAt = Time.unscaledTime + RuntimeReconcileIntervalSeconds;
            var snapshot = ConfigManager.SnapshotHostControlled();
            if (snapshot.Count == 0)
            {
                return;
            }

            var hash = ComputeSnapshotHash(snapshot);
            if (hash == _lastSnapshotHash)
            {
                if (Time.unscaledTime >= _nextRuntimeRebroadcastAt)
                {
                    _nextRuntimeRebroadcastAt = Time.unscaledTime + RuntimeRebroadcastIntervalSeconds;
                    TrySendSnapshot();
                }
                return;
            }

            _lastSnapshotHash = hash;
            _nextRuntimeRebroadcastAt = Time.unscaledTime + RuntimeRebroadcastIntervalSeconds;
            TrySendSnapshot();
        }

        private static void TrySendSnapshot(int[]? targetActors = null)
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            if (!PhotonNetwork.IsMasterClient)
            {
                return;
            }

            var snapshot = ConfigManager.SnapshotHostControlled();
            if (snapshot.Count == 0)
            {
                return;
            }

            var payload = new ExitGames.Client.Photon.Hashtable(snapshot.Count);
            foreach (var kvp in snapshot)
            {
                payload[kvp.Key] = kvp.Value;
            }

            var options = new RaiseEventOptions
            {
                Receivers = targetActors == null ? ReceiverGroup.Others : ReceiverGroup.All,
                TargetActors = targetActors
            };

            PhotonNetwork.RaiseEvent(ConfigSyncEventCode, payload, options, SendOptions.SendReliable);
        }

        private static int ComputeSnapshotHash(Dictionary<string, string> snapshot)
        {
            unchecked
            {
                var hash = 17;
                var keys = new List<string>(snapshot.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(key);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(snapshot[key] ?? string.Empty);
                }
                return hash;
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent == null || photonEvent.Code != ConfigSyncEventCode)
            {
                return;
            }

            if (PhotonNetwork.IsMasterClient)
            {
                return;
            }

            if (photonEvent.CustomData is not ExitGames.Client.Photon.Hashtable table)
            {
                return;
            }

            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in table)
            {
                if (entry.Key is string key && entry.Value is string value)
                {
                    snapshot[key] = value;
                }
            }

            if (snapshot.Count > 0)
            {
                ConfigManager.ApplyHostSnapshot(snapshot);
            }
        }

    }
}
