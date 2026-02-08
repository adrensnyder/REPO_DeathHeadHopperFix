using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;

#nullable enable

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class PlayerDeathHeadUpdatePatch
    {
        private static readonly FieldInfo s_triggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");
        private static readonly FieldInfo s_spectatePlayerField = AccessTools.Field(typeof(SpectateCamera), "player");

        private static float _jumpTimer = 1f;

        [HarmonyPrefix]
        private static void Prefix(PlayerDeathHead __instance, PhysGrabObject ___physGrabObject)
        {
            if (__instance == null || ___physGrabObject == null)
                return;

            var avatar = __instance.playerAvatar;
            if (avatar == null || avatar.photonView == null || !avatar.photonView.IsMine)
                return;

            if (s_triggeredField == null || s_triggeredField.GetValue(__instance) is not bool triggered || !triggered)
                return;

            ((Component)avatar).transform.position = ((Component)___physGrabObject).transform.position;

            if (SemiFunc.InputMovementX() != 0f || SemiFunc.InputMovementY() != 0f)
            {
                // Avoid TargetExceptions by skipping SetValue when SpectateCamera.instance is null.
                if (s_spectatePlayerField != null && SpectateCamera.instance != null)
                    s_spectatePlayerField.SetValue(SpectateCamera.instance, avatar);
            }

            if (_jumpTimer > 0f)
            {
                _jumpTimer -= Time.deltaTime;
                return;
            }

            if (!SemiFunc.InputHold(InputKey.Jump))
                return;

            _jumpTimer = 1f;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector3 dir = Vector3.zero;
            float mx = SemiFunc.InputMovementX();
            float my = SemiFunc.InputMovementY();

            if (my > 0f) dir += forward;
            if (my < 0f) dir -= forward;
            if (mx < 0f) dir -= right;
            if (mx > 0f) dir += right;

            dir += Vector3.up;
            dir = dir.normalized * 4.8f;

            if (FeatureFlags.BatteryJumpEnabled)
            {
                var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
                if (!allowance.allowed)
                    return;
            }

            var grabber = avatar.physGrabber;
            if (grabber == null || avatar.photonView == null)
                return;

            ___physGrabObject.GrabLink(
                avatar.photonView.ViewID,
                0,
                grabber.physGrabPointPullerPosition - dir,
                Vector3.zero,
                Vector3.zero);

            ___physGrabObject.GrabStarted(grabber);
            ___physGrabObject.GrabEnded(grabber);

            // Battery consumption is centralized in BatteryJumpModule (head jump event listener).
            // Keep this patch responsible only for LastChance jump movement/allowance.
        }
    }
}
