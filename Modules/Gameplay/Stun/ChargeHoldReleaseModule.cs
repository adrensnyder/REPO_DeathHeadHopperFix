#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Stun
{
    internal static class ChargeHoldReleaseModule
    {
        private const string ChargeStrengthLogKey = "Fix:Charge.Strength";

        private static ManualLogSource? s_log;
        private static FieldInfo? s_chargeHandlerChargeStrengthField;
        private static MethodInfo? s_chargeHandlerAbilityLevelGetter;
        private static FieldInfo? s_chargeHandlerImpactDetectorField;
        private static FieldInfo? s_chargeHandlerControllerField;
        private static FieldInfo? s_chargeHandlerMaxBouncesField;
        private static FieldInfo? s_chargeHandlerWindupTimerField;
        private static FieldInfo? s_chargeHandlerWindupTimeField;
        private static FieldInfo? s_chargeHandlerEnemiesHitField;
        private static MethodInfo? s_chargeHandlerEndChargeMethod;
        private static MethodInfo? s_chargeHandlerCancelChargeMethod;
        private static MethodInfo? s_chargeHandlerStateGetter;
        private static FieldInfo? s_deathHeadControllerAudioHandlerField;
        private static MethodInfo? s_audioHandlerStopWindupMethod;
        private static FieldInfo? s_impactDetectorPhysGrabObjectField;
        private static FieldInfo? s_cachedPhysGrabObjectGrabbedField;
        private static MethodInfo? s_chargeAbilityOnAbilityUpPrefixMethod;
        private static MethodInfo? s_stunHandlerStunDurationGetter;
        private static FieldInfo? s_stunHandlerChargeHandlerField;
        private static readonly Dictionary<int, ChargeHoldState> s_chargeHoldStates = new();

        private sealed class ChargeHoldState
        {
            public float StartTime;
            public bool IsHolding;
            public float LaunchScale = 1f;
        }

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            s_log = log;
            PatchChargeHandlerDamageModeIfPossible(harmony, asm);
            PatchChargeAbilityHoldReleaseIfPossible(harmony, asm);
            PatchStunHandlerHoldScalingIfPossible(harmony, asm);
        }

        private static void PatchChargeHandlerDamageModeIfPossible(Harmony harmony, Assembly asm)
        {
            var chargeHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.ChargeHandler", throwOnError: false);
            if (chargeHandlerType == null)
                return;

            var mWindup = AccessTools.Method(chargeHandlerType, "ChargeWindup", new[] { typeof(Vector3) });
            var windupPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ChargeWindup_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPrefix != null)
                harmony.Patch(mWindup, prefix: new HarmonyMethod(windupPrefix));
            var windupPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ChargeWindup_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPostfix != null)
                harmony.Patch(mWindup, postfix: new HarmonyMethod(windupPostfix));

            var mReset = AccessTools.Method(chargeHandlerType, "ResetState", Type.EmptyTypes);
            var mFixedUpdate = AccessTools.Method(chargeHandlerType, "FixedUpdate", Type.EmptyTypes);
            var mCancelCharge = AccessTools.Method(chargeHandlerType, "CancelCharge", Type.EmptyTypes);
            var mEnemyHit = AccessTools.Method(chargeHandlerType, "EnemyHit");
            if (s_chargeHandlerChargeStrengthField == null)
            {
                s_chargeHandlerChargeStrengthField = AccessTools.Field(chargeHandlerType, "chargeStrength");
            }
            if (s_chargeHandlerAbilityLevelGetter == null)
            {
                s_chargeHandlerAbilityLevelGetter = AccessTools.PropertyGetter(chargeHandlerType, "AbilityLevel");
            }
            if (s_chargeHandlerControllerField == null)
            {
                s_chargeHandlerControllerField = AccessTools.Field(chargeHandlerType, "controller");
            }
            s_chargeHandlerMaxBouncesField ??= AccessTools.Field(chargeHandlerType, "maxBounces");
            s_chargeHandlerWindupTimerField ??= AccessTools.Field(chargeHandlerType, "windupTimer");
            s_chargeHandlerWindupTimeField ??= AccessTools.Field(chargeHandlerType, "windupTime");
            s_chargeHandlerEnemiesHitField ??= AccessTools.Field(chargeHandlerType, "enemiesHit");
            s_chargeHandlerEndChargeMethod ??= AccessTools.Method(chargeHandlerType, "EndCharge", Type.EmptyTypes);
            s_chargeHandlerCancelChargeMethod ??= AccessTools.Method(chargeHandlerType, "CancelCharge", Type.EmptyTypes);
            s_chargeHandlerStateGetter ??= AccessTools.PropertyGetter(chargeHandlerType, "State");
            var resetPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ResetState_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mReset != null && resetPostfix != null)
                harmony.Patch(mReset, postfix: new HarmonyMethod(resetPostfix));
            var mEndCharge = AccessTools.Method(chargeHandlerType, "EndCharge", Type.EmptyTypes);
            var endChargePostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_EndCharge_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mEndCharge != null && endChargePostfix != null)
                harmony.Patch(mEndCharge, postfix: new HarmonyMethod(endChargePostfix));
            var fixedUpdatePostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_FixedUpdate_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mFixedUpdate != null && fixedUpdatePostfix != null)
                harmony.Patch(mFixedUpdate, postfix: new HarmonyMethod(fixedUpdatePostfix));
            var cancelPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_CancelCharge_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mCancelCharge != null && cancelPostfix != null)
                harmony.Patch(mCancelCharge, postfix: new HarmonyMethod(cancelPostfix));
            var enemyHitPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_EnemyHit_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mEnemyHit != null && enemyHitPrefix != null)
                harmony.Patch(mEnemyHit, prefix: new HarmonyMethod(enemyHitPrefix));
        }

        private static bool ChargeHandler_ChargeWindup_Prefix(object __instance)
        {
            if (__instance == null)
                return true;

            if (IsChargeHandlerHeadGrabbed(__instance))
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:Charge.Grabbed", 30))
                {
                    Debug.Log("[Fix:Charge] Charge windup blocked because the head is grabbed.");
                }
                return false;
            }

            return true;
        }

        private static void ChargeHandler_ChargeWindup_Postfix(object __instance)
        {
            if (__instance == null)
                return;

            if (!IsChargeState(__instance, "Windup"))
                return;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0)
                return;

            var state = GetOrCreateChargeHoldState(id);
            state.StartTime = Time.time;
            state.IsHolding = true;
            state.LaunchScale = 1f;
            AbilityModule.SetChargeSlotActivationProgress(0f);
        }

        private static void ChargeHandler_FixedUpdate_Postfix(object __instance)
        {
            if (__instance == null)
                return;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var state))
                return;

            if (!IsChargeState(__instance, "Windup"))
                return;

            var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
            var progress = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
            var requiredScale = GetMinimumChargeReleaseScale(__instance);
            AbilityModule.SetChargeSlotActivationProgress(progress, requiredScale);

            if (!state.IsHolding)
                return;

            if (s_chargeHandlerWindupTimerField != null && s_chargeHandlerWindupTimeField != null)
            {
                var windupTime = s_chargeHandlerWindupTimeField.GetValue(__instance) is float wt ? wt : 1.8f;
                s_chargeHandlerWindupTimerField.SetValue(__instance, Mathf.Max(0.01f, windupTime));
            }
        }

        private static void PatchChargeAbilityHoldReleaseIfPossible(Harmony harmony, Assembly asm)
        {
            var tChargeAbility = asm.GetType("DeathHeadHopper.Abilities.Charge.ChargeAbility", throwOnError: false);
            if (tChargeAbility == null)
                return;

            var mOnAbilityUp = AccessTools.Method(tChargeAbility, "OnAbilityUp", Type.EmptyTypes);
            if (mOnAbilityUp == null)
                return;

            s_chargeAbilityOnAbilityUpPrefixMethod ??= typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityUp_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (s_chargeAbilityOnAbilityUpPrefixMethod == null)
                return;

            harmony.Patch(mOnAbilityUp, prefix: new HarmonyMethod(s_chargeAbilityOnAbilityUpPrefixMethod));
        }

        private static void PatchStunHandlerHoldScalingIfPossible(Harmony harmony, Assembly asm)
        {
            var tStunHandler = asm.GetType("DeathHeadHopper.DeathHead.Handlers.StunHandler", throwOnError: false);
            if (tStunHandler == null)
                return;

            s_stunHandlerChargeHandlerField ??= AccessTools.Field(tStunHandler, "chargeHandler");
            s_stunHandlerStunDurationGetter ??= AccessTools.PropertyGetter(tStunHandler, "StunDuration");
            if (s_stunHandlerStunDurationGetter == null)
                return;

            var prefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(StunHandler_StunDuration_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(s_stunHandlerStunDurationGetter, prefix: new HarmonyMethod(prefix));
        }

        private static void ChargeHandler_CancelCharge_Postfix(object __instance)
        {
            ClearChargeHoldState(__instance);
        }

        private static bool ChargeHandler_EnemyHit_Prefix(object __instance)
        {
            if (__instance == null)
                return true;
            if (s_chargeHandlerEnemiesHitField == null || s_chargeHandlerEndChargeMethod == null || s_chargeHandlerAbilityLevelGetter == null)
                return true;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var holdState))
                return true;

            if (s_chargeHandlerEnemiesHitField.GetValue(__instance) is not int enemiesHit)
                return true;

            enemiesHit++;
            s_chargeHandlerEnemiesHitField.SetValue(__instance, enemiesHit);

            var levelObj = s_chargeHandlerAbilityLevelGetter.Invoke(__instance, null);
            var abilityLevel = levelObj is int v ? v : 0;
            var vanillaMax = Mathf.FloorToInt(EvaluateStatWithDiminishingReturns(1f, 0.5f, abilityLevel, 20, 0.9f).FinalValue);
            var scaledMax = Mathf.Max(1, Mathf.RoundToInt(vanillaMax * Mathf.Clamp01(holdState.LaunchScale)));
            if (enemiesHit >= scaledMax)
            {
                s_chargeHandlerEndChargeMethod.Invoke(__instance, null);
            }

            return false;
        }

        private static bool StunHandler_StunDuration_Prefix(object __instance, ref float __result)
        {
            if (__instance == null)
                return true;
            if (s_stunHandlerChargeHandlerField == null || s_chargeHandlerAbilityLevelGetter == null)
                return true;

            var chargeHandler = s_stunHandlerChargeHandlerField.GetValue(__instance);
            if (chargeHandler == null)
                return true;

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var holdState))
                return true;

            var levelObj = s_chargeHandlerAbilityLevelGetter.Invoke(chargeHandler, null);
            var abilityLevel = levelObj is int v ? v : 0;
            var vanillaStun = 5f + (1f * abilityLevel);
            __result = vanillaStun * Mathf.Clamp01(holdState.LaunchScale);
            return false;
        }

        private static bool ChargeAbility_OnAbilityUp_Prefix()
        {
            return TryReleaseHeldCharge();
        }

        private static bool TryReleaseHeldCharge()
        {
            var chargeHandler = GetLocalChargeHandler();
            if (chargeHandler == null)
                return true;

            if (!IsChargeState(chargeHandler, "Windup"))
                return true;

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var state))
                return true;
            if (!state.IsHolding)
                return true;

            var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
            var scale = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
            var requiredScale = GetMinimumChargeReleaseScale(chargeHandler);
            if (scale < requiredScale)
            {
                state.IsHolding = false;
                state.LaunchScale = 0f;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                StopChargeWindupLoop(chargeHandler);
                s_chargeHandlerCancelChargeMethod?.Invoke(chargeHandler, null);
                return false;
            }

            state.IsHolding = false;
            state.LaunchScale = scale;

            if (s_chargeHandlerChargeStrengthField != null && s_chargeHandlerChargeStrengthField.GetValue(chargeHandler) is float chargeStrength)
            {
                s_chargeHandlerChargeStrengthField.SetValue(chargeHandler, chargeStrength * scale);
            }

            if (s_chargeHandlerMaxBouncesField != null && s_chargeHandlerMaxBouncesField.GetValue(chargeHandler) is float maxBounces)
            {
                s_chargeHandlerMaxBouncesField.SetValue(chargeHandler, Mathf.Max(0f, maxBounces * scale));
            }

            if (s_chargeHandlerWindupTimerField != null)
            {
                s_chargeHandlerWindupTimerField.SetValue(chargeHandler, -1f);
            }

            AbilityModule.SetChargeSlotActivationProgress(0f);
            return true;
        }

        private static float GetMinimumChargeReleaseScale(object chargeHandler)
        {
            if (chargeHandler == null)
                return 0f;

            var required = 0f;

            if (s_chargeHandlerChargeStrengthField?.GetValue(chargeHandler) is float chargeStrength)
            {
                required = Mathf.Max(required, RequiredScaleForMinimumOne(chargeStrength));
            }

            if (s_chargeHandlerMaxBouncesField?.GetValue(chargeHandler) is float maxBounces)
            {
                required = Mathf.Max(required, RequiredScaleForMinimumOne(maxBounces));
            }

            if (s_chargeHandlerAbilityLevelGetter != null)
            {
                var levelObj = s_chargeHandlerAbilityLevelGetter.Invoke(chargeHandler, null);
                var abilityLevel = levelObj is int v ? v : 0;
                var enemiesBase = Mathf.FloorToInt(EvaluateStatWithDiminishingReturns(1f, 0.5f, abilityLevel, 20, 0.9f).FinalValue);
                var stunBase = 5f + (1f * abilityLevel);

                required = Mathf.Max(required, RequiredScaleForMinimumOne(enemiesBase));
                required = Mathf.Max(required, RequiredScaleForMinimumOne(stunBase));
            }

            if (float.IsNaN(required) || float.IsInfinity(required))
                return 1f;

            return Mathf.Clamp01(required);
        }

        private static float RequiredScaleForMinimumOne(float baseValue)
        {
            if (baseValue <= 0f)
                return float.PositiveInfinity;
            return 1f / baseValue;
        }

        private static bool IsChargeHandlerHeadGrabbed(object chargeHandler)
        {
            var impactDetector = GetChargeImpactDetector(chargeHandler);
            if (impactDetector == null)
                return false;

            var physGrabObject = GetImpactPhysGrabObject(impactDetector);
            if (physGrabObject == null)
                return false;

            var grabbedField = s_cachedPhysGrabObjectGrabbedField;
            if (grabbedField == null || grabbedField.DeclaringType != physGrabObject.GetType())
            {
                grabbedField = AccessTools.Field(physGrabObject.GetType(), "grabbed");
                s_cachedPhysGrabObjectGrabbedField = grabbedField;
            }

            return grabbedField != null && grabbedField.GetValue(physGrabObject) is bool grabbed && grabbed;
        }

        private static int GetUnityObjectInstanceId(object obj)
        {
            return obj is UnityEngine.Object unityObj ? unityObj.GetInstanceID() : 0;
        }

        private static ChargeHoldState GetOrCreateChargeHoldState(int id)
        {
            if (!s_chargeHoldStates.TryGetValue(id, out var state))
            {
                state = new ChargeHoldState();
                s_chargeHoldStates[id] = state;
            }

            return state;
        }

        private static bool IsChargeState(object chargeHandler, string stateName)
        {
            if (chargeHandler == null)
                return false;

            if (s_chargeHandlerStateGetter == null || s_chargeHandlerStateGetter.DeclaringType != chargeHandler.GetType())
            {
                s_chargeHandlerStateGetter = AccessTools.PropertyGetter(chargeHandler.GetType(), "State");
            }

            if (s_chargeHandlerStateGetter == null)
                return false;

            var stateValue = s_chargeHandlerStateGetter.Invoke(chargeHandler, null);
            return stateValue != null && string.Equals(stateValue.ToString(), stateName, StringComparison.Ordinal);
        }

        private static object? GetLocalChargeHandler()
        {
            var avatar = PlayerAvatar.instance;
            if (avatar?.playerDeathHead == null)
                return null;

            var controller = avatar.playerDeathHead.GetComponent("DeathHeadController");
            if (controller == null)
                return null;

            var chargeHandlerField = AccessTools.Field(controller.GetType(), "chargeHandler");
            return chargeHandlerField?.GetValue(controller);
        }

        private static void ClearChargeHoldState(object? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id != 0)
            {
                s_chargeHoldStates.Remove(id);
            }
            AbilityModule.SetChargeSlotActivationProgress(0f);
        }

        private static object? GetChargeImpactDetector(object chargeHandler)
        {
            var field = s_chargeHandlerImpactDetectorField;
            if (field == null || field.DeclaringType != chargeHandler.GetType())
            {
                field = AccessTools.Field(chargeHandler.GetType(), "impactDetector");
                s_chargeHandlerImpactDetectorField = field;
            }

            return field?.GetValue(chargeHandler);
        }

        private static object? GetImpactPhysGrabObject(object impactDetector)
        {
            if (impactDetector == null)
                return null;

            var field = s_impactDetectorPhysGrabObjectField;
            if (field == null || field.DeclaringType != impactDetector.GetType())
            {
                field = AccessTools.Field(impactDetector.GetType(), "physGrabObject");
                s_impactDetectorPhysGrabObjectField = field;
            }

            return field?.GetValue(impactDetector);
        }

        private static void ChargeHandler_ResetState_Postfix(object __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            if (__instance == null || s_chargeHandlerChargeStrengthField == null || s_chargeHandlerAbilityLevelGetter == null)
                return;

            var levelObj = s_chargeHandlerAbilityLevelGetter.Invoke(__instance, null);
            var level = levelObj is int value ? value : 0;

            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHChargeStrengthBaseValue,
                FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                level,
                FeatureFlags.DHHChargeStrengthThresholdLevel,
                FeatureFlags.DHHChargeStrengthDiminishingFactor);

            s_chargeHandlerChargeStrengthField.SetValue(__instance, stat.FinalValue);
            LogChargeStrength(__instance, stat);
        }

        private static void ChargeHandler_EndCharge_Postfix(object __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
        }

        private static void StopChargeWindupLoop(object? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            try
            {
                var controllerField = s_chargeHandlerControllerField ??= AccessTools.Field(chargeHandler.GetType(), "controller");
                if (controllerField == null)
                    return;

                var controller = controllerField.GetValue(chargeHandler);
                if (controller == null)
                    return;

                var audioField = s_deathHeadControllerAudioHandlerField ??= AccessTools.Field(controller.GetType(), "audioHandler");
                if (audioField == null)
                    return;

                var audioHandler = audioField.GetValue(controller);
                if (audioHandler == null)
                    return;

                var stopMethod = s_audioHandlerStopWindupMethod ??= AccessTools.Method(audioHandler.GetType(), "StopWindupSound", Type.EmptyTypes);
                stopMethod?.Invoke(audioHandler, null);
            }
            catch
            {
                // Audio stop is cosmetic; failures must not affect stun/charge logic.
            }
        }

        private static DiminishingReturnsResult EvaluateStatWithDiminishingReturns(float baseValue, float increasePerLevel, int currentLevel, int thresholdLevel, float diminishingFactor)
        {
            var appliedLevel = currentLevel;
            var normalizedLevel = Math.Max(0, appliedLevel - 1);
            var normalizedThreshold = Math.Max(0, thresholdLevel - 1);
            var linearLevels = Mathf.Min(normalizedLevel, normalizedThreshold);
            var extraLevels = Mathf.Max(0, normalizedLevel - normalizedThreshold);
            var diminishingComponent = extraLevels * Mathf.Pow(diminishingFactor, extraLevels);
            var linearContribution = increasePerLevel * linearLevels;
            var diminishingContribution = increasePerLevel * diminishingComponent;
            var finalValue = baseValue + linearContribution + diminishingContribution;

            return new DiminishingReturnsResult(
                baseValue,
                increasePerLevel,
                appliedLevel,
                thresholdLevel,
                diminishingFactor,
                linearLevels,
                extraLevels,
                linearContribution,
                diminishingContribution,
                diminishingComponent,
                finalValue);
        }

        private readonly struct DiminishingReturnsResult
        {
            public DiminishingReturnsResult(float baseValue, float increasePerLevel, int appliedLevel, int thresholdLevel, float diminishingFactor,
                int linearLevels, int extraLevels, float linearContribution, float diminishingContribution, float diminishingComponent, float finalValue)
            {
                BaseValue = baseValue;
                IncreasePerLevel = increasePerLevel;
                AppliedLevel = appliedLevel;
                ThresholdLevel = thresholdLevel;
                DiminishingFactor = diminishingFactor;
                LinearLevels = linearLevels;
                ExtraLevels = extraLevels;
                LinearContribution = linearContribution;
                DiminishingContribution = diminishingContribution;
                DiminishingComponent = diminishingComponent;
                FinalValue = finalValue;
            }

            public float BaseValue { get; }
            public float IncreasePerLevel { get; }
            public int AppliedLevel { get; }
            public int ThresholdLevel { get; }
            public float DiminishingFactor { get; }
            public int LinearLevels { get; }
            public int ExtraLevels { get; }
            public float LinearContribution { get; }
            public float DiminishingContribution { get; }
            public float DiminishingComponent { get; }
            public float FinalValue { get; }
        }

        private static string GetHandlerLabel(object? handler, string fallback)
        {
            if (handler is Component component)
            {
                return component.name ?? component.GetType().Name;
            }

            return handler?.GetType().Name ?? fallback;
        }

        private static void LogChargeStrength(object chargeHandler, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(ChargeStrengthLogKey, 60))
                return;

            var label = GetHandlerLabel(chargeHandler, "ChargeHandler");
            var message = $"[Fix:Charge] {label} Strength={stat.FinalValue:F3} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} level={stat.AppliedLevel} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            s_log?.LogInfo(message);
            Debug.Log(message);
        }

        private static void TryImpactEffect(object impactDetector, Vector3 contactPoint)
        {
            if (impactDetector == null)
                return;

            try
            {
                if (!SemiFunc.IsMultiplayer())
                {
                    var method = AccessTools.Method(impactDetector.GetType(), "ImpactEffectRPC");
                    if (method != null)
                    {
                        var pars = method.GetParameters();
                        if (pars.Length == 2)
                        {
                            var info = Activator.CreateInstance(pars[1].ParameterType);
                            method.Invoke(impactDetector, new object?[] { contactPoint, info });
                            return;
                        }
                    }
                }

                var photonViewField = AccessTools.Field(impactDetector.GetType(), "photonView");
                if (photonViewField?.GetValue(impactDetector) is PhotonView pv)
                    pv.RPC("ImpactEffectRPC", 0, new object[] { contactPoint });
            }
            catch
            {
                // Visual impact RPC is non-critical; gameplay state already applied locally.
            }
        }

        private static Vector3 CalculateEnemyBounceNormal(Transform? self, Vector3 enemyCenterPoint)
        {
            if (self == null)
                return Vector3.up;

            var frontPoint = self.TransformPoint(Vector3.up * 0.3f);
            var directionVector = frontPoint - enemyCenterPoint;
            return Vector3.ProjectOnPlane(directionVector, Vector3.up).normalized;
        }
    }
}
