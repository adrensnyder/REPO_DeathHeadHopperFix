#nullable enable

using System;
using System.Collections;
using System.Reflection;
using DeathHeadHopper.Managers;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Gameplay.Core.Interop;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Input
{
    internal static class InputModule
    {
        private static ManualLogSource? _log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            PatchDhhInputManagerAwakeIfPossible(harmony, asm);
            DHHPunViewFixModule.Apply(harmony, asm, log);
        }

        private static void PatchDhhInputManagerAwakeIfPossible(Harmony harmony, Assembly asm)
        {
            if (harmony == null || asm == null)
                return;

            var mAwake = AccessTools.Method(typeof(DHHInputManager), nameof(DHHInputManager.Awake));
            if (mAwake == null)
                return;

            var prefix = typeof(InputModule).GetMethod(nameof(DHHInputManager_Awake_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mAwake, prefix: new HarmonyMethod(prefix));
        }


        private static void DHHInputManager_Awake_Prefix(MonoBehaviour __instance)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(DHHPunManager.hostVersion))
                    return;

                if (SemiFunc.IsMasterClientOrSingleplayer())
                {
                    var local = DHHPunManager.localVersion;
                    if (string.IsNullOrWhiteSpace(local))
                        local = GetDeathHeadHopperVersionString();
                    if (!string.IsNullOrWhiteSpace(local))
                        DHHPunManager.hostVersion = local!;
                    return;
                }

                DHHPunManager.hostVersion = "pending";

                __instance.StartCoroutine(DHHInputManager_InvokeVersionCheckWhenReady(DHHPunManager.instance));

                __instance.StartCoroutine(DHHInputManager_WaitForHostVersion());
            }
            catch
            {
                // Non-critical hostVersion bootstrap; keep original flow if reflection fails.
            }
        }

        private static IEnumerator DHHInputManager_WaitForHostVersion()
        {
            const int maxFrames = 300;

            for (int i = 0; i < maxFrames; i++)
            {
                var host = DHHPunManager.hostVersion;
                if (!string.IsNullOrWhiteSpace(host) && !string.Equals(host, "pending", StringComparison.OrdinalIgnoreCase))
                    yield break;

                yield return null;
            }

            var currentHost = DHHPunManager.hostVersion;
            if (string.Equals(currentHost, "pending", StringComparison.OrdinalIgnoreCase))
            {
                DHHPunManager.hostVersion = string.Empty;
                _log?.LogWarning("Host does not have DeathHeadHopper installed!");
            }
        }

        private static IEnumerator DHHInputManager_InvokeVersionCheckWhenReady(DHHPunManager? punManager)
        {
            if (punManager == null)
                yield break;

            const int maxFrames = 300;
            for (int i = 0; i < maxFrames; i++)
            {
                if (punManager.photonView != null && punManager.photonView.ViewID > 0)
                    break;

                yield return null;
            }

            if (punManager.photonView == null || punManager.photonView.ViewID <= 0)
                yield break;

            punManager.VersionCheck();
        }

        private static string? GetDeathHeadHopperVersionString()
        {
            return DeathHeadHopper.DeathHeadHopper.Version.ToString();
        }

    }
}

