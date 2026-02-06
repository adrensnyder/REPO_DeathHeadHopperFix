using System;
using System.Collections.Generic;

namespace DeathHeadHopperFix.Modules.Utilities
{
    /// <summary>
    /// Limits repetitive logs that run inside Update/LateUpdate/Tick loops.
    /// Usage:
    ///   if (LogLimiter.ShouldLog("DHHBattery.JumpAllowance")) Logger.LogInfo(...);
    /// </summary>
    internal static class LogLimiter
    {
        // Default: 120 frames (~2s at 60fps)
        public const int DefaultFrameInterval = 240;

        private static readonly Dictionary<string, int> _lastFrameByKey = new(StringComparer.Ordinal);

        public static bool ShouldLog(string key, int frameInterval = DefaultFrameInterval)
        {
            if (string.IsNullOrEmpty(key))
                return true;

            if (frameInterval <= 0)
                return true;

            int current = UnityEngine.Time.frameCount;

            if (_lastFrameByKey.TryGetValue(key, out int last))
            {
                if (current - last < frameInterval)
                    return false;
            }

            _lastFrameByKey[key] = current;
            return true;
        }

        public static void Reset(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _lastFrameByKey.Remove(key);
        }

        public static void Clear()
        {
            _lastFrameByKey.Clear();
        }
    }
}
