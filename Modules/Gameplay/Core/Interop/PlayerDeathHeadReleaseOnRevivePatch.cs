#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    [HarmonyPatch]
    internal static class PlayerDeathHeadReleaseOnRevivePatch
    {
        private const int ReleaseObjectViewId = -1;

        private static readonly FieldInfo? PlayerDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo? PlayerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo? PlayerDeathHeadPhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
        private static readonly Dictionary<int, bool> LastDeadStateByPlayer = new();

        [HarmonyPatch(typeof(PlayerAvatar), "Update")]
        [HarmonyPostfix]
        private static void PlayerAvatar_Update_Postfix(PlayerAvatar __instance)
        {
            if (__instance == null || !FeatureFlags.LastChangeMode)
            {
                return;
            }

            var key = __instance.GetInstanceID();
            var isDead = IsPlayerDead(__instance);
            if (LastDeadStateByPlayer.TryGetValue(key, out var wasDead) && wasDead && !isDead)
            {
                TryReleaseDeathHeadGrabbers(__instance);
            }

            LastDeadStateByPlayer[key] = isDead;
        }

        [HarmonyPatch(typeof(PlayerAvatar), "OnDestroy")]
        [HarmonyPostfix]
        private static void PlayerAvatar_OnDestroy_Postfix(PlayerAvatar __instance)
        {
            if (__instance == null)
            {
                return;
            }

            LastDeadStateByPlayer.Remove(__instance.GetInstanceID());
        }

        private static bool IsPlayerDead(PlayerAvatar player)
        {
            var deadSet = PlayerDeadSetField?.GetValue(player) as bool? ?? false;
            var disabled = PlayerIsDisabledField?.GetValue(player) as bool? ?? false;
            return deadSet || disabled;
        }

        private static void TryReleaseDeathHeadGrabbers(PlayerAvatar player)
        {
            var deathHead = player.playerDeathHead;
            if (deathHead == null)
            {
                return;
            }

            var physGrabObject = PlayerDeathHeadPhysGrabObjectField?.GetValue(deathHead) as PhysGrabObject;
            if (physGrabObject == null || physGrabObject.playerGrabbing == null || physGrabObject.playerGrabbing.Count == 0)
            {
                return;
            }

            foreach (var grabber in physGrabObject.playerGrabbing.ToList())
            {
                if (grabber == null)
                {
                    continue;
                }

                if (!SemiFunc.IsMultiplayer())
                {
                    grabber.ReleaseObjectRPC(true, 2f, ReleaseObjectViewId);
                }
                else
                {
                    grabber.photonView.RPC("ReleaseObjectRPC", RpcTarget.All, new object[]
                    {
                        false,
                        1f,
                        ReleaseObjectViewId
                    });
                }
            }
        }
    }
}
