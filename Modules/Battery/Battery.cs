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
    internal sealed class BatteryJumpModule : MonoBehaviour
    {
        private const float JumpBlockDuration = 0.5f;
        private const float EnergyWarningCheckInterval = 0.5f;
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
        private bool _overrideSpectatedCleared;
        private float _energyWarningAccumulator;
        private bool _inactiveStateApplied;
        private void Awake()
        {
            if (s_headJumpEventField == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.HeadJumpEvent.Missing", 600))
                {
                    Debug.Log("[Fix:DHHBattery] Head jump event field not found; BatteryJumpModule disabled.");
                }
                enabled = false;
                return;
            }

            _controllerInstance = GetComponent(s_headJumpEventField.DeclaringType);
            if (_controllerInstance == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.HeadJumpEvent.NoController", 600))
                {
                    Debug.Log("[Fix:DHHBattery] Head jump controller component missing; BatteryJumpModule disabled.");
                }
                enabled = false;
                return;
            }

            _jumpEvent = s_headJumpEventField.GetValue(_controllerInstance) as UnityEvent;
            if (_jumpEvent == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.HeadJumpEvent.Null", 600))
                {
                    Debug.Log("[Fix:DHHBattery] Head jump UnityEvent is null; BatteryJumpModule disabled.");
                }
                enabled = false;
                return;
            }

            _jumpAction = new UnityAction(OnHeadJump);
            _jumpEvent.AddListener(_jumpAction);

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.HeadJumpEvent.Hooked", 600))
            {
                Debug.Log("[Fix:DHHBattery] Head jump listener hooked.");
            }

            _photonView = GetComponent<PhotonView>();
            _isOwner = !SemiFunc.IsMultiplayer() || (_photonView != null && _photonView.IsMine);

            SetupEyeWarningCondition();
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

            if (!FeatureFlags.BatteryJumpEnabled || FeatureFlags.DisableBatteryModule)
            {
                if (!_inactiveStateApplied)
                {
                    ResetBlockedState();
                    _inactiveStateApplied = true;
                }
                return;
            }

            _inactiveStateApplied = false;

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

        private void ResetBlockedState()
        {
            if (_jumpBlocked)
            {
                _jumpBlocked = false;
                TrySyncEyeWarningState(false);
            }

            var spectate = SpectateCamera.instance;
            if (spectate != null)
            {
                DHHBatteryHelper.SetEnergyEnough(spectate, true);
            }

            _energyWarningAccumulator = EnergyWarningCheckInterval;
        }

        private void TryClearStuckOverrideSpectated(SpectateCamera spectate)
        {
            if (spectate == null || s_spectateCurrentStateField == null || s_overrideSpectatedField == null || s_overrideSpectatedResetMethod == null)
                return;

            if (!LogLimiter.ShouldLog("DHHBattery.TryClearOverrideSpectated", 30))
                return;

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
                // Optional cleanup path: reflection can fail when target API changes.
            }
        }

        private static readonly Type? s_physGrabObjectType = AccessTools.TypeByName("PhysGrabObject");
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

                return false;
            }
            catch
            {
                // Reflection probe failed; fall back to "not grabbed".
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
                // RPC may fail during disconnect/teardown; state will resync on next update.
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
            if (!_isOwner)
                return;

            if (!FeatureFlags.BatteryJumpEnabled || FeatureFlags.DisableBatteryModule)
                return;

            if (DHHBatteryHelper.HasRecentJumpConsumption())
                return;

            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (!allowance.allowed)
            {
                NotifyJumpBlocked(allowance.currentEnergy, allowance.reference, allowance.readyFlag);
            }
        }

        internal void NotifyJumpBlocked(float currentEnergy, float reference, bool? readyFlag)
        {
            if (!FeatureFlags.BatteryJumpEnabled || FeatureFlags.DisableBatteryModule)
                return;

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
}
