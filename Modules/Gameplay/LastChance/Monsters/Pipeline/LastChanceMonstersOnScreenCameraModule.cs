#nullable enable

using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch(typeof(EnemyOnScreen), "Awake")]
    internal static class LastChanceMonstersOnScreenCameraModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.ThinMan");
        private static readonly System.Collections.Generic.Dictionary<string, bool> s_lastBoolStateByKey = new();

        [HarmonyPostfix]
        private static void AwakePostfix(EnemyOnScreen __instance)
        {
            if (__instance == null)
            {
                return;
            }

            if (__instance.GetComponent<OnScreenCameraSyncRuntime>() == null)
            {
                __instance.gameObject.AddComponent<OnScreenCameraSyncRuntime>();
                DebugLog("OnScreen.AwakeAttach", $"enemy={__instance.gameObject.name} attachedRuntimeSync=True");
            }
        }

        internal static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceThinManFlow)
            {
                return;
            }

            if (!LogLimiter.ShouldLog($"ThinMan.{reason}", 300))
            {
                return;
            }

            Log.LogInfo($"[ThinMan][{reason}] {detail}");
        }

        internal static void DebugLogOnBoolTransition(string reason, string key, bool value, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceThinManFlow)
            {
                return;
            }

            var stateKey = $"{reason}.{key}";
            if (s_lastBoolStateByKey.TryGetValue(stateKey, out var previous) && previous == value)
            {
                if (!LogLimiter.ShouldLog($"ThinMan.{stateKey}.Heartbeat", 600))
                {
                    return;
                }
            }

            s_lastBoolStateByKey[stateKey] = value;
            Log.LogInfo($"[ThinMan][{reason}] {detail}");
        }
    }

    internal sealed class OnScreenCameraSyncRuntime : MonoBehaviour
    {
        private static readonly FieldInfo? s_mainCameraField = AccessTools.Field(typeof(EnemyOnScreen), "MainCamera");
        private static readonly FieldInfo? s_onScreenLocalField = AccessTools.Field(typeof(EnemyOnScreen), "OnScreenLocal");
        private static readonly FieldInfo? s_culledLocalField = AccessTools.Field(typeof(EnemyOnScreen), "CulledLocal");
        private static readonly MethodInfo? s_onScreenPlayerUpdateMethod = AccessTools.Method(typeof(EnemyOnScreen), "OnScreenPlayerUpdate");
        private EnemyOnScreen? _onScreen;
        private bool _lastSyncedOnScreenLocal;
        private bool _lastSyncedCulledLocal;
        private bool _hasSyncSnapshot;
        private int _lastCameraInstanceId;
        private bool _hasCameraSnapshot;

        private void Awake()
        {
            _onScreen = GetComponent<EnemyOnScreen>();
        }

        private void LateUpdate()
        {
            if (_onScreen == null || s_mainCameraField == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return;
            }

            var current = CameraUtils.Instance != null ? CameraUtils.Instance.MainCamera : Camera.main;
            if (current != null)
            {
                s_mainCameraField.SetValue(_onScreen, current);

                var currentCameraId = current.GetInstanceID();
                var cameraChanged = !_hasCameraSnapshot || _lastCameraInstanceId != currentCameraId;
                if (cameraChanged)
                {
                    LastChanceMonstersOnScreenCameraModule.DebugLog("Camera.Sync", $"enemy={_onScreen.gameObject.name} camera={current.name} changed={cameraChanged}");
                }

                _lastCameraInstanceId = currentCameraId;
                _hasCameraSnapshot = true;
            }

            SyncLocalHeadProxyOnScreenState();
        }

        private void SyncLocalHeadProxyOnScreenState()
        {
            if (_onScreen == null || !GameManager.Multiplayer() || s_onScreenLocalField == null || s_culledLocalField == null || s_onScreenPlayerUpdateMethod == null)
            {
                return;
            }

            var localPlayer = GetLocalPlayerAvatar();
            if (localPlayer == null || localPlayer.photonView == null || localPlayer.photonView.ViewID < 0)
            {
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(localPlayer))
            {
                _hasSyncSnapshot = false;
                LastChanceMonstersOnScreenCameraModule.DebugLog("Sync.Skip.NoHeadProxy", $"enemy={_onScreen.gameObject.name} player={localPlayer.photonView.ViewID}");
                return;
            }

            var onScreenLocal = s_onScreenLocalField.GetValue(_onScreen) as bool? ?? false;
            var culledLocal = s_culledLocalField.GetValue(_onScreen) as bool? ?? false;

            if (_hasSyncSnapshot && onScreenLocal == _lastSyncedOnScreenLocal && culledLocal == _lastSyncedCulledLocal)
            {
                return;
            }

            _lastSyncedOnScreenLocal = onScreenLocal;
            _lastSyncedCulledLocal = culledLocal;
            _hasSyncSnapshot = true;

            s_onScreenPlayerUpdateMethod.Invoke(_onScreen, new object[] { localPlayer.photonView.ViewID, onScreenLocal, culledLocal });
            LastChanceMonstersOnScreenCameraModule.DebugLogOnBoolTransition(
                "Sync.PlayerUpdate",
                $"{_onScreen.GetInstanceID()}.{localPlayer.photonView.ViewID}.OnScreen",
                onScreenLocal,
                $"enemy={_onScreen.gameObject.name} player={localPlayer.photonView.ViewID} onScreenLocal={onScreenLocal} culledLocal={culledLocal}");
            LastChanceMonstersOnScreenCameraModule.DebugLogOnBoolTransition(
                "Sync.PlayerUpdate",
                $"{_onScreen.GetInstanceID()}.{localPlayer.photonView.ViewID}.Culled",
                culledLocal,
                $"enemy={_onScreen.gameObject.name} player={localPlayer.photonView.ViewID} onScreenLocal={onScreenLocal} culledLocal={culledLocal}");
        }

        private static PlayerAvatar? GetLocalPlayerAvatar()
        {
            var director = GameDirector.instance;
            if (director == null || director.PlayerList == null)
            {
                return null;
            }

            foreach (var player in director.PlayerList)
            {
                if (player != null && player.photonView != null && player.photonView.IsMine)
                {
                    return player;
                }
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(EnemyOnScreen), "GetOnScreen")]
    internal static class LastChanceMonstersOnScreenSafeLookupPatch
    {
        private static readonly FieldInfo? s_onScreenLocalField = AccessTools.Field(typeof(EnemyOnScreen), "OnScreenLocal");
        private static readonly FieldInfo? s_onScreenPlayerField = AccessTools.Field(typeof(EnemyOnScreen), "OnScreenPlayer");

        [HarmonyPrefix]
        private static bool Prefix(EnemyOnScreen __instance, PlayerAvatar _playerAvatar, ref bool __result)
        {
            if (__instance == null || _playerAvatar == null)
            {
                __result = false;
                return false;
            }

            if (!GameManager.Multiplayer())
            {
                __result = s_onScreenLocalField?.GetValue(__instance) as bool? ?? false;
                LastChanceMonstersOnScreenCameraModule.DebugLog(
                    "GetOnScreen.Singleplayer",
                    $"enemy={__instance.gameObject.name} player={( _playerAvatar != null && _playerAvatar.photonView != null ? _playerAvatar.photonView.ViewID.ToString() : "n/a")} result={__result}");
                return false;
            }

            if (LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() &&
                LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(_playerAvatar) &&
                _playerAvatar.photonView != null &&
                _playerAvatar.photonView.IsMine)
            {
                __result = s_onScreenLocalField?.GetValue(__instance) as bool? ?? false;
                LastChanceMonstersOnScreenCameraModule.DebugLog(
                    "GetOnScreen.HeadProxyLocal",
                    $"enemy={__instance.gameObject.name} player={_playerAvatar.photonView.ViewID} result={__result}");
                return false;
            }

            if (s_onScreenPlayerField?.GetValue(__instance) is not System.Collections.IDictionary dictionary)
            {
                __result = false;
                return false;
            }

            var key = _playerAvatar.photonView != null ? _playerAvatar.photonView.ViewID : -1;
            if (key < 0)
            {
                __result = false;
                return false;
            }

            if (!dictionary.Contains(key))
            {
                dictionary[key] = false;
                __result = false;
                LastChanceMonstersOnScreenCameraModule.DebugLog(
                    "GetOnScreen.DictMiss",
                    $"enemy={__instance.gameObject.name} player={key} result={__result}");
                return false;
            }

            __result = dictionary[key] as bool? ?? false;
            LastChanceMonstersOnScreenCameraModule.DebugLogOnBoolTransition(
                "GetOnScreen.DictHit",
                $"{__instance.GetInstanceID()}.{key}",
                __result,
                $"enemy={__instance.gameObject.name} player={key} result={__result}");
            return false;
        }
    }
}

