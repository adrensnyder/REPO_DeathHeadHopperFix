#nullable enable

using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay
{
    internal static class InputModule
    {
        private static ManualLogSource? _log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            PatchDhhInputManagerAwakeIfPossible(harmony, asm);
            PatchDhhPunManagerAwakeIfPossible(harmony, asm);
        }

        private static void PatchDhhInputManagerAwakeIfPossible(Harmony harmony, Assembly asm)
        {
            if (harmony == null || asm == null)
                return;

            var tInput = asm.GetType("DeathHeadHopper.Managers.DHHInputManager", throwOnError: false);
            if (tInput == null)
                return;

            var mAwake = AccessTools.Method(tInput, "Awake");
            if (mAwake == null)
                return;

            var prefix = typeof(InputModule).GetMethod(nameof(DHHInputManager_Awake_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mAwake, prefix: new HarmonyMethod(prefix));
        }

        private static void PatchDhhPunManagerAwakeIfPossible(Harmony harmony, Assembly asm)
        {
            if (harmony == null || asm == null)
                return;

            var tPun = asm.GetType("DeathHeadHopper.Managers.DHHPunManager", throwOnError: false);
            if (tPun == null)
                return;

            var mAwake = AccessTools.Method(tPun, "Awake");
            if (mAwake == null)
                return;

            var postfix = typeof(InputModule).GetMethod(nameof(DHHPunManager_Awake_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(mAwake, postfix: new HarmonyMethod(postfix));
        }

        private static void DHHInputManager_Awake_Prefix(MonoBehaviour __instance)
        {
            try
            {
                var tPun = AccessTools.TypeByName("DeathHeadHopper.Managers.DHHPunManager");
                if (tPun == null)
                    return;

                var fHost = tPun.GetField("hostVersion", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var fLocal = tPun.GetField("localVersion", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (fHost == null)
                    return;

                var host = fHost.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(host))
                    return;

                if (IsMasterClientOrSingleplayer())
                {
                    var local = fLocal?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(local))
                        local = GetDeathHeadHopperVersionString();
                    if (!string.IsNullOrWhiteSpace(local))
                        fHost.SetValue(null, local);
                    return;
                }

                fHost.SetValue(null, "pending");

                var inst = ReflectionHelper.GetStaticInstanceByName("DeathHeadHopper.Managers.DHHPunManager");
                var mVersionCheck = inst?.GetType().GetMethod("VersionCheck", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                mVersionCheck?.Invoke(inst, Array.Empty<object?>());

                __instance.StartCoroutine(DHHInputManager_WaitForHostVersion());
            }
            catch
            {
                // ignore
            }
        }

        private static IEnumerator DHHInputManager_WaitForHostVersion()
        {
            const int maxFrames = 300;
            var tPun = AccessTools.TypeByName("DeathHeadHopper.Managers.DHHPunManager");
            var fHost = tPun?.GetField("hostVersion", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            for (int i = 0; i < maxFrames; i++)
            {
                var host = fHost?.GetValue(null) as string;
                if (!string.IsNullOrWhiteSpace(host) && !string.Equals(host, "pending", StringComparison.OrdinalIgnoreCase))
                    yield break;

                yield return null;
            }

            if (fHost != null)
            {
                var host = fHost.GetValue(null) as string;
                if (string.Equals(host, "pending", StringComparison.OrdinalIgnoreCase))
                {
                    fHost.SetValue(null, string.Empty);
                    _log?.LogWarning("Host does not have DeathHeadHopper installed!");
                }
            }
        }

        private static bool IsMasterClientOrSingleplayer()
        {
            try
            {
                var tSemiFunc = AccessTools.TypeByName("SemiFunc");
                var m = tSemiFunc?.GetMethod("IsMasterClientOrSingleplayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null && m.Invoke(null, Array.Empty<object?>()) is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        private static string? GetDeathHeadHopperVersionString()
        {
            try
            {
                var t = AccessTools.TypeByName("DeathHeadHopper.DeathHeadHopper");
                var p = t?.GetProperty("Version", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var v = p?.GetValue(null);
                return v?.ToString();
            }
            catch { }
            return null;
        }

        private static void DHHPunManager_Awake_Postfix(MonoBehaviour __instance)
        {
            try
            {
                UnityEngine.Object.DontDestroyOnLoad(__instance.gameObject);

                var t = __instance.GetType();
                var mEnsure = AccessTools.Method(t, "EnsurePhotonView");
                mEnsure?.Invoke(__instance, Array.Empty<object?>());

                var fPv = AccessTools.Field(t, "photonView");
                if (fPv?.GetValue(__instance) is PhotonView pv)
                {
                    if (pv.ViewID == 0)
                        pv.ViewID = 745;
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
