using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DeathHeadHopperFix.Modules.Config
{
    internal static class ConfigManager
    {
        private const string SectionRechargeBattery = "1. Battery";
        private const string SectionStaminaRecharge = "2. Stamina & Recharge";
        private const string SectionChargeAbility = "3. Charge ability tunables (DHH)";
        private const string SectionJump = "4. Jump (DHH)";
        private const string SectionChargeVanilla = "5. Charge (DHH)";
        private const string SectionUpgrades = "6. Upgrades";
        private const string SectionDebug = "7. Debug";
        private static ManualLogSource _log;
        private static readonly Dictionary<string, object> s_defaultValues = new(StringComparer.Ordinal);
        private struct RangeF { public float Min, Max; }
        private struct RangeI { public int Min, Max; }
        private static readonly Dictionary<string, RangeF> s_floatRanges = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, RangeI> s_intRanges = new(StringComparer.Ordinal);

        private const string DescriptionBatteryJumpEnabled = "Enables the battery authority system that blocks jumps when the energy meter is too low.";
        private const string DescriptionBatteryJumpUsage = "Amount of battery drained per death-head jump; larger values drain faster.";
        private const string DescriptionBatteryJumpMinimumEnergy = "Minimum battery level that must be filled before the death head can hop. 0.25f matches the vanilla talk threshold so the head can still speak.";
        private const string DescriptionJumpBlockDuration = "Duration (in seconds) that jump blocking remains active after the energy warning fires.";
        private const string DescriptionHeadStationaryVelocitySqrThreshold = "Velocity squared threshold the death head must stay below to be considered stationary for recharge.";
        private const string DescriptionRechargeTickInterval = "Interval (seconds) between stamina-based recharge ticks.";
        private const string DescriptionEnergyWarningCheckInterval = "Interval (seconds) between energy warning / SpectateCamera checks.";
        private const string DescriptionRechargeWithStamina = "Mirrors vanilla stamina regen to refill the death-head battery instead of draining energy.";
        private const string DescriptionRechargeStaminaOnlyStationary = "When true, the death-head only recharges while standing still, matching vanilla stamina guard behavior.";
        private const string DescriptionChargeAbilityStaminaCost = "Charge ability custom stamina cost (always read). How much player stamina the vanilla Charge ability consumes when executed.";
        private const string DescriptionChargeAbilityCooldown = "Cooldown in seconds before Charge can be used again.";
        private const string DescriptionDHHChargeStrengthBaseValue = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Default values mirror vanilla ChargeHandler.ResetState: DHHFunc.StatWithDiminishingReturns(baseStrength(12f), ChargeStrengthIncrease, AbilityLevel, 10, 0.75f). Base impact strength used to compute the Charge ability hit force.";
        private const string DescriptionDHHChargeStrengthIncreasePerLevel = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Strength increase applied each ability level before diminishing returns.";
        private const string DescriptionDHHChargeStrengthThresholdLevel = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Ability level threshold where extra strength gain starts to shrink.";
        private const string DescriptionDHHChargeStrengthDiminishingFactor = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Fraction that scales down extra strength beyond the threshold.";
        private const string DescriptionDHHHopJumpBaseValue = "Default values mirror vanilla HopHandler.JumpForce: DHHFunc.StatWithDiminishingReturns(3f, jumpIncrease(0.11f), PowerLevel+1, 5, 0.9f). Base slot value that determines the vertical boost for hop upgrades.";
        private const string DescriptionDHHHopJumpIncreasePerLevel = "Additional boost added for each hop upgrade level before the threshold.";
        private const string DescriptionDHHJumpForceBaseValue = "Default values mirror DeathHeadHopper JumpHandler: DHHFunc.StatWithDiminishingReturns(2.8f, forceIncrease(0.4f), PowerLevel+1, 5, 0.9f). Base jump force the death head uses when leaping off the ground.";
        private const string DescriptionDHHJumpForceIncreasePerLevel = "Force increment applied for each power level before the threshold.";
        private const string DescriptionDHHHopJumpThresholdLevel = "Level after which hop upgrades start diminishing in effectiveness.";
        private const string DescriptionDHHHopJumpDiminishingFactor = "Curve factor that controls how quickly extra hop levels taper off.";
        private const string DescriptionDHHJumpForceThresholdLevel = "Threshold level where jump force increases start to diminish.";
        private const string DescriptionDHHJumpForceDiminishingFactor = "Diminishing factor that cuts additional force beyond the threshold.";
        private const string DescriptionDHHShopMaxItems = "Maximum number of DeathHeadHopper mod items that can spawn in the shop (-1 = unlimited).";
        private const string DescriptionDHHShopSpawnChance = "Chance each DeathHeadHopper shop slot actually spawns an item.";
        private const string DescriptionShopItemsSpawnChance = "Second-tier chance that a DeathHeadHopper slot produces an item after it was selected.";
        private const string DescriptionDebugLogging = "Dump extra log lines that help trace the battery/ability logic.";

        internal static void Initialize(ConfigFile config, ManualLogSource log)
        {
            _log = log;

            //Battery
            BindBool(config, SectionRechargeBattery, "BatteryJumpEnabled", FeatureFlags.BatteryJumpEnabled,
                DescriptionBatteryJumpEnabled,
                value => FeatureFlags.BatteryJumpEnabled = value);

            BindFloat(config, SectionRechargeBattery, "BatteryJumpUsage", FeatureFlags.BatteryJumpUsage,
                DescriptionBatteryJumpUsage,
                value => FeatureFlags.BatteryJumpUsage = value,
                minValue: 0.01f, maxValue: 1f);
            BindFloat(config, SectionRechargeBattery, "BatteryJumpMinimumEnergy", FeatureFlags.BatteryJumpMinimumEnergy,
                DescriptionBatteryJumpMinimumEnergy,
                value => FeatureFlags.BatteryJumpMinimumEnergy = value,
                minValue: 0.1f, maxValue: 1f);

            BindFloat(config, SectionRechargeBattery, "JumpBlockDuration", FeatureFlags.JumpBlockDuration,
                DescriptionJumpBlockDuration,
                value => FeatureFlags.JumpBlockDuration = value,
                minValue: 0.1f, maxValue: 1f);

            BindFloat(config, SectionRechargeBattery, "HeadStationaryVelocitySqrThreshold", FeatureFlags.HeadStationaryVelocitySqrThreshold,
                DescriptionHeadStationaryVelocitySqrThreshold,
                value => FeatureFlags.HeadStationaryVelocitySqrThreshold = value,
                minValue: 0.01f, maxValue: 1f);

            BindFloat(config, SectionRechargeBattery, "RechargeTickInterval", FeatureFlags.RechargeTickInterval,
                DescriptionRechargeTickInterval,
                value => FeatureFlags.RechargeTickInterval = value,
                minValue: 0.1f, maxValue: 1f);

            BindFloat(config, SectionRechargeBattery, "EnergyWarningCheckInterval", FeatureFlags.EnergyWarningCheckInterval,
                DescriptionEnergyWarningCheckInterval,
                value => FeatureFlags.EnergyWarningCheckInterval = value,
                minValue: 0.1f, maxValue: 1f);

            //Stamina & Recharge
            BindBool(config, SectionStaminaRecharge, "RechargeWithStamina", FeatureFlags.RechargeWithStamina,
                DescriptionRechargeWithStamina,
                value => FeatureFlags.RechargeWithStamina = value);

            BindBool(config, SectionStaminaRecharge, "RechargeStaminaOnlyStationary", FeatureFlags.RechargeStaminaOnlyStationary,
                DescriptionRechargeStaminaOnlyStationary,
                value => FeatureFlags.RechargeStaminaOnlyStationary = value);

            // Charge (DHH)
            BindInt(config, SectionChargeAbility, "ChargeAbilityStaminaCost", FeatureFlags.ChargeAbilityStaminaCost,
                DescriptionChargeAbilityStaminaCost,
                value => FeatureFlags.ChargeAbilityStaminaCost = value,
                minValue: 10, maxValue: 200);

            BindInt(config, SectionChargeAbility, "ChargeAbilityCooldown", FeatureFlags.ChargeAbilityCooldown,
                DescriptionChargeAbilityCooldown,
                value => FeatureFlags.ChargeAbilityCooldown = value,
                minValue: 1, maxValue: 20);

            BindInt(config, SectionChargeVanilla, "DHHChargeStrengthBaseValue", FeatureFlags.DHHChargeStrengthBaseValue,
                DescriptionDHHChargeStrengthBaseValue,
                value => FeatureFlags.DHHChargeStrengthBaseValue = value,
                minValue: 1, maxValue: 100);

            BindInt(config, SectionChargeVanilla, "DHHChargeStrengthIncreasePerLevel", FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                DescriptionDHHChargeStrengthIncreasePerLevel,
                value => FeatureFlags.DHHChargeStrengthIncreasePerLevel = value,
                minValue: 1, maxValue: 10);

            BindInt(config, SectionChargeVanilla, "DHHChargeStrengthThresholdLevel", FeatureFlags.DHHChargeStrengthThresholdLevel,
                DescriptionDHHChargeStrengthThresholdLevel,
                value => FeatureFlags.DHHChargeStrengthThresholdLevel = value,
                minValue: 1, maxValue: 100);

            BindFloat(config, SectionChargeVanilla, "DHHChargeStrengthDiminishingFactor", FeatureFlags.DHHChargeStrengthDiminishingFactor,
                DescriptionDHHChargeStrengthDiminishingFactor,
                value => FeatureFlags.DHHChargeStrengthDiminishingFactor = value,
                minValue: 0.1f, maxValue: 0.99f);

            // Jump (DHH)
            BindInt(config, SectionJump, "DHHHopJumpBaseValue", FeatureFlags.DHHHopJumpBaseValue,
                DescriptionDHHHopJumpBaseValue,
                value => FeatureFlags.DHHHopJumpBaseValue = value,
                minValue: 1, maxValue: 10);

            BindFloat(config, SectionJump, "DHHHopJumpIncreasePerLevel", FeatureFlags.DHHHopJumpIncreasePerLevel,
                DescriptionDHHHopJumpIncreasePerLevel,
                value => FeatureFlags.DHHHopJumpIncreasePerLevel = value,
                minValue: 0.1f, maxValue: 1f);

            BindFloat(config, SectionJump, "DHHJumpForceBaseValue", FeatureFlags.DHHJumpForceBaseValue,
                DescriptionDHHJumpForceBaseValue,
                value => FeatureFlags.DHHJumpForceBaseValue = value,
                minValue: 0.1f, maxValue: 1f);

            BindFloat(config, SectionJump, "DHHJumpForceIncreasePerLevel", FeatureFlags.DHHJumpForceIncreasePerLevel,
                DescriptionDHHJumpForceIncreasePerLevel,
                value => FeatureFlags.DHHJumpForceIncreasePerLevel = value,
                minValue: 0.1f, maxValue: 1f);

            BindInt(config, SectionJump, "DHHHopJumpThresholdLevel", FeatureFlags.DHHHopJumpThresholdLevel,
                DescriptionDHHHopJumpThresholdLevel,
                value => FeatureFlags.DHHHopJumpThresholdLevel = value,
                minValue: 1, maxValue: 10);

            BindFloat(config, SectionJump, "DHHHopJumpDiminishingFactor", FeatureFlags.DHHHopJumpDiminishingFactor,
                DescriptionDHHHopJumpDiminishingFactor,
                value => FeatureFlags.DHHHopJumpDiminishingFactor = value,
                minValue: 0.1f, maxValue: 0.99f);

            BindInt(config, SectionJump, "DHHJumpForceThresholdLevel", FeatureFlags.DHHJumpForceThresholdLevel,
                DescriptionDHHJumpForceThresholdLevel,
                value => FeatureFlags.DHHJumpForceThresholdLevel = value,
                minValue: 1, maxValue: 10);

            BindFloat(config, SectionJump, "DHHJumpForceDiminishingFactor", FeatureFlags.DHHJumpForceDiminishingFactor,
                DescriptionDHHJumpForceDiminishingFactor,
                value => FeatureFlags.DHHJumpForceDiminishingFactor = value,
                minValue: 0.1f, maxValue: 0.99f);

            // Upgrades
            BindInt(config, SectionUpgrades, "DHHShopMaxItems", FeatureFlags.DHHShopMaxItems,
                DescriptionDHHShopMaxItems,
                value => FeatureFlags.DHHShopMaxItems = value,
                minValue: -1, maxValue: 12);

            BindFloat(config, SectionUpgrades, "DHHShopSpawnChance", FeatureFlags.DHHShopSpawnChance,
                DescriptionDHHShopSpawnChance,
                value => FeatureFlags.DHHShopSpawnChance = value,
                minValue: 0.1f, maxValue: 1f);

            BindFloat(config, SectionUpgrades, "ShopItemsSpawnChance", FeatureFlags.ShopItemsSpawnChance,
                DescriptionShopItemsSpawnChance,
                value => FeatureFlags.ShopItemsSpawnChance = value,
                minValue: 0.1f, maxValue: 1f);

            // Debug
            BindBool(config, SectionDebug, "DebugLogging", FeatureFlags.DebugLogging,
                DescriptionDebugLogging,
                value => FeatureFlags.DebugLogging = value);

            // Advanced Debug
            //BindBool(config, SectionDebug, "DisableSpectateChecks", FeatureFlags.DisableSpectateChecks,
            //    "Skip SpectateCamera override/hints when evaluating battery status (debug test).",
            //    value => FeatureFlags.DisableSpectateChecks = value);

            //BindBool(config, SectionDebug, "DisableBatteryModule", FeatureFlags.DisableBatteryModule,
            //    "Temporarily disable the BatteryModule component.",
            //    value => FeatureFlags.DisableBatteryModule = value);

            //BindBool(config, SectionDebug, "DisableAbilityPatches", FeatureFlags.DisableAbilityPatches,
            //    "Skip ability-related Harmony patches (charge rename, ability cooldown sync, etc.).",
            //    value => FeatureFlags.DisableAbilityPatches = value);

        }

        private static void BindBool(ConfigFile config, string section, string key, bool fallback, string description, Action<bool> setter)
        {
            RegisterDefault(key, fallback);
            var entry = config.Bind(section, key, fallback, new ConfigDescription(description));
            ApplyAndWatch(entry, setter);
        }

        private static void BindFloat(ConfigFile config, string section, string key, float fallback, string description, Action<float> setter, float minValue = 0.1f, float maxValue = 999f)
        {
            RegisterDefault(key, fallback);
            RegisterFloatRange(key, minValue, Math.Max(minValue, maxValue));

            var entry = config.Bind(section, key, fallback,
                new ConfigDescription(description,
                    new AcceptableValueRange<float>(minValue, Math.Max(minValue, maxValue))));
            ApplyAndWatch(entry, setter);
        }

        private static void BindInt(ConfigFile config, string section, string key, int fallback, string description, Action<int> setter, int minValue = 1, int maxValue = 999)
        {
            RegisterDefault(key, fallback);
            RegisterIntRange(key, minValue, Math.Max(minValue, maxValue));

            var entry = config.Bind(section, key, fallback,
                new ConfigDescription(description,
                    new AcceptableValueRange<int>(minValue, Math.Max(minValue, maxValue))));
            ApplyAndWatch(entry, setter);
        }

        private static void ApplyAndWatch<T>(ConfigEntry<T> entry, Action<T> setter)
        {
            if (entry == null || setter == null)
                return;

            void Update(bool initial)
            {
                var sanitized = SanitizeValue(entry.Value, entry.Definition.Key, initial);
                setter(sanitized);
            }

            Update(true);
            entry.SettingChanged += (_, _) => Update(false);
        }

        private static T SanitizeValue<T>(T value, string key, bool initial)
        {
            if (value is float f)
            {
                var range = s_floatRanges.TryGetValue(key, out var storedRange) ? storedRange : new RangeF { Min = 0.1f, Max = float.MaxValue };
                if (initial && f < range.Min && s_defaultValues.TryGetValue(key, out var defObj))
                {
                    var defaultValue = defObj is float defFloat ? defFloat : 0.1f;
                    return (T)(object)Math.Max(range.Min, defaultValue);
                }

                var clampedF = Math.Min(range.Max, Math.Max(range.Min, f));
                return (T)(object)clampedF;
            }

            if (value is int i)
            {
                var range = s_intRanges.TryGetValue(key, out var storedRange) ? storedRange : new RangeI { Min = 1, Max = int.MaxValue };
                if (initial && i < range.Min && s_defaultValues.TryGetValue(key, out var defObj))
                {
                    var defaultValue = defObj is int defInt ? defInt : 1;
                    return (T)(object)Math.Max(range.Min, defaultValue);
                }

                var clampedI = Math.Min(range.Max, Math.Max(range.Min, i));
                return (T)(object)clampedI;
            }

            return value;
        }

        private static void RegisterDefault(string key, object fallback)
        {
            s_defaultValues[key] = fallback;
        }

        private static void RegisterFloatRange(string key, float min, float max)
        {
            s_floatRanges[key] = new RangeF { Min = min, Max = max };
        }

        private static void RegisterIntRange(string key, int min, int max)
        {
            s_intRanges[key] = new RangeI { Min = min, Max = max };
        }

    }
}

