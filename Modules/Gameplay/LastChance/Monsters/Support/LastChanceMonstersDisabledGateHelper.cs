#nullable enable

using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support
{
    internal static class LastChanceMonstersDisabledGateHelper
    {
        internal static bool ShouldTreatDisabledAsActive(PlayerAvatar? player)
        {
            return player != null &&
                   LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() &&
                   LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
        }
    }
}
