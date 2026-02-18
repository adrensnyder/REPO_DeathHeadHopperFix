using System.Reflection;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

#nullable enable

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class PlayerDeathHeadUpdatePatch
    {
        private const string JumpTraceKey = "Fix:Jump.Trace";
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
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(JumpTraceKey, 2))
            {
                Debug.Log($"[Fix:Jump] Triggered jump input. move=({mx:0.00},{my:0.00}) dirMag={dir.magnitude:0.00}");
            }

            if (FeatureFlags.BatteryJumpEnabled)
            {
                var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
                if (!allowance.allowed)
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(JumpTraceKey + ".Blocked", 2))
                    {
                        Debug.Log($"[Fix:Jump] Blocked by battery gate. energy={allowance.currentEnergy:0.000} ref={allowance.reference:0.000} ready={allowance.readyFlag}");
                    }
                    return;
                }

                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(JumpTraceKey + ".BatteryOk", 2))
                {
                    Debug.Log($"[Fix:Jump] Battery gate passed. energy={allowance.currentEnergy:0.000} ref={allowance.reference:0.000} ready={allowance.readyFlag}");
                }
            }
            else if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(JumpTraceKey + ".NoBattery", 2))
            {
                Debug.Log("[Fix:Jump] Battery gate disabled (BatteryJumpEnabled=false).");
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
            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(JumpTraceKey + ".Applied", 2))
            {
                Debug.Log("[Fix:Jump] Applied grab-based jump impulse.");
            }

            if (FeatureFlags.BatteryJumpEnabled && !DHHBatteryHelper.HasRecentJumpConsumption())
            {
                var spectate = SpectateCamera.instance;
                if (spectate != null)
                {
                    var usage = DHHBatteryHelper.GetEffectiveBatteryJumpUsage();
                    var reference = DHHBatteryHelper.GetJumpThreshold();
                    DHHBatteryHelper.ApplyConsumption(spectate, usage, reference);
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog(JumpTraceKey + ".Consume", 2))
                    {
                        Debug.Log($"[Fix:Jump] Applied battery consumption. usage={usage:0.000} ref={reference:0.000}");
                    }
                }
            }
        }
    }
}

