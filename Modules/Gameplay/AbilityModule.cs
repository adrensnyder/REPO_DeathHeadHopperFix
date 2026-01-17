#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay
{
    internal static class AbilityModule
    {
        private static Type? s_abilityBaseType;
        private static MethodInfo? s_abilityCooldownGetter;
        private static MethodInfo? s_abilityEnergyCostGetter;
        private static MethodInfo? s_abilityNameGetter;
        private static Type? s_abilitySpotType;
        private static MethodInfo? s_abilitySpotSetCooldown;
        private static FieldInfo? s_abilitySpotsField;

        internal static void ApplyAbilitySpotLabelOverlay(Harmony harmony, Assembly asm)
        {
            var tAbilitySpot = asm.GetType("DeathHeadHopper.UI.AbilitySpot", throwOnError: false);
            if (tAbilitySpot == null)
                return;

            var mStart = AccessTools.Method(tAbilitySpot, "Start");
            var mUpdate = AccessTools.Method(tAbilitySpot, "UpdateUI");
            var mOnDestroy = tAbilitySpot.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var startPostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_Start_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var updatePostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_UpdateUI_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var destroyPostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_OnDestroy_Postfix), BindingFlags.Static | BindingFlags.NonPublic);

            if (mStart != null && startPostfix != null)
                harmony.Patch(mStart, postfix: new HarmonyMethod(startPostfix));
            if (mUpdate != null && updatePostfix != null)
                harmony.Patch(mUpdate, postfix: new HarmonyMethod(updatePostfix));
            if (mOnDestroy != null && destroyPostfix != null)
                harmony.Patch(mOnDestroy, postfix: new HarmonyMethod(destroyPostfix));
        }

        internal static void ApplyAbilityManagerHooks(Harmony harmony, Assembly asm)
        {
            var tAbilityManager = asm.GetType("DeathHeadHopper.Managers.DHHAbilityManager", throwOnError: false);
            if (tAbilityManager == null)
                return;

            EnsureAbilityReflection();
            if (s_abilityBaseType == null)
                return;

            var mOnAbilityUsed = AccessTools.Method(tAbilityManager, "OnAbilityUsed", new[] { s_abilityBaseType });
            if (mOnAbilityUsed == null)
                return;

            var postfix = typeof(AbilityModule).GetMethod(nameof(DHHAbilityManager_OnAbilityUsed_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(mOnAbilityUsed, postfix: new HarmonyMethod(postfix));
        }

        private static void AbilitySpot_Start_Postfix(object? __instance)
        {
            if (__instance == null)
                return;
            AbilitySpotLabelOverlay.EnsureLabel(__instance);
        }

        private static void AbilitySpot_UpdateUI_Postfix(object? __instance)
        {
            if (__instance == null)
                return;
            AbilitySpotLabelOverlay.UpdateLabel(__instance);
        }

        private static void AbilitySpot_OnDestroy_Postfix(object? __instance)
        {
            if (__instance == null)
                return;
            AbilitySpotLabelOverlay.ClearLabel(__instance);
        }

        private static void EnsureAbilityReflection()
        {
            if (s_abilityBaseType == null)
            {
                s_abilityBaseType = AccessTools.TypeByName("DeathHeadHopper.Abilities.AbilityBase");
            }

            if (s_abilityCooldownGetter == null && s_abilityBaseType != null)
            {
                s_abilityCooldownGetter = AccessTools.PropertyGetter(s_abilityBaseType, "Cooldown");
            }

            if (s_abilityEnergyCostGetter == null && s_abilityBaseType != null)
            {
                s_abilityEnergyCostGetter = AccessTools.PropertyGetter(s_abilityBaseType, "EnergyCost");
            }

            if (s_abilityNameGetter == null && s_abilityBaseType != null)
            {
                s_abilityNameGetter = AccessTools.PropertyGetter(s_abilityBaseType, "AbilityName");
            }

            if (s_abilitySpotType == null)
            {
                s_abilitySpotType = AccessTools.TypeByName("DeathHeadHopper.UI.AbilitySpot");
            }

            if (s_abilitySpotSetCooldown == null && s_abilitySpotType != null)
            {
                s_abilitySpotSetCooldown = AccessTools.Method(s_abilitySpotType, "SetCooldown", new[] { typeof(float) });
            }
        }

        private static void DHHAbilityManager_OnAbilityUsed_Postfix(object __instance, object ability)
        {
            if (ability == null)
                return;

            EnsureAbilityReflection();
            if (s_abilityCooldownGetter == null || s_abilitySpotSetCooldown == null)
                return;

            var cooldownObj = s_abilityCooldownGetter.Invoke(ability, null);
            var cooldown = cooldownObj is float value ? value : 0f;
            if (cooldown <= 0f)
                return;

            var spotsField = s_abilitySpotsField ??= __instance?.GetType().GetField("abilitySpots", BindingFlags.Instance | BindingFlags.NonPublic);
            if (spotsField == null)
                return;

            if (spotsField.GetValue(__instance) is not Array spots)
                return;

            foreach (var spot in spots)
            {
                if (spot == null)
                    continue;

                try
                {
                    s_abilitySpotSetCooldown?.Invoke(spot, new object[] { cooldown });
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static class AbilitySpotLabelOverlay
        {
            private static readonly Dictionary<object, Component> Labels = new();
            private static readonly Type? LabelType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
            private static readonly Type? AlignmentType = AccessTools.TypeByName("TMPro.TextAlignmentOptions");
            private static readonly PropertyInfo? TextProperty = LabelType?.GetProperty("text");
            private static readonly PropertyInfo? ColorProperty = LabelType?.GetProperty("color");
            private static readonly PropertyInfo? AlignmentProperty = LabelType?.GetProperty("alignment");
            private static readonly PropertyInfo? FontSizeProperty = LabelType?.GetProperty("fontSize");
            private static readonly PropertyInfo? AutoSizeProperty = LabelType?.GetProperty("enableAutoSizing");
            private static readonly PropertyInfo? WordWrapProperty = LabelType?.GetProperty("enableWordWrapping");
            private static readonly PropertyInfo? RichTextProperty = LabelType?.GetProperty("richText");
            private static readonly object? CenterAlignment = AlignmentType != null
                ? Enum.Parse(AlignmentType, "Center")
                : null;

            private static Type? SpotType;
            private static FieldInfo? SpotIndexField;

            internal static void EnsureLabel(object spot)
            {
                if (spot == null || LabelType == null)
                    return;
                if (Labels.ContainsKey(spot))
                    return;

                if (spot is not Component component)
                    return;

                var overlay = new GameObject("DHHAbilityLabel", typeof(RectTransform));
                overlay.transform.SetParent(component.transform, false);
                var rect = overlay.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                rect.SetAsLastSibling();

                var label = overlay.AddComponent(LabelType);
                SetLabelDefaults(label);
                Labels[spot] = label;
                UpdateLabel(spot);
            }

            internal static void UpdateLabel(object spot)
            {
                if (spot == null)
                    return;
                var label = GetLabel(spot);
                if (label == null)
                    return;

                var text = GetSlotTag(spot);
                SetLabelText(label, text);
            }

            internal static void ClearLabel(object spot)
            {
                if (spot == null)
                    return;
                if (!Labels.TryGetValue(spot, out var label))
                    return;

                Labels.Remove(spot);
                if (label is Component component)
                {
                    UnityEngine.Object.Destroy(component.gameObject);
                }
            }

            private static Component? GetLabel(object spot)
            {
                return Labels.TryGetValue(spot, out var label) ? label : null;
            }

            private static string GetSlotTag(object spot)
            {
                var index = GetAbilityIndex(spot);
                return index switch
                {
                    _ => string.Empty,
                };
            }

            private static int GetAbilityIndex(object spot)
            {
                if (spot == null)
                    return -1;

                var type = spot.GetType();
                if (SpotType == null || SpotType != type)
                {
                    SpotType = type;
                    SpotIndexField = type.GetField("abilitySpotIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }

                if (SpotIndexField == null)
                    return -1;

                return SpotIndexField.GetValue(spot) is int value ? value : -1;
            }

            private static void SetLabelDefaults(Component label)
            {
                if (label == null)
                    return;

                if (ColorProperty != null)
                    ColorProperty.SetValue(label, Color.white);
                if (FontSizeProperty != null)
                    FontSizeProperty.SetValue(label, 10f);
                if (AutoSizeProperty != null)
                    AutoSizeProperty.SetValue(label, false);
                if (WordWrapProperty != null)
                    WordWrapProperty.SetValue(label, false);
                if (RichTextProperty != null)
                    RichTextProperty.SetValue(label, false);
                if (AlignmentProperty != null && CenterAlignment != null)
                    AlignmentProperty.SetValue(label, CenterAlignment);

                SetLabelText(label, string.Empty);
            }

            private static void SetLabelText(Component label, string text)
            {
                if (label == null)
                    return;
                if (TextProperty != null)
                    TextProperty.SetValue(label, text);
                if (label is Behaviour behaviour)
                    behaviour.enabled = !string.IsNullOrEmpty(text);
            }
        }
    }
}
