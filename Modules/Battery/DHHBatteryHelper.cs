#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Battery
{
    internal static class DHHBatteryHelper
    {
        private static readonly FieldInfo? s_headEnergyField = typeof(SpectateCamera).GetField("headEnergy", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? s_headEnergyEnoughField = typeof(SpectateCamera).GetField("headEnergyEnough", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? s_playerSprintRechargeAmountField = typeof(PlayerController).GetField("sprintRechargeAmount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo? s_playerDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo? s_playerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");

        private const float JumpConsumptionCoalesceWindow = 0.2f;
        private static float s_lastJumpConsumptionTime = float.NegativeInfinity;

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
            if (!FeatureFlags.DebugLogging || !FeatureFlags.BatteryJumpEnabled)
                return;

            if (!IsDeathHeadContext())
                return;

            // This log is emitted by paths that run every frame, so we rate-limit the output.
            if (!LogLimiter.ShouldLog("DHHBattery.JumpAllowance", 120))
                return;

            var readyState = readyFlag.HasValue ? readyFlag.Value.ToString() : "unknown";
            Debug.Log($"[Fix:DHHBattery] Jump allowance: allowed={allowed}, energy={currentEnergy:F3}, ref={reference:F3}, readyFlag={readyState}");
        }

        private static bool IsDeathHeadContext()
        {
            if (SpectateContextHelper.IsSpectatingLocalDeathHead())
                return true;

            var avatar = PlayerAvatar.instance;
            if (avatar == null)
                return false;

            if (s_playerIsDisabledField != null &&
                s_playerIsDisabledField.GetValue(avatar) is bool disabled &&
                disabled)
            {
                return true;
            }

            if (s_playerDeadSetField != null &&
                s_playerDeadSetField.GetValue(avatar) is bool dead &&
                dead)
            {
                return true;
            }

            return false;
        }



        internal static float GetEffectiveBatteryJumpUsage()
        {
            return Math.Max(0f, FeatureFlags.BatteryJumpUsage);
        }

        internal static bool HasRecentJumpConsumption()
        {
            return Time.time - s_lastJumpConsumptionTime < JumpConsumptionCoalesceWindow;
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
            s_lastJumpConsumptionTime = Time.time;
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

            Debug.Log($"[Fix:DHHBattery] Stamina recharge {amount:F3} (stamina={energy:F3} / {max:F3})");
        }
    }
}
