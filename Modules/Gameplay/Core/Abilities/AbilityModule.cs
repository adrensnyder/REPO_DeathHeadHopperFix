#nullable enable

using System;
using System.Collections.Generic;
using DeathHeadHopper.Abilities;
using DeathHeadHopperFix.API.Abilities;
using DeathHeadHopper.Managers;
using DeathHeadHopper.UI;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Spectate;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class AbilityModule
    {
        private static readonly HashSet<AbilitySpot> s_trackedSpots = new();
        private static readonly Dictionary<AbilitySpot, Vector3> s_spotBaseLocalPos = new();

        private const int ChargeAbilitySlotIndex = 0;
        private const string ProviderDemandPrefix = "DeathHeadHopperFix.AbilityProvider.";
        private static readonly Dictionary<string, ProviderRegistrationState> s_providerByOwner = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> s_providerOwnerBySlot = new();
        private static readonly HashSet<string> s_providerCollisionWarnings = new(StringComparer.Ordinal);
        private static readonly HashSet<string> s_providerCallbackWarnings = new(StringComparer.Ordinal);
        private static bool s_providerReconcileInProgress;

        internal static void ApplyAbilitySpotLabelOverlay(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(AbilitySpotStartPatch)).Patch();
            harmony.CreateClassProcessor(typeof(AbilitySpotUpdateUiPatch)).Patch();
        }

        internal static void ApplyAbilityManagerHooks(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(DhhAbilityManagerOnAbilityUsedPatch)).Patch();
        }

        [HarmonyPatch(typeof(AbilitySpot), nameof(AbilitySpot.Start))]
        private static class AbilitySpotStartPatch
        {
            [HarmonyPostfix]
            private static void Postfix(AbilitySpot __instance)
            {
                AbilitySpot_Start_Postfix(__instance);
            }
        }

        [HarmonyPatch(typeof(AbilitySpot), nameof(AbilitySpot.UpdateUI))]
        private static class AbilitySpotUpdateUiPatch
        {
            [HarmonyPostfix]
            private static void Postfix(AbilitySpot __instance)
            {
                AbilitySpot_UpdateUI_Postfix(__instance);
            }
        }

        [HarmonyPatch(typeof(DHHAbilityManager), nameof(DHHAbilityManager.OnAbilityUsed), typeof(AbilityBase))]
        private static class DhhAbilityManagerOnAbilityUsedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(DHHAbilityManager __instance, AbilityBase ability)
            {
                DHHAbilityManager_OnAbilityUsed_Postfix(__instance, ability);
            }
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
            ReconcileProviderForSpot(__instance);
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
            ReconcileProviderForSpot(__instance);
        }

        private static void AfterAbilitySpotUpdate(AbilitySpot __instance)
        {
            if (__instance == null || InternalDebugFlags.DisableAbilityPatches)
                return;

            var slotIndex = GetAbilityIndex(__instance);
            if (TryGetProviderForSlot(slotIndex, out var provider) && provider.State.Visible && IsProviderBoundToSpot(provider, __instance))
            {
                SlotLayoutOverrides.EnsureBasePosition(__instance);
            }
        }

        private static void AbilitySpot_OnDestroy(AbilitySpot spot)
        {
            if (spot == null)
                return;

            s_trackedSpots.Remove(spot);
            s_spotBaseLocalPos.Remove(spot);
            foreach (var provider in s_providerByOwner.Values)
            {
                if (ReferenceEquals(provider.BoundSpot, spot))
                {
                    provider.BoundSpot = null;
                }
            }
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

        internal static bool TryRegisterProvider(AbilitySlotRegistration registration)
        {
            if (!IsValidRegistration(registration, out var slotIndex))
            {
                return false;
            }

            if (s_providerByOwner.ContainsKey(registration.OwnerId))
            {
                return false;
            }

            if (s_providerOwnerBySlot.TryGetValue(slotIndex, out var existingOwner))
            {
                WarnProviderCollisionOnce(slotIndex, registration.OwnerId, existingOwner);
                return false;
            }

            var adapter = ScriptableObject.CreateInstance<ProviderAbilityAdapter>();
            var registrationSnapshot = new AbilitySlotRegistration
            {
                OwnerId = registration.OwnerId,
                Slot = registration.Slot,
                AbilityName = registration.AbilityName,
                Icon = registration.Icon,
                OnDown = registration.OnDown,
                OnHold = registration.OnHold,
                OnUp = registration.OnUp,
                OnCancel = registration.OnCancel
            };
            var provider = new ProviderRegistrationState(registrationSnapshot, adapter);
            adapter.Initialize(provider);
            adapter.icon = registration.Icon!;
            s_providerByOwner.Add(registration.OwnerId, provider);
            s_providerOwnerBySlot.Add(slotIndex, registration.OwnerId);
            ReconcileProvider(provider);
            return true;
        }

        internal static bool TryUpdateProvider(string ownerId, AbilitySlotState state)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || !s_providerByOwner.TryGetValue(ownerId, out var provider))
            {
                return false;
            }

            var wasInteractive = provider.State.Visible && provider.State.Available;
            state.ActivationProgress01 = Mathf.Clamp01(state.ActivationProgress01);
            provider.State = state;
            provider.Adapter.icon = (state.Icon ?? provider.Registration.Icon)!;

            if (provider.InputHeld && wasInteractive && (!state.Visible || !state.Available))
            {
                CancelProviderInput(provider);
            }

            ReconcileProvider(provider);
            return true;
        }

        internal static bool TryStartProviderCooldown(string ownerId, float seconds)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || !s_providerByOwner.TryGetValue(ownerId, out var provider))
            {
                return false;
            }

            var clamped = Mathf.Max(0f, seconds);
            var state = provider.State;
            state.ActivationProgress01 = 0f;
            provider.State = state;
            var spot = provider.BoundSpot;
            if (spot == null || !IsProviderBoundToSpot(provider, spot))
            {
                return clamped <= 0f;
            }

            ApplyProviderVisualState(provider, spot);
            if (clamped <= 0f)
            {
                return true;
            }

            try
            {
                spot.SetCooldown(clamped);
                return true;
            }
            catch
            {
                provider.BoundSpot = null;
                return false;
            }
        }

        internal static bool UnregisterProvider(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || !s_providerByOwner.TryGetValue(ownerId, out var provider))
            {
                return false;
            }

            CancelProviderInput(provider);
            AbilityBarVisibilityAnchor.SetExternalDemand(GetProviderDemandId(ownerId), false);

            var spot = provider.BoundSpot;
            if (spot != null && IsSpotUsable(spot) && ReferenceEquals(spot.CurrentAbility, provider.Adapter))
            {
                try
                {
                    spot.RemoveAbility();
                    RestoreProviderVisualState(spot);
                }
                catch
                {
                    // Scene teardown can invalidate UI references before provider cleanup runs.
                }
            }

            provider.BoundSpot = null;
            s_providerByOwner.Remove(ownerId);
            s_providerOwnerBySlot.Remove((int)provider.Registration.Slot);
            RemoveCollisionWarningsForOwner(ownerId);
            RemoveCallbackWarningsForOwner(ownerId);
            UnityEngine.Object.Destroy(provider.Adapter);
            return true;
        }

        private static bool IsValidRegistration(AbilitySlotRegistration? registration, out int slotIndex)
        {
            slotIndex = -1;
            if (registration == null || string.IsNullOrWhiteSpace(registration.OwnerId) || string.IsNullOrWhiteSpace(registration.AbilityName))
            {
                return false;
            }

            slotIndex = (int)registration.Slot;
            if (slotIndex != (int)ExtensibleAbilitySlot.Slot2 && slotIndex != (int)ExtensibleAbilitySlot.Slot3)
            {
                return false;
            }

            return registration.OnDown != null &&
                   registration.OnHold != null &&
                   registration.OnUp != null &&
                   registration.OnCancel != null;
        }

        private static void ReconcileProviderForSpot(AbilitySpot spot)
        {
            if (spot == null || s_providerReconcileInProgress)
            {
                return;
            }

            var slotIndex = GetAbilityIndex(spot);
            if (!TryGetProviderForSlot(slotIndex, out var provider))
            {
                return;
            }

            ReconcileProvider(provider, spot);
        }

        private static void ReconcileProvider(ProviderRegistrationState provider, AbilitySpot? preferredSpot = null)
        {
            if (provider == null || s_providerReconcileInProgress)
            {
                return;
            }

            s_providerReconcileInProgress = true;
            try
            {
                var spot = preferredSpot;
                if (spot == null || GetAbilityIndex(spot) != (int)provider.Registration.Slot)
                {
                    spot = FindTrackedSpot((int)provider.Registration.Slot);
                }

                if (spot == null || !IsSpotUsable(spot))
                {
                    provider.BoundSpot = null;
                    AbilityBarVisibilityAnchor.SetExternalDemand(GetProviderDemandId(provider.Registration.OwnerId), false);
                    return;
                }

                if (!provider.State.Visible)
                {
                    if (ReferenceEquals(spot.CurrentAbility, provider.Adapter))
                    {
                        spot.RemoveAbility();
                        RestoreProviderVisualState(spot);
                    }
                    provider.BoundSpot = null;
                    AbilityBarVisibilityAnchor.SetExternalDemand(GetProviderDemandId(provider.Registration.OwnerId), false);
                    return;
                }

                if (spot.CurrentAbility == null)
                {
                    spot.EquipAbility(provider.Adapter);
                }
                else if (!ReferenceEquals(spot.CurrentAbility, provider.Adapter))
                {
                    WarnOccupiedSpotOnce(provider, spot);
                    provider.BoundSpot = null;
                    AbilityBarVisibilityAnchor.SetExternalDemand(GetProviderDemandId(provider.Registration.OwnerId), false);
                    return;
                }

                provider.BoundSpot = spot;
                AbilityBarVisibilityAnchor.SetExternalDemand(GetProviderDemandId(provider.Registration.OwnerId), true);
                ApplyProviderVisualState(provider, spot);
            }
            catch
            {
                provider.BoundSpot = null;
                AbilityBarVisibilityAnchor.SetExternalDemand(GetProviderDemandId(provider.Registration.OwnerId), false);
            }
            finally
            {
                s_providerReconcileInProgress = false;
            }
        }

        private static AbilitySpot? FindTrackedSpot(int slotIndex)
        {
            foreach (var spot in s_trackedSpots)
            {
                if (IsSpotUsable(spot) && GetAbilityIndex(spot) == slotIndex)
                {
                    return spot;
                }
            }

            return null;
        }

        private static bool TryGetProviderForSlot(int slotIndex, out ProviderRegistrationState provider)
        {
            provider = null!;
            return s_providerOwnerBySlot.TryGetValue(slotIndex, out var ownerId) &&
                   s_providerByOwner.TryGetValue(ownerId, out provider);
        }

        private static bool IsProviderBoundToSpot(ProviderRegistrationState provider, AbilitySpot spot)
        {
            return provider != null && spot != null && ReferenceEquals(provider.BoundSpot, spot) && ReferenceEquals(spot.CurrentAbility, provider.Adapter);
        }

        private static void ApplyProviderVisualState(ProviderRegistrationState provider, AbilitySpot spot)
        {
            if (provider == null || spot == null)
            {
                return;
            }

            var state = provider.State;
            var icon = state.Icon != null ? state.Icon : provider.Registration.Icon;
            if (icon != null)
            {
                SlotVisualOverrides.ApplyProviderIcon(spot, icon);
            }
            else
            {
                SlotVisualOverrides.RestoreDefaultIcon(spot);
            }

            SlotCostOverrides.SetProviderCostText(spot, state.Label ?? string.Empty);
            AbilitySpotLabelOverlay.SetProviderLabel(spot, string.Empty);
            SlotVisualOverrides.ApplyProviderActivationProgress(spot, state.ActivationProgress01);
            SlotVisualOverrides.ApplyProviderAvailability(spot, state.Available, state.ActivationProgress01);
            SlotLayoutOverrides.EnsureBasePosition(spot);
        }

        private static void RestoreProviderVisualState(AbilitySpot spot)
        {
            if (spot == null)
            {
                return;
            }

            AbilitySpotLabelOverlay.UpdateLabel(spot);
            SlotCostOverrides.RestoreDefaultCostText(spot);
            SlotVisualOverrides.RestoreDefaultIcon(spot);
            SlotVisualOverrides.ApplyProviderActivationProgress(spot, 0f);
            SlotLayoutOverrides.RestoreBasePosition(spot);
        }

        private static void InvokeProviderCallback(ProviderRegistrationState provider, string callbackName, Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                var key = provider.Registration.OwnerId + ":" + callbackName;
                if (s_providerCallbackWarnings.Add(key))
                {
                    Debug.LogError($"[Fix:Ability] Provider '{provider.Registration.OwnerId}' callback '{callbackName}' failed: {ex.Message}");
                }
            }
        }

        private static void CancelProviderInput(ProviderRegistrationState provider)
        {
            if (provider == null || !provider.InputHeld)
            {
                return;
            }

            provider.InputHeld = false;
            InvokeProviderCallback(provider, "Cancel", provider.Registration.OnCancel!);
        }

        private static string GetProviderDemandId(string ownerId)
        {
            return ProviderDemandPrefix + ownerId;
        }

        private static void WarnProviderCollisionOnce(int slotIndex, string ownerId, string existingOwner)
        {
            var key = slotIndex + ":" + ownerId;
            if (s_providerCollisionWarnings.Add(key))
            {
                Debug.LogWarning($"[Fix:Ability] Provider '{ownerId}' cannot reserve slot index {slotIndex}; it is owned by '{existingOwner}'.");
            }
        }

        private static void WarnOccupiedSpotOnce(ProviderRegistrationState provider, AbilitySpot spot)
        {
            var slotIndex = (int)provider.Registration.Slot;
            var key = slotIndex + ":" + provider.Registration.OwnerId + ":occupied";
            if (s_providerCollisionWarnings.Add(key))
            {
                var occupiedBy = spot.CurrentAbility?.AbilityName ?? "unknown ability";
                Debug.LogWarning($"[Fix:Ability] Provider '{provider.Registration.OwnerId}' is registered for slot index {slotIndex}, but the spot is occupied by '{occupiedBy}'. The existing ability is preserved.");
            }
        }

        private static void RemoveCollisionWarningsForOwner(string ownerId)
        {
            s_providerCollisionWarnings.RemoveWhere(key => key.EndsWith(":" + ownerId, StringComparison.Ordinal));
        }

        private static void RemoveCallbackWarningsForOwner(string ownerId)
        {
            s_providerCallbackWarnings.RemoveWhere(key => key.StartsWith(ownerId + ":", StringComparison.Ordinal));
        }

        private sealed class ProviderRegistrationState
        {
            internal ProviderRegistrationState(AbilitySlotRegistration registration, ProviderAbilityAdapter adapter)
            {
                Registration = registration;
                Adapter = adapter;
                State = new AbilitySlotState
                {
                    Visible = false,
                    Available = false,
                    ActivationProgress01 = 0f,
                    Label = string.Empty,
                    Icon = null
                };
            }

            internal AbilitySlotRegistration Registration { get; }
            internal ProviderAbilityAdapter Adapter { get; }
            internal AbilitySlotState State { get; set; }
            internal AbilitySpot? BoundSpot { get; set; }
            internal bool InputHeld { get; set; }
        }

        private sealed class ProviderAbilityAdapter : AbilityBase
        {
            private ProviderRegistrationState? _provider;

            internal void Initialize(ProviderRegistrationState provider)
            {
                _provider = provider;
            }

            public override string AbilityName => _provider?.Registration.AbilityName ?? string.Empty;
            public override float Cooldown => 0f;
            public override float EnergyCost => 0f;
            public override int AbilityLevel => 1;

            public override void OnAbilityDown()
            {
                var provider = _provider;
                if (provider == null || !provider.State.Visible || !provider.State.Available || provider.InputHeld)
                {
                    return;
                }

                provider.InputHeld = true;
                InvokeProviderCallback(provider, "Down", provider.Registration.OnDown!);
            }

            public override void OnAbilityHold()
            {
                var provider = _provider;
                if (provider == null || !provider.InputHeld || !provider.State.Visible || !provider.State.Available)
                {
                    return;
                }

                InvokeProviderCallback(provider, "Hold", provider.Registration.OnHold!);
            }

            public override void OnAbilityUp()
            {
                var provider = _provider;
                if (provider == null || !provider.InputHeld)
                {
                    return;
                }

                provider.InputHeld = false;
                if (!provider.State.Visible || !provider.State.Available)
                {
                    return;
                }

                InvokeProviderCallback(provider, "Up", provider.Registration.OnUp!);
            }

            public override void OnAbilityCancel()
            {
                var provider = _provider;
                if (provider == null)
                {
                    return;
                }

                CancelProviderInput(provider);
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

            internal static void SetProviderLabel(AbilitySpot spot, string text)
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
            internal static void SetProviderCostText(AbilitySpot spot, string costText)
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

            internal static void ApplyProviderIcon(AbilitySpot spot, Sprite sprite)
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

            internal static void ApplyProviderActivationProgress(AbilitySpot spot, float progress01)
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

            internal static void ApplyProviderAvailability(AbilitySpot spot, bool hasEnoughEnergy, float progress01)
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
