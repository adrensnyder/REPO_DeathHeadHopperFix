#nullable enable

using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;

namespace DeathHeadHopperFix.API.Abilities
{
    public static class AbilitySlotOrchestrator
    {
        public static bool TryRegister(AbilitySlotRegistration registration)
        {
            return AbilityModule.TryRegisterProvider(registration);
        }

        public static bool TryUpdate(string ownerId, AbilitySlotState state)
        {
            return AbilityModule.TryUpdateProvider(ownerId, state);
        }

        public static bool TryStartCooldown(string ownerId, float seconds)
        {
            return AbilityModule.TryStartProviderCooldown(ownerId, seconds);
        }

        public static bool Unregister(string ownerId)
        {
            return AbilityModule.UnregisterProvider(ownerId);
        }
    }
}
