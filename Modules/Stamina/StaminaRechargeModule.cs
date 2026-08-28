#nullable enable

using DeathHeadHopper.DeathHead;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;
using Photon.Pun;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Stamina
{
    internal sealed class StaminaRechargeModule : MonoBehaviour
    {
        private DeathHeadController? _controller;
        private bool _isOwner;
        private float _rechargeAccumulator;
        private Rigidbody? _rb;

        private void Awake()
        {
            _controller = GetComponent<DeathHeadController>();
            if (_controller == null)
            {
                enabled = false;
                return;
            }

            var photonView = GetComponent<PhotonView>();
            _isOwner = !SemiFunc.IsMultiplayer() || (photonView != null && photonView.IsMine);
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (!_isOwner || _controller == null)
                return;

            if (!FeatureFlags.RechargeWithStamina)
            {
                _rechargeAccumulator = 0f;
                return;
            }

            // Recharge depends on elapsed gameplay time and has no upstream event, so polling is
            // limited to the local owner and batched by the configured interval.
            _rechargeAccumulator += Time.deltaTime;
            if (_rechargeAccumulator < FeatureFlags.RechargeTickInterval)
                return;

            if (!FeatureFlags.RechargeStaminaOnlyStationary || IsHeadStationary())
                DHHBatteryHelper.RechargeDhhAbilityEnergy(_controller, _rechargeAccumulator);

            _rechargeAccumulator = 0f;
        }

        private bool IsHeadStationary()
        {
            return _rb == null || _rb.velocity.sqrMagnitude < FeatureFlags.HeadStationaryVelocitySqrThreshold;
        }
    }
}
