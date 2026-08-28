#nullable enable

using System;
using System.Collections.Generic;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Battery
{
    internal sealed class BatteryJumpModule : MonoBehaviour
    {
        private List<Func<bool>>? _eyeNegativeConditions;
        private Func<bool>? _eyeCondition;
        private PhotonView? _photonView;
        private bool _isOwner;
        private bool _lastSyncedEyeWarningState;
        private float _jumpBlockedTimer;
        private float _lastBlockedLogTime;
        private bool _jumpBlocked;
        private float _energyWarningAccumulator;

        private void Awake()
        {
            _photonView = GetComponent<PhotonView>();
            _isOwner = !SemiFunc.IsMultiplayer() || (_photonView != null && _photonView.IsMine);
            ConfigManager.HostControlledChanged += RefreshFeatureState;
            RefreshFeatureState();
        }

        private void OnDestroy()
        {
            ConfigManager.HostControlledChanged -= RefreshFeatureState;
            RemoveEyeWarningCondition();
        }

        private void RefreshFeatureState()
        {
            var active = FeatureFlags.BatteryJumpEnabled && !InternalDebugFlags.DisableBatteryModule;
            if (active)
            {
                SetupEyeWarningCondition();
                _energyWarningAccumulator = FeatureFlags.EnergyWarningCheckInterval;
                enabled = true;
                return;
            }

            ResetBlockedState();
            RemoveEyeWarningCondition();
            enabled = false;
        }

        private void Update()
        {
            if (!_isOwner)
                return;

            if (_jumpBlocked && _jumpBlockedTimer > 0f)
            {
                _jumpBlockedTimer -= Time.deltaTime;
                if (_jumpBlockedTimer <= 0f)
                {
                    _jumpBlocked = false;
                    TrySyncEyeWarningState(false);
                }
            }

            // Vanilla head energy can change without an exposed event. Polling is local-owner only
            // and batched by EnergyWarningCheckInterval to clear the visual warning after recharge.
            _energyWarningAccumulator += Time.deltaTime;
            if (_energyWarningAccumulator < FeatureFlags.EnergyWarningCheckInterval)
                return;

            _energyWarningAccumulator %= FeatureFlags.EnergyWarningCheckInterval;
            UpdateEnergyWarningState();
        }

        private void UpdateEnergyWarningState()
        {
            if (InternalDebugFlags.DisableSpectateChecks)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.SpectateChecksDisabled", 120))
                    Debug.Log("[Fix:DHHBattery] Spectate checks disabled; skipping energy warning evaluation.");
                return;
            }

            if (SpectateCamera.instance == null)
                return;

            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (allowance.allowed)
            {
                _jumpBlocked = false;
                TrySyncEyeWarningState(false);
                return;
            }

            if (_jumpBlocked)
                return;

            _jumpBlocked = true;
            _jumpBlockedTimer = FeatureFlags.JumpBlockDuration;
            TrySyncEyeWarningState(true);
        }

        private void ResetBlockedState()
        {
            if (_jumpBlocked)
                TrySyncEyeWarningState(false);

            _jumpBlocked = false;
            _jumpBlockedTimer = 0f;
            _energyWarningAccumulator = 0f;
        }

        private void TrySyncEyeWarningState(bool blocked)
        {
            if (!_isOwner || _photonView == null || blocked == _lastSyncedEyeWarningState)
                return;

            _lastSyncedEyeWarningState = blocked;
            if (!SemiFunc.IsMultiplayer() || !PhotonNetwork.InRoom)
                return;

            try
            {
                _photonView.RPC(nameof(SyncEyeWarningStateRPC), RpcTarget.OthersBuffered, blocked);
            }
            catch
            {
                // RPC can fail during disconnect or scene teardown; local state remains valid.
            }
        }

        [PunRPC]
        private void SyncEyeWarningStateRPC(bool blocked)
        {
            _jumpBlocked = blocked;
            _jumpBlockedTimer = blocked ? FeatureFlags.JumpBlockDuration : 0f;
        }

        internal void NotifyJumpBlocked(float currentEnergy, float reference, bool? readyFlag)
        {
            if (!FeatureFlags.BatteryJumpEnabled || InternalDebugFlags.DisableBatteryModule)
                return;

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHBattery.JumpBlocked", 120))
            {
                var timeSinceLog = Time.time - _lastBlockedLogTime;
                if (!_jumpBlocked || timeSinceLog >= FeatureFlags.JumpBlockDuration)
                {
                    _lastBlockedLogTime = Time.time;
                    var readyState = readyFlag.HasValue ? readyFlag.Value.ToString() : "unknown";
                    Debug.Log($"[Fix:DHHBattery] Jump blocked, energy too low (current={currentEnergy:F3}, readyFlag={readyState}, reference={reference:F3})");
                }
            }

            _jumpBlocked = true;
            _jumpBlockedTimer = FeatureFlags.JumpBlockDuration;
            TrySyncEyeWarningState(true);
        }

        private void SetupEyeWarningCondition()
        {
            if (_eyeCondition != null)
                return;

            var eyeHandler = GetComponent<EyeHandler>();
            if (eyeHandler?.eyeNegativeConditions == null)
                return;

            _eyeCondition = () => _jumpBlocked;
            eyeHandler.eyeNegativeConditions.Add(_eyeCondition);
            _eyeNegativeConditions = eyeHandler.eyeNegativeConditions;
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
