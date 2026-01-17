#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay
{
    internal static class AudioModule
    {
        private static readonly HashSet<int> AudioInitDone = new();
        private static ManualLogSource? _log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            _log = log;
            if (harmony == null || asm == null)
                return;

            var tAudioHandler = asm.GetType("DeathHeadHopper.DeathHead.Handlers.AudioHandler", throwOnError: false);
            if (tAudioHandler == null)
                return;

            var mAudioAwake = AccessTools.Method(tAudioHandler, "Awake", Type.EmptyTypes);
            if (mAudioAwake == null)
                return;

            var miPrefix = typeof(AudioModule).GetMethod(nameof(AudioHandler_Awake_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (miPrefix == null)
                return;

            harmony.Patch(mAudioAwake, prefix: new HarmonyMethod(miPrefix));
        }

        private static bool AudioHandler_Awake_Prefix(MonoBehaviour __instance)
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

        private static IEnumerator AudioHandler_InitWhenReady(MonoBehaviour handler)
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

        private static bool TryInitAudioHandlerSafe(MonoBehaviour handler)
        {
            var id = handler.GetInstanceID();
            if (AudioInitDone.Contains(id))
                return true;

            var t = handler.GetType();

            var fController = AccessTools.Field(t, "controller");
            var controller = fController?.GetValue(handler);
            if (controller == null)
            {
                var tController = t.Assembly.GetType("DeathHeadHopper.DeathHead.DeathHeadController", throwOnError: false);
                controller = tController != null ? handler.GetComponent(tController) : null;
                if (controller == null)
                    return false;

                fController?.SetValue(handler, controller);
            }

            var deathHead = controller != null ? AccessTools.Field(controller.GetType(), "deathHead")?.GetValue(controller) : null;
            var playerAvatar = deathHead != null
                ? AccessTools.Field(deathHead.GetType(), "playerAvatar")?.GetValue(deathHead)
                : null;

            var audioPreset = TryGetAudioPreset(handler);
            if (audioPreset == null && playerAvatar == null && deathHead == null)
                return false;

            if (!TryEnsureSound(handler, t, audioPreset, deathHead, playerAvatar))
                return false;

            AudioInitDone.Add(id);
            _log?.LogInfo("[Fix] AudioHandler initialized safely (deferred).");
            return true;
        }

        private static object? TryGetAudioPreset(MonoBehaviour handler)
        {
            try
            {
                var tNvo = AccessTools.TypeByName("NotValuableObject");
                var nvo = tNvo != null ? handler.gameObject.GetComponent(tNvo) : null;
                return nvo != null ? AccessTools.Field(nvo.GetType(), "audioPreset")?.GetValue(nvo) : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryEnsureSound(MonoBehaviour handler, Type t, object? audioPreset, object? deathHead, object? playerAvatar)
        {
            AudioSource CreateSrc()
            {
                var mCreateAudioSource = AccessTools.Method(t, "CreateAudioSource");
                if (mCreateAudioSource == null)
                    throw new NullReferenceException("CreateAudioSource() not found.");

                var srcObj = mCreateAudioSource.Invoke(handler, Array.Empty<object?>());
                if (srcObj is not AudioSource src)
                    throw new NullReferenceException("CreateAudioSource() returned invalid type.");
                return src;
            }

            static AudioClip[]? CloneClipsFromSoundLike(object? soundLike)
            {
                if (soundLike == null)
                    return null;

                var clipsObj = AccessTools.Field(soundLike.GetType(), "Sounds")?.GetValue(soundLike);
                if (clipsObj is not AudioClip[] clips || clips.Length == 0)
                    return null;

                return clips.Clone() as AudioClip[];
            }

            static object? CreateSound(Type tSound, AudioClip[]? clips, AudioSource? src, float vol, float volRand, float pitch, float pitchRand)
            {
                if (clips == null || clips.Length == 0)
                    return null;

                var snd = Activator.CreateInstance(tSound);
                if (snd == null)
                    return null;

                if (src != null)
                    AccessTools.Field(tSound, "Source")?.SetValue(snd, src);

                AccessTools.Field(tSound, "Sounds")?.SetValue(snd, clips);
                AccessTools.Field(tSound, "Volume")?.SetValue(snd, vol);
                AccessTools.Field(tSound, "VolumeRandom")?.SetValue(snd, volRand);
                AccessTools.Field(tSound, "Pitch")?.SetValue(snd, pitch);
                AccessTools.Field(tSound, "PitchRandom")?.SetValue(snd, pitchRand);
                return snd;
            }

            var tSound = AccessTools.TypeByName("Sound");
            if (tSound == null)
                return false;

            try
            {
                if (AccessTools.Field(t, "jumpSound")?.GetValue(handler) == null && audioPreset != null)
                {
                    var impactMedium = AccessTools.Field(audioPreset.GetType(), "impactMedium")?.GetValue(audioPreset);
                    var clips = CloneClipsFromSoundLike(impactMedium);
                    var snd = CreateSound(tSound, clips, src: null, vol: 0.12f, volRand: 0f, pitch: 0.8f, pitchRand: 0f);
                    if (snd != null)
                        AccessTools.Field(t, "jumpSound")?.SetValue(handler, snd);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (AccessTools.Field(t, "anchorBreakSound")?.GetValue(handler) == null && playerAvatar != null)
                {
                    var tumbleBreakFree = AccessTools.Field(playerAvatar.GetType(), "tumbleBreakFreeSound")?.GetValue(playerAvatar);
                    var clips = CloneClipsFromSoundLike(tumbleBreakFree);
                    var snd = CreateSound(tSound, clips, CreateSrc(), vol: 0.1f, volRand: 0f, pitch: 1f, pitchRand: 0f);
                    if (snd != null)
                        AccessTools.Field(t, "anchorBreakSound")?.SetValue(handler, snd);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (AccessTools.Field(t, "anchorAttachSound")?.GetValue(handler) == null && deathHead != null)
                {
                    var eyeFlashNeg = AccessTools.Field(deathHead.GetType(), "eyeFlashNegativeSound")?.GetValue(deathHead);
                    var clips = CloneClipsFromSoundLike(eyeFlashNeg);
                    var snd = CreateSound(tSound, clips, CreateSrc(), vol: 0.5f, volRand: 0f, pitch: 0.3f, pitchRand: 0.03f);
                    if (snd != null)
                        AccessTools.Field(t, "anchorAttachSound")?.SetValue(handler, snd);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (AccessTools.Field(t, "windupSound")?.GetValue(handler) == null)
                {
                    var tPlayerAvatar = AccessTools.TypeByName("PlayerAvatar");
                    object? player = null;
                    if (tPlayerAvatar != null)
                    {
                        player = ReflectionHelper.GetStaticInstanceValue(tPlayerAvatar, "instance");
                    }

                    var tumble = player != null ? AccessTools.Field(player.GetType(), "tumble")?.GetValue(player) : null;
                    var tumbleMove = tumble != null ? AccessTools.Field(tumble.GetType(), "tumbleMoveSound")?.GetValue(tumble) : null;

                    var clips = CloneClipsFromSoundLike(tumbleMove);
                    var snd = CreateSound(tSound, clips, CreateSrc(), vol: 0.4f, volRand: 0.02f, pitch: 0.8f, pitchRand: 0f);
                    if (snd != null)
                        AccessTools.Field(t, "windupSound")?.SetValue(handler, snd);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (AccessTools.Field(t, "rechargeSound")?.GetValue(handler) == null)
                {
                    var tAssetManager = AccessTools.TypeByName("AssetManager");
                    object? assetMgr = null;
                    if (tAssetManager != null)
                    {
                        assetMgr = ReflectionHelper.GetStaticInstanceValue(tAssetManager, "instance");
                    }

                    var batteryCharge = assetMgr != null ? AccessTools.Field(assetMgr.GetType(), "batteryChargeSound")?.GetValue(assetMgr) : null;
                    var clips = CloneClipsFromSoundLike(batteryCharge);
                    var snd = CreateSound(tSound, clips, CreateSrc(), vol: 0.2f, volRand: 0.01f, pitch: 1f, pitchRand: 0.02f);
                    if (snd != null)
                        AccessTools.Field(t, "rechargeSound")?.SetValue(handler, snd);
                }
            }
            catch { /* ignore */ }

            try
            {
                if (AccessTools.Field(t, "unAnchoringSound")?.GetValue(handler) == null)
                {
                    var tMaterialPreset = AccessTools.TypeByName("MaterialPreset");
                    if (tMaterialPreset != null)
                    {
                        var all = Resources.FindObjectsOfTypeAll(tMaterialPreset);
                        object? presetType2 = null;

                        var fType = AccessTools.Field(tMaterialPreset, "Type");
                        foreach (var obj in all)
                        {
                            if (obj == null)
                                continue;

                            var value = fType?.GetValue(obj);
                            if (value is int i && i == 2)
                            {
                                presetType2 = obj;
                                break;
                            }
                        }

                        if (presetType2 != null)
                        {
                            var slide = AccessTools.Field(tMaterialPreset, "SlideOneShot")?.GetValue(presetType2);
                            var clips = CloneClipsFromSoundLike(slide);
                            var snd = CreateSound(tSound, clips, CreateSrc(), vol: 1f, volRand: 0f, pitch: 0.6f, pitchRand: 0f);
                            if (snd != null)
                                AccessTools.Field(t, "unAnchoringSound")?.SetValue(handler, snd);
                        }
                    }
                }
            }
            catch { /* ignore */ }

            return true;
        }
    }
}
