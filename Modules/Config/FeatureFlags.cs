

namespace DeathHeadHopperFix.Modules.Config
{
    internal static class FeatureFlags
    {
        // Battery protection and usage tunables.
        // Enables the battery authority system that blocks jumps when the energy meter is too low.
        public static bool BatteryJumpEnabled = true;
        // Amount of battery drained per death-head jump; larger values drain faster.
        public static float BatteryJumpUsage = 0.02f;
        // Minimum battery level that must be filled before the death head can hop.
        // 0.25f matches the vanilla talk threshold so the head can still speak.
        public static float BatteryJumpMinimumEnergy = 0.25f;

        // Battery enforcement helpers.
        // How long (in seconds) a jump stays blocked after the energy warning triggers (matches the previous hard-coded duration).
        public static float JumpBlockDuration = 0.5f;
        // Velocity squared threshold below which the death head is treated as stationary when recharging.
        public static float HeadStationaryVelocitySqrThreshold = 0.04f;
        // Interval (seconds) between recharge attempts when using stamina-based battery regen.
        public static float RechargeTickInterval = 0.5f;
        // Interval (seconds) to run the energy warning/spectate checks.
        public static float EnergyWarningCheckInterval = 0.5f;

        // Stamina & recharge helpers.
        // Mirrors vanilla stamina regen to refill the death-head battery instead of draining energy.
        public static bool RechargeWithStamina = true;
        // When true, the death-head only recharges while standing still, matching vanilla stamina guard behavior.
        public static bool RechargeStaminaOnlyStationary = false;

        // Charge (DHH)
        // Charge ability custom stamina cost (always read).
        // How much player stamina the vanilla Charge ability consumes when executed.
        public static int ChargeAbilityStaminaCost = 60;
        // Cooldown in seconds before Charge can be used again.
        public static int ChargeAbilityCooldown = 6;
        
        // Strength upgrade custom tunables (used only when DHHEnableCustomDHHValues is true).
        // Default values mirror vanilla ChargeHandler.ResetState:
        //   DHHFunc.StatWithDiminishingReturns(baseStrength(12f), ChargeStrengthIncrease, AbilityLevel, 10, 0.75f)
        // Base impact strength used to compute the Charge ability hit force.
        public static int DHHChargeStrengthBaseValue = 12;
        // Strength increase applied each ability level before diminishing returns.
        public static int DHHChargeStrengthIncreasePerLevel = 1;
        // Ability level threshold where extra strength gain starts to shrink.
        public static int DHHChargeStrengthThresholdLevel = 10;
        // Fraction that scales down extra strength beyond the threshold.
        public static float DHHChargeStrengthDiminishingFactor = 0.75f;

        // Jump (DHH)
        // Default values mirror vanilla HopHandler.JumpForce:
        //   DHHFunc.StatWithDiminishingReturns(3f, jumpIncrease(0.11f), PowerLevel+1, 5, 0.9f)
        // Base slot value that determines the vertical boost for hop upgrades (vanilla=3).
        public static int DHHHopJumpBaseValue = 2;
        // Additional boost added for each hop upgrade level before the threshold (vanilla=0.11).
        public static float DHHHopJumpIncreasePerLevel = 0.3f;
        // Level after which hop upgrades start diminishing in effectiveness (vanilla=5).
        public static int DHHHopJumpThresholdLevel = 2;
        // Curve factor that controls how quickly extra hop levels taper off.
        public static float DHHHopJumpDiminishingFactor = 0.9f;

        // Jump (DHH)
        // Default values mirror DeathHeadHopper JumpHandler:
        //   DHHFunc.StatWithDiminishingReturns(2.8f, forceIncrease(0.4f), PowerLevel+1, 5, 0.9f)
        // Base jump force the death head uses when leaping off the ground (vanilla base=2.8).
        public static float DHHJumpForceBaseValue = 0.5f;
        // Force increment applied for each power level before the threshold (vanilla inc=0.4).
        public static float DHHJumpForceIncreasePerLevel = 0.3f;
        // Threshold level where jump force increases start to diminish (vanilla=5).
        public static int DHHJumpForceThresholdLevel = 2;
        // Diminishing factor that cuts additional force beyond the threshold.
        public static float DHHJumpForceDiminishingFactor = 0.9f;

        // DeathHeadHopper shop tunables.
        // Maximum number of DeathHeadHopper items that can spawn in a shop run (-1 = unlimited).
        public static int DHHShopMaxItems = 5;
        // Chance for each DeathHeadHopper shop slot to spawn an item (1 = always spawn).
        public static float DHHShopSpawnChance = 0.5f;
        // Second-tier chance (after a slot is picked) that the DeathHeadHopper item actually appears.
        public static float ShopItemsSpawnChance = 0.5f;

        // Debug helpers.
        // Dump extra log lines that help trace the battery/ability logic.
        public static bool DebugLogging = false;
        // Kill the battery module entirely so no jump safeguards run (only for testing).
        public static bool DisableBatteryModule = false;
        // Skip the Harmony patches that rename charge data and sync cooldowns.
        public static bool DisableAbilityPatches = false;
        // Let the vanilla spectate camera run even when we normally override it during battery checks.
        public static bool DisableSpectateChecks = false;
    }
}

