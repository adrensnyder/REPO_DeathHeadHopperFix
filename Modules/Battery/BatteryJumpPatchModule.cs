#nullable enable

using BepInEx.Logging;
using DeathHeadHopper.DeathHead;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Managers;
using DeathHeadHopperFix.API.Battery;
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

            try
            {
                harmony.CreateClassProcessor(typeof(DeathHeadControllerStartPatch)).Patch();
                harmony.CreateClassProcessor(typeof(DhhInputManagerJumpPatch)).Patch();
                harmony.CreateClassProcessor(typeof(JumpHandlerHeadJumpedPatch)).Patch();
                s_applied = true;
            }
            catch (System.Exception ex)
            {
                log?.LogWarning($"DHH battery jump patch setup failed: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(DeathHeadController), nameof(DeathHeadController.Start))]
        private static class DeathHeadControllerStartPatch
        {
            [HarmonyPostfix]
            private static void Postfix(DeathHeadController __instance)
            {
                DeathHeadControllerStartPostfix(__instance);
            }
        }

        [HarmonyPatch(typeof(DHHInputManager), nameof(DHHInputManager.Jump))]
        private static class DhhInputManagerJumpPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(DHHInputManager __instance)
            {
                return DhhInputManagerJumpPrefix(__instance);
            }
        }

        [HarmonyPatch(typeof(JumpHandler), nameof(JumpHandler.HeadJumped), typeof(float))]
        private static class JumpHandlerHeadJumpedPatch
        {
            [HarmonyPostfix]
            private static void Postfix(JumpHandler __instance)
            {
                JumpHandlerHeadJumpedPostfix(__instance);
            }
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
            if (!BatteryJumpOverrideLease.TryGetEffectiveState(out var batteryJumpEnabled) || !batteryJumpEnabled || InternalDebugFlags.DisableBatteryModule)
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
            if (!BatteryJumpOverrideLease.TryGetEffectiveState(out var batteryJumpEnabled) || !batteryJumpEnabled || InternalDebugFlags.DisableBatteryModule || __instance == null)
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
