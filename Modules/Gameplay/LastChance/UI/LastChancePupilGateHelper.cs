#nullable enable

using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Runtime;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.UI
{
    internal static class LastChancePupilGateHelper
    {
        private static readonly FieldInfo? HeadTriggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");

        internal static bool IsEnabled()
        {
            // Forced LastChance-mode gate: ON flag alone is not enough outside LastChance mode.
            return FeatureFlags.LastChangeMode &&
                   FeatureFlags.LastChancePupilVisualsEnabled &&
                   LastChanceTimerController.IsActive;
        }

        internal static bool IsEligibleHead(PlayerDeathHead? head, PlayerAvatar? player)
        {
            if (head == null || !IsEnabled())
            {
                return false;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                return false;
            }

            return HeadTriggeredField?.GetValue(head) as bool? ?? false;
        }
    }
}
