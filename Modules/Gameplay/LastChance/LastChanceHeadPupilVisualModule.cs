#nullable enable

using System.Reflection;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class LastChanceHeadPupilVisualModule
    {
        private readonly struct PupilVisualSnapshot
        {
            internal PupilVisualSnapshot(int colorPropertyId, int amountPropertyId, Color color, float amount)
            {
                ColorPropertyId = colorPropertyId;
                AmountPropertyId = amountPropertyId;
                Color = color;
                Amount = amount;
            }

            internal int ColorPropertyId { get; }
            internal int AmountPropertyId { get; }
            internal Color Color { get; }
            internal float Amount { get; }
        }

        private static readonly FieldInfo? HeadPlayerAvatarField = AccessTools.Field(typeof(PlayerDeathHead), "playerAvatar");
        private static readonly FieldInfo? HeadTriggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");
        private static readonly FieldInfo? HeadPlayerEyesField = AccessTools.Field(typeof(PlayerDeathHead), "playerEyes");
        private static readonly FieldInfo? HeadPupilScaleTransformRightField = AccessTools.Field(typeof(PlayerDeathHead), "pupilScaleTransformRight");
        private static readonly FieldInfo? HeadPupilScaleTransformLeftField = AccessTools.Field(typeof(PlayerDeathHead), "pupilScaleTransformLeft");
        private static readonly FieldInfo? HeadPupilScaleDefaultField = AccessTools.Field(typeof(PlayerDeathHead), "pupilScaleDefault");
        private static readonly FieldInfo? HeadPupilMaterialField = AccessTools.Field(typeof(PlayerDeathHead), "pupilMaterial");
        private static readonly FieldInfo? HeadEyeMaterialAmountField = AccessTools.Field(typeof(PlayerDeathHead), "eyeMaterialAmount");
        private static readonly FieldInfo? HeadEyeMaterialColorField = AccessTools.Field(typeof(PlayerDeathHead), "eyeMaterialColor");
        private static readonly Color PupilBlack = Color.black;
        private static readonly Dictionary<int, PupilVisualSnapshot> OriginalPupilVisualByHeadId = new();

        internal static void ResetRuntimeState()
        {
            var heads = UnityEngine.Object.FindObjectsOfType<PlayerDeathHead>();
            for (var i = 0; i < heads.Length; i++)
            {
                var head = heads[i];
                if (head == null)
                {
                    continue;
                }

                var headId = head.GetInstanceID();
                RestorePupilVisualIfNeeded(head, headId);
            }

            OriginalPupilVisualByHeadId.Clear();
        }

        [HarmonyPostfix]
        private static void Postfix(PlayerDeathHead __instance)
        {
            if (__instance == null)
            {
                return;
            }

            var headId = __instance.GetInstanceID();

            var player = HeadPlayerAvatarField?.GetValue(__instance) as PlayerAvatar;
            if (!LastChanceTimerController.IsActive || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                RestorePupilVisualIfNeeded(__instance, headId);
                return;
            }

            if (!(HeadTriggeredField?.GetValue(__instance) as bool? ?? false))
            {
                RestorePupilVisualIfNeeded(__instance, headId);
                return;
            }

            CapturePupilVisualIfNeeded(__instance, headId);
            ForcePupilsVisible(__instance);
            ForcePupilsBlack(__instance);
            ForceEyeLookPipeline(__instance);
        }

        private static void ForcePupilsVisible(PlayerDeathHead head)
        {
            var right = HeadPupilScaleTransformRightField?.GetValue(head) as Transform;
            var left = HeadPupilScaleTransformLeftField?.GetValue(head) as Transform;
            if (right == null || left == null)
            {
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
        }

        private static void ForcePupilsBlack(PlayerDeathHead head)
        {
            var pupilMaterial = HeadPupilMaterialField?.GetValue(head) as Material;
            if (pupilMaterial == null)
            {
                return;
            }

            var colorPropertyId = HeadEyeMaterialColorField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlay");
            var amountPropertyId = HeadEyeMaterialAmountField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlayAmount");
            pupilMaterial.SetColor(colorPropertyId, PupilBlack);
            pupilMaterial.SetFloat(amountPropertyId, 1f);
        }

        private static void CapturePupilVisualIfNeeded(PlayerDeathHead head, int headId)
        {
            if (OriginalPupilVisualByHeadId.ContainsKey(headId))
            {
                return;
            }

            var pupilMaterial = HeadPupilMaterialField?.GetValue(head) as Material;
            if (pupilMaterial == null)
            {
                return;
            }

            var colorPropertyId = HeadEyeMaterialColorField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlay");
            var amountPropertyId = HeadEyeMaterialAmountField?.GetValue(head) as int? ?? Shader.PropertyToID("_ColorOverlayAmount");
            var originalColor = pupilMaterial.GetColor(colorPropertyId);
            var originalAmount = pupilMaterial.GetFloat(amountPropertyId);
            OriginalPupilVisualByHeadId[headId] = new PupilVisualSnapshot(colorPropertyId, amountPropertyId, originalColor, originalAmount);
        }

        private static void RestorePupilVisualIfNeeded(PlayerDeathHead head, int headId)
        {
            if (!OriginalPupilVisualByHeadId.TryGetValue(headId, out var snapshot))
            {
                return;
            }

            var pupilMaterial = HeadPupilMaterialField?.GetValue(head) as Material;
            if (pupilMaterial != null)
            {
                pupilMaterial.SetColor(snapshot.ColorPropertyId, snapshot.Color);
                pupilMaterial.SetFloat(snapshot.AmountPropertyId, snapshot.Amount);
            }

            OriginalPupilVisualByHeadId.Remove(headId);
        }

        private static void ForceEyeLookPipeline(PlayerDeathHead head)
        {
            var eyes = HeadPlayerEyesField?.GetValue(head) as PlayerEyes;
            if (eyes != null && !eyes.enabled)
            {
                eyes.enabled = true;
            }
        }
    }
}
