using DeathHeadHopper.DeathHead;
using HarmonyLib;

#nullable enable

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class PlayerDeathHeadUpdatePatch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayerDeathHead __instance)
        {
            if (__instance == null)
                return;

            NativeJumpSuppression.ClearVanillaJumpStateIfDhhControls(__instance);
            PlayerDeathHeadStuckOverrideReset.TryReset(__instance);

            var physGrabObject = __instance.physGrabObject;
            if (physGrabObject == null || !__instance.triggered)
                return;

            var avatar = __instance.playerAvatar;
            var isLocalAvatar = avatar != null &&
                                (!GameManager.Multiplayer() ||
                                 (avatar.photonView != null && avatar.photonView.IsMine));
            if (!isLocalAvatar)
                return;

            avatar!.transform.position = physGrabObject.transform.position;

            if (SemiFunc.InputMovementX() != 0f || SemiFunc.InputMovementY() != 0f)
            {
                var spectate = SpectateCamera.instance;
                if (spectate != null && (spectate.player == null || spectate.player == avatar))
                    spectate.player = avatar;
            }

        }
    }

    internal static class PlayerDeathHeadStuckOverrideReset
    {
        internal static void TryReset(PlayerDeathHead head)
        {
            if (head == null || SpectateCamera.instance == null ||
                SpectateCamera.instance.currentState != SpectateCamera.State.Head)
                return;

            var localHead = PlayerController.instance?.playerAvatarScript?.playerDeathHead;
            if (localHead != head || !head.overrideSpectated || head.physGrabObject == null || head.physGrabObject.grabbed)
                return;

            head.OverrideSpectatedReset();
        }
    }

    internal static class NativeJumpSuppression
    {
        internal static bool IsDhhControllerActive(PlayerDeathHead? head)
        {
            if (head == null || !head.triggered)
                return false;

            var controller = head.GetComponent<DeathHeadController>();
            return controller != null && controller.spectated;
        }

        internal static void ClearVanillaJumpStateIfDhhControls(PlayerDeathHead? head)
        {
            if (!IsDhhControllerActive(head))
                return;

            head!.spectatedJumpLocalPlayerInput = false;
            head.spectatedJumpCharging = false;
            head.spectatedJumpGrounded = false;
            head.spectatedJumpGroundedTimer = 0f;
            head.spectatedJumpChargeAmount = 0f;
            head.spectatedJumpForce = 0f;
            head.spectatedJumpCooldown = 0f;
        }

        internal static bool SuppressInput(PlayerDeathHead? head)
        {
            if (!IsDhhControllerActive(head))
                return false;

            ClearVanillaJumpStateIfDhhControls(head);
            return true;
        }
    }

    [HarmonyPatch]
    internal static class PlayerDeathHeadNativeJumpPatch
    {
        [HarmonyPatch(typeof(PlayerDeathHead), nameof(PlayerDeathHead.SpectatedJumpLocalInput))]
        [HarmonyPrefix]
        private static bool SpectatedJumpLocalInput_Prefix(PlayerDeathHead __instance, ref bool _input)
        {
            if (!NativeJumpSuppression.SuppressInput(__instance))
                return true;

            _input = false;
            return true;
        }

        [HarmonyPatch(typeof(PlayerDeathHead), nameof(PlayerDeathHead.SpectatedJumpLocalInputRPC))]
        [HarmonyPrefix]
        private static bool SpectatedJumpLocalInputRPC_Prefix(PlayerDeathHead __instance, ref bool _input)
        {
            if (!NativeJumpSuppression.SuppressInput(__instance))
                return true;

            _input = false;
            return true;
        }
    }
}
