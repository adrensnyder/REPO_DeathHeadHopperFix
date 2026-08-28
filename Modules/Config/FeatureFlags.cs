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
            public const string Shop = "6. Shop";
            public const string Debug = "7. Debug";
            public const string Camera = "8. Camera";
            
        }

        internal static class Descriptions
        {
            public const string BatteryJumpEnabled = "Enables the battery authority system that blocks jumps when the energy meter is too low.";
            public const string BatteryJumpUsage = "Amount of battery drained after a successful DHH jump; this never changes jump physics.";
            public const string BatteryJumpMinimumEnergy = "Minimum battery level that must be filled before the death head can hop. 0.25f matches the vanilla talk threshold so the head can still speak.";
            public const string JumpBlockDuration = "Duration (in seconds) that jump blocking remains active after the energy warning fires.";
            public const string HeadStationaryVelocitySqrThreshold = "Velocity squared threshold the death head must stay below to be considered stationary for recharge.";
            public const string RechargeTickInterval = "Interval (seconds) between stamina-based recharge ticks.";
            public const string EnergyWarningCheckInterval = "Interval (seconds) between energy warning / SpectateCamera checks.";
            public const string RechargeWithStamina = "Mirrors vanilla stamina regen to refill the death-head battery instead of draining energy.";
            public const string RechargeStaminaOnlyStationary = "When true, the death-head only recharges while standing still, matching vanilla stamina guard behavior.";
            public const string ChargeAbilityStaminaCost = "Charge ability custom stamina cost (always read). How much player stamina the vanilla Charge ability consumes when executed.";
            public const string ChargeAbilityCooldown = "Cooldown in seconds before Charge can be used again.";
            public const string ChargeAbilityHoldSeconds = "Seconds to hold slot1 Charge to reach 100% power. Release before this value to launch at proportional power.";
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
            public const string DHHJumpVertical = "Vertical component written to JumpHandler.jumpDirection when the original DHH jump fires.";
            public const string DHHJumpRotationForce = "Torque multiplier used by the original DHH jump.";
            public const string DHHJumpCooldown = "Original DHH jump cooldown in seconds.";
            public const string DHHJumpBufferDuration = "Original DHH jump input buffer in seconds.";
            public const string DHHHopMoveBaseValue = "Base horizontal impulse used by HopHandler.MoveForce for positive power levels.";
            public const string DHHHopMoveIncreasePerLevel = "Horizontal impulse increase per hop upgrade level.";
            public const string DHHHopMoveThresholdLevel = "Diminishing-return threshold for horizontal hop movement.";
            public const string DHHHopMoveDiminishingFactor = "Diminishing factor for horizontal hop movement.";
            public const string DHHHopRotationForce = "Original hop rotation acceleration multiplier.";
            public const string DHHHopDamping = "Original hop angular damping multiplier.";
            public const string DHHHopAngleThreshold = "Angle threshold in degrees used to finish hop realignment.";
            public const string DHHHopVelocityThreshold = "Angular velocity threshold used to finish hop realignment.";
            public const string DHHHopCooldown = "Original hop cooldown in seconds.";
            public const string DHHHopMoveDelay = "Delay before the original horizontal hop impulse in seconds.";
            public const string HeadChargerShopPoolMode = "Controls how Item DHH Head Charge enters the vanilla shop item pool. Disabled = never eligible, Default = use vanilla shop stands with balanced copy count, Reduced = minimum shop presence.";
            public const string DHHUpgradesShopPoolMode = "Controls how Item Upgrade DHH Charge and Item Upgrade DHH Power enter the vanilla shop upgrade pool. Disabled = never eligible, Default = use vanilla upgrade stands with balanced copy count, Reduced = minimum shop presence.";
            public const string DebugLogging = "Dump extra log lines that help trace the battery/ability logic.";
            public const string DHHSpectateDefaultFov = "Default field of view restored while DHH spectate is active when the active camera FOV is invalid or stuck.";
        }

        internal static class ShopPoolModes
        {
            public const string Disabled = "Disabled";
            public const string Default = "Default";
            public const string Reduced = "Reduced";
        }

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.BatteryJumpEnabled)]
        public static bool BatteryJumpEnabled = false;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.BatteryJumpUsage, Min = 0.001f, Max = 1f)]
        public static float BatteryJumpUsage = 0.02f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.BatteryJumpMinimumEnergy, Min = 0f, Max = 1f)]
        public static float BatteryJumpMinimumEnergy = 0.25f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.JumpBlockDuration, Min = 0.1f, Max = 2f)]
        public static float JumpBlockDuration = 0.5f;

        [FeatureConfigEntry(Sections.RechargeBattery, Descriptions.HeadStationaryVelocitySqrThreshold, Min = 0.001f, Max = 1f)]
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

        [FeatureConfigEntry(Sections.ChargeAbility, Descriptions.ChargeAbilityHoldSeconds, Min = 0.2f, Max = 5f)]
        public static float ChargeAbilityHoldSeconds = 2f;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthBaseValue, Min = 1f, Max = 100f)]
        public static int DHHChargeStrengthBaseValue = 12;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthIncreasePerLevel, Min = 1f, Max = 10f)]
        public static int DHHChargeStrengthIncreasePerLevel = 1;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthThresholdLevel, Min = 1f, Max = 100f)]
        public static int DHHChargeStrengthThresholdLevel = 10;

        [FeatureConfigEntry(Sections.ChargeVanilla, Descriptions.DHHChargeStrengthDiminishingFactor, Min = 0.1f, Max = 0.99f)]
        public static float DHHChargeStrengthDiminishingFactor = 0.75f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceBaseValue, Min = 0.1f, Max = 10f)]
        public static float DHHJumpForceBaseValue = 2.8f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceIncreasePerLevel, Min = 0f, Max = 2f)]
        public static float DHHJumpForceIncreasePerLevel = 0.4f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceThresholdLevel, Min = 1f, Max = 20f)]
        public static int DHHJumpForceThresholdLevel = 5;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpForceDiminishingFactor, Min = 0f, Max = 1f)]
        public static float DHHJumpForceDiminishingFactor = 0.9f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpIncreasePerLevel, Min = 0f, Max = 2f)]
        public static float DHHHopJumpIncreasePerLevel = 0.11f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpDiminishingFactor, Min = 0f, Max = 1f)]
        public static float DHHHopJumpDiminishingFactor = 0.9f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpBaseValue, Min = 0.1f, Max = 10f)]
        public static float DHHHopJumpBaseValue = 3f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopJumpThresholdLevel, Min = 1f, Max = 20f)]
        public static int DHHHopJumpThresholdLevel = 5;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpVertical, Min = 0f, Max = 2f)]
        public static float DHHJumpVertical = 1f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpRotationForce, Min = 0f, Max = 1f)]
        public static float DHHJumpRotationForce = 0.05f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpCooldown, Min = 0.1f, Max = 5f)]
        public static float DHHJumpCooldown = 1f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHJumpBufferDuration, Min = 0.05f, Max = 1f)]
        public static float DHHJumpBufferDuration = 0.25f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopMoveBaseValue, Min = 0f, Max = 5f)]
        public static float DHHHopMoveBaseValue = 1f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopMoveIncreasePerLevel, Min = 0f, Max = 1f)]
        public static float DHHHopMoveIncreasePerLevel = 0.05f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopMoveThresholdLevel, Min = 1f, Max = 20f)]
        public static int DHHHopMoveThresholdLevel = 5;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopMoveDiminishingFactor, Min = 0f, Max = 1f)]
        public static float DHHHopMoveDiminishingFactor = 0.9f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopRotationForce, Min = 0f, Max = 50f)]
        public static float DHHHopRotationForce = 20f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopDamping, Min = 0f, Max = 30f)]
        public static float DHHHopDamping = 12f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopAngleThreshold, Min = 0.1f, Max = 30f)]
        public static float DHHHopAngleThreshold = 2f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopVelocityThreshold, Min = 0.001f, Max = 1f)]
        public static float DHHHopVelocityThreshold = 0.03f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopCooldown, Min = 0.1f, Max = 3f)]
        public static float DHHHopCooldown = 0.6f;

        [FeatureConfigEntry(Sections.Jump, Descriptions.DHHHopMoveDelay, Min = 0f, Max = 0.5f)]
        public static float DHHHopMoveDelay = 0.04f;

        [FeatureConfigEntry(Sections.Shop, Descriptions.HeadChargerShopPoolMode, Options = new[] { ShopPoolModes.Disabled, ShopPoolModes.Default, ShopPoolModes.Reduced })]
        public static string HeadChargerShopPoolMode = ShopPoolModes.Default;

        [FeatureConfigEntry(Sections.Shop, Descriptions.DHHUpgradesShopPoolMode, Options = new[] { ShopPoolModes.Disabled, ShopPoolModes.Default, ShopPoolModes.Reduced })]
        public static string DHHUpgradesShopPoolMode = ShopPoolModes.Default;

        [FeatureConfigEntry(Sections.Debug, Descriptions.DebugLogging, HostControlled = false)]
        public static bool DebugLogging = false;

        [FeatureConfigEntry(Sections.Camera, Descriptions.DHHSpectateDefaultFov, Min = 0f, Max = 120f, HostControlled = false)]
        public static int DHHSpectateDefaultFov = 70;


    }
}
