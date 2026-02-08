#nullable enable

using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    internal sealed class LastChanceSurrenderNetwork : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        private const byte LastChanceSurrenderEventCode = 80;
        private const byte LastChanceTimerStateEventCode = 81;
        private static LastChanceSurrenderNetwork? s_instance;

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

            PhotonNetwork.RaiseEvent(LastChanceSurrenderEventCode, actorNumber, options, SendOptions.SendReliable);
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
                LastChanceTimerStateEventCode,
                new object[] { active, secondsRemaining },
                options,
                SendOptions.SendReliable);
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

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == LastChanceTimerStateEventCode)
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

            if (photonEvent.Code != LastChanceSurrenderEventCode)
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
