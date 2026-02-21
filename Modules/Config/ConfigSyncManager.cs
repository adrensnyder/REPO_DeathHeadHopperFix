#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using DeathHeadHopperFix.Modules.Utilities;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeathHeadHopperFix.Modules.Config
{
    internal sealed class ConfigSyncManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private static ConfigSyncManager? s_instance;

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
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySendSnapshot();
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.RemoveCallbackTarget(this);
            ConfigManager.HostControlledChanged -= OnHostControlledChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
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

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Re-assert host-controlled values after scene transitions (including procedural level loads).
            if (PhotonNetwork.IsMasterClient)
            {
                TrySendSnapshot();
            }
        }

        public override void OnLeftRoom()
        {
            ConfigManager.RestoreLocalHostControlledBaseline();
        }

        internal static void RequestHostSnapshotBroadcast()
        {
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

            PhotonNetwork.RaiseEvent(PhotonEventCodes.ConfigSync, payload, options, SendOptions.SendReliable);
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent == null || photonEvent.Code != PhotonEventCodes.ConfigSync)
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
