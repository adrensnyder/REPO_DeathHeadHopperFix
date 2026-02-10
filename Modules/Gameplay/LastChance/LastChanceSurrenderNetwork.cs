#nullable enable

using ExitGames.Client.Photon;
using DeathHeadHopperFix.Modules.Utilities;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    internal sealed class LastChanceSurrenderNetwork : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private static LastChanceSurrenderNetwork? s_instance;
        private static float s_lastTruckHintSentAt;
        private static int s_lastTruckHintRoomHash;
        private static int s_lastTruckHintLevelStamp = -1;
        private const float TruckHintBroadcastIntervalSeconds = 0.5f;

        internal static void EnsureCreated()
        {
            if (s_instance != null)
            {
                return;
            }

            var go = new GameObject("DHHFix.LastChanceSurrender");
            Object.DontDestroyOnLoad(go);
            s_instance = go.AddComponent<LastChanceSurrenderNetwork>();
        }

        internal static void NotifyLocalSurrender(int actorNumber)
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            EnsureCreated();

            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.All
            };

            PhotonNetwork.RaiseEvent(PhotonEventCodes.LastChanceSurrender, actorNumber, options, SendOptions.SendReliable);
        }

        internal static void NotifyTimerState(bool active, float secondsRemaining)
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            EnsureCreated();
            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.All
            };

            PhotonNetwork.RaiseEvent(
                PhotonEventCodes.LastChanceTimerState,
                new object[] { active, secondsRemaining },
                options,
                SendOptions.SendReliable);
        }

        internal static void NotifyDirectionPenaltyRequest()
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            EnsureCreated();
            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.MasterClient
            };

            PhotonNetwork.RaiseEvent(
                PhotonEventCodes.LastChanceDirectionPenaltyRequest,
                null,
                options,
                SendOptions.SendReliable);
        }

        internal static void NotifyUiState(int requiredOnTruck, object[] playerStatesPayload)
        {
            if (!PhotonNetwork.InRoom)
            {
                return;
            }

            EnsureCreated();
            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.All
            };

            PhotonNetwork.RaiseEvent(
                PhotonEventCodes.LastChanceUiState,
                new object[] { requiredOnTruck, playerStatesPayload },
                options,
                SendOptions.SendReliable);
        }

        internal static void TryBroadcastLocalPlayerTruckHint()
        {
            if (!PhotonNetwork.InRoom || SemiFunc.IsMasterClient() || PhotonNetwork.LocalPlayer == null)
            {
                return;
            }

            EnsureCreated();
            if (!PlayerTruckDistanceHelper.TryBuildLocalPlayerTruckHint(out var roomHash, out var heightDelta, out var levelStamp))
            {
                return;
            }

            var roomChanged = roomHash != s_lastTruckHintRoomHash || levelStamp != s_lastTruckHintLevelStamp;
            var dueByTime = Time.unscaledTime - s_lastTruckHintSentAt >= TruckHintBroadcastIntervalSeconds;
            if (!roomChanged && !dueByTime)
            {
                return;
            }

            var options = new RaiseEventOptions
            {
                Receivers = ReceiverGroup.MasterClient
            };

            PhotonNetwork.RaiseEvent(
                PhotonEventCodes.LastChancePlayerTruckHint,
                new object[] { PhotonNetwork.LocalPlayer.ActorNumber, roomHash, heightDelta, levelStamp },
                options,
                SendOptions.SendUnreliable);

            s_lastTruckHintSentAt = Time.unscaledTime;
            s_lastTruckHintRoomHash = roomHash;
            s_lastTruckHintLevelStamp = levelStamp;
        }

        public override void OnEnable()
        {
            base.OnEnable();
            PhotonNetwork.AddCallbackTarget(this);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            base.OnMasterClientSwitched(newMasterClient);

            LastChanceTimerController.SuppressForCurrentRoom(
                "[LastChance] Master client switched; disabling LastChance and related runtime features for room safety.");
        }

        public override void OnJoinedRoom()
        {
            base.OnJoinedRoom();
            LastChanceTimerController.ClearRoomSuppression();
        }

        public override void OnLeftRoom()
        {
            base.OnLeftRoom();
            LastChanceTimerController.ClearRoomSuppression();
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == PhotonEventCodes.LastChanceTimerState)
            {
                if (photonEvent.CustomData is object[] timerPayload &&
                    timerPayload.Length >= 2 &&
                    timerPayload[0] is bool active &&
                    timerPayload[1] is float remaining)
                {
                    LastChanceTimerController.ApplyNetworkTimerState(active, remaining);
                }
                return;
            }

            if (photonEvent.Code == PhotonEventCodes.LastChanceDirectionPenaltyRequest)
            {
                LastChanceTimerController.HandleDirectionPenaltyRequest(photonEvent.Sender);
                return;
            }

            if (photonEvent.Code == PhotonEventCodes.LastChanceUiState)
            {
                if (photonEvent.CustomData is object[] uiPayload &&
                    uiPayload.Length >= 2 &&
                    uiPayload[0] is int required &&
                    uiPayload[1] is object[] states)
                {
                    LastChanceTimerController.ApplyNetworkUiState(required, states, photonEvent.Sender);
                }
                return;
            }

            if (photonEvent.Code == PhotonEventCodes.LastChancePlayerTruckHint)
            {
                if (photonEvent.CustomData is object[] hintPayload &&
                    hintPayload.Length >= 4 &&
                    hintPayload[0] is int hintActorNumber &&
                    hintPayload[1] is int roomHash &&
                    hintPayload[2] is float heightDelta &&
                    hintPayload[3] is int levelStamp)
                {
                    PlayerTruckDistanceHelper.ApplyRemotePlayerHint(hintActorNumber, roomHash, heightDelta, levelStamp);
                }
                return;
            }

            if (photonEvent.Code != PhotonEventCodes.LastChanceSurrender)
            {
                return;
            }

            if (photonEvent.CustomData is int actorNumber)
            {
                LastChanceTimerController.RegisterRemoteSurrender(actorNumber);
                return;
            }

            if (photonEvent.CustomData is object[] payload &&
                payload.Length > 0 &&
                payload[0] is int payloadActor)
            {
                LastChanceTimerController.RegisterRemoteSurrender(payloadActor);
            }
        }
    }
}
