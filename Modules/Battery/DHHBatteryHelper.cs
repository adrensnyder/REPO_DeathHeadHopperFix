#nullable enable

using System;
using DeathHeadHopper.DeathHead;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Battery
{
    internal static class DHHBatteryHelper
    {
        internal static float GetHeadEnergy(SpectateCamera? spectate)
        {
            return spectate?.headEnergy ?? 0f;
        }

        internal static void SetHeadEnergy(SpectateCamera spectate, float value)
        {
            if (spectate != null)
                spectate.headEnergy = value;
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
            bool? readyFlag = spectate == null ? null : spectate.headEnergyEnough;
            var allowed = currentEnergy >= reference;
            LogAllowance(currentEnergy, reference, allowed, readyFlag);
            return (allowed, readyFlag, reference, currentEnergy);
        }

        internal static void RechargeDhhAbilityEnergy(DeathHeadController? controller, float deltaTime)
        {
            if (!FeatureFlags.RechargeWithStamina || controller == null || deltaTime <= 0f)
                return;

            var handler = controller.abilityEnergyHandler;
            if (handler == null || handler.EnergyMax <= 0f || handler.Energy >= handler.EnergyMax)
                return;

            var rechargeRate01PerSec = GetPlayerSprintRechargeAmount();
            if (rechargeRate01PerSec <= 0f)
                return;

            var amount = rechargeRate01PerSec * deltaTime;
            handler.IncreaseEnergy(amount);
            LogRecharge(amount, handler.Energy, handler.EnergyMax);
        }

        internal static void RechargeHeadEnergy(float deltaTime)
        {
            // Kept as a compatibility NOOP: DHH ability energy is separate from vanilla head energy.
        }

        internal static float GetEffectiveBatteryJumpUsage()
        {
            return Math.Max(0f, FeatureFlags.BatteryJumpUsage);
        }

        internal static float ComputeVanillaBatteryJumpUsage()
        {
            var avatar = PlayerController.instance?.playerAvatarScript;
            if (avatar == null)
                return 0.02f;

            var capacity = 25f;
            var increment = 5f;
            for (var level = avatar.upgradeDeathHeadBattery; level > 0f; level -= 1f)
            {
                capacity += increment;
                increment *= 0.95f;
            }

            return 0.5f / capacity;
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
            LogConsumption(currentEnergy, nextValue, consumption, reference);
            return nextValue;
        }

        internal static float ApplyDamageEnergyPenalty(float penalty)
        {
            if (penalty <= 0f || SpectateCamera.instance == null)
                return 0f;

            return ApplyConsumption(SpectateCamera.instance, penalty, GetJumpThreshold());
        }

        internal static float GetPlayerSprintRechargeAmount()
        {
            return PlayerController.instance?.sprintRechargeAmount ?? 0f;
        }

        private static void LogAllowance(float currentEnergy, float reference, bool allowed, bool? readyFlag)
        {
            if (!FeatureFlags.DebugLogging || !FeatureFlags.BatteryJumpEnabled ||
                !InternalDebugFlags.DebugDhhBatteryJumpAllowanceLog || !IsDeathHeadContext() ||
                !LogLimiter.ShouldLog("DHHBattery.JumpAllowance", 120))
                return;

            var readyState = readyFlag.HasValue ? readyFlag.Value.ToString() : "unknown";
            Debug.Log($"[Fix:DHHBattery] Jump allowance: allowed={allowed}, energy={currentEnergy:F3}, ref={reference:F3}, readyFlag={readyState}");
        }

        private static bool IsDeathHeadContext()
        {
            if (SpectateContextHelper.IsSpectatingLocalDeathHead())
                return true;

            var avatar = PlayerAvatar.instance;
            return avatar != null && (avatar.isDisabled || avatar.deadSet);
        }

        private static void LogConsumption(float before, float after, float amount, float reference)
        {
            if (!FeatureFlags.DebugLogging || !LogLimiter.ShouldLog("DHHBattery.Consumption", 120))
                return;

            Debug.Log($"[Fix:DHHBattery] Energy consume {amount:F3} (before={before:F3}, after={after:F3}, ref={reference:F3})");
        }

        private static void LogRecharge(float amount, float energy, float max)
        {
            if (!FeatureFlags.DebugLogging || !InternalDebugFlags.DebugDhhChargeRechargeLog ||
                !LogLimiter.ShouldLog("DHHBattery.Recharge", 240))
                return;

            Debug.Log($"[Fix:DHHCharge] Stamina recharge {amount:F3} (stamina={energy:F3} / {max:F3})");
        }
    }
}
