#nullable enable

using System.Reflection;
using System.Collections.Generic;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Runtime;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Visuals
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class HeadPupilVisualModule
    {
        private sealed class HeadVisualState
        {
            internal HeadVisualState(
                PlayerDeathHead head,
                bool rightWasActive,
                bool leftWasActive,
                Vector3 rightScale,
                Vector3 leftScale,
                float pupilOverlayAmount,
                Color pupilOverlayColor,
                bool eyesWereEnabled)
            {
                Head = head;
                RightWasActive = rightWasActive;
                LeftWasActive = leftWasActive;
                RightScale = rightScale;
                LeftScale = leftScale;
                PupilOverlayAmount = pupilOverlayAmount;
                PupilOverlayColor = pupilOverlayColor;
                EyesWereEnabled = eyesWereEnabled;
            }

            internal PlayerDeathHead Head { get; }
            internal bool RightWasActive { get; }
            internal bool LeftWasActive { get; }
            internal Vector3 RightScale { get; }
            internal Vector3 LeftScale { get; }
            internal float PupilOverlayAmount { get; }
            internal Color PupilOverlayColor { get; }
            internal bool EyesWereEnabled { get; }
        }

        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Eyes");
        private static readonly FieldInfo? HeadPlayerAvatarField = AccessTools.Field(typeof(PlayerDeathHead), "playerAvatar");
        private static readonly FieldInfo? HeadTriggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");
        private static readonly FieldInfo? HeadPlayerEyesField = AccessTools.Field(typeof(PlayerDeathHead), "playerEyes");
        private static readonly FieldInfo? HeadPupilScaleTransformRightField = AccessTools.Field(typeof(PlayerDeathHead), "pupilScaleTransformRight");
        private static readonly FieldInfo? HeadPupilScaleTransformLeftField = AccessTools.Field(typeof(PlayerDeathHead), "pupilScaleTransformLeft");
        private static readonly FieldInfo? HeadPupilScaleDefaultField = AccessTools.Field(typeof(PlayerDeathHead), "pupilScaleDefault");
        private static readonly FieldInfo? HeadPupilMaterialField = AccessTools.Field(typeof(PlayerDeathHead), "pupilMaterial");
        private static readonly FieldInfo? HeadEyeMaterialField = AccessTools.Field(typeof(PlayerDeathHead), "eyeMaterial");
        private static readonly FieldInfo? HeadEyeMaterialAmountField = AccessTools.Field(typeof(PlayerDeathHead), "eyeMaterialAmount");
        private static readonly FieldInfo? HeadEyeMaterialColorField = AccessTools.Field(typeof(PlayerDeathHead), "eyeMaterialColor");
        private static readonly Dictionary<int, Color> LastEyeColorByHeadId = new();
        private static readonly Dictionary<int, HeadVisualState> BaselineByHeadId = new();

        internal static void ResetRuntimeState()
        {
            RestoreAllTrackedVisuals();
            LastEyeColorByHeadId.Clear();
            BaselineByHeadId.Clear();
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerDeathHead __instance)
        {
            if (__instance == null)
            {
                return;
            }

            var player = HeadPlayerAvatarField?.GetValue(__instance) as PlayerAvatar;
            if (!ShouldForceVisuals(__instance, player))
            {
                RestoreTrackedVisualsIfNeeded(__instance);
                return;
            }

            EnsureBaselineCaptured(__instance);
            ForcePupilsVisible(__instance);
            ForcePupilOverlayVisible(__instance);
            ForceEyeLookPipeline(__instance);
        }

        private static void ForcePupilsVisible(PlayerDeathHead head)
        {
            var right = HeadPupilScaleTransformRightField?.GetValue(head) as Transform;
            var left = HeadPupilScaleTransformLeftField?.GetValue(head) as Transform;
            if (right == null || left == null)
            {
                DebugLog("Pupil.Visible.MissingTransforms", $"headId={head.GetInstanceID()}");
                return;
            }

            var defaultScale = HeadPupilScaleDefaultField?.GetValue(head) as Vector3? ?? Vector3.one;
            if (!right.gameObject.activeSelf)
            {
                right.gameObject.SetActive(true);
            }

            if (!left.gameObject.activeSelf)
            {
                left.gameObject.SetActive(true);
            }

            right.localScale = defaultScale;
            left.localScale = defaultScale;
            DebugLog(
                "Pupil.Visible.Forced",
                $"headId={head.GetInstanceID()} leftActive={left.gameObject.activeSelf} rightActive={right.gameObject.activeSelf} leftScale={left.localScale} rightScale={right.localScale}");
        }

        private static void ForcePupilOverlayVisible(PlayerDeathHead head)
        {
            var pupilMaterial = HeadPupilMaterialField?.GetValue(head) as Material;
            if (pupilMaterial == null)
            {
                DebugLog("Pupil.Overlay.MissingMaterial", $"headId={head.GetInstanceID()}");
                return;
            }

            var amountPropertyId = HeadEyeMaterialAmountField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlayAmount");
            var colorPropertyId = HeadEyeMaterialColorField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlay");
            pupilMaterial.SetFloat(amountPropertyId, 1f);

            var eyeMaterial = HeadEyeMaterialField?.GetValue(head) as Material;
            var headId = head.GetInstanceID();
            if (eyeMaterial != null)
            {
                var eyeColor = eyeMaterial.GetColor(colorPropertyId);
                if (!LastEyeColorByHeadId.TryGetValue(headId, out var lastEyeColor) || !ApproximatelyEqual(lastEyeColor, eyeColor))
                {
                    LastEyeColorByHeadId[headId] = eyeColor;
                    var oppositePupilColor = GetOppositeColor(eyeColor);
                    pupilMaterial.SetColor(colorPropertyId, oppositePupilColor);
                    DebugLog(
                        "Pupil.Color.SyncedOnEyeChange",
                        $"headId={headId} eyeColor={eyeColor} pupilColor={oppositePupilColor}");
                }
            }

            var amountReadback = pupilMaterial.GetFloat(amountPropertyId);
            var colorReadback = pupilMaterial.GetColor(colorPropertyId);
            DebugLog(
                "Pupil.Overlay.Forced",
                $"headId={headId} amountPropertyId={amountPropertyId} amount={amountReadback:F3} colorPropertyId={colorPropertyId} color={colorReadback}");

            var renderers = head.pupilRenderers;
            if (renderers == null || renderers.Length == 0)
            {
                DebugLog("Pupil.Renderers.Missing", $"headId={head.GetInstanceID()}");
                return;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    DebugLog("Pupil.Renderers.Null", $"headId={head.GetInstanceID()} idx={i}");
                    continue;
                }

                DebugLog(
                    "Pupil.Renderers.State",
                    $"headId={head.GetInstanceID()} idx={i} rendererEnabled={renderer.enabled} activeSelf={renderer.gameObject.activeSelf} activeInHierarchy={renderer.gameObject.activeInHierarchy}");
            }
        }

        private static void ForceEyeLookPipeline(PlayerDeathHead head)
        {
            var eyes = HeadPlayerEyesField?.GetValue(head) as PlayerEyes;
            if (eyes == null)
            {
                DebugLog("Eyes.MissingPlayerEyes", $"headId={head.GetInstanceID()}");
                return;
            }

            if (!eyes.enabled)
            {
                eyes.enabled = true;
                DebugLog("Eyes.Enabled.Forced", $"headId={head.GetInstanceID()}");
                return;
            }

            DebugLog("Eyes.Enabled.Already", $"headId={head.GetInstanceID()}");
        }

        private static bool ShouldForceVisuals(PlayerDeathHead head, PlayerAvatar? player)
        {
            if (!FeatureFlags.LastChancePupilVisualsEnabled)
            {
                DebugLog("Skip.FlagDisabled", "LastChancePupilVisualsEnabled=false");
                return false;
            }

            if (!LastChanceTimerController.IsActive || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                DebugLog("Skip.Inactive", $"lastChance={LastChanceTimerController.IsActive} headProxy={LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player)}");
                return false;
            }

            if (!(HeadTriggeredField?.GetValue(head) as bool? ?? false))
            {
                DebugLog("Skip.NotTriggered", $"playerId={GetPlayerId(player)}");
                return false;
            }

            return true;
        }

        private static void EnsureBaselineCaptured(PlayerDeathHead head)
        {
            var headId = head.GetInstanceID();
            if (BaselineByHeadId.ContainsKey(headId))
            {
                return;
            }

            var right = HeadPupilScaleTransformRightField?.GetValue(head) as Transform;
            var left = HeadPupilScaleTransformLeftField?.GetValue(head) as Transform;
            var eyes = HeadPlayerEyesField?.GetValue(head) as PlayerEyes;
            var pupilMaterial = HeadPupilMaterialField?.GetValue(head) as Material;
            if (right == null || left == null || pupilMaterial == null)
            {
                DebugLog("Baseline.Skip.MissingRefs", $"headId={headId}");
                return;
            }

            var amountPropertyId = HeadEyeMaterialAmountField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlayAmount");
            var colorPropertyId = HeadEyeMaterialColorField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlay");
            var state = new HeadVisualState(
                head,
                right.gameObject.activeSelf,
                left.gameObject.activeSelf,
                right.localScale,
                left.localScale,
                pupilMaterial.GetFloat(amountPropertyId),
                pupilMaterial.GetColor(colorPropertyId),
                eyes != null && eyes.enabled);
            BaselineByHeadId[headId] = state;
            DebugLog(
                "Baseline.Captured",
                $"headId={headId} rightActive={state.RightWasActive} leftActive={state.LeftWasActive} eyesEnabled={state.EyesWereEnabled} overlayAmount={state.PupilOverlayAmount:F3}");
        }

        private static void RestoreTrackedVisualsIfNeeded(PlayerDeathHead head)
        {
            var headId = head.GetInstanceID();
            if (!BaselineByHeadId.TryGetValue(headId, out var state))
            {
                LastEyeColorByHeadId.Remove(headId);
                return;
            }

            var right = HeadPupilScaleTransformRightField?.GetValue(head) as Transform;
            var left = HeadPupilScaleTransformLeftField?.GetValue(head) as Transform;
            var eyes = HeadPlayerEyesField?.GetValue(head) as PlayerEyes;
            var pupilMaterial = HeadPupilMaterialField?.GetValue(head) as Material;
            if (right != null)
            {
                right.localScale = state.RightScale;
                right.gameObject.SetActive(state.RightWasActive);
            }

            if (left != null)
            {
                left.localScale = state.LeftScale;
                left.gameObject.SetActive(state.LeftWasActive);
            }

            if (pupilMaterial != null)
            {
                var amountPropertyId = HeadEyeMaterialAmountField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlayAmount");
                var colorPropertyId = HeadEyeMaterialColorField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlay");
                pupilMaterial.SetFloat(amountPropertyId, state.PupilOverlayAmount);
                pupilMaterial.SetColor(colorPropertyId, state.PupilOverlayColor);
            }

            if (eyes != null)
            {
                eyes.enabled = state.EyesWereEnabled;
            }

            LastEyeColorByHeadId.Remove(headId);
            BaselineByHeadId.Remove(headId);
            DebugLog("Baseline.Restored", $"headId={headId}");
        }

        private static void RestoreAllTrackedVisuals()
        {
            if (BaselineByHeadId.Count == 0)
            {
                return;
            }

            var states = new List<HeadVisualState>(BaselineByHeadId.Values);
            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                if (state.Head == null)
                {
                    continue;
                }

                RestoreTrackedVisualsIfNeeded(state.Head);
            }
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!FeatureFlags.DebugLogging || !InternalDebugFlags.DebugLastChanceEyesFlow || !LogLimiter.ShouldLog($"LastChance.Eyes.{reason}", 90))
            {
                return;
            }

            Log.LogInfo($"[LastChance][Eyes][{reason}] {detail}");
        }

        private static int GetPlayerId(PlayerAvatar? player)
        {
            if (player == null)
            {
                return -1;
            }

            return player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
        }

        private static Color GetOppositeColor(Color source)
        {
            return new Color(1f - source.r, 1f - source.g, 1f - source.b, source.a);
        }

        private static bool ApproximatelyEqual(Color a, Color b)
        {
            const float epsilon = 0.001f;
            return Mathf.Abs(a.r - b.r) < epsilon &&
                   Mathf.Abs(a.g - b.g) < epsilon &&
                   Mathf.Abs(a.b - b.b) < epsilon &&
                   Mathf.Abs(a.a - b.a) < epsilon;
        }
    }
}

