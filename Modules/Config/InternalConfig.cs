#nullable enable

namespace DeathHeadHopperFix.Modules.Config
{
    // Internal runtime tunables (not exposed via BepInEx).
    // Intended for engineering-level control of shared behavior.
    internal static class InternalConfig
    {
        // Controls only camera-forcing behavior for monster lock pipelines during LastChance.
        // false: keep detection/lock gameplay active, but do NOT force the player's camera target.
        // true: allow camera forcing while lock is active (still bounded by max-lock and cooldown timers).
        internal static bool LastChanceMonstersForceCameraOnLock = false;

        internal static float LastChanceMonstersCameraLockMaxSeconds = 5f;
        internal static float LastChanceMonstersCameraLockCooldownSeconds = 15f;
        internal static float LastChanceMonstersCameraLockKeepAliveGraceSeconds = 0.6f;
        internal static float LastChanceMonstersVisionLockSourceBucketSize = 1f;
    }
}
