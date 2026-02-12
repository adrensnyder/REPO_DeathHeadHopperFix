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
        // Done "ceiling eye,hidden,heart hugger"
        // Check "tricycle,spinny,,thin man,tumbler";
        public static string DebugAutoSpawnMonsterNamesCsv = "tricycle,tricycle,tricycle";

        // Temporary diagnostics for Hidden carry pipeline in LastChance.
        public static bool DebugLastChanceHiddenCarryFlow = false;

        // Temporary diagnostics for Ceiling Eye / camera lock flow in LastChance.
        public static bool DebugLastChanceCeilingEyeFlow = false;

        // Temporary diagnostics for Heart Hugger gas capture / pull in LastChance.
        public static bool DebugLastChanceHeartHuggerFlow = false;
    }
}
