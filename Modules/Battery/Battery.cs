#nullable enable

using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Battery
{
    internal sealed class BatteryModule : MonoBehaviour
    {
        private const float JumpBlockDuration = 0.5f;
        private const float HeadStationaryVelocitySqrThreshold = 0.04f;
        private const float RechargeTickInterval = 0.5f;
        private const float EnergyWarningCheckInterval = 0.5f;
        // Guard keeps overrideSpectated from forcing headEnergy=1f permanently (seen in the DHH bug).
        private static readonly FieldInfo? s_spectateCurrentStateField = AccessTools.Field(typeof(SpectateCamera), "currentState");
        private static readonly FieldInfo? s_overrideSpectatedField = AccessTools.Field(typeof(PlayerDeathHead), "overrideSpectated");
        private static readonly MethodInfo? s_overrideSpectatedResetMethod = AccessTools.Method(typeof(PlayerDeathHead), "OverrideSpectatedReset");

        private static readonly FieldInfo? s_headJumpEventField = AccessTools.TypeByName("DeathHeadHopper.DeathHead.DeathHeadController")
            ?.GetField("m_HeadJumpEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly Type? s_eyeHandlerType = AccessTools.TypeByName("DeathHeadHopper.DeathHead.Handlers.EyeHandler");
        private static readonly FieldInfo? s_eyeNegativeConditionsField = s_eyeHandlerType?.GetField("eyeNegativeConditions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private object? _controllerInstance;
        private UnityEvent? _jumpEvent;
        private UnityAction? _jumpAction;

        private IList? _eyeNegativeConditions;
        private Func<bool>? _eyeCondition;

        private PhotonView? _photonView;
        private bool _isOwner;
        private bool _lastSyncedEyeWarningState;

        private float _jumpBlockedTimer;
        private float _lastBlockedLogTime;
        private bool _jumpBlocked;
        private float _rechargeAccumulator;
        private bool _overrideSpectatedCleared;
        private float _energyWarningAccumulator;

        private void Awake()
        {
            if (!FeatureFlags.BatteryJumpEnabled)
            {
                Destroy(this);
                return;
            }

            if (s_headJumpEventField == null)
            {
                enabled = false;
                return;
            }

            _controllerInstance = GetComponent(s_headJumpEventField.DeclaringType);
            if (_controllerInstance == null)
            {
                enabled = false;
                return;
            }

            _jumpEvent = s_headJumpEventField.GetValue(_controllerInstance) as UnityEvent;
            if (_jumpEvent == null)
            {
                enabled = false;
                return;
            }

            _jumpAction = new UnityAction(OnHeadJump);
            _jumpEvent.AddListener(_jumpAction);

            _photonView = GetComponent<PhotonView>();
            _isOwner = _photonView == null || _photonView.IsMine;

            SetupEyeWarningCondition();
            _rb = GetComponent<Rigidbody>();
        }

        private void OnDestroy()
        {
            if (_jumpEvent != null && _jumpAction != null)
            {
                _jumpEvent.RemoveListener(_jumpAction);
            }

            RemoveEyeWarningCondition();
        }

        private void Update()
        {
            if (!_isOwner)
                return;

            if (FeatureFlags.RechargeWithStamina)
            {
                _rechargeAccumulator += Time.deltaTime;
                if (_rechargeAccumulator >= RechargeTickInterval)
                {
                    var canRechargeByMovement = !FeatureFlags.RechargeStaminaOnlyStationary || IsHeadStationary();
                    if (canRechargeByMovement || _jumpBlocked)
                    {
                        DHHBatteryHelper.RechargeDhhAbilityEnergy(_controllerInstance, _rechargeAccumulator);
                    }
                    _rechargeAccumulator = 0f;
                }
            }

            if (_jumpBlocked && _jumpBlockedTimer > 0f)
            {
                _jumpBlockedTimer -= Time.deltaTime;
                if (_jumpBlockedTimer <= 0f)
                {
                    _jumpBlocked = false;
                    TrySyncEyeWarningState(false);
                }
            }

            _energyWarningAccumulator += Time.deltaTime;
            if (_energyWarningAccumulator >= EnergyWarningCheckInterval)
            {
                UpdateEnergyWarningState();
                _energyWarningAccumulator %= EnergyWarningCheckInterval;
            }
        }

        private Rigidbody? _rb;

        private bool IsHeadStationary()
        {
            if (_rb == null)
                return true;

            // Minimum threshold prevents recharging while the head is bouncing or moving.
            // The game does not expose an isStationary flag for the head, so this is a best-effort approximation.
            return _rb.velocity.sqrMagnitude < HeadStationaryVelocitySqrThreshold;
        }

        private void UpdateEnergyWarningState()
        {
            if (FeatureFlags.DisableSpectateChecks)
            {
                if (FeatureFlags.DebugLogging)
                    Debug.Log("[Fix:DHHBattery] Spectate checks disabled; skipping energy warning evaluation.");
                return;
            }

            var spectate = SpectateCamera.instance;
            if (spectate == null)
                return;

            TryClearStuckOverrideSpectated(spectate);

            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (allowance.allowed)
            {
                _jumpBlocked = false;
                TrySyncEyeWarningState(false);
                if (spectate != null)
                {
                    DHHBatteryHelper.SetEnergyEnough(spectate, true);
                }
                return;
            }

            if (!_jumpBlocked)
            {
                _jumpBlocked = true;
                _jumpBlockedTimer = JumpBlockDuration;
                TrySyncEyeWarningState(true);
            }
            DHHBatteryHelper.SetEnergyEnough(spectate, false);
        }
        private void TryClearStuckOverrideSpectated(SpectateCamera spectate)
        {
            if (spectate == null || s_spectateCurrentStateField == null || s_overrideSpectatedField == null || s_overrideSpectatedResetMethod == null)
                return;

            // This guard does not need to run every frame, so we throttle the check.
            if (!LogLimiter.ShouldLog("DHHBattery.TryClearOverrideSpectated", 30))
                return;

            // Reset the flag whenever we leave the "Head" state.
            var stateObj = s_spectateCurrentStateField.GetValue(spectate);
            if (stateObj == null || !string.Equals(stateObj.ToString(), "Head", StringComparison.Ordinal))
            {
                _overrideSpectatedCleared = false;
                return;
            }

            if (_overrideSpectatedCleared)
                return;

            var head = PlayerController.instance?.playerAvatarScript?.playerDeathHead;
            if (head == null)
                return;

            var isOverride = s_overrideSpectatedField.GetValue(head) as bool? ?? false;
            if (!isOverride)
                return;

            // If someone is physically grabbing the head, the override is likely intentional, so leave it alone.
            // Some builds do not expose physGrabObject on PlayerDeathHead, so resolve it via best-effort reflection.
            if (IsHeadGrabbedBestEffort(head))
                return;

            try
            {
                s_overrideSpectatedResetMethod.Invoke(head, null);
                _overrideSpectatedCleared = true;

                if (FeatureFlags.DebugLogging)
                    Debug.Log("[Fix:DHHBattery] Cleared stuck overrideSpectated to prevent headEnergy=1f lock.");
            }
            catch
            {
                // ignore
            }
        }


        private static readonly Type? s_physGrabObjectType = AccessTools.TypeByName("PhysGrabObject");

        // Avoid AccessTools.Property for "grabbed" because HarmonyX warns when the property is missing.
        // In this build, "grabbed" is also a field, so rely on the field directly.
        private static readonly FieldInfo? s_physGrabObjectGrabbedField =
            s_physGrabObjectType != null ? AccessTools.Field(s_physGrabObjectType, "grabbed") : null;

        private static bool IsHeadGrabbedBestEffort(PlayerDeathHead head)
        {
            try
            {
                if (head == null)
                    return false;

                if (s_physGrabObjectType == null)
                    return false;

                var comp = head.gameObject.GetComponent(s_physGrabObjectType);
                if (comp == null)
                    return false;

                if (s_physGrabObjectGrabbedField != null && s_physGrabObjectGrabbedField.FieldType == typeof(bool))
                    return (bool)(s_physGrabObjectGrabbedField.GetValue(comp) ?? false);

                // Assume the head is not grabbed when the grabbed flag is missing in this build.
                return false;

            }
            catch
            {
                // ignore
            }

            return false;
        }


        private void TrySyncEyeWarningState(bool blocked)
        {
            if (!_isOwner || _photonView == null)
                return;

            if (blocked == _lastSyncedEyeWarningState)
                return;

            _lastSyncedEyeWarningState = blocked;
            try
            {
                _photonView.RPC(nameof(SyncEyeWarningStateRPC), RpcTarget.OthersBuffered, blocked);
            }
            catch
            {
                // ignore
            }
        }

        [PunRPC]
        private void SyncEyeWarningStateRPC(bool blocked)
        {
            _jumpBlocked = blocked;
            _jumpBlockedTimer = blocked ? JumpBlockDuration : 0f;
        }

        private void OnHeadJump()
        {
            var spectate = SpectateCamera.instance;
            if (spectate == null)
                return;

            var previousEnergy = DHHBatteryHelper.GetHeadEnergy(spectate);
            var consumption = DHHBatteryHelper.GetEffectiveBatteryJumpUsage();
            var reference = DHHBatteryHelper.GetJumpThreshold();
            var nextEnergy = DHHBatteryHelper.ApplyConsumption(spectate, consumption, reference);

            if (FeatureFlags.DebugLogging)
            {
                if (!LogLimiter.ShouldLog("DHHBattery.JumpConsumed", 120))
                    return;
            }
        }

        internal void NotifyJumpBlocked(float currentEnergy, float reference, bool? readyFlag)
        {
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.JumpBlocked", 120))
            {
                var timeSinceLog = Time.time - _lastBlockedLogTime;
                if (!_jumpBlocked || timeSinceLog >= JumpBlockDuration)
                {
                    _lastBlockedLogTime = Time.time;
                    var readyState = readyFlag.HasValue ? readyFlag.Value.ToString() : "unknown";
                    Debug.Log($"[Fix:DHHBattery] Jump blocked, energy too low (current={currentEnergy:F3}, readyFlag={readyState}, reference={reference:F3})");
                }
            }

            _jumpBlocked = true;
            _jumpBlockedTimer = JumpBlockDuration;
            TrySyncEyeWarningState(true);

            var spectate = SpectateCamera.instance;
            if (spectate != null)
            {
                DHHBatteryHelper.SetEnergyEnough(spectate, false);
            }
        }

        private void SetupEyeWarningCondition()
        {
            if (s_eyeHandlerType == null || s_eyeNegativeConditionsField == null)
                return;

            var eyeHandler = GetComponent(s_eyeHandlerType);
            if (eyeHandler == null)
                return;

            var conditionList = s_eyeNegativeConditionsField.GetValue(eyeHandler) as IList;
            if (conditionList == null)
                return;

            _eyeCondition = () => _jumpBlocked;
            conditionList.Add(_eyeCondition);
            _eyeNegativeConditions = conditionList;
        }

        private void RemoveEyeWarningCondition()
        {
            if (_eyeNegativeConditions == null || _eyeCondition == null)
                return;

            _eyeNegativeConditions.Remove(_eyeCondition);
            _eyeNegativeConditions = null;
            _eyeCondition = null;
        }
    }

    internal static class DHHBatteryHelper
    {
        private static readonly FieldInfo? s_headEnergyField = typeof(SpectateCamera).GetField("headEnergy", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? s_headEnergyEnoughField = typeof(SpectateCamera).GetField("headEnergyEnough", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? s_playerSprintRechargeAmountField = typeof(PlayerController).GetField("sprintRechargeAmount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // The DHH mod tracks its own dedicated ability energy pool instead of SpectateCamera.headEnergy.
        private static readonly FieldInfo? s_dhhAbilityEnergyHandlerField = AccessTools.TypeByName("DeathHeadHopper.DeathHead.DeathHeadController")
            ?.GetField("abilityEnergyHandler", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly Type? s_dhhAbilityEnergyHandlerType = AccessTools.TypeByName("DeathHeadHopper.DeathHead.Handlers.AbilityEnergyHandler");
        private static readonly System.Reflection.PropertyInfo? s_dhhAbilityEnergyProp = s_dhhAbilityEnergyHandlerType
            ?.GetProperty("Energy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly System.Reflection.PropertyInfo? s_dhhAbilityEnergyMaxProp = s_dhhAbilityEnergyHandlerType
            ?.GetProperty("EnergyMax", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo? s_dhhIncreaseEnergyMethod = s_dhhAbilityEnergyHandlerType
            ?.GetMethod("IncreaseEnergy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(float) }, null);

        internal static float GetHeadEnergy(SpectateCamera? spectate)
        {
            if (spectate == null || s_headEnergyField == null)
                return 0f;

            return (float)(s_headEnergyField.GetValue(spectate) ?? 0f);
        }

        internal static void SetHeadEnergy(SpectateCamera spectate, float value)
        {
            if (s_headEnergyField != null)
            {
                s_headEnergyField.SetValue(spectate, value);
            }
        }

        internal static void SetEnergyEnough(SpectateCamera spectate, bool value)
        {
            if (s_headEnergyEnoughField != null)
            {
                s_headEnergyEnoughField.SetValue(spectate, value);
            }
        }

        internal static float GetJumpThreshold()
        {
            return FeatureFlags.BatteryJumpMinimumEnergy;
        }

        internal static (bool allowed, bool? readyFlag, float reference, float currentEnergy) EvaluateJumpAllowance()
        {
            var spectate = SpectateCamera.instance;
            var currentEnergy = GetHeadEnergy(spectate);
            var reference = GetJumpThreshold();
            bool? readyFlag = null;

            if (spectate != null && s_headEnergyEnoughField != null)
            {
                readyFlag = s_headEnergyEnoughField.GetValue(spectate) as bool?;
            }

            var allowed = currentEnergy >= reference;
            LogAllowance(currentEnergy, reference, allowed, readyFlag);
            return (allowed, readyFlag, reference, currentEnergy);
        }

        internal static void RechargeDhhAbilityEnergy(object? controllerInstance, float deltaTime)
        {
            // IMPORTANT:
            // - SpectateCamera.headEnergy and headEnergyEnough drive the vanilla death battery (also tied to speaking).
            // - The original DHH mod maintains its own bar (AbilityEnergyHandler.Energy).
            // Recharging the vanilla values previously altered vanilla behavior and spawned anomalies.
            // From now on we only recharge the DHH mod energy value.
            if (!FeatureFlags.RechargeWithStamina || deltaTime <= 0f)
                return;

            if (controllerInstance == null)
                return;

            if (s_dhhAbilityEnergyHandlerField == null || s_dhhAbilityEnergyProp == null || s_dhhAbilityEnergyMaxProp == null || s_dhhIncreaseEnergyMethod == null)
                return;

            var rechargeRate01PerSec = GetPlayerSprintRechargeAmount();
            if (rechargeRate01PerSec <= 0f)
                return;

            object? handler;
            try
            {
                handler = s_dhhAbilityEnergyHandlerField.GetValue(controllerInstance);
            }
            catch
            {
                return;
            }

            if (handler == null)
                return;

            float energy;
            float energyMax;
            try
            {
                energy = (float)(s_dhhAbilityEnergyProp.GetValue(handler) ?? 0f);
                energyMax = (float)(s_dhhAbilityEnergyMaxProp.GetValue(handler) ?? 0f);
            }
            catch
            {
                return;
            }

            if (energyMax <= 0f || energy >= energyMax)
                return;

            // Scale the recharge as a fraction of EnergyMax to keep a rhythm similar to vanilla stamina.
            var amount = rechargeRate01PerSec * deltaTime;

            try
            {
                s_dhhIncreaseEnergyMethod.Invoke(handler, new object[] { amount });
                LogRecharge(amount, energy + amount, energyMax);
            }
            catch
            {
                // ignore
            }
        }

        // Legacy: keep this method for internal compatibility, but it should no longer be used.
        internal static void RechargeHeadEnergy(float deltaTime)
        {
            // Intentionally empty so we stop modifying the vanilla death battery.
        }


        private static void LogAllowance(float currentEnergy, float reference, bool allowed, bool? readyFlag)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            // This log is emitted by paths that run every frame, so we rate-limit the output.
            if (!LogLimiter.ShouldLog("DHHBattery.JumpAllowance", 120))
                return;

            var readyState = readyFlag.HasValue ? readyFlag.Value.ToString() : "unknown";
            Debug.Log($"[Fix:DHHBattery] Jump allowance: allowed={allowed}, energy={currentEnergy:F3}, ref={reference:F3}, readyFlag={readyState}");
        }



        internal static float GetEffectiveBatteryJumpUsage()
        {
            return Math.Max(0f, FeatureFlags.BatteryJumpUsage);
        }

        internal static float ComputeVanillaBatteryJumpUsage()
        {
            var player = PlayerController.instance;
            if (player == null || player.playerAvatarScript == null)
                return 0.02f;

            float num = 25f;
            float increment = 5f;
            var upgradeValue = GetUpgradeDeathHeadBattery(player.playerAvatarScript);
            for (float i = upgradeValue; i > 0f; i -= 1f)
            {
                num += increment;
                increment *= 0.95f;
            }

            return 0.5f / num;
        }

        internal static float GetVanillaBatteryJumpMinimumEnergy()
        {
            return 0.25f;
        }

        internal static float ApplyConsumption(SpectateCamera spectate, float consumption, float reference)
        {
            var currentEnergy = GetHeadEnergy(spectate);
            var nextValue = Mathf.Max(0f, currentEnergy - consumption);
            SetHeadEnergy(spectate, nextValue);
            SetEnergyEnough(spectate, nextValue >= reference);
            LogConsumption(currentEnergy, nextValue, consumption, reference);
            return nextValue;
        }

        internal static float ApplyDamageEnergyPenalty(float penalty)
        {
            if (penalty <= 0f)
                return 0f;

            var spectate = SpectateCamera.instance;
            if (spectate == null)
                return 0f;

            return ApplyConsumption(spectate, penalty, GetJumpThreshold());
        }

        internal static float GetPlayerSprintRechargeAmount()
        {
            var controller = PlayerController.instance;
            if (controller == null || s_playerSprintRechargeAmountField == null)
                return 0f;

            return (float)(s_playerSprintRechargeAmountField.GetValue(controller) ?? 0f);
        }

        private static float GetUpgradeDeathHeadBattery(PlayerAvatar avatar)
        {
            if (avatar == null)
                return 0f;

            var field = AccessTools.Field(typeof(PlayerAvatar), "upgradeDeathHeadBattery");
            if (field == null)
                return 0f;

            return (float)(field.GetValue(avatar) ?? 0f);
        }

        private static void LogConsumption(float before, float after, float amount, float reference)
        {
            if (!FeatureFlags.DebugLogging)
                return;
            if (!LogLimiter.ShouldLog("DHHBattery.Consumption", 120))
                return;

            Debug.Log($"[Fix:DHHBattery] Energy consume {amount:F3} (before={before:F3}, after={after:F3}, ref={reference:F3})");
        }

        private static void LogRecharge(float amount, float energy, float max)
        {
            if (!FeatureFlags.DebugLogging)
                return;
            if (!LogLimiter.ShouldLog("DHHBattery.Recharge", 240))
                return;

            Debug.Log($"[Fix:DHHBattery] Recharge {amount:F3} (energy={energy:F3} / {max:F3})");
        }
    }
}
