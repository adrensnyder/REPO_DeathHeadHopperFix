#nullable enable

using BepInEx.Logging;
using DeathHeadHopper.DeathHead;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Stamina;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Battery
{
    internal static class BatteryJumpPatchModule
    {
        private static bool s_applied;

        internal static void Apply(Harmony harmony, ManualLogSource? log)
        {
            if (s_applied || harmony == null)
                return;

            var controllerStart = AccessTools.Method(typeof(DeathHeadController), nameof(DeathHeadController.Start));
            var inputJump = AccessTools.Method(typeof(DHHInputManager), nameof(DHHInputManager.Jump));
            var headJumped = AccessTools.Method(typeof(JumpHandler), nameof(JumpHandler.HeadJumped), new[] { typeof(float) });
            if (controllerStart == null || inputJump == null || headJumped == null)
            {
                log?.LogWarning("DHH battery jump disabled: one or more required publicized members are unavailable.");
                return;
            }

            harmony.Patch(controllerStart,
                postfix: new HarmonyMethod(typeof(BatteryJumpPatchModule), nameof(DeathHeadControllerStartPostfix)));
            harmony.Patch(inputJump,
                prefix: new HarmonyMethod(typeof(BatteryJumpPatchModule), nameof(DhhInputManagerJumpPrefix)));
            harmony.Patch(headJumped,
                postfix: new HarmonyMethod(typeof(BatteryJumpPatchModule), nameof(JumpHandlerHeadJumpedPostfix)));
            s_applied = true;
        }

        private static void DeathHeadControllerStartPostfix(DeathHeadController __instance)
        {
            if (__instance == null)
                return;

            var go = __instance.gameObject;
            if (go.GetComponent<BatteryJumpModule>() == null)
                go.AddComponent<BatteryJumpModule>();
            if (go.GetComponent<StaminaRechargeModule>() == null)
                go.AddComponent<StaminaRechargeModule>();
        }

        private static bool DhhInputManagerJumpPrefix(DHHInputManager __instance)
        {
            if (!FeatureFlags.BatteryJumpEnabled || InternalDebugFlags.DisableBatteryModule)
                return true;

            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (allowance.allowed)
                return true;

            __instance?.headController?.GetComponent<BatteryJumpModule>()
                ?.NotifyJumpBlocked(allowance.currentEnergy, allowance.reference, allowance.readyFlag);
            return false;
        }

        private static void JumpHandlerHeadJumpedPostfix(JumpHandler __instance)
        {
            if (!FeatureFlags.BatteryJumpEnabled || InternalDebugFlags.DisableBatteryModule || __instance == null)
                return;

            var avatar = __instance.controller?.deathHead?.playerAvatar;
            if (avatar == null || !avatar.isLocal)
                return;

            var spectate = SpectateCamera.instance;
            if (spectate == null)
                return;

            DHHBatteryHelper.ApplyConsumption(
                spectate,
                DHHBatteryHelper.GetEffectiveBatteryJumpUsage(),
                DHHBatteryHelper.GetJumpThreshold());
        }
    }
}
