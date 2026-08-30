#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopper.Abilities.Charge;
using DeathHeadHopper.DeathHead;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Managers;
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
        private const string ChargePermissiveFallbackLogKey = "Fix:Charge.PermissiveFallback";
        private const float RemoteReleaseCommandTag = -777f;
        private const float RemoteCancelCommandTag = -778f;

        private static ManualLogSource? s_log;
        private static readonly Dictionary<int, ChargeHoldState> s_chargeHoldStates = new();
        private static float s_lastLocalHoldInputStartTime;
        private static bool s_localHoldUiActive;
        private static bool s_localHoldInputPending;

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
            var mWindup = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.ChargeWindup), new[] { typeof(Vector3) });
            var windupPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ChargeWindup_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPrefix != null)
                harmony.Patch(mWindup, prefix: new HarmonyMethod(windupPrefix));
            var windupPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ChargeWindup_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPostfix != null)
                harmony.Patch(mWindup, postfix: new HarmonyMethod(windupPostfix));

            var mReset = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.ResetState), Type.EmptyTypes);
            var mFixedUpdate = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.FixedUpdate), Type.EmptyTypes);
            var mCancelCharge = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.CancelCharge), Type.EmptyTypes);
            var mEnemyHit = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.EnemyHit));
            var mUpdateWindupDirection = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.UpdateWindupDirection), new[] { typeof(Vector3) });
            var mSyncChargeState = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.SyncChargeStateRPC));
            var syncChargeStatePrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_SyncChargeStateRPC_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            var resetPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_ResetState_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mReset != null && resetPostfix != null)
                harmony.Patch(mReset, postfix: new HarmonyMethod(resetPostfix));
            var mEndCharge = AccessTools.Method(typeof(ChargeHandler), nameof(ChargeHandler.EndCharge), Type.EmptyTypes);
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
            var updateWindupDirectionPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_UpdateWindupDirection_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mUpdateWindupDirection != null && updateWindupDirectionPrefix != null)
                harmony.Patch(mUpdateWindupDirection, prefix: new HarmonyMethod(updateWindupDirectionPrefix));
            var syncChargeStatePostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeHandler_SyncChargeStateRPC_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mSyncChargeState != null)
            {
                if (syncChargeStatePrefix != null)
                    harmony.Patch(mSyncChargeState, prefix: new HarmonyMethod(syncChargeStatePrefix));
                if (syncChargeStatePostfix != null)
                    harmony.Patch(mSyncChargeState, postfix: new HarmonyMethod(syncChargeStatePostfix));
            }
        }

        private static bool ChargeHandler_ChargeWindup_Prefix(ChargeHandler __instance)
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

        private static void ChargeHandler_ChargeWindup_Postfix(ChargeHandler __instance)
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
            s_localHoldUiActive = true;
            AbilityModule.SetChargeSlotActivationProgress(0f);
        }

        private static void ChargeHandler_FixedUpdate_Postfix(ChargeHandler __instance)
        {
            if (__instance == null)
                return;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0)
                return;

            if (!IsLocalChargeHandler(__instance))
            {
                if (!s_chargeHoldStates.TryGetValue(id, out var remoteState))
                    return;

                if (!IsChargeState(__instance, "Windup"))
                {
                    s_chargeHoldStates.Remove(id);
                    return;
                }

                // Keep authoritative windup open until explicit release/cancel command arrives.
                if (remoteState.IsHolding)
                {
                    __instance.windupTimer = Mathf.Max(0.01f, __instance.windupTime);
                }
                return;
            }

            if (!s_chargeHoldStates.TryGetValue(id, out var state))
            {
                // Non-host clients do not execute ChargeWindup locally (RPC goes to master).
                // Bootstrap local hold preview when authoritative state sync reaches Windup.
                if (IsChargeState(__instance, "Windup") && s_localHoldInputPending)
                {
                    state = GetOrCreateChargeHoldState(id);
                    state.StartTime = s_lastLocalHoldInputStartTime > 0f ? s_lastLocalHoldInputStartTime : Time.time;
                    state.IsHolding = true;
                    state.LaunchScale = 1f;
                    s_localHoldUiActive = true;
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                }
                else
                {
                    if (!s_localHoldUiActive)
                        AbilityModule.SetChargeSlotActivationProgress(0f);
                    return;
                }
            }

            if (!IsChargeState(__instance, "Windup"))
            {
                s_chargeHoldStates.Remove(id);
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                if (!s_localHoldUiActive)
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                return;
            }

            if (s_localHoldUiActive && state.IsHolding)
            {
                var holdSecondsRemote = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
                var progressRemote = Mathf.Clamp01((Time.time - state.StartTime) / holdSecondsRemote);
                var requiredScaleRemote = GetMinimumChargeReleaseScale(__instance);
                AbilityModule.SetChargeSlotActivationProgress(progressRemote, requiredScaleRemote);
            }
            else if (!state.IsHolding)
            {
                if (!s_localHoldUiActive)
                {
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                }

                if (!IsChargeState(__instance, "Windup"))
                {
                    s_chargeHoldStates.Remove(id);
                }
                return;
            }
            else
            {
                if (!IsChargeState(__instance, "Windup"))
                {
                    s_chargeHoldStates.Remove(id);
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                    return;
                }

                var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
                var progress = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
                var requiredScale = GetMinimumChargeReleaseScale(__instance);
                AbilityModule.SetChargeSlotActivationProgress(progress, requiredScale);
            }

            if (!state.IsHolding)
                return;

            __instance.windupTimer = Mathf.Max(0.01f, __instance.windupTime);
        }

        private static void PatchChargeAbilityHoldReleaseIfPossible(Harmony harmony, Assembly asm)
        {
            var mOnAbilityDown = AccessTools.Method(typeof(ChargeAbility), nameof(ChargeAbility.OnAbilityDown), Type.EmptyTypes);
            var mOnAbilityUp = AccessTools.Method(typeof(ChargeAbility), nameof(ChargeAbility.OnAbilityUp), Type.EmptyTypes);
            var mOnAbilityCancel = AccessTools.Method(typeof(ChargeAbility), nameof(ChargeAbility.OnAbilityCancel), Type.EmptyTypes);
            if (mOnAbilityUp == null)
                return;

            var onAbilityUpPrefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityUp_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (onAbilityUpPrefix == null)
                return;

            var onAbilityDownPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityDown_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var onAbilityCancelPostfix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(ChargeAbility_OnAbilityCancel_Postfix), BindingFlags.Static | BindingFlags.NonPublic);

            if (mOnAbilityDown != null && onAbilityDownPostfix != null)
                harmony.Patch(mOnAbilityDown, postfix: new HarmonyMethod(onAbilityDownPostfix));
            harmony.Patch(mOnAbilityUp, prefix: new HarmonyMethod(onAbilityUpPrefix));
            if (mOnAbilityCancel != null && onAbilityCancelPostfix != null)
                harmony.Patch(mOnAbilityCancel, postfix: new HarmonyMethod(onAbilityCancelPostfix));
        }

        private static void PatchStunHandlerHoldScalingIfPossible(Harmony harmony, Assembly asm)
        {
            var stunDurationGetter = AccessTools.PropertyGetter(typeof(StunHandler), nameof(StunHandler.StunDuration));
            if (stunDurationGetter == null)
                return;

            var prefix = typeof(ChargeHoldReleaseModule).GetMethod(nameof(StunHandler_StunDuration_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(stunDurationGetter, prefix: new HarmonyMethod(prefix));
        }

        private static void ChargeHandler_CancelCharge_Postfix(ChargeHandler __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            SynchronizeFinalChargeState(__instance);
        }

        private static void ChargeHandler_SyncChargeStateRPC_Prefix(ChargeHandler __instance, ChargeHandler.ChargeState state)
        {
            if (__instance == null)
                return;

            if (state == ChargeHandler.ChargeState.Windup || state == ChargeHandler.ChargeState.Charging)
                return;

            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
        }

        private static void ChargeHandler_SyncChargeStateRPC_Postfix(ChargeHandler __instance)
        {
            if (__instance == null)
                return;

            if (IsChargeState(__instance, "Windup") || IsChargeState(__instance, "Charging"))
                return;

            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
        }

        private static bool ChargeHandler_EnemyHit_Prefix(ChargeHandler __instance)
        {
            if (__instance == null)
                return true;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var holdState))
                return true;

            __instance.enemiesHit++;

            var abilityLevel = __instance.AbilityLevel;
            var vanillaMax = Mathf.FloorToInt(EvaluateStatWithDiminishingReturns(1f, 0.5f, abilityLevel, 20, 0.9f).FinalValue);
            var scaledMax = Mathf.Max(1, Mathf.RoundToInt(vanillaMax * Mathf.Clamp01(holdState.LaunchScale)));
            if (__instance.enemiesHit >= scaledMax)
            {
                __instance.EndCharge();
            }

            return false;
        }

        private static bool StunHandler_StunDuration_Prefix(StunHandler __instance, ref float __result)
        {
            if (__instance == null)
                return true;

            var chargeHandler = __instance.chargeHandler;
            if (chargeHandler == null)
                return true;

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var holdState))
                return true;

            var abilityLevel = chargeHandler.AbilityLevel;
            var vanillaStun = 5f + (1f * abilityLevel);
            __result = vanillaStun * Mathf.Clamp01(holdState.LaunchScale);
            return false;
        }

        private static bool ChargeAbility_OnAbilityUp_Prefix()
        {
            return TryReleaseHeldCharge();
        }

        private static void ChargeAbility_OnAbilityDown_Postfix()
        {
            s_lastLocalHoldInputStartTime = Time.time;
            s_localHoldInputPending = true;
            AbilityModule.SetChargeSlotActivationProgress(0f);

            var chargeHandler = GetLocalChargeHandler();
            if (chargeHandler != null && !IsChargeState(chargeHandler, "Windup"))
            {
                ClearChargeHoldState(chargeHandler);
            }
        }

        private static void ChargeAbility_OnAbilityCancel_Postfix()
        {
            s_localHoldInputPending = false;
            s_localHoldUiActive = false;
            AbilityModule.SetChargeSlotActivationProgress(0f);

            var chargeHandler = GetLocalChargeHandler();
            if (chargeHandler == null)
                return;

            ClearChargeHoldState(chargeHandler);
        }

        private static bool TryReleaseHeldCharge()
        {
            var chargeHandler = GetLocalChargeHandler();
            if (chargeHandler == null)
            {
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                return true;
            }

            if (!IsChargeState(chargeHandler, "Windup"))
            {
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                return true;
            }

            var id = GetUnityObjectInstanceId(chargeHandler);
            if (id == 0)
            {
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                return true;
            }
            if (!s_chargeHoldStates.TryGetValue(id, out var state))
            {
                state = GetOrCreateChargeHoldState(id);
                state.StartTime = s_lastLocalHoldInputStartTime > 0f ? s_lastLocalHoldInputStartTime : Time.time;
                state.IsHolding = true;
                state.LaunchScale = 1f;
            }
            if (!state.IsHolding)
                return true;

            var isRemoteClient = SemiFunc.IsMultiplayer() && !SemiFunc.IsMasterClientOrSingleplayer();

            if (isRemoteClient)
            {
                if (!ConfigSyncManager.IsRemoteHostFixCompatible())
                {
                    state.IsHolding = false;
                    state.LaunchScale = 0f;
                    s_localHoldInputPending = false;
                    s_localHoldUiActive = false;
                    AbilityModule.SetChargeSlotActivationProgress(0f);
                    StopChargeWindupLoop(chargeHandler);
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(ChargePermissiveFallbackLogKey, 120))
                    {
                        Debug.Log("[Fix:DHHCharge][PermissiveGate] Host fix marker missing. Sending authoritative cancel fallback.");
                    }

                    TrySendVanillaRemoteCancelCommand(chargeHandler);
                    return false;
                }

                state.IsHolding = false;
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                // Remote client sends only "release input"; host computes scale/threshold authoritatively.
                if (!TrySendRemoteReleaseCommand(chargeHandler))
                {
                    StopChargeWindupLoop(chargeHandler);
                    TrySendVanillaRemoteCancelCommand(chargeHandler);
                }
                return false;
            }

            var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
            var scale = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
            var requiredScale = GetMinimumChargeReleaseScale(chargeHandler);
            if (scale < requiredScale)
            {
                state.IsHolding = false;
                state.LaunchScale = 0f;
                s_localHoldInputPending = false;
                s_localHoldUiActive = false;
                AbilityModule.SetChargeSlotActivationProgress(0f);
                StopChargeWindupLoop(chargeHandler);
                chargeHandler.CancelCharge();
                return false;
            }

            state.IsHolding = false;
            state.LaunchScale = scale;

            chargeHandler.chargeStrength *= scale;

            chargeHandler.maxBounces = Mathf.Max(0f, chargeHandler.maxBounces * scale);

            chargeHandler.windupTimer = -1f;

            s_localHoldInputPending = false;
            s_localHoldUiActive = false;
            AbilityModule.SetChargeSlotActivationProgress(0f);
            return true;
        }

        private static bool ChargeHandler_UpdateWindupDirection_Prefix(ChargeHandler __instance, Vector3 chargeDirection)
        {
            if (__instance == null)
                return true;

            if (!SemiFunc.IsMasterClientOrSingleplayer())
                return true;

            if (Mathf.Abs(chargeDirection.x - RemoteCancelCommandTag) < 0.001f)
            {
                __instance.CancelCharge();
                return false;
            }

            if (Mathf.Abs(chargeDirection.x - RemoteReleaseCommandTag) > 0.001f)
                return true;

            var id = GetUnityObjectInstanceId(__instance);
            if (id == 0 || !s_chargeHoldStates.TryGetValue(id, out var state))
                return false;

            if (!IsChargeState(__instance, "Windup"))
                return false;

            var holdSeconds = Mathf.Max(0.2f, FeatureFlags.ChargeAbilityHoldSeconds);
            var scale = Mathf.Clamp01((Time.time - state.StartTime) / holdSeconds);
            var requiredScale = GetMinimumChargeReleaseScale(__instance);
            state.IsHolding = false;
            if (scale < requiredScale)
            {
                state.LaunchScale = 0f;
                __instance.CancelCharge();
                return false;
            }

            state.LaunchScale = scale;

            __instance.chargeStrength *= scale;

            __instance.maxBounces = Mathf.Max(0f, __instance.maxBounces * scale);

            __instance.windupTimer = -1f;
            return false;
        }

        private static bool TrySendRemoteReleaseCommand(ChargeHandler chargeHandler)
        {
            var pv = GetChargePhotonView(chargeHandler);
            if (pv == null || pv.ViewID <= 0)
                return false;
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                return false;

            pv.RPC("UpdateWindupDirection", RpcTarget.MasterClient, new object[] { new Vector3(RemoteReleaseCommandTag, 0f, 0f) });
            return true;
        }

        private static bool TrySendVanillaRemoteCancelCommand(ChargeHandler chargeHandler)
        {
            var pv = GetChargePhotonView(chargeHandler);
            if (pv == null || pv.ViewID <= 0)
                return false;
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                return false;

            pv.RPC("CancelCharge", RpcTarget.MasterClient, Array.Empty<object>());
            return true;
        }

        private static PhotonView? GetChargePhotonView(ChargeHandler chargeHandler)
        {
            if (chargeHandler is Component component)
            {
                var componentPhotonView = component.GetComponent<PhotonView>();
                if (componentPhotonView != null && componentPhotonView.ViewID > 0)
                    return componentPhotonView;
            }

            return GetDhhInputManagerHeadPhotonView();
        }

        private static PhotonView? GetDhhInputManagerHeadPhotonView()
        {
            return DHHInputManager.instance != null ? DHHInputManager.instance.headPhotonView : null;
        }

        private static float GetMinimumChargeReleaseScale(ChargeHandler chargeHandler)
        {
            if (chargeHandler == null)
                return 0f;

            var required = 0f;
            TryGetEffectiveChargeAbilityLevel(chargeHandler, out var abilityLevel);

            var chargeStrength = GetEffectiveChargeStrengthForThreshold(chargeHandler, abilityLevel);
            if (chargeStrength > 0f)
            {
                required = Mathf.Max(required, RequiredScaleForMinimumOne(chargeStrength));
            }

            var maxBounces = GetEffectiveMaxBouncesForThreshold(chargeHandler, abilityLevel);
            if (maxBounces > 0f)
            {
                required = Mathf.Max(required, RequiredScaleForMinimumOne(maxBounces));
            }

            if (abilityLevel > 0)
            {
                var stunBase = 5f + (1f * abilityLevel);
                required = Mathf.Max(required, RequiredScaleForMinimumOne(stunBase));
            }

            if (float.IsNaN(required) || float.IsInfinity(required))
                return 1f;

            return Mathf.Clamp01(required);
        }

        private static float GetEffectiveChargeStrengthForThreshold(ChargeHandler chargeHandler, int abilityLevel)
        {
            if (chargeHandler.chargeStrength > 0f)
            {
                return chargeHandler.chargeStrength;
            }

            if (abilityLevel <= 0)
                return 0f;

            return EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHChargeStrengthBaseValue,
                FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                abilityLevel,
                FeatureFlags.DHHChargeStrengthThresholdLevel,
                FeatureFlags.DHHChargeStrengthDiminishingFactor).FinalValue;
        }

        private static float GetEffectiveMaxBouncesForThreshold(ChargeHandler chargeHandler, int abilityLevel)
        {
            if (chargeHandler.maxBounces > 0f)
            {
                return chargeHandler.maxBounces;
            }

            if (abilityLevel <= 0)
                return 0f;

            var baseMaxBounces = chargeHandler.baseMaxBounces > 0 ? chargeHandler.baseMaxBounces : 3f;

            return Mathf.FloorToInt(EvaluateStatWithDiminishingReturns(baseMaxBounces, 0.5f, abilityLevel, 20, 0.9f).FinalValue);
        }

        private static bool TryGetEffectiveChargeAbilityLevel(ChargeHandler chargeHandler, out int abilityLevel)
        {
            abilityLevel = Mathf.Max(0, chargeHandler.AbilityLevel);
            if (abilityLevel > 0)
                return true;

            // On non-master local clients, ChargeWindup/ResetState is authoritative on host.
            // During that phase the local ChargeHandler can transiently report level 0, causing UI threshold=100%.
            // Fallback to local stats entry only for local preview path.
            if (!SemiFunc.IsMasterClientOrSingleplayer() && IsLocalChargeHandler(chargeHandler) && TryGetLocalPlayerChargeUpgrade(out var localUpgrade))
            {
                abilityLevel = Mathf.Max(0, localUpgrade);
                return true;
            }

            return abilityLevel >= 0;
        }

        private static bool TryGetLocalPlayerChargeUpgrade(out int upgrade)
        {
            upgrade = 0;

            var avatar = PlayerAvatar.instance;
            var steamId = avatar != null ? SemiFunc.PlayerGetSteamID(avatar) : null;
            if (string.IsNullOrWhiteSpace(steamId))
                return false;

            try
            {
                upgrade = DHHStatsManager.GetHeadChargeUpgrade(steamId!);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float RequiredScaleForMinimumOne(float baseValue)
        {
            if (baseValue <= 0f)
                return float.PositiveInfinity;
            return 1f / baseValue;
        }

        private static bool IsChargeHandlerHeadGrabbed(ChargeHandler chargeHandler)
        {
            var impactDetector = chargeHandler.impactDetector;
            if (impactDetector == null)
                return false;

            var physGrabObject = impactDetector.physGrabObject;
            if (physGrabObject == null)
                return false;

            return physGrabObject.grabbed;
        }

        private static int GetUnityObjectInstanceId(UnityEngine.Object obj)
        {
            return obj != null ? obj.GetInstanceID() : 0;
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

        private static bool IsChargeState(ChargeHandler chargeHandler, string stateName)
        {
            if (chargeHandler == null)
                return false;

            return string.Equals(chargeHandler.State.ToString(), stateName, StringComparison.Ordinal);
        }

        private static bool IsLocalChargeHandler(ChargeHandler chargeHandler)
        {
            var local = GetLocalChargeHandler();
            if (local == null)
                return false;
            return ReferenceEquals(local, chargeHandler);
        }

        private static ChargeHandler? GetLocalChargeHandler()
        {
            var avatar = PlayerAvatar.instance;
            if (avatar?.playerDeathHead == null)
                return null;

            return avatar.playerDeathHead.GetComponent<DeathHeadController>()?.chargeHandler;
        }

        private static void ClearChargeHoldState(ChargeHandler? chargeHandler)
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

        private static void ChargeHandler_ResetState_Postfix(ChargeHandler __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            if (__instance == null)
                return;

            var level = __instance.AbilityLevel;

            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHChargeStrengthBaseValue,
                FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                level,
                FeatureFlags.DHHChargeStrengthThresholdLevel,
                FeatureFlags.DHHChargeStrengthDiminishingFactor);

            __instance.chargeStrength = stat.FinalValue;
            LogChargeStrength(__instance, stat);
        }

        private static void ChargeHandler_EndCharge_Postfix(ChargeHandler __instance)
        {
            StopChargeWindupLoop(__instance);
            ClearChargeHoldState(__instance);
            SynchronizeFinalChargeState(__instance);
        }

        private static void SynchronizeFinalChargeState(ChargeHandler? chargeHandler)
        {
            if (chargeHandler == null ||
                chargeHandler.State != ChargeHandler.ChargeState.None ||
                chargeHandler.previousState == ChargeHandler.ChargeState.None ||
                !SemiFunc.IsMultiplayer() ||
                !SemiFunc.IsMasterClient())
            {
                return;
            }

            try
            {
                var photonView = chargeHandler.controller?.photonView;
                if (photonView == null || photonView.ViewID <= 0)
                    return;

                photonView.RPC(
                    nameof(ChargeHandler.SyncChargeStateRPC),
                    RpcTarget.Others,
                    new object[] { ChargeHandler.ChargeState.None });
                chargeHandler.previousState = ChargeHandler.ChargeState.None;

                if (FeatureFlags.DebugLogging)
                    s_log?.LogDebug($"[Fix:DHHChargeAudio] Synchronized final Charge state immediately for view {photonView.ViewID}.");
            }
            catch (Exception ex)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:DHHChargeAudio.FinalStateSync", 30))
                {
                    s_log?.LogDebug($"[Fix:DHHChargeAudio] Immediate final-state sync failed: {ex.GetType().Name}; native Update sync remains available.");
                }
            }
        }

        private static void StopChargeWindupLoop(ChargeHandler? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            try
            {
                var controller = chargeHandler.controller;
                if (controller == null)
                    return;

                var chargeEffects = controller.GetComponentInChildren<ChargeEffects>(true);
                if (chargeEffects != null)
                {
                    try
                    {
                        chargeEffects.StopWindupState();
                    }
                    catch
                    {
                        // Ignore and continue with the remaining cleanup path.
                    }

                    try
                    {
                        chargeEffects.StopChargeState();
                    }
                    catch
                    {
                        // Ignore and continue with the remaining cleanup path.
                    }

                    return;
                }

                var audioHandler = controller.audioHandler;
                if (audioHandler == null)
                    return;

                audioHandler.StopWindupSound();
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
