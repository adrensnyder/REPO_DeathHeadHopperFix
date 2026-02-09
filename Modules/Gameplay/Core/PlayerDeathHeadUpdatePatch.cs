using System.Reflection;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

#nullable enable

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class PlayerDeathHeadUpdatePatch
    {
        private static readonly FieldInfo s_triggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");
        private static readonly FieldInfo s_spectatePlayerField = AccessTools.Field(typeof(SpectateCamera), "player");
        private static float s_jumpTimer = 1f;

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

            avatar.transform.position = ___physGrabObject.transform.position;

            if (SemiFunc.InputMovementX() != 0f || SemiFunc.InputMovementY() != 0f)
            {
                if (s_spectatePlayerField != null && SpectateCamera.instance != null)
                    s_spectatePlayerField.SetValue(SpectateCamera.instance, avatar);
            }

            if (s_jumpTimer > 0f)
            {
                s_jumpTimer -= Time.deltaTime;
                return;
            }

            if (!SemiFunc.InputHold(InputKey.Jump))
                return;

            s_jumpTimer = 1f;
            var cam = Camera.main;
            if (cam == null)
                return;

            var forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var dir = Vector3.zero;
            var mx = SemiFunc.InputMovementX();
            var my = SemiFunc.InputMovementY();

            if (my > 0f) dir += forward;
            if (my < 0f) dir -= forward;
            if (mx < 0f) dir -= right;
            if (mx > 0f) dir += right;

            dir = (dir + Vector3.up).normalized * 4.8f;

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

            if (FeatureFlags.BatteryJumpEnabled && !DHHBatteryHelper.HasRecentJumpConsumption())
            {
                var spectate = SpectateCamera.instance;
                if (spectate != null)
                {
                    var usage = DHHBatteryHelper.GetEffectiveBatteryJumpUsage();
                    var reference = DHHBatteryHelper.GetJumpThreshold();
                    DHHBatteryHelper.ApplyConsumption(spectate, usage, reference);
                }
            }
        }
    }
}
