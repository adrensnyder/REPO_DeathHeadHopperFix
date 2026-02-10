#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
        private static readonly FieldInfo? HeadSpectatedField =
            AccessTools.Field(typeof(PlayerDeathHead), "spectated");
        private static readonly Type? DhhControllerType =
            AccessTools.TypeByName("DeathHeadHopper.DeathHead.DeathHeadController");
        private static readonly FieldInfo? DhhControllerSpectatedField =
            DhhControllerType == null ? null : AccessTools.Field(DhhControllerType, "spectated");

        internal static void ResetRuntimeState()
        {
            var voiceChats = UnityEngine.Object.FindObjectsOfType<PlayerVoiceChat>();
            for (var i = 0; i < voiceChats.Length; i++)
            {
                var voiceChat = voiceChats[i];
                if (voiceChat == null)
                {
                    continue;
                }

                var photonView = VoicePhotonViewField?.GetValue(voiceChat) as Photon.Pun.PhotonView;
                var viewId = photonView?.ViewID ?? -1;
                RestoreVolumes(voiceChat, viewId);
            }

            OriginalAudioSourceVolumeByViewId.Clear();
            OriginalTtsVolumeByViewId.Clear();
        }

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

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (HeadSpectatedField == null)
            {
                return instructions;
            }

            var replacement = AccessTools.Method(typeof(LastChanceMonstersVoiceEnemyOnlyModule), nameof(GetEffectiveHeadSpectated));
            if (replacement == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode == OpCodes.Ldfld && ins.operand is FieldInfo f && f == HeadSpectatedField)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = replacement;
                }
            }

            return list;
        }

        private static bool ShouldApply(PlayerAvatar player)
        {
            return FeatureFlags.LastChanceMonstersVoiceEnemyOnlyEnabled &&
                   LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() &&
                   LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
        }

        private static bool GetEffectiveHeadSpectated(PlayerDeathHead? head)
        {
            if (head == null)
            {
                return false;
            }

            // Vanilla State.Head path.
            if (HeadSpectatedField?.GetValue(head) is bool vanillaSpectated && vanillaSpectated)
            {
                return true;
            }

            // Outside LastChance, keep vanilla behavior unchanged.
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !FeatureFlags.LastChanceMonstersVoiceEnemyOnlyEnabled)
            {
                return false;
            }

            // DHH path: SpectateCamera Head is blocked, but DHH controller can still be spectated.
            if (DhhControllerType != null && DhhControllerSpectatedField != null)
            {
                var controller = head.GetComponent(DhhControllerType);
                if (controller != null && DhhControllerSpectatedField.GetValue(controller) is bool dhhSpectated && dhhSpectated)
                {
                    return true;
                }
            }

            return false;
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
