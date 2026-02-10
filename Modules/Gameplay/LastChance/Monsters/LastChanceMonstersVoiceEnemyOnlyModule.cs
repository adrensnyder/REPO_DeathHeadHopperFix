#nullable enable

using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters
{
    [HarmonyPatch(typeof(PlayerVoiceChat), "Update")]
    internal static class LastChanceMonstersVoiceEnemyOnlyModule
    {
        private static readonly Dictionary<int, float> OriginalAudioSourceVolumeByViewId = new();
        private static readonly Dictionary<int, float> OriginalTtsVolumeByViewId = new();
        private static readonly FieldInfo? VoicePlayerAvatarField =
            AccessTools.Field(typeof(PlayerVoiceChat), "playerAvatar");
        private static readonly FieldInfo? VoicePhotonViewField =
            AccessTools.Field(typeof(PlayerVoiceChat), "photonView");
        private static readonly FieldInfo? VoiceAudioSourceField =
            AccessTools.Field(typeof(PlayerVoiceChat), "audioSource");
        private static readonly FieldInfo? VoiceTtsAudioSourceField =
            AccessTools.Field(typeof(PlayerVoiceChat), "ttsAudioSource");
        private static readonly FieldInfo? VoiceOverrideNoTalkAnimationTimerField =
            AccessTools.Field(typeof(PlayerVoiceChat), "overrideNoTalkAnimationTimer");

        [HarmonyPostfix]
        private static void Postfix(PlayerVoiceChat __instance)
        {
            if (__instance == null)
            {
                return;
            }

            var playerAvatar = VoicePlayerAvatarField?.GetValue(__instance) as PlayerAvatar;
            var photonView = VoicePhotonViewField?.GetValue(__instance) as Photon.Pun.PhotonView;
            if (playerAvatar == null || photonView == null)
            {
                return;
            }

            var viewId = photonView.ViewID;
            if (!ShouldApply(playerAvatar))
            {
                RestoreVolumes(__instance, viewId);
                return;
            }

            // Keep vanilla PlayerVoiceChat pipeline active (incl. investigate logic), but force no audible playback to players.
            ApplyEnemyOnlyVoiceMix(__instance, viewId);
            ForceTalkAnimationEnabled(__instance);
        }

        private static bool ShouldApply(PlayerAvatar player)
        {
            return FeatureFlags.LastChanceMonstersVoiceEnemyOnlyEnabled &&
                   LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() &&
                   LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
        }

        private static void ApplyEnemyOnlyVoiceMix(PlayerVoiceChat voiceChat, int viewId)
        {
            var audioSource = VoiceAudioSourceField?.GetValue(voiceChat) as AudioSource;
            if (audioSource != null)
            {
                if (!OriginalAudioSourceVolumeByViewId.ContainsKey(viewId))
                {
                    OriginalAudioSourceVolumeByViewId[viewId] = audioSource.volume;
                }

                audioSource.volume = 0f;
            }

            var ttsAudioSource = VoiceTtsAudioSourceField?.GetValue(voiceChat) as AudioSource;
            if (ttsAudioSource != null)
            {
                if (!OriginalTtsVolumeByViewId.ContainsKey(viewId))
                {
                    OriginalTtsVolumeByViewId[viewId] = ttsAudioSource.volume;
                }

                ttsAudioSource.volume = 0f;
            }
        }

        private static void ForceTalkAnimationEnabled(PlayerVoiceChat voiceChat)
        {
            if (VoiceOverrideNoTalkAnimationTimerField == null)
            {
                return;
            }

            VoiceOverrideNoTalkAnimationTimerField.SetValue(voiceChat, 0f);
        }

        private static void RestoreVolumes(PlayerVoiceChat voiceChat, int viewId)
        {
            var audioSource = VoiceAudioSourceField?.GetValue(voiceChat) as AudioSource;
            if (audioSource != null && OriginalAudioSourceVolumeByViewId.TryGetValue(viewId, out var originalVolume))
            {
                audioSource.volume = originalVolume;
                OriginalAudioSourceVolumeByViewId.Remove(viewId);
            }

            var ttsAudioSource = VoiceTtsAudioSourceField?.GetValue(voiceChat) as AudioSource;
            if (ttsAudioSource != null && OriginalTtsVolumeByViewId.TryGetValue(viewId, out var originalTtsVolume))
            {
                ttsAudioSource.volume = originalTtsVolume;
                OriginalTtsVolumeByViewId.Remove(viewId);
            }
        }
    }
}
