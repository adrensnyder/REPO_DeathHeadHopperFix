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
        // Done "ceiling eye,hidden,heart hugger,tricycle,tumbler"
        // Check "spinny,thin man,tumbler";
        public static bool EnableDebugSpawnRuntime = true;
        public static string DebugAutoSpawnMonsterNamesCsv = "thin man,thin man,thin man,thin man,thin man";

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

        // Temporary diagnostics for Thin Man on-screen camera pipeline in LastChance.
        public static bool DebugLastChanceThinManFlow = false;

        // Extra diagnostics for JumpForceModule "[Fix:Jump]" logs.
        public static bool DebugJumpForceLog = false;

        // Extra diagnostics for charge tuning and stamina recharge logs.
        public static bool DebugDhhChargeTuningLog = false;
        public static bool DebugDhhChargeRechargeLog = false;

        // Extra diagnostics for DHH battery jump-allowance logs.
        public static bool DebugDhhBatteryJumpAllowanceLog = false;
    }
}
