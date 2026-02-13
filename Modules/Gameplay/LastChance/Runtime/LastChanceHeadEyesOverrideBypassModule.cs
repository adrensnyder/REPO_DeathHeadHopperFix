#nullable enable

using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Runtime
{
    [HarmonyPatch(typeof(PlayerEyes), nameof(PlayerEyes.Override))]
    internal static class LastChanceHeadEyesOverrideBypassModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Eyes");
        private static readonly FieldInfo? EyesPlayerAvatarField = AccessTools.Field(typeof(PlayerEyes), "playerAvatar");
        private static readonly FieldInfo? HeadTriggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");
        private static readonly FieldInfo? HeadSpectatedField = AccessTools.Field(typeof(PlayerDeathHead), "spectated");

        internal static void ResetRuntimeState()
        {
            // No persisted runtime state in this module.
        }

        [HarmonyPrefix]
        private static bool Prefix(PlayerEyes __instance, GameObject _obj)
        {
            if (!FeatureFlags.LastChancePupilVisualsEnabled)
            {
                DebugLog("Override.Skip.FlagDisabled", "LastChancePupilVisualsEnabled=false");
                return true;
            }

            if (__instance == null || !LastChanceTimerController.IsActive)
            {
                DebugLog("Override.Skip.Inactive", $"lastChance={LastChanceTimerController.IsActive} eyesNull={__instance == null}");
                return true;
            }

            var player = EyesPlayerAvatarField?.GetValue(__instance) as PlayerAvatar;
            if (!LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                DebugLog("Override.Skip.NoHeadProxy", $"playerId={GetPlayerId(player)}");
                return true;
            }

            var head = player?.playerDeathHead;
            if (head == null)
            {
                DebugLog("Override.Skip.NoHead", $"playerId={GetPlayerId(player)}");
                return true;
            }

            if (!(HeadTriggeredField?.GetValue(head) as bool? ?? false))
            {
                DebugLog("Override.Skip.NotTriggered", $"playerId={GetPlayerId(player)}");
                return true;
            }

            // Keep vanilla behavior while spectated; bypass only the non-spectated forced head override.
            if (HeadSpectatedField?.GetValue(head) as bool? ?? false)
            {
                DebugLog("Override.Pass.Spectated", $"playerId={GetPlayerId(player)}");
                return true;
            }

            if (_obj == head.gameObject)
            {
                DebugLog("Override.Block.HeadSelf", $"playerId={GetPlayerId(player)}");
                return false;
            }

            DebugLog("Override.Pass.OtherObj", $"playerId={GetPlayerId(player)} obj={( _obj != null ? _obj.name : "null")}");
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
