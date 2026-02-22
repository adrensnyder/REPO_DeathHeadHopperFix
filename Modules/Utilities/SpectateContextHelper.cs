#nullable enable

using System.Reflection;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class SpectateContextHelper
    {
        private static readonly FieldInfo? s_spectatePlayerField =
            AccessTools.Field(typeof(SpectateCamera), "player");
        private static readonly FieldInfo? s_playerDeathHeadSpectatedField =
            AccessTools.Field(typeof(PlayerDeathHead), "spectated");

        internal static bool IsSpectatingLocalPlayerTarget()
        {
            var spectate = SpectateCamera.instance;
            var local = PlayerAvatar.instance;
            if (spectate == null || local == null || s_spectatePlayerField == null)
            {
                return false;
            }

            var target = s_spectatePlayerField.GetValue(spectate) as PlayerAvatar;
            return target != null && ReferenceEquals(target, local);
        }

        internal static bool IsSpectatingLocalDeathHead()
        {
            var spectate = SpectateCamera.instance;
            if (spectate == null || !spectate.CheckState(SpectateCamera.State.Head))
            {
                return false;
            }

            var localHead = PlayerController.instance?.playerAvatarScript?.playerDeathHead;
            if (localHead == null || s_playerDeathHeadSpectatedField == null)
            {
                return false;
            }

            return s_playerDeathHeadSpectatedField.GetValue(localHead) as bool? ?? false;
        }

        internal static bool IsLocalDeathHeadSpectated()
        {
            var localHead = PlayerController.instance?.playerAvatarScript?.playerDeathHead;
            if (localHead == null || s_playerDeathHeadSpectatedField == null)
            {
                return false;
            }

            return s_playerDeathHeadSpectatedField.GetValue(localHead) as bool? ?? false;
        }
    }
}
