#nullable enable

using System;
using System.Collections.Generic;
using DeathHeadHopperFix.Modules.Config;

namespace DeathHeadHopperFix.API.Battery
{
    public static class BatteryJumpOverrideLease
    {
        private const string BatteryJumpEnabledKey = "BatteryJumpEnabled";
        private static readonly object Sync = new();
        private static string? s_ownerId;
        private static bool s_overrideValue;
        private static Dictionary<string, string>? s_preRuntimeSnapshot;

        public static bool TryAcquireHostOverride(string ownerId, bool batteryJumpEnabled)
        {
            if (string.IsNullOrWhiteSpace(ownerId) || !SemiFunc.IsMasterClientOrSingleplayer())
            {
                return false;
            }

            var normalizedOwnerId = ownerId.Trim();
            var applyOverride = false;
            lock (Sync)
            {
                if (s_ownerId != null && !string.Equals(s_ownerId, normalizedOwnerId, StringComparison.Ordinal))
                {
                    return false;
                }

                if (s_ownerId == null)
                {
                    s_preRuntimeSnapshot = ConfigManager.SnapshotHostControlledKeys(new[] { BatteryJumpEnabledKey });
                    s_ownerId = normalizedOwnerId;
                    s_overrideValue = batteryJumpEnabled;
                    applyOverride = true;
                }
                else if (s_overrideValue != batteryJumpEnabled)
                {
                    s_overrideValue = batteryJumpEnabled;
                    applyOverride = true;
                }
            }

            if (applyOverride)
            {
                ConfigManager.SetHostRuntimeOverride(
                    BatteryJumpEnabledKey,
                    batteryJumpEnabled ? bool.TrueString : bool.FalseString);
            }

            return true;
        }

        public static bool ReleaseHostOverride(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                return false;
            }

            var normalizedOwnerId = ownerId.Trim();
            Dictionary<string, string>? preRuntimeSnapshot;
            lock (Sync)
            {
                if (s_ownerId == null || !string.Equals(s_ownerId, normalizedOwnerId, StringComparison.Ordinal))
                {
                    return false;
                }

                preRuntimeSnapshot = s_preRuntimeSnapshot;
                s_ownerId = null;
                s_overrideValue = false;
                s_preRuntimeSnapshot = null;
            }

            // Restore the configured value while the runtime override is still authoritative,
            // then clear the override so consumers observe exactly the pre-runtime state.
            if (preRuntimeSnapshot != null && preRuntimeSnapshot.Count > 0)
            {
                ConfigManager.ApplyHostSnapshot(preRuntimeSnapshot);
            }

            ConfigManager.ClearHostRuntimeOverride(BatteryJumpEnabledKey);
            return true;
        }

        public static bool TryGetEffectiveState(out bool batteryJumpEnabled)
        {
            lock (Sync)
            {
                if (s_ownerId != null && SemiFunc.IsMasterClientOrSingleplayer())
                {
                    batteryJumpEnabled = s_overrideValue;
                    return true;
                }
            }

            batteryJumpEnabled = FeatureFlags.BatteryJumpEnabled;
            return true;
        }
    }
}
