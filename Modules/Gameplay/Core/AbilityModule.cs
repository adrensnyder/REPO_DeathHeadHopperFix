#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
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
        private static readonly HashSet<object> s_trackedSpots = new();
        private static readonly Dictionary<object, Vector3> s_spotBaseLocalPos = new();

        private const int DirectionIndicatorSlotIndex = 1;
        private const string DirectionIconFileName = "Direction.png";

        internal static void ApplyAbilitySpotLabelOverlay(Harmony harmony, Assembly asm)
        {
            var tAbilitySpot = asm.GetType("DeathHeadHopper.UI.AbilitySpot", throwOnError: false);
            if (tAbilitySpot == null)
                return;

            var mStart = AccessTools.Method(tAbilitySpot, "Start");
            var mUpdate = AccessTools.Method(tAbilitySpot, "Update");
            var mUpdateUi = AccessTools.Method(tAbilitySpot, "UpdateUI");
            var mOnDestroy = tAbilitySpot.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var startPostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_Start_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var updatePostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_Update_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var updateUiPostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_UpdateUI_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var destroyPostfix = typeof(AbilityModule).GetMethod(nameof(AbilitySpot_OnDestroy_Postfix), BindingFlags.Static | BindingFlags.NonPublic);

            if (mStart != null && startPostfix != null)
                harmony.Patch(mStart, postfix: new HarmonyMethod(startPostfix));
            if (mUpdate != null && updatePostfix != null)
                harmony.Patch(mUpdate, postfix: new HarmonyMethod(updatePostfix));
            if (mUpdateUi != null && updateUiPostfix != null)
                harmony.Patch(mUpdateUi, postfix: new HarmonyMethod(updateUiPostfix));
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
            if (FeatureFlags.DisableAbilityPatches)
                return;

            s_trackedSpots.Add(__instance);
            if (__instance is Component component)
            {
                s_spotBaseLocalPos[__instance] = component.transform.localPosition;
            }
            AbilitySpotLabelOverlay.EnsureLabel(__instance);
            ApplySlot2DirectionVisual(__instance);
        }

        private static void AbilitySpot_UpdateUI_Postfix(object? __instance)
        {
            if (__instance == null)
                return;
            if (FeatureFlags.DisableAbilityPatches)
                return;

            AbilitySpotLabelOverlay.UpdateLabel(__instance);
            ApplySlot2DirectionVisual(__instance);
        }

        private static void AbilitySpot_Update_Postfix(object? __instance)
        {
            if (__instance == null)
                return;
            if (FeatureFlags.DisableAbilityPatches)
                return;
            if (GetAbilityIndex(__instance) != DirectionIndicatorSlotIndex)
                return;
            if (!LastChanceTimerController.IsDirectionIndicatorUiVisible)
                return;

            // AbilitySpot.Update() pushes empty slots down every frame.
            // Force slot2 to stay in active position while direction indicator is visible.
            SlotLayoutOverrides.EnsureBasePosition(__instance);
        }

        private static void AbilitySpot_OnDestroy_Postfix(object? __instance)
        {
            if (__instance == null)
                return;
            if (FeatureFlags.DisableAbilityPatches)
                return;

            s_trackedSpots.Remove(__instance);
            s_spotBaseLocalPos.Remove(__instance);
            AbilitySpotLabelOverlay.ClearLabel(__instance);
        }

        internal static void TriggerDirectionSlotCooldown(float cooldownSeconds)
        {
            if (FeatureFlags.DisableAbilityPatches)
                return;

            EnsureAbilityReflection();
            if (s_abilitySpotSetCooldown == null)
                return;

            var clamped = Mathf.Max(0f, cooldownSeconds);
            if (clamped <= 0f)
                return;

            foreach (var spot in s_trackedSpots)
            {
                if (spot == null || GetAbilityIndex(spot) != DirectionIndicatorSlotIndex)
                    continue;

                try
                {
                    s_abilitySpotSetCooldown.Invoke(spot, new object[] { clamped });
                }
                catch
                {
                    // ignore
                }
            }
        }

        internal static void RefreshDirectionSlotVisuals()
        {
            if (FeatureFlags.DisableAbilityPatches)
                return;

            if (s_trackedSpots.Count == 0)
                return;

            var staleSpots = new List<object>();
            foreach (var spot in s_trackedSpots)
            {
                if (!IsSpotUsable(spot))
                {
                    staleSpots.Add(spot);
                    continue;
                }

                if (GetAbilityIndex(spot) != DirectionIndicatorSlotIndex)
                    continue;

                try
                {
                    ApplySlot2DirectionVisual(spot);
                }
                catch
                {
                    staleSpots.Add(spot);
                }
            }

            if (staleSpots.Count > 0)
            {
                foreach (var stale in staleSpots)
                {
                    s_trackedSpots.Remove(stale);
                    s_spotBaseLocalPos.Remove(stale);
                }
            }
        }

        private static void ApplySlot2DirectionVisual(object spot)
        {
            var slotIndex = GetAbilityIndex(spot);
            if (slotIndex != DirectionIndicatorSlotIndex)
                return;

            var visible = LastChanceTimerController.IsDirectionIndicatorUiVisible;
            if (!visible)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("AbilityModule.DirectionIconHidden", 60))
                {
                    Debug.Log($"[Fix:Ability] Slot2 hidden. slotIndex={slotIndex} visible={visible} mode={FeatureFlags.LastChanceIndicators}");
                }
                AbilitySpotLabelOverlay.SetDirectionLabel(spot, string.Empty);
                SlotCostOverrides.RestoreDefaultCostText(spot);
                SlotVisualOverrides.RestoreDefaultIcon(spot);
                SlotLayoutOverrides.RestoreBasePosition(spot);
                return;
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("AbilityModule.DirectionIconApply", 60))
            {
                Debug.Log($"[Fix:Ability] Slot2 apply icon. slotIndex={slotIndex} visible={visible} mode={FeatureFlags.LastChanceIndicators}");
            }
            AbilitySpotLabelOverlay.SetDirectionLabel(spot, string.Empty);
            SlotCostOverrides.SetDirectionCostText(spot, GetDirectionCostLabel());
            SlotVisualOverrides.ApplyDirectionIcon(spot, DirectionIconFileName);
            SlotLayoutOverrides.EnsureBasePosition(spot);
        }

        private static string GetDirectionCostLabel()
        {
            var preview = LastChanceTimerController.GetDirectionIndicatorPenaltySecondsPreview();
            var seconds = Mathf.RoundToInt(Mathf.Max(0f, preview));
            return $"{seconds}s";
        }

        private static int GetAbilityIndex(object spot)
        {
            if (spot == null)
                return -1;

            var type = spot.GetType();
            if (AbilitySpotLabelOverlay.SpotType == null || AbilitySpotLabelOverlay.SpotType != type)
            {
                AbilitySpotLabelOverlay.SpotType = type;
                AbilitySpotLabelOverlay.SpotIndexField = type.GetField("abilitySpotIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            if (AbilitySpotLabelOverlay.SpotIndexField == null)
                return -1;

            return AbilitySpotLabelOverlay.SpotIndexField.GetValue(spot) is int value ? value : -1;
        }

        private static bool IsSpotUsable(object spot)
        {
            if (spot == null)
                return false;

            if (spot is not Component component)
                return false;

            return component != null && component.gameObject != null;
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

            internal static Type? SpotType;
            internal static FieldInfo? SpotIndexField;

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
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 2f);
                rect.sizeDelta = new Vector2(0f, 16f);
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

            internal static void SetDirectionLabel(object spot, string text)
            {
                var label = GetLabel(spot);
                if (label == null)
                    return;
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
                return string.Empty;
            }

            private static void SetLabelDefaults(Component label)
            {
                if (label == null)
                    return;

                if (ColorProperty != null)
                    ColorProperty.SetValue(label, Color.white);
                if (FontSizeProperty != null)
                    FontSizeProperty.SetValue(label, 11f);
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

        private static class SlotLayoutOverrides
        {
            internal static void EnsureBasePosition(object spot)
            {
                if (spot is not Component component)
                    return;

                if (!s_spotBaseLocalPos.TryGetValue(spot, out var basePos))
                {
                    basePos = component.transform.localPosition;
                    s_spotBaseLocalPos[spot] = basePos;
                }

                component.transform.localPosition = basePos;
            }

            internal static void RestoreBasePosition(object spot)
            {
                if (spot is not Component component)
                    return;
                if (!s_spotBaseLocalPos.TryGetValue(spot, out var basePos))
                    return;
                component.transform.localPosition = basePos;
            }
        }

        private static class SlotCostOverrides
        {
            private static FieldInfo? s_energyCostField;
            private static MethodInfo? s_currentAbilityGetter;
            private static MethodInfo? s_energyCostGetter;
            private static PropertyInfo? s_textProperty;

            internal static void SetDirectionCostText(object spot, string costText)
            {
                var text = GetEnergyCostComponent(spot);
                if (text == null)
                {
                    return;
                }

                SetText(text, costText);
            }

            internal static void RestoreDefaultCostText(object spot)
            {
                var text = GetEnergyCostComponent(spot);
                if (text == null)
                {
                    return;
                }

                var defaultCost = "0";
                var ability = GetCurrentAbility(spot);
                if (ability != null)
                {
                    var abilityType = ability.GetType();
                    if (s_energyCostGetter == null || s_energyCostGetter.DeclaringType != abilityType)
                    {
                        s_energyCostGetter = AccessTools.PropertyGetter(abilityType, "EnergyCost");
                    }

                    if (s_energyCostGetter?.Invoke(ability, null) is float energyCost)
                    {
                        defaultCost = Mathf.RoundToInt(energyCost).ToString();
                    }
                }

                SetText(text, defaultCost);
            }

            private static Component? GetEnergyCostComponent(object spot)
            {
                var type = spot.GetType();
                s_energyCostField ??= type.GetField("energyCost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return s_energyCostField?.GetValue(spot) as Component;
            }

            private static object? GetCurrentAbility(object spot)
            {
                var type = spot.GetType();
                s_currentAbilityGetter ??= type.GetProperty("CurrentAbility", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod;
                return s_currentAbilityGetter?.Invoke(spot, null);
            }

            private static void SetText(Component target, string value)
            {
                if (target == null)
                    return;

                var type = target.GetType();
                if (s_textProperty == null || s_textProperty.DeclaringType != type)
                {
                    s_textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                }

                s_textProperty?.SetValue(target, value ?? string.Empty);
            }
        }

        private static class SlotVisualOverrides
        {
            private static FieldInfo? s_backgroundIconField;
            private static FieldInfo? s_cooldownIconField;
            private static FieldInfo? s_noAbilityField;
            private static MethodInfo? s_currentAbilityGetter;
            private static FieldInfo? s_abilityIconField;
            private static MethodInfo? s_setIconMethod;
            private static Sprite? s_directionSprite;
            private static string s_loadedFrom = string.Empty;
            private static PropertyInfo? s_behaviourEnabledProp;
            private static PropertyInfo? s_imageSpriteProp;

            internal static void ApplyDirectionIcon(object spot, string fileName)
            {
                if (spot is not Component)
                    return;

                var type = spot.GetType();
                s_backgroundIconField ??= type.GetField("backgroundIcon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_cooldownIconField ??= type.GetField("cooldownIcon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_noAbilityField ??= type.GetField("noAbility", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                var sprite = GetOrLoadDirectionSprite(fileName);
                if (sprite == null)
                    return;

                var backgroundImage = s_backgroundIconField?.GetValue(spot);
                SetImageSpriteAndEnable(backgroundImage, sprite);

                var cooldownImage = s_cooldownIconField?.GetValue(spot);
                SetImageSpriteAndEnable(cooldownImage, sprite);

                if (s_noAbilityField?.GetValue(spot) is Behaviour noAbilityText)
                {
                    noAbilityText.enabled = false;
                }
            }

            internal static void RestoreDefaultIcon(object spot)
            {
                if (spot == null)
                    return;
                if (spot is not Component component || component == null || component.gameObject == null)
                    return;

                var type = spot.GetType();
                s_currentAbilityGetter ??= type.GetProperty("CurrentAbility", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod;
                s_setIconMethod ??= type.GetMethod("SetIcon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_noAbilityField ??= type.GetField("noAbility", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                object? currentAbility = null;
                if (s_currentAbilityGetter != null)
                {
                    currentAbility = s_currentAbilityGetter.Invoke(spot, null);
                }

                Sprite? icon = null;
                if (currentAbility != null)
                {
                    var abilityType = currentAbility.GetType();
                    if (s_abilityIconField == null || s_abilityIconField.DeclaringType != abilityType)
                    {
                        s_abilityIconField = abilityType.GetField("icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }

                    icon = s_abilityIconField?.GetValue(currentAbility) as Sprite;
                }

                if (s_setIconMethod != null)
                {
                    try
                    {
                        s_setIconMethod.Invoke(spot, new object?[] { icon });
                    }
                    catch
                    {
                        // AbilitySpot.SetIcon can throw during scene unload if UI refs are already torn down.
                        return;
                    }
                }

                if (s_noAbilityField?.GetValue(spot) is Behaviour noAbilityText)
                {
                    noAbilityText.enabled = currentAbility == null;
                }
            }

            private static void SetImageSpriteAndEnable(object? imageLikeObject, Sprite sprite)
            {
                if (imageLikeObject == null || sprite == null)
                    return;

                var type = imageLikeObject.GetType();
                s_behaviourEnabledProp ??= typeof(Behaviour).GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
                if (s_imageSpriteProp == null || s_imageSpriteProp.DeclaringType != type)
                {
                    s_imageSpriteProp = type.GetProperty("sprite", BindingFlags.Instance | BindingFlags.Public);
                }

                if (s_imageSpriteProp != null && s_imageSpriteProp.PropertyType == typeof(Sprite))
                {
                    s_imageSpriteProp.SetValue(imageLikeObject, sprite);
                }

                if (s_behaviourEnabledProp != null && typeof(Behaviour).IsAssignableFrom(type))
                {
                    s_behaviourEnabledProp.SetValue(imageLikeObject, true);
                }
            }

            private static Sprite? GetOrLoadDirectionSprite(string fileName)
            {
                if (s_directionSprite != null)
                    return s_directionSprite;

                if (!ImageAssetLoader.TryLoadSprite(fileName, ImageAssetLoader.GetDefaultAssetsDirectory(), out var sprite, out var resolvedPath))
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("AbilityModule.DirectionIconLoadFail", 60))
                    {
                        var baseDir = ImageAssetLoader.GetDefaultAssetsDirectory();
                        Debug.LogWarning($"[Fix:Ability] Failed to load slot2 direction icon. file={fileName} baseDir={baseDir}");
                    }
                    return null;
                }

                s_directionSprite = sprite;
                s_loadedFrom = resolvedPath;
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("AbilityModule.DirectionIconLoaded", 60))
                {
                    Debug.Log($"[Fix:Ability] Loaded slot2 direction icon from: {s_loadedFrom}");
                }

                return s_directionSprite;
            }
        }
    }
}
