#nullable enable

using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.UI
{
    [HarmonyPatch(typeof(PlayerEyes), nameof(PlayerEyes.Override))]
    internal static class LastChanceEyesOverrideBypassUIModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Eyes");
        private static readonly FieldInfo? EyesPlayerAvatarField = AccessTools.Field(typeof(PlayerEyes), "playerAvatar");
        private static readonly FieldInfo? HeadSpectatedField = AccessTools.Field(typeof(PlayerDeathHead), "spectated");

        internal static void ResetRuntimeState()
        {
        }

        [HarmonyPrefix]
        private static bool Prefix(PlayerEyes __instance, GameObject _obj)
        {
            if (__instance == null)
            {
                return true;
            }

            var player = EyesPlayerAvatarField?.GetValue(__instance) as PlayerAvatar;
            var head = player?.playerDeathHead;
            if (!LastChancePupilGateHelper.IsEligibleHead(head, player))
            {
                return true;
            }
            if (head == null)
            {
                return true;
            }

            if (HeadSpectatedField?.GetValue(head) as bool? ?? false)
            {
                return true;
            }

            if (_obj == head.gameObject)
            {
                return false;
            }

            DebugLog("Override.Pass.OtherObj", $"playerId={GetPlayerId(player)} obj={(_obj != null ? _obj.name : "null")}");
            return true;
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!FeatureFlags.DebugLogging || !InternalDebugFlags.DebugLastChanceEyesFlow || !LogLimiter.ShouldLog($"LastChance.Eyes.Override.{reason}", 90))
            {
                return;
            }

            Log.LogInfo($"[LastChance][EyesOverride][{reason}] {detail}");
        }

        private static int GetPlayerId(PlayerAvatar? player)
        {
            if (player == null)
            {
                return -1;
            }

            return player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
        }
    }
}
