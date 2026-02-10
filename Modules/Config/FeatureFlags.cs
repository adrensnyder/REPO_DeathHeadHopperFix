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
            public const string LastChanceQuick = "7a. LastChance: Quick Setup";
            public const string LastChanceTimer = "7b. LastChance: Timer Calculation";
            public const string LastChanceGameplay = "7c. LastChance: Gameplay & UI";
            public const string Spectate = "8. Spectate";
            public const string Debug = "9. Debug";
            
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
            public const string DHHShopMaxItems = "Maximum number of DeathHeadHopper mod items that can spawn in the shop (-1 = unlimited).";
            public const string DHHShopSpawnChance = "Chance each DeathHeadHopper shop slot actually spawns an item.";
            public const string ShopItemsSpawnChance = "Second-tier chance that a DeathHeadHopper slot produces an item after it was selected.";
            public const string LastChanceMode = "When true, prevent the vanilla run manager from switching to the dump level when all players die.";
            public const string LastChanceTimerSeconds = "LastChance timer duration in seconds (integer, 30s steps).";
            public const string LastChanceDynamicTimerEnabled = "Enable dynamic LastChance timer scaling from base timer and run context metrics.";
            public const string LastChanceTimerPerRequiredPlayerSeconds = "Extra seconds added per required player that must reach the truck.";
            public const string LastChanceLevelContextRoomWeight = "Extra multiplier weight applied to level contribution from room-path difficulty (0 disables room context).";
            public const string LastChanceLevelContextMonsterWeight = "Extra multiplier weight applied to level contribution from active search monsters (0 disables monster context).";
            public const string LastChanceTimerPerFarthestMeterSeconds = "Extra seconds added per meter of total required-player distance to truck.";
            public const string LastChanceTimerPerBelowTruckPlayerSeconds = "Extra seconds added per required player below the truck threshold height.";
            public const string LastChanceTimerPerBelowTruckMeterSeconds = "Extra seconds added per meter below threshold (only when height delta <= threshold).";
            public const string LastChanceBelowTruckThresholdMeters = "Height delta threshold (playerY - truckY) below which low-altitude penalties apply. -0.5 means at least half meter below.";
            public const string LastChanceTimerPerRoomStepSeconds = "Extra seconds added per total room-step count (sum of required players shortest paths to truck).";
            public const string LastChanceDynamicMaxMinutesAtLevel = "Level where level-based growth stops increasing (timer still only capped by MaxMinutes).";
            public const string LastChanceDynamicMaxMinutes = "Hard cap (minutes) for final LastChance timer after dynamic scaling.";
            public const string LastChanceConsolationMoney = "LastChance consolation money added on success (integer).";
            public const string LastChanceMissingPlayers = "Number of players allowed to stay outside the truck before LastChance success triggers (0 = all players required).";
            public const string LastChanceSurrenderSeconds = "Seconds the player must hold Crouch to surrender during LastChance.";
            public const string LastChanceIndicators = "LastChance indicators mode: None, Direction.";
            public const string LastChanceIndicatorHoldSeconds = "Seconds to hold Tumble before triggering the selected indicator.";
            public const string LastChanceIndicatorDirectionDurationSeconds = "Seconds the Direction indicator stays active once triggered.";
            public const string LastChanceIndicatorDirectionCooldownSeconds = "Cooldown seconds before Direction can be triggered again.";
            public const string LastChanceIndicatorDirectionPenaltyMaxSeconds = "Maximum timer penalty per Direction trigger (low difficulty, and always used when dynamic timer is disabled).";
            public const string LastChanceIndicatorDirectionPenaltyMinSeconds = "Minimum timer penalty per Direction trigger (high difficulty).";
            public const string LastChanceMonstersSearchEnabled = "During LastChance, monsters treat disabled players as valid targets (harder return to truck).";
            public const string LastChanceMonstersVoiceEnemyOnlyEnabled = "During LastChance, disabled death-head voice keeps enemy reactions/talk animation but mutes playback to players (enemy-only voice aggro).";
            public const string LastChanceTimerPerMonsterSeconds = "Extra seconds added per active spawned monster when LastChanceMonstersSearch is enabled.";
            public const string DebugLogging = "Dump extra log lines that help trace the battery/ability logic.";
            public const string DisableBatteryModule = "Temporarily disable the BatteryModule component.";
            public const string DisableAbilityPatches = "Skip ability-related Harmony patches (charge rename, ability cooldown sync, etc.).";
            public const string DisableSpectateChecks = "Skip SpectateCamera override/hints when evaluating battery status (debug test).";
            public const string SpectateDeadPlayers = "Allow SpectateCamera to cycle through disabled players (dead bodies) when toggling targets.";
            public const string SpectateDeadPlayersMode = "Mode for dead-player spectate switch: Always, LastChanceOnly, Disabled.";
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
        public static int DHHShopMaxItems = 6;

        [FeatureConfigEntry(Sections.Upgrades, Descriptions.DHHShopSpawnChance, Min = 0.1f, Max = 1f)]
        public static float DHHShopSpawnChance = 0.5f;

        [FeatureConfigEntry(Sections.Upgrades, Descriptions.ShopItemsSpawnChance, Min = 0.1f, Max = 1f)]
        public static float ShopItemsSpawnChance = 0.75f;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceMode)]
        [FeatureConfigAlias(Sections.LastChanceQuick, "LastChanceMode")]
        public static bool LastChangeMode = true;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceTimerSeconds, Min = 30f, Max = 600f)]
        public static int LastChanceTimerSeconds = 60;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceDynamicTimerEnabled)]
        public static bool LastChanceDynamicTimerEnabled = true;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceDynamicMaxMinutes, Min = 5f, Max = 20f)]
        public static int LastChanceDynamicMaxMinutes = 10;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceDynamicMaxMinutesAtLevel, Min = 5f, Max = 60f)]
        public static int LastChanceDynamicMaxMinutesAtLevel = 25;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceMissingPlayers, Min = 0f, Max = 32f)]
        public static int LastChanceMissingPlayers = 0;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceMonstersSearchEnabled)]
        public static bool LastChanceMonstersSearchEnabled = true;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceMonstersVoiceEnemyOnlyEnabled)]
        public static bool LastChanceMonstersVoiceEnemyOnlyEnabled = true;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceTimerPerMonsterSeconds, Min = 0f, Max = 60f)]
        public static float LastChanceTimerPerMonsterSeconds = 3f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceTimerPerRequiredPlayerSeconds, Min = 0f, Max = 120f)]
        public static float LastChanceTimerPerRequiredPlayerSeconds = 8f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceLevelContextRoomWeight, Min = 0f, Max = 3f)]
        public static float LastChanceLevelContextRoomWeight = 0.5f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceLevelContextMonsterWeight, Min = 0f, Max = 3f)]
        public static float LastChanceLevelContextMonsterWeight = 0.3f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceTimerPerFarthestMeterSeconds, Min = 0f, Max = 20f)]
        public static float LastChanceTimerPerFarthestMeterSeconds = 0.6f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceTimerPerRoomStepSeconds, Min = 0f, Max = 60f)]
        public static float LastChanceTimerPerRoomStepSeconds = 3f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceTimerPerBelowTruckPlayerSeconds, Min = 0f, Max = 120f)]
        public static float LastChanceTimerPerBelowTruckPlayerSeconds = 15f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceTimerPerBelowTruckMeterSeconds, Min = 0f, Max = 30f)]
        public static float LastChanceTimerPerBelowTruckMeterSeconds = 15f;

        [FeatureConfigEntry(Sections.LastChanceTimer, Descriptions.LastChanceBelowTruckThresholdMeters, Min = -5f, Max = 0f)]
        public static float LastChanceBelowTruckThresholdMeters = -0.5f;

        [FeatureConfigEntry(Sections.LastChanceQuick, Descriptions.LastChanceConsolationMoney, Min = 0f, Max = 5f)]
        public static int LastChanceConsolationMoney = 1;

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceSurrenderSeconds, Min = 2f, Max = 10f)]
        public static int LastChanceSurrenderSeconds = 5;

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceIndicators, Options = new[] { "None", "Direction" })]
        public static string LastChanceIndicators = "Direction";

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceIndicatorHoldSeconds, Min = 0.2f, Max = 5f)]
        public static float LastChanceIndicatorHoldSeconds = 2f;

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceIndicatorDirectionDurationSeconds, Min = 0.5f, Max = 20f)]
        public static float LastChanceIndicatorDirectionDurationSeconds = 5f;

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceIndicatorDirectionCooldownSeconds, Min = 1f, Max = 60f)]
        public static float LastChanceIndicatorDirectionCooldownSeconds = 15f;

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceIndicatorDirectionPenaltyMaxSeconds, Min = 0f, Max = 60f)]
        public static float LastChanceIndicatorDirectionPenaltyMaxSeconds = 8f;

        [FeatureConfigEntry(Sections.LastChanceGameplay, Descriptions.LastChanceIndicatorDirectionPenaltyMinSeconds, Min = 0f, Max = 60f)]
        public static float LastChanceIndicatorDirectionPenaltyMinSeconds = 4f;

        [FeatureConfigEntry(Sections.Spectate, Descriptions.SpectateDeadPlayers)]
        public static bool SpectateDeadPlayers = true;

        [FeatureConfigEntry(Sections.Spectate, Descriptions.SpectateDeadPlayersMode, Options = new[] { "Always", "LastChanceOnly", "Disabled" })]
        public static string SpectateDeadPlayersMode = "Always";

        [FeatureConfigEntry(Sections.Debug, Descriptions.DebugLogging, HostControlled = false)]
        public static bool DebugLogging = false;

        //[FeatureConfigEntry(Sections.Debug, Descriptions.DisableBatteryModule)]
        public static bool DisableBatteryModule = false;

        //[FeatureConfigEntry(Sections.Debug, Descriptions.DisableAbilityPatches)]
        public static bool DisableAbilityPatches = false;

        //[FeatureConfigEntry(Sections.Debug, Descriptions.DisableSpectateChecks)]
        public static bool DisableSpectateChecks = false;

        
    }
}
