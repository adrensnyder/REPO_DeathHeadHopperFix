#nullable enable

using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class ScalerCoreInteropBridge
    {
        private const string ScalerCorePluginGuid = "Vippy.ScalerCore";
        private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string ScaleManagerTypeName = "ScalerCore.ScaleManager";

        private static Type? s_scaleManagerType;
        private static MethodInfo? s_isScaledMethod;
        private static MethodInfo? s_restoreImmediateMethod;
        private static bool s_lastChanceRestoreAttempted;

        internal static bool TryRestoreLocalPlayerCameraState()
        {
            if (!LastChanceInteropBridge.IsLastChanceModeEnabled() || !LastChanceInteropBridge.IsLastChanceActive())
            {
                s_lastChanceRestoreAttempted = false;
                return false;
            }

            if (s_lastChanceRestoreAttempted)
            {
                return false;
            }

            s_lastChanceRestoreAttempted = true;

            if (!IsScalerCoreAvailable())
            {
                return false;
            }

            var local = PlayerAvatar.instance;
            if (local == null || local.gameObject == null)
            {
                return false;
            }

            if (!IsScaled(local.gameObject))
            {
                return false;
            }

            try
            {
                s_restoreImmediateMethod?.Invoke(null, new object[] { local.gameObject });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsScalerCoreAvailable()
        {
            if (!Chainloader.PluginInfos.ContainsKey(ScalerCorePluginGuid))
            {
                return false;
            }

            ResolveMembers();
            return s_scaleManagerType != null && s_isScaledMethod != null && s_restoreImmediateMethod != null;
        }

        private static bool IsScaled(GameObject target)
        {
            if (s_isScaledMethod == null)
            {
                return false;
            }

            try
            {
                return s_isScaledMethod.Invoke(null, new object[] { target }) as bool? ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static void ResolveMembers()
        {
            s_scaleManagerType ??= AccessTools.TypeByName(ScaleManagerTypeName);
            if (s_scaleManagerType == null)
            {
                return;
            }

            s_isScaledMethod ??= s_scaleManagerType.GetMethod("IsScaled", StaticAny, null, new[] { typeof(GameObject) }, null);
            s_restoreImmediateMethod ??= s_scaleManagerType.GetMethod("RestoreImmediate", StaticAny, null, new[] { typeof(GameObject) }, null);
        }
    }
}
