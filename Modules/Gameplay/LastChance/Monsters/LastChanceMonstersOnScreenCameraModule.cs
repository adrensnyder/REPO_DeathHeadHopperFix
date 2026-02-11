#nullable enable

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters
{
    [HarmonyPatch(typeof(EnemyOnScreen), "Awake")]
    internal static class LastChanceMonstersOnScreenCameraModule
    {
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
            }
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
                return false;
            }

            if (LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() &&
                LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(_playerAvatar) &&
                _playerAvatar.photonView != null &&
                _playerAvatar.photonView.IsMine)
            {
                __result = s_onScreenLocalField?.GetValue(__instance) as bool? ?? false;
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
                return false;
            }

            __result = dictionary[key] as bool? ?? false;
            return false;
        }
    }
}
