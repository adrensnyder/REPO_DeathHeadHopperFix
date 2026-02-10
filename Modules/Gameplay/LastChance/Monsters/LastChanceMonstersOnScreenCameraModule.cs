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
        private EnemyOnScreen? _onScreen;

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
