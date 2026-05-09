#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using DeathHeadHopper.Abilities.Charge;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Audio
{
    internal static class AudioModule
    {
        private static readonly HashSet<int> AudioInitDone = new();
        private static ManualLogSource? _log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            if (harmony == null)
                return;

            var mAudioAwake = AccessTools.Method(typeof(AudioHandler), nameof(AudioHandler.Awake), Type.EmptyTypes);
            if (mAudioAwake == null)
                return;

            var miPrefix = typeof(AudioModule).GetMethod(nameof(AudioHandler_Awake_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (miPrefix == null)
                return;

            harmony.Patch(mAudioAwake, prefix: new HarmonyMethod(miPrefix));

            var mChargeEffectsUpdate = AccessTools.Method(typeof(ChargeEffects), nameof(ChargeEffects.Update), Type.EmptyTypes);
            var chargeEffectsUpdateTranspiler = typeof(AudioModule).GetMethod(nameof(ChargeEffects_Update_Transpiler), BindingFlags.Static | BindingFlags.NonPublic);
            if (mChargeEffectsUpdate != null && chargeEffectsUpdateTranspiler != null)
            {
                harmony.Patch(mChargeEffectsUpdate, transpiler: new HarmonyMethod(chargeEffectsUpdateTranspiler));
            }
        }

        private static bool AudioHandler_Awake_Prefix(AudioHandler __instance)
        {
            try
            {
                var id = __instance.GetInstanceID();
                if (AudioInitDone.Contains(id))
                    return false;

                __instance.StartCoroutine(AudioHandler_InitWhenReady(__instance));
            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }

            return false;
        }

        private static IEnumerator AudioHandler_InitWhenReady(AudioHandler handler)
        {
            const int maxFrames = 600;
            int frames = 0;

            while (frames++ < maxFrames)
            {
                if (handler == null)
                    yield break;

                if (TryInitAudioHandlerSafe(handler))
                    yield break;

                yield return null;
            }

            _log?.LogWarning("[Fix] AudioHandler init timed out; audio may be partially disabled.");
        }

        private static bool TryInitAudioHandlerSafe(AudioHandler handler)
        {
            var id = handler.GetInstanceID();
            if (AudioInitDone.Contains(id))
                return true;

            handler.controller ??= handler.GetComponent<DeathHeadHopper.DeathHead.DeathHeadController>();
            var controller = handler.controller;
            if (controller == null)
                return false;

            var deathHead = controller.deathHead;
            var playerAvatar = deathHead != null ? deathHead.playerAvatar : null;
            var audioPreset = handler.GetComponent<NotValuableObject>()?.audioPreset;
            if (audioPreset == null && playerAvatar == null && deathHead == null)
                return false;

            if (!TryEnsureSound(handler, audioPreset, deathHead, playerAvatar))
                return false;

            AudioInitDone.Add(id);

            if (FeatureFlags.DebugLogging)
                _log?.LogInfo("[Fix] AudioHandler initialized safely (deferred).");
            return true;
        }

        private static bool TryEnsureSound(AudioHandler handler, PhysAudio? audioPreset, PlayerDeathHead? deathHead, PlayerAvatar? playerAvatar)
        {
            try
            {
                handler.jumpSound ??= CreateSound(CloneClips(audioPreset?.impactMedium), null, 0.12f, 0f, 0.8f, 0f);
            }
            catch { /* ignore */ }

            try
            {
                handler.anchorBreakSound ??= CreateSound(CloneClips(playerAvatar?.tumbleBreakFreeSound), handler.CreateAudioSource(), 0.1f, 0f, 1f, 0f);
            }
            catch { /* ignore */ }

            try
            {
                handler.anchorAttachSound ??= CreateSound(CloneClips(deathHead?.eyeFlashNegativeSound), handler.CreateAudioSource(), 0.5f, 0f, 0.3f, 0.03f);
            }
            catch { /* ignore */ }

            try
            {
                handler.windupSound ??= CreateSound(CloneClips(PlayerAvatar.instance?.tumble?.tumbleMoveSound), handler.CreateAudioSource(), 0.4f, 0.02f, 0.8f, 0f);
            }
            catch { /* ignore */ }

            try
            {
                handler.rechargeSound ??= CreateSound(CloneClips(AssetManager.instance?.batteryChargeSound), handler.CreateAudioSource(), 0.2f, 0.01f, 1f, 0.02f);
            }
            catch { /* ignore */ }

            try
            {
                var rug = Materials.Instance != null
                    ? Materials.Instance.MaterialList.FirstOrDefault(x => x.Type == Materials.Type.Rug)
                    : null;
                handler.unAnchoringSound ??= CreateSound(CloneClips(rug?.SlideOneShot), handler.CreateAudioSource(), 1f, 0f, 0.6f, 0f);
            }
            catch { /* ignore */ }

            return handler.jumpSound != null
                || handler.anchorBreakSound != null
                || handler.anchorAttachSound != null
                || handler.windupSound != null
                || handler.rechargeSound != null
                || handler.unAnchoringSound != null;
        }

        private static IEnumerable<CodeInstruction> ChargeEffects_Update_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(AudioHandler), nameof(AudioHandler.PlayWindupSoundLoop), new[] { typeof(float) });
            var replacement = AccessTools.Method(typeof(AudioModule), nameof(PlayWindupSoundLoopCompat), new[] { typeof(AudioHandler), typeof(float) });
            if (original == null || replacement == null)
            {
                foreach (var instruction in instructions)
                    yield return instruction;

                yield break;
            }

            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, original))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    continue;
                }

                yield return instruction;
            }
        }

        private static void PlayWindupSoundLoopCompat(AudioHandler? handler, float pitch)
        {
            try
            {
                var sound = handler != null ? handler.windupSound : null;
                if (sound == null)
                    return;

                sound.PlayLoop(true, 3f, 3f, pitch, 1f);
            }
            catch (Exception ex)
            {
                _log?.LogWarning($"[Fix] Failed to play DHH windup sound via compatibility prefix: {ex.Message}");
            }
        }

        private static AudioClip[]? CloneClips(Sound? sound)
        {
            return sound?.Sounds != null && sound.Sounds.Length > 0
                ? sound.Sounds.Clone() as AudioClip[]
                : null;
        }

        private static Sound? CreateSound(AudioClip[]? clips, AudioSource? src, float vol, float volRand, float pitch, float pitchRand)
        {
            if (clips == null || clips.Length == 0)
                return null;

            return new Sound
            {
                Source = src,
                Sounds = clips,
                Volume = vol,
                VolumeRandom = volRand,
                Pitch = pitch,
                PitchRandom = pitchRand
            };
        }
    }
}
