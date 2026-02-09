#nullable enable

using System;
using ExitGames.Client.Photon;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    internal static class DHHPunViewFixModule
    {
        private const string DhhPunViewIdRoomKey = "DHHFix.PunViewId";
        private static ManualLogSource? _log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            if (harmony == null || asm == null)
                return;

            var tPunManager = asm.GetType("DeathHeadHopper.Managers.DHHPunManager", throwOnError: false);
            if (tPunManager == null)
                return;

            var mAwake = AccessTools.Method(tPunManager, "Awake");
            var mEnsurePhotonView = AccessTools.Method(tPunManager, "EnsurePhotonView");
            var mVersionCheck = AccessTools.Method(tPunManager, "VersionCheck");

            var awakePostfix = typeof(DHHPunViewFixModule).GetMethod(nameof(DHHPunManager_Awake_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var ensurePrefix = typeof(DHHPunViewFixModule).GetMethod(nameof(DHHPunManager_EnsurePhotonView_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var versionPrefix = typeof(DHHPunViewFixModule).GetMethod(nameof(DHHPunManager_VersionCheck_Prefix), BindingFlags.Static | BindingFlags.NonPublic);

            if (mAwake != null && awakePostfix != null)
                harmony.Patch(mAwake, postfix: new HarmonyMethod(awakePostfix));

            if (mEnsurePhotonView != null && ensurePrefix != null)
                harmony.Patch(mEnsurePhotonView, prefix: new HarmonyMethod(ensurePrefix));

            if (mVersionCheck != null && versionPrefix != null)
                harmony.Patch(mVersionCheck, prefix: new HarmonyMethod(versionPrefix));
        }

        private static void DHHPunManager_Awake_Postfix(MonoBehaviour __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                BindDedicatedPhotonView(__instance);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"DHHPunViewFix Awake postfix failed: {ex.GetType().Name}");
            }
        }

        private static bool DHHPunManager_EnsurePhotonView_Prefix(MonoBehaviour __instance)
        {
            try
            {
                if (__instance == null)
                    return true;

                BindDedicatedPhotonView(__instance);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"DHHPunViewFix EnsurePhotonView prefix failed: {ex.GetType().Name}");
            }

            // Keep original flow: DHH EnsurePhotonView may perform additional setup needed by clients.
            return true;
        }

        private static bool DHHPunManager_VersionCheck_Prefix(MonoBehaviour __instance)
        {
            try
            {
                if (__instance == null)
                    return false;

                // Re-bind before VersionCheck to avoid sending RPC with illegal viewID 0.
                BindDedicatedPhotonView(__instance);

                var pv = __instance.GetComponent<PhotonView>();
                if (pv == null || pv.ViewID <= 0)
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHPunView.VersionCheck.NoView", 30))
                    {
                        _log?.LogWarning("[Fix:DHHPunView] Skipping VersionCheck RPC because PhotonView ID is not valid yet.");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"DHHPunViewFix VersionCheck prefix failed: {ex.GetType().Name}");
                return false;
            }

            return true;
        }

        private static void BindDedicatedPhotonView(MonoBehaviour sourceInstance)
        {
            const string logKey = "Fix:DHHPunView";
            var dhhType = sourceInstance.GetType();
            var instanceProp = dhhType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var photonViewField = dhhType.GetField("photonView", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (sourceInstance.gameObject == null)
                return;

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog($"{logKey}.Host", 30))
            {
                _log?.LogInfo($"[Fix:DHHPunView] Using dedicated host GO='{sourceInstance.gameObject.name}'.");
            }

            var boundInstance = sourceInstance;
            UnityEngine.Object.DontDestroyOnLoad(boundInstance.gameObject);

            var photonView = sourceInstance.gameObject.GetComponent<PhotonView>();
            if (photonView == null)
            {
                photonView = sourceInstance.gameObject.AddComponent<PhotonView>();
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog($"{logKey}.AddPhotonView", 30))
                {
                    _log?.LogInfo("[Fix:DHHPunView] Added PhotonView to DHHPunManager GO.");
                }
            }

            EnsureSynchronizedViewId(photonView);

            instanceProp?.SetValue(null, boundInstance, null);
            photonViewField?.SetValue(boundInstance, photonView);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog($"{logKey}.Bound", 15))
            {
                _log?.LogInfo($"[Fix:DHHPunView] Bound DHHPunManager to dedicated PhotonView {photonView.ViewID} on '{sourceInstance.gameObject.name}'.");
            }
        }

        private static void EnsureSynchronizedViewId(PhotonView photonView)
        {
            if (photonView == null)
                return;

            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return;

            try
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    if (photonView.ViewID <= 0 && !PhotonNetwork.AllocateViewID(photonView))
                        return;

                    var viewId = photonView.ViewID;
                    var roomProps = PhotonNetwork.CurrentRoom.CustomProperties;
                    if (roomProps == null || !roomProps.TryGetValue(DhhPunViewIdRoomKey, out var existing) || existing is not int existingId || existingId != viewId)
                    {
                        var set = new Hashtable
                        {
                            [DhhPunViewIdRoomKey] = viewId
                        };
                        PhotonNetwork.CurrentRoom.SetCustomProperties(set);
                    }
                    return;
                }

                var props = PhotonNetwork.CurrentRoom.CustomProperties;
                if (props == null || !props.TryGetValue(DhhPunViewIdRoomKey, out var syncedObj) || syncedObj is not int syncedId || syncedId <= 0)
                    return;

                if (photonView.ViewID != syncedId)
                {
                    photonView.ViewID = syncedId;
                }
            }
            catch (Exception ex)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHPunView.Sync.Error", 30))
                {
                    _log?.LogWarning($"DHHPunViewFix sync failed: {ex.GetType().Name}");
                }
            }
        }
    }
}
