namespace DeathHeadHopperFix.Modules.Config
{
    internal static class FeatureFlags
    {
        internal static class Sections
        {
            public const string RechargeBattery = "1. Battery";
            public const string StaminaRecharge = "2. Stamina & Recharge";
            public const string ChargeAbility = "3. Charge ability tunables (DHH)";
            public const string Jump = "4. Jump (DHH)";
            public const string ChargeVanilla = "5. Charge (DHH)";
            public const string Upgrades = "6. Upgrades";
            public const string Debug = "7. Debug";
        }

        internal static class Descriptions
        {
            public const string BatteryJumpEnabled = "Enables the battery authority system that blocks jumps when the energy meter is too low.";
            public const string BatteryJumpUsage = "Amount of battery drained per death-head jump; larger values drain faster.";
            public const string BatteryJumpMinimumEnergy = "Minimum battery level that must be filled before the death head can hop. 0.25f matches the vanilla talk threshold so the head can still speak.";
            public const string JumpBlockDuration = "Duration (in seconds) that jump blocking remains active after the energy warning fires.";
            public const string HeadStationaryVelocitySqrThreshold = "Velocity squared threshold the death head must stay below to be considered stationary for recharge.";
            public const string RechargeTickInterval = "Interval (seconds) between stamina-based recharge ticks.";
            public const string EnergyWarningCheckInterval = "Interval (seconds) between energy warning / SpectateCamera checks.";
            public const string RechargeWithStamina = "Mirrors vanilla stamina regen to refill the death-head battery instead of draining energy.";
            public const string RechargeStaminaOnlyStationary = "When true, the death-head only recharges while standing still, matching vanilla stamina guard behavior.";
            public const string ChargeAbilityStaminaCost = "Charge ability custom stamina cost (always read). How much player stamina the vanilla Charge ability consumes when executed.";
            public const string ChargeAbilityCooldown = "Cooldown in seconds before Charge can be used again.";
            public const string DHHChargeStrengthBaseValue = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Default values mirror vanilla ChargeHandler.ResetState: DHHFunc.StatWithDiminishingReturns(baseStrength(12f), ChargeStrengthIncrease, AbilityLevel, 10, 0.75f). Base impact strength used to compute the Charge ability hit force.";
            public const string DHHChargeStrengthIncreasePerLevel = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Strength increase applied each ability level before diminishing returns.";
            public const string DHHChargeStrengthThresholdLevel = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Ability level threshold where extra strength gain starts to shrink.";
            public const string DHHChargeStrengthDiminishingFactor = "Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true). Fraction that scales down extra strength beyond the threshold.";
            public const string DHHHopJumpBaseValue = "Default values mirror vanilla HopHandler.JumpForce: DHHFunc.StatWithDiminishingReturns(3f, jumpIncrease(0.11f), PowerLevel+1, 5, 0.9f). Base slot value that determines the vertical boost for hop upgrades.";
            public const string DHHHopJumpIncreasePerLevel = "Additional boost added for each hop upgrade level before the threshold.";
            public const string DHHJumpForceBaseValue = "Default values mirror DeathHeadHopper JumpHandler: DHHFunc.StatWithDiminishingReturns(2.8f, forceIncrease(0.4f), PowerLevel+1, 5, 0.9f). Base jump force the death head uses when leaping off the ground.";
            public const string DHHJumpForceIncreasePerLevel = "Force increment applied for each power level before the threshold.";
            public const string DHHHopJumpThresholdLevel = "Level after which hop upgrades start diminishing in effectiveness.";
            public const string DHHHopJumpDiminishingFactor = "Curve factor that controls how quickly extra hop levels taper off.";
            public const string DHHJumpForceThresholdLevel = "Threshold level where jump force increases start to diminish.";
            public const string DHHJumpForceDiminishingFactor = "Diminishing factor that cuts additional force beyond the threshold.";
            public const string DHHShopMaxItems = "Maximum number of DeathHeadHopper mod items that can spawn in the shop (-1 = unlimited).";
            public const string DHHShopSpawnChance = "Chance each DeathHeadHopper shop slot actually spawns an item.";
            public const string ShopItemsSpawnChance = "Second-tier chance that a DeathHeadHopper slot produces an item after it was selected.";
            public const string DebugLogging = "Dump extra log lines that help trace the battery/ability logic.";
            public const string DisableBatteryModule = "Temporarily disable the BatteryModule component.";
            public const string DisableAbilityPatches = "Skip ability-related Harmony patches (charge rename, ability cooldown sync, etc.).";
            public const string DisableSpectateChecks = "Skip SpectateCamera override/hints when evaluating battery status (debug test).";
        }

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.BatteryJumpEnabled)]
        public static bool BatteryJumpEnabled = true;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.BatteryJumpUsage, Min = 0.01f, Max = 1f)]
        public static float BatteryJumpUsage = 0.02f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.BatteryJumpMinimumEnergy, Min = 0.01f, Max = 1f)]
        public static float BatteryJumpMinimumEnergy = 0.25f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.JumpBlockDuration, Min = 0.1f, Max = 1f)]
        public static float JumpBlockDuration = 0.5f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.HeadStationaryVelocitySqrThreshold, Min = 0.01f, Max = 1f)]
        public static float HeadStationaryVelocitySqrThreshold = 0.04f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.RechargeTickInterval, Min = 0.1f, Max = 1f)]
        public static float RechargeTickInterval = 0.5f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.EnergyWarningCheckInterval, Min = 0.1f, Max = 1f)]
        public static float EnergyWarningCheckInterval = 0.5f;

        [FeatureConfigEntry(Sections.StaminaRecharge, Descriptions.RechargeWithStamina)]
        public static bool RechargeWithStamina = true;

        [FeatureConfigEntry(Sections.StaminaRecharge, Descriptions.RechargeStaminaOnlyStationary)]
        public static bool RechargeStaminaOnlyStationary = false;

        [FeatureConfigEntry(Sections.ChargeAbility, Descriptions.ChargeAbilityStaminaCost, Min = 10f, Max = 200f)]
        public static int ChargeAbilityStaminaCost = 60;

        [FeatureConfigEntry(Sections.ChargeAbility, Descriptions.ChargeAbilityCooldown, Min = 1f, Max = 20f)]
        public static int ChargeAbilityCooldown = 6;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthBaseValue, Min = 1f, Max = 100f)]
        public static int DHHChargeStrengthBaseValue = 12;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthIncreasePerLevel, Min = 1f, Max = 10f)]
        public static int DHHChargeStrengthIncreasePerLevel = 1;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthThresholdLevel, Min = 1f, Max = 100f)]
        public static int DHHChargeStrengthThresholdLevel = 10;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthDiminishingFactor, Min = 0.1f, Max = 0.99f)]
        public static float DHHChargeStrengthDiminishingFactor = 0.75f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpBaseValue, Min = 1f, Max = 10f)]
        public static int DHHHopJumpBaseValue = 2;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpIncreasePerLevel, Min = 0.1f, Max = 1f)]
        public static float DHHHopJumpIncreasePerLevel = 0.3f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceBaseValue, Min = 0.1f, Max = 5f)]
        public static float DHHJumpForceBaseValue = 0.5f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceIncreasePerLevel, Min = 0.1f, Max = 2f)]
        public static float DHHJumpForceIncreasePerLevel = 0.3f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpThresholdLevel, Min = 1f, Max = 10f)]
        public static int DHHHopJumpThresholdLevel = 2;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpDiminishingFactor, Min = 0.1f, Max = 0.99f)]
        public static float DHHHopJumpDiminishingFactor = 0.9f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceThresholdLevel, Min = 1f, Max = 10f)]
        public static int DHHJumpForceThresholdLevel = 2;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceDiminishingFactor, Min = 0.1f, Max = 0.99f)]
        public static float DHHJumpForceDiminishingFactor = 0.9f;

        [FeatureConfigEntry(Sections.Upgrades, Descriptions.DHHShopMaxItems, Min = -1f, Max = 12f)]
        public static int DHHShopMaxItems = 5;

        [FeatureConfigEntry(Sections.Upgrades, Descriptions.DHHShopSpawnChance, Min = 0.1f, Max = 1f)]
        public static float DHHShopSpawnChance = 0.5f;

        [FeatureConfigEntry(Sections.Upgrades, Descriptions.ShopItemsSpawnChance, Min = 0.1f, Max = 1f)]
        public static float ShopItemsSpawnChance = 0.5f;

        [FeatureConfigEntry(Sections.Debug, Descriptions.DebugLogging)]
        public static bool DebugLogging = false;

        //[FeatureConfigEntry(Sections.Debug, Descriptions.DisableBatteryModule)]
        public static bool DisableBatteryModule = false;

        //[FeatureConfigEntry(Sections.Debug, Descriptions.DisableAbilityPatches)]
        public static bool DisableAbilityPatches = false;

        //[FeatureConfigEntry(Sections.Debug, Descriptions.DisableSpectateChecks)]
        public static bool DisableSpectateChecks = false;
    }
}
