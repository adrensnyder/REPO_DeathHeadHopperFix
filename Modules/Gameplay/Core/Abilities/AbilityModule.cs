#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopper.Abilities;
using DeathHeadHopper.Managers;
using DeathHeadHopper.UI;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class AbilityModule
    {
        private const string DirectionEnergyLogKey = "Fix:Ability.DirectionEnergy";
        private static readonly HashSet<AbilitySpot> s_trackedSpots = new();
        private static readonly Dictionary<AbilitySpot, Vector3> s_spotBaseLocalPos = new();
        private static readonly Dictionary<AbilitySpot, bool> s_lastDirectionVisibilityBySpot = new();
        private static readonly Dictionary<AbilitySpot, string> s_lastDirectionCostLabelBySpot = new();
        private static readonly Dictionary<AbilitySpot, float> s_lastDirectionProgressBySpot = new();
        private static readonly Dictionary<AbilitySpot, bool> s_lastDirectionEnergySufficientBySpot = new();
        private static float s_directionActivationProgress;

        private const int DirectionIndicatorSlotIndex = 1;
        private const int ChargeAbilitySlotIndex = 0;

        internal static void ApplyAbilitySpotLabelOverlay(Harmony harmony, Assembly asm)
        {
            harmony.Patch(
                AccessTools.Method(typeof(AbilitySpot), nameof(AbilitySpot.Start)),
                postfix: new HarmonyMethod(typeof(AbilityModule), nameof(AbilitySpot_Start_Postfix)));
            harmony.Patch(
                AccessTools.Method(typeof(AbilitySpot), nameof(AbilitySpot.UpdateUI)),
                postfix: new HarmonyMethod(typeof(AbilityModule), nameof(AbilitySpot_UpdateUI_Postfix)));
        }

        internal static void ApplyAbilityManagerHooks(Harmony harmony, Assembly asm)
        {
            harmony.Patch(
                AccessTools.Method(typeof(DHHAbilityManager), nameof(DHHAbilityManager.OnAbilityUsed), new[] { typeof(AbilityBase) }),
                postfix: new HarmonyMethod(typeof(AbilityModule), nameof(DHHAbilityManager_OnAbilityUsed_Postfix)));
        }

        private static void AbilitySpot_Start_Postfix(AbilitySpot __instance)
        {
            if (__instance == null)
                return;
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            s_trackedSpots.Add(__instance);
            s_spotBaseLocalPos[__instance] = __instance.transform.localPosition;
            AbilitySpotLabelOverlay.EnsureLabel(__instance);
            ApplySlot2DirectionVisual(__instance);
            var driver = __instance.GetComponent<AbilitySpotUpdateDriver>() ?? __instance.gameObject.AddComponent<AbilitySpotUpdateDriver>();
            driver.Initialize(__instance);
            __instance.enabled = false;
        }

        private static void AbilitySpot_UpdateUI_Postfix(AbilitySpot __instance)
        {
            if (__instance == null)
                return;
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            AbilitySpotLabelOverlay.UpdateLabel(__instance);
            ApplySlot2DirectionVisual(__instance);
        }

        private static void AfterAbilitySpotUpdate(AbilitySpot __instance)
        {
            if (__instance == null)
                return;
            if (InternalDebugFlags.DisableAbilityPatches)
                return;
            if (GetAbilityIndex(__instance) != DirectionIndicatorSlotIndex)
                return;
            if (!LastChanceInteropBridge.IsDirectionIndicatorUiVisible())
                return;

            // AbilitySpot.Update() pushes empty slots down every frame.
            // Force slot2 to stay in active position while direction indicator is visible.
            SlotLayoutOverrides.EnsureBasePosition(__instance);
        }

        private static void AbilitySpot_OnDestroy(AbilitySpot spot)
        {
            if (spot == null)
                return;

            s_trackedSpots.Remove(spot);
            s_spotBaseLocalPos.Remove(spot);
            s_lastDirectionVisibilityBySpot.Remove(spot);
            s_lastDirectionCostLabelBySpot.Remove(spot);
            s_lastDirectionProgressBySpot.Remove(spot);
            s_lastDirectionEnergySufficientBySpot.Remove(spot);
            AbilitySpotLabelOverlay.ClearLabel(spot);
        }

        private sealed class AbilitySpotUpdateDriver : MonoBehaviour
        {
            private AbilitySpot? _spot;

            internal void Initialize(AbilitySpot spot)
            {
                _spot = spot;
            }

            private void Update()
            {
                var spot = _spot;
                if (spot == null)
                    return;
                if (InternalDebugFlags.DisableAbilityPatches)
                    return;

                var ability = spot.CurrentAbility;
                if (ability == null)
                {
                    spot.SemiUIScoot(new Vector2(0f, -20f), 0.2f);
                }
                else
                {
                    spot.level.text = $"LV. {ability.AbilityLevel}";
                }

                RunSemiUiUpdate(spot);
                AfterAbilitySpotUpdate(spot);
            }

            private void OnDestroy()
            {
                if (_spot != null)
                {
                    AbilitySpot_OnDestroy(_spot);
                }
            }

            private static void RunSemiUiUpdate(SemiUI ui)
            {
                if (ui.initializedTimer > 0f)
                {
                    ui.initializedTimer -= Time.deltaTime;
                    return;
                }

                var deltaTime = Time.deltaTime;
                if (ui.scootTimer >= 0f)
                {
                    ui.scootTimer -= deltaTime;
                }

                ui.FlashColorLogic(deltaTime);
                ui.HideAnimationLogic(deltaTime);
                ui.HideTimer(deltaTime);
                ui.SpringScaleLogic(deltaTime);
                ui.ScootPositionLogic(deltaTime);
                ui.SpringShakeLogic(deltaTime);
                ui.UpdatePositionLogic();
                ui.prevShowTimer = ui.showTimer;
                ui.prevHideTimer = ui.hideTimer;
                ui.prevScootTimer = ui.scootTimer;
                ui.prevStopHidingTimer = ui.stopHidingTimer;
                ui.prevStopShowingTimer = ui.stopShowingTimer;

                if (ui.hideTimer >= 0f)
                    ui.hideTimer -= deltaTime;
                if (ui.showTimer >= 0f)
                    ui.showTimer -= deltaTime;
                if (ui.stopShowingTimer >= 0f)
                    ui.stopShowingTimer -= deltaTime;
                if (ui.stopHidingTimer >= 0f)
                    ui.stopHidingTimer -= deltaTime;
                if (ui.stopScootingTimer >= 0f)
                    ui.stopScootingTimer -= deltaTime;
            }
        }

        internal static void TriggerDirectionSlotCooldown(float cooldownSeconds)
        {
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            s_directionActivationProgress = 0f;
            var clamped = Mathf.Max(0f, cooldownSeconds);
            if (clamped <= 0f)
                return;

            foreach (var spot in s_trackedSpots)
            {
                if (spot == null || GetAbilityIndex(spot) != DirectionIndicatorSlotIndex)
                    continue;

                try
                {
                    spot.SetCooldown(clamped);
                    SlotVisualOverrides.ApplyDirectionActivationProgress(spot, 0f);
                    SlotVisualOverrides.ApplyDirectionEnergyAvailability(
                        spot,
                        LastChanceInteropBridge.IsDirectionIndicatorEnergySufficientPreview(),
                        s_directionActivationProgress);
                }
                catch
                {
                    // UI element may be destroyed during scene/menu transitions.
                }
            }
        }

        internal static void SetDirectionSlotActivationProgress(float progress01)
        {
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            s_directionActivationProgress = Mathf.Clamp01(progress01);
            if (s_trackedSpots.Count == 0)
                return;

            foreach (var spot in s_trackedSpots)
            {
                if (!IsSpotUsable(spot))
                    continue;
                if (GetAbilityIndex(spot) != DirectionIndicatorSlotIndex)
                    continue;
                if (!LastChanceInteropBridge.IsDirectionIndicatorUiVisible())
                    continue;

                SlotVisualOverrides.ApplyDirectionActivationProgress(spot, s_directionActivationProgress);
                SlotVisualOverrides.ApplyDirectionEnergyAvailability(
                    spot,
                    LastChanceInteropBridge.IsDirectionIndicatorEnergySufficientPreview(),
                    s_directionActivationProgress);
            }
        }

        internal static void SetChargeSlotActivationProgress(float progress01, float releaseThreshold01 = 0f)
        {
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            if (s_trackedSpots.Count == 0)
                return;

            var clamped = Mathf.Clamp01(progress01);
            var threshold = Mathf.Clamp01(releaseThreshold01);
            var canReleaseActivate = clamped >= threshold;
            foreach (var spot in s_trackedSpots)
            {
                if (!IsSpotUsable(spot))
                    continue;
                if (GetAbilityIndex(spot) != ChargeAbilitySlotIndex)
                    continue;

                SlotVisualOverrides.ApplyChargeActivationProgress(spot, clamped, canReleaseActivate);
            }
        }

        internal static void RefreshDirectionSlotVisuals()
        {
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            if (s_trackedSpots.Count == 0)
                return;

            var staleSpots = new List<AbilitySpot>();
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

        private static void ApplySlot2DirectionVisual(AbilitySpot spot)
        {
            var slotIndex = GetAbilityIndex(spot);
            if (slotIndex != DirectionIndicatorSlotIndex)
                return;

            var visible = LastChanceInteropBridge.IsDirectionIndicatorUiVisible();
            var previousVisible = s_lastDirectionVisibilityBySpot.TryGetValue(spot, out var prev) ? prev : (bool?)null;
            s_lastDirectionVisibilityBySpot[spot] = visible;
            if (!visible)
            {
                if (FeatureFlags.DebugLogging && previousVisible != false)
                {
                    Debug.Log($"[Fix:Ability] Slot2 hidden. slotIndex={slotIndex} visible={visible} mode={LastChanceInteropBridge.GetLastChanceIndicatorsMode()}");
                }
                if (previousVisible == false)
                {
                    return;
                }
                AbilitySpotLabelOverlay.SetDirectionLabel(spot, string.Empty);
                SlotCostOverrides.RestoreDefaultCostText(spot);
                SlotVisualOverrides.RestoreDefaultIcon(spot);
                SlotVisualOverrides.ApplyDirectionActivationProgress(spot, 0f);
                SlotLayoutOverrides.RestoreBasePosition(spot);
                s_lastDirectionCostLabelBySpot.Remove(spot);
                s_lastDirectionProgressBySpot.Remove(spot);
                s_lastDirectionEnergySufficientBySpot.Remove(spot);
                return;
            }

            if (FeatureFlags.DebugLogging && previousVisible != true)
            {
                Debug.Log($"[Fix:Ability] Slot2 apply icon. slotIndex={slotIndex} visible={visible} mode={LastChanceInteropBridge.GetLastChanceIndicatorsMode()}");
            }
            var costLabel = GetDirectionCostLabel();
            var roundedProgress = Mathf.Round(s_directionActivationProgress * 1000f) * 0.001f;
            var progressChanged = !s_lastDirectionProgressBySpot.TryGetValue(spot, out var lastProgress) ||
                                  Mathf.Abs(lastProgress - roundedProgress) > 0.0001f;
            var hasDirectionEnergy = LastChanceInteropBridge.IsDirectionIndicatorEnergySufficientPreview();
            if (FeatureFlags.DebugLogging &&
                InternalDebugFlags.DebugDirectionSlotEnergyPreviewLog &&
                LogLimiter.ShouldLog(DirectionEnergyLogKey, 120))
            {
                LastChanceInteropBridge.GetDirectionIndicatorEnergyDebugSnapshot(
                    out var directionVisible,
                    out var timerRemaining,
                    out var penaltyPreview,
                    out var snapshotHasEnoughEnergy);
                Debug.Log(
                    $"[Fix:Ability] Slot2 energy preview visible={directionVisible} timer={timerRemaining:F1}s cost={penaltyPreview:F1}s enough={snapshotHasEnoughEnergy} appliedEnough={hasDirectionEnergy} progress={roundedProgress:F3}");
            }
            var energyStateChanged = !s_lastDirectionEnergySufficientBySpot.TryGetValue(spot, out var lastEnergyState) ||
                                     lastEnergyState != hasDirectionEnergy;
            var costChanged = !s_lastDirectionCostLabelBySpot.TryGetValue(spot, out var lastCostLabel) ||
                              !string.Equals(lastCostLabel, costLabel, StringComparison.Ordinal);
            var becameVisible = previousVisible != true;
            if (!becameVisible && !progressChanged && !costChanged && !energyStateChanged)
            {
                return;
            }

            AbilitySpotLabelOverlay.SetDirectionLabel(spot, string.Empty);
            if (becameVisible || costChanged)
            {
                SlotCostOverrides.SetDirectionCostText(spot, costLabel);
                if (LastChanceInteropBridge.TryGetDirectionSlotSprite(out var directionSprite) && directionSprite != null)
                {
                    SlotVisualOverrides.ApplyDirectionIcon(spot, directionSprite);
                }
                SlotLayoutOverrides.EnsureBasePosition(spot);
                s_lastDirectionCostLabelBySpot[spot] = costLabel;
            }

            if (becameVisible || progressChanged)
            {
                SlotVisualOverrides.ApplyDirectionActivationProgress(spot, roundedProgress);
                s_lastDirectionProgressBySpot[spot] = roundedProgress;
            }

            SlotVisualOverrides.ApplyDirectionEnergyAvailability(spot, hasDirectionEnergy, roundedProgress);
            s_lastDirectionEnergySufficientBySpot[spot] = hasDirectionEnergy;
        }

        private static string GetDirectionCostLabel()
        {
            var preview = LastChanceInteropBridge.GetDirectionIndicatorPenaltySecondsPreview();
            var seconds = Mathf.RoundToInt(Mathf.Max(0f, preview));
            return $"{seconds}s";
        }

        private static int GetAbilityIndex(AbilitySpot spot)
        {
            if (spot == null)
                return -1;

            return spot.abilitySpotIndex;
        }

        private static bool IsSpotUsable(AbilitySpot spot)
        {
            if (spot == null)
                return false;

            return spot.gameObject != null;
        }

        private static void DHHAbilityManager_OnAbilityUsed_Postfix(DHHAbilityManager __instance, AbilityBase ability)
        {
            if (ability == null)
                return;

            var cooldown = ability.Cooldown;
            if (cooldown <= 0f)
                return;

            if (__instance?.abilitySpots == null)
                return;

            var matchingSpots = new List<AbilitySpot>();
            foreach (var spot in __instance.abilitySpots)
            {
                if (spot == null)
                    continue;

                if (ReferenceEquals(spot.CurrentAbility, ability))
                {
                    matchingSpots.Add(spot);
                }
            }

            if (matchingSpots.Count == 0 && !string.IsNullOrWhiteSpace(ability.AbilityName))
            {
                foreach (var spot in __instance.abilitySpots)
                {
                    if (spot?.CurrentAbility == null)
                        continue;

                    if (string.Equals(spot.CurrentAbility.AbilityName, ability.AbilityName, StringComparison.Ordinal))
                    {
                        matchingSpots.Add(spot);
                    }
                }
            }

            foreach (var spot in matchingSpots)
            {
                try
                {
                    spot.SetCooldown(cooldown);
                }
                catch
                {
                    // Keep processing other spots if one UI reference is already invalid.
                }
            }
        }

        private static class AbilitySpotLabelOverlay
        {
            private static readonly Dictionary<AbilitySpot, TextMeshProUGUI> Labels = new();

            internal static void EnsureLabel(AbilitySpot spot)
            {
                if (spot == null)
                    return;
                if (Labels.ContainsKey(spot))
                    return;

                var overlay = new GameObject("DHHAbilityLabel", typeof(RectTransform));
                overlay.transform.SetParent(spot.transform, false);
                var rect = overlay.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 2f);
                rect.sizeDelta = new Vector2(0f, 16f);
                rect.localScale = Vector3.one;
                rect.SetAsLastSibling();

                var label = overlay.AddComponent<TextMeshProUGUI>();
                SetLabelDefaults(label);
                Labels[spot] = label;
                UpdateLabel(spot);
            }

            internal static void UpdateLabel(AbilitySpot spot)
            {
                if (spot == null)
                    return;
                var label = GetLabel(spot);
                if (label == null)
                    return;

                var text = GetSlotTag(spot);
                SetLabelText(label, text);
            }

            internal static void SetDirectionLabel(AbilitySpot spot, string text)
            {
                var label = GetLabel(spot);
                if (label == null)
                    return;
                SetLabelText(label, text);
            }

            internal static void ClearLabel(AbilitySpot spot)
            {
                if (spot == null)
                    return;
                if (!Labels.TryGetValue(spot, out var label))
                    return;

                Labels.Remove(spot);
                UnityEngine.Object.Destroy(label.gameObject);
            }

            private static TextMeshProUGUI? GetLabel(AbilitySpot spot)
            {
                return Labels.TryGetValue(spot, out var label) ? label : null;
            }

            private static string GetSlotTag(AbilitySpot spot)
            {
                return string.Empty;
            }

            private static void SetLabelDefaults(TextMeshProUGUI label)
            {
                if (label == null)
                    return;

                label.color = Color.white;
                label.fontSize = 11f;
                label.enableAutoSizing = false;
                label.enableWordWrapping = false;
                label.richText = false;
                label.alignment = TextAlignmentOptions.Center;

                SetLabelText(label, string.Empty);
            }

            private static void SetLabelText(TextMeshProUGUI label, string text)
            {
                if (label == null)
                    return;
                label.text = text;
                label.enabled = !string.IsNullOrEmpty(text);
            }
        }

        private static class SlotLayoutOverrides
        {
            internal static void EnsureBasePosition(AbilitySpot spot)
            {
                if (spot == null)
                    return;

                if (!s_spotBaseLocalPos.TryGetValue(spot, out var basePos))
                {
                    basePos = spot.transform.localPosition;
                    s_spotBaseLocalPos[spot] = basePos;
                }

                spot.transform.localPosition = basePos;
            }

            internal static void RestoreBasePosition(AbilitySpot spot)
            {
                if (spot == null)
                    return;
                if (!s_spotBaseLocalPos.TryGetValue(spot, out var basePos))
                    return;
                spot.transform.localPosition = basePos;
            }
        }


        private static class SlotCostOverrides
        {
            internal static void SetDirectionCostText(AbilitySpot spot, string costText)
            {
                if (spot?.energyCost == null)
                {
                    return;
                }

                spot.energyCost.text = costText ?? string.Empty;
            }

            internal static void RestoreDefaultCostText(AbilitySpot spot)
            {
                if (spot?.energyCost == null)
                {
                    return;
                }

                var defaultCost = "0";
                var ability = spot.CurrentAbility;
                if (ability != null)
                {
                    defaultCost = Mathf.RoundToInt(ability.EnergyCost).ToString();
                }

                spot.energyCost.text = defaultCost;
            }
        }

        private static class SlotVisualOverrides
        {
            private static readonly Dictionary<Image, Color> s_cooldownIconBaseColors = new();
            private static readonly Dictionary<Image, float> s_chargeHoldRestoreFillAmounts = new();
            private static readonly Dictionary<Image, Color> s_chargeHoldRestoreColors = new();

            internal static void ApplyDirectionIcon(AbilitySpot spot, Sprite sprite)
            {
                if (spot == null || sprite == null)
                    return;

                SetImageSpriteAndEnable(spot.backgroundIcon, sprite);
                SetImageSpriteAndEnable(spot.cooldownIcon, sprite);
                if (spot.noAbility != null)
                {
                    spot.noAbility.enabled = false;
                }
            }

            internal static void RestoreDefaultIcon(AbilitySpot spot)
            {
                if (spot == null)
                    return;
                if (spot.gameObject == null)
                    return;

                var currentAbility = spot.CurrentAbility;
                try
                {
                    spot.SetIcon(currentAbility?.icon);
                }
                catch
                {
                    // AbilitySpot.SetIcon can throw during scene unload if UI refs are already torn down.
                    return;
                }

                if (spot.noAbility != null)
                {
                    spot.noAbility.enabled = currentAbility == null;
                }
            }

            internal static void ApplyDirectionActivationProgress(AbilitySpot spot, float progress01)
            {
                if (spot == null)
                    return;

                var cooldownImage = spot.cooldownIcon;
                if (cooldownImage == null)
                    return;

                if (!s_cooldownIconBaseColors.TryGetValue(cooldownImage, out var baseColor))
                {
                    baseColor = cooldownImage.color;
                    s_cooldownIconBaseColors[cooldownImage] = baseColor;
                }

                var clamped = Mathf.Clamp01(progress01);
                if (clamped <= 0f)
                {
                    cooldownImage.fillAmount = 0f;
                    cooldownImage.color = baseColor;
                    return;
                }

                // Reuse cooldown mask as "arming" fill (reverse of cooldown drain), but tint green.
                cooldownImage.fillAmount = clamped;
                cooldownImage.color = new Color(0.2f, 1f, 0.2f, baseColor.a);
                cooldownImage.enabled = true;
            }

            internal static void ApplyDirectionEnergyAvailability(AbilitySpot spot, bool hasEnoughEnergy, float progress01)
            {
                if (spot == null)
                    return;

                var cooldownImage = spot.cooldownIcon;
                if (cooldownImage == null)
                    return;

                if (!s_cooldownIconBaseColors.TryGetValue(cooldownImage, out var baseColor))
                {
                    baseColor = cooldownImage.color;
                    s_cooldownIconBaseColors[cooldownImage] = baseColor;
                }

                // Match DHH AbilityCooldown behavior:
                // - ready => fillAmount 1 and alpha 1
                // - not ready => fillAmount < 1 and alpha 0.3
                // Keep hold-progress visualization when progress > 0.
                var clampedProgress = Mathf.Clamp01(progress01);
                var fill = clampedProgress > 0f ? clampedProgress : (hasEnoughEnergy ? 1f : 0f);
                var newColor = baseColor;
                newColor.a = fill < 1f ? 0.3f : 1f;
                cooldownImage.fillAmount = fill;
                cooldownImage.color = newColor;
            }

            internal static void ApplyChargeActivationProgress(AbilitySpot spot, float progress01, bool canReleaseActivate)
            {
                if (spot == null)
                    return;

                var cooldownImage = spot.cooldownIcon;
                if (cooldownImage == null)
                    return;

                if (!s_cooldownIconBaseColors.TryGetValue(cooldownImage, out var baseColor))
                {
                    baseColor = cooldownImage.color;
                    s_cooldownIconBaseColors[cooldownImage] = baseColor;
                }

                var clamped = Mathf.Clamp01(progress01);
                if (clamped <= 0f)
                {
                    // Restore the visual state that existed before hold started
                    // (ready/cooling/energy-limited), instead of forcing "empty/off".
                    var restoredFill = false;
                    var restoredColor = false;
                    if (s_chargeHoldRestoreFillAmounts.TryGetValue(cooldownImage, out var restoreFill))
                    {
                        cooldownImage.fillAmount = restoreFill;
                        s_chargeHoldRestoreFillAmounts.Remove(cooldownImage);
                        restoredFill = true;
                    }

                    if (s_chargeHoldRestoreColors.TryGetValue(cooldownImage, out var restoreColor))
                    {
                        cooldownImage.color = restoreColor;
                        s_chargeHoldRestoreColors.Remove(cooldownImage);
                        restoredColor = true;
                    }

                    // Fallback: if no snapshot was captured (can happen on remote clients),
                    // force icon back to its default non-hold visual state.
                    if (!restoredFill)
                    {
                        cooldownImage.fillAmount = 1f;
                    }

                    if (!restoredColor)
                    {
                        cooldownImage.color = baseColor;
                    }
                    return;
                }

                if (!s_chargeHoldRestoreFillAmounts.ContainsKey(cooldownImage))
                {
                    s_chargeHoldRestoreFillAmounts[cooldownImage] = cooldownImage.fillAmount;
                }

                if (!s_chargeHoldRestoreColors.ContainsKey(cooldownImage))
                {
                    s_chargeHoldRestoreColors[cooldownImage] = cooldownImage.color;
                }

                // Slot1 charge hold: red while below minimum hold threshold, green when release will activate.
                var tint = canReleaseActivate
                    ? new Color(0.2f, 1f, 0.2f, baseColor.a)
                    : new Color(1f, 0.2f, 0.2f, baseColor.a);
                cooldownImage.fillAmount = clamped;
                cooldownImage.color = tint;
                cooldownImage.enabled = true;
            }

            private static void SetImageSpriteAndEnable(Image? image, Sprite sprite)
            {
                if (image == null || sprite == null)
                    return;

                image.sprite = sprite;
                image.enabled = true;
            }
        }
    }
}

