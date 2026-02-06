#nullable enable

using System;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class PlayerDeathHeadUpdateGuardPatch
    {
        private const string LogKey = "LastChance.PlayerDeathHead.UpdateGuard";
        private static readonly FieldInfo? s_setupField = AccessTools.Field(typeof(PlayerDeathHead), "setup");
        private static readonly FieldInfo? s_roomVolumeCheckField = AccessTools.Field(typeof(PlayerDeathHead), "roomVolumeCheck");
        private static readonly FieldInfo? s_smokeParticlesField = AccessTools.Field(typeof(PlayerDeathHead), "smokeParticles");

        [HarmonyPrefix]
        private static bool Prefix(PlayerDeathHead __instance, PhysGrabObject ___physGrabObject)
        {
            if (!FeatureFlags.LastChangeMode)
            {
                return true;
            }

            if (!SemiFunc.IsMasterClientOrSingleplayer())
            {
                return true;
            }

            if (__instance == null)
            {
                return false;
            }

            if (___physGrabObject == null)
            {
                return false;
            }

            var avatar = __instance.playerAvatar;
            if (avatar == null)
            {
                return false;
            }

            if (avatar.localCamera == null)
            {
                return false;
            }

            if (s_setupField != null && s_setupField.GetValue(__instance) is bool setup && !setup)
            {
                return false;
            }

            if (s_roomVolumeCheckField != null && s_roomVolumeCheckField.GetValue(__instance) == null)
            {
                return false;
            }

            if (s_smokeParticlesField != null && s_smokeParticlesField.GetValue(__instance) == null)
            {
                return false;
            }

            return true;
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            if (!FeatureFlags.LastChangeMode)
            {
                return __exception;
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(LogKey, 900))
            {
                UnityEngine.Debug.LogWarning("[LastChance] Suppressed PlayerDeathHead.Update exception during LastChance.");
            }

            return null;
        }
    }
}
