#nullable enable

namespace DeathHeadHopperFix.Modules.Config
{
    // Internal-only debug switches.
    // Not bound to BepInEx config and not intended for end users.
    internal static class InternalDebugFlags
    {
        public static bool DisableBatteryModule = false;
        public static bool DisableAbilityPatches = false;
        public static bool DisableSpectateChecks = false;

        // Comma-separated enemy names/tokens to auto-spawn for debug test.
        // Empty string => disabled.
        // Development note:
        // - EnableDebugSpawnRuntime=true by default is intentional while this repository is in active development/testing.
        // - This is expected and should not be treated as a production-critical issue by itself.
        // - Production-like behavior is obtained by setting EnableDebugSpawnRuntime=false (and/or clearing CSV).
        // Done "ceiling eye,hidden,heart hugger,tricycle,tumbler,thin man,spinny"
        // Check "";
        public static bool EnableDebugSpawnRuntime = true;
        public static float DebugAutoSpawnDelaySeconds = 15f;
        //public static string DebugAutoSpawnMonsterNamesCsv =
        //    "animal,bang,beamer,birthday boy,bomb thrower,bowtie,ceiling eye,duck,elsa,floater," +
        //    "gnome,head,head grabber,heart hugger,hidden,hunter,oogly,robe,runner,shadow," +
        //    "slow mouth,slow walker,spinny,thin man,tick,tricycle,tumbler,upscream,valuable thrower";
        public static string DebugAutoSpawnMonsterNamesCsv = "headman,headman,headman,headman";

        // Temporary diagnostics for Hidden carry pipeline in LastChance.
        public static bool DebugLastChanceHiddenCarryFlow = false;

        // Temporary diagnostics for Ceiling Eye / camera lock flow in LastChance.
        public static bool DebugLastChanceCeilingEyeFlow = false;

        // Temporary diagnostics for Heart Hugger gas capture / pull in LastChance.
        public static bool DebugLastChanceHeartHuggerFlow = false;

        // Temporary diagnostics for Tricycle path-blocking reaction flow in LastChance.
        public static bool DebugLastChanceTricycleFlow = false;

        // Temporary diagnostics for Spinny-like tumble lock pipelines in LastChance.
        public static bool DebugLastChanceSpinnyFlow = false;
        public static bool DebugLastChanceSpinnyVerbose = false;

        // Temporary diagnostics for Headgrab target/grab state transitions in LastChance.
        public static bool DebugLastChanceHeadgrabFlow = false;
        public static bool DebugLastChanceHeadgrabVerbose = false;

        // Temporary diagnostics for Headman (EnemyHeadController + shared chase states).
        public static bool DebugLastChanceHeadmanFlow = false;
        public static bool DebugLastChanceHeadmanVerbose = false;

        // Temporary diagnostics for Animal collision/hit pipeline in LastChance.
        public static bool DebugLastChanceAnimalCollisionFlow = false;
        public static bool DebugLastChanceAnimalCollisionVerbose = false;

        // Temporary diagnostics for Thin Man on-screen camera pipeline in LastChance.
        public static bool DebugLastChanceThinManFlow = false;
        
        // Temporary diagnostics for LastChance eyes/pupil visibility + override bypass flow.
        public static bool DebugLastChanceEyesFlow = false;

        // Extra diagnostics for JumpForceModule "[Fix:Jump]" logs.
        public static bool DebugJumpForceLog = false;

        // Extra diagnostics for charge tuning and stamina recharge logs.
        public static bool DebugDhhChargeTuningLog = false;
        public static bool DebugDhhChargeRechargeLog = false;

        // Extra diagnostics for DHH battery jump-allowance logs.
        public static bool DebugDhhBatteryJumpAllowanceLog = false;

        // Extra diagnostics for slot2 Direction energy/visibility preview checks.
        public static bool DebugDirectionSlotEnergyPreviewLog = false;

        // Extra diagnostics for global guard that blocks velocity writes on kinematic rigidbodies.
        public static bool DebugPhysicsKinematicVelocityGuardLog = false;
    }
}
