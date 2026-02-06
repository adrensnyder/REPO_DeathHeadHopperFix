#nullable enable

using System;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    internal static class LastChanceSaveDeleteState
    {
        private static bool s_blockAutoDelete = true;

        internal static bool BlockAutoDelete
        {
            get => s_blockAutoDelete;
            set => s_blockAutoDelete = value;
        }

        internal static bool ShouldBlockAutoDelete()
        {
            if (!FeatureFlags.LastChangeMode || !LastChanceTimerController.IsActive)
            {
                return false;
            }

            if (!AllPlayersDeadGuard.AllPlayersDisabled())
            {
                s_blockAutoDelete = true;
                return false;
            }

            return s_blockAutoDelete;
        }

        internal static void AllowAutoDelete()
        {
            s_blockAutoDelete = false;
        }

        internal static void ResetAutoDeleteBlock()
        {
            s_blockAutoDelete = true;
        }
    }

    [HarmonyPatch(typeof(StatsManager), "SaveFileDelete")]
    internal static class StatsManagerSaveFileDeleteLastChancePatch
    {
        private const string LogKey = "LastChance.SaveDelete.Blocked";
        [ThreadStatic]
        private static bool s_allowManualDelete;

        internal static void AllowManualDelete()
        {
            s_allowManualDelete = true;
        }

        [HarmonyPrefix]
        private static bool Prefix(string saveFileName)
        {
            if (!LastChanceSaveDeleteState.ShouldBlockAutoDelete())
            {
                return true;
            }

            if (s_allowManualDelete)
            {
                return true;
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(LogKey, 120))
            {
                UnityEngine.Debug.Log($"[LastChance] Blocked auto delete '{saveFileName}'.");
            }

            return false;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            s_allowManualDelete = false;
        }
    }

    [HarmonyPatch(typeof(MenuPageSaves), "OnDeleteGame")]
    internal static class MenuPageSavesOnDeleteGameLastChancePatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            StatsManagerSaveFileDeleteLastChancePatch.AllowManualDelete();
        }
    }
}
