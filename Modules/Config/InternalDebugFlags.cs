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
