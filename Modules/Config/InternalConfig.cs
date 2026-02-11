#nullable enable

namespace DeathHeadHopperFix.Modules.Config
{
    // Internal runtime tunables (not exposed via BepInEx).
    // Intended for engineering-level control of shared behavior.
    internal static class InternalConfig
    {
        // If false, keep gameplay lock logic but skip camera forcing.
        internal static bool LastChanceMonstersForceCameraOnLock = false;

        internal static float LastChanceMonstersCameraLockMaxSeconds = 5f;
        internal static float LastChanceMonstersCameraLockCooldownSeconds = 15f;
        internal static float LastChanceMonstersCameraLockKeepAliveGraceSeconds = 0.6f;
    }
}
