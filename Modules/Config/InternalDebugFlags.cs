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
        //public static string DebugAutoSpawnMonsterNamesCsv = "ceiling eye,tricycle,spinny,heart hugger,thin man,hidden";
        public static string DebugAutoSpawnMonsterNamesCsv = "heart hugger";

        // Temporary diagnostics for Hidden carry pipeline in LastChance.
        public static bool DebugLastChanceHiddenCarryFlow = false;

        // Temporary diagnostics for Ceiling Eye / camera lock flow in LastChance.
        public static bool DebugLastChanceCeilingEyeFlow = false;
    }
}
