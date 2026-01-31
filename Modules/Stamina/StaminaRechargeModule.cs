#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;

namespace DeathHeadHopperFix.Modules.Stamina
{
    internal sealed class StaminaRechargeModule : MonoBehaviour
    {
        private static readonly Type? s_deathHeadControllerType = AccessTools.TypeByName("DeathHeadHopper.DeathHead.DeathHeadController");

        private object? _controllerInstance;
        private PhotonView? _photonView;
        private bool _isOwner;
        private float _rechargeAccumulator;
        private Rigidbody? _rb;

        private void Awake()
        {
            if (s_deathHeadControllerType == null)
            {
                enabled = false;
                return;
            }

            _controllerInstance = GetComponent(s_deathHeadControllerType);
            if (_controllerInstance == null)
            {
                enabled = false;
                return;
            }

            _photonView = GetComponent<PhotonView>();
            _isOwner = _photonView == null || _photonView.IsMine;

            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (!_isOwner)
                return;

            if (!FeatureFlags.RechargeWithStamina)
            {
                _rechargeAccumulator = 0f;
                return;
            }

            if (_controllerInstance == null)
                return;

            _rechargeAccumulator += Time.deltaTime;
            if (_rechargeAccumulator < FeatureFlags.RechargeTickInterval)
                return;

            var canRechargeByMovement = !FeatureFlags.RechargeStaminaOnlyStationary || IsHeadStationary();
            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (canRechargeByMovement || !allowance.allowed)
            {
                DHHBatteryHelper.RechargeDhhAbilityEnergy(_controllerInstance, _rechargeAccumulator);
            }

            _rechargeAccumulator = 0f;
        }

        private bool IsHeadStationary()
        {
            if (_rb == null)
                return true;

            return _rb.velocity.sqrMagnitude < FeatureFlags.HeadStationaryVelocitySqrThreshold;
        }
    }
}
