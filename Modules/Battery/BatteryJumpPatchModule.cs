#nullable enable

using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Stamina;

namespace DeathHeadHopperFix.Modules.Battery
{
    internal static class BatteryJumpPatchModule
    {
        private static FieldInfo? s_jumpHandlerJumpBufferField;

        internal static void Apply(Harmony harmony, Assembly asm)
        {
            PatchDeathHeadControllerModulesIfPossible(harmony, asm);
            PatchJumpHandlerUpdateIfPossible(harmony, asm);
        }

        private static void PatchDeathHeadControllerModulesIfPossible(Harmony harmony, Assembly asm)
        {
            var controllerType = asm.GetType("DeathHeadHopper.DeathHead.DeathHeadController", throwOnError: false);
            if (controllerType == null)
                return;

            var startMethod = AccessTools.Method(controllerType, "Start");
            if (startMethod == null)
                return;

            var postfix = typeof(BatteryJumpPatchModule).GetMethod(nameof(DeathHeadController_Start_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(startMethod, postfix: new HarmonyMethod(postfix));
        }

        private static void DeathHeadController_Start_Postfix(object __instance)
        {
            if (__instance is not MonoBehaviour mono)
                return;

            var go = mono.gameObject;
            if (go.GetComponent<BatteryJumpModule>() == null)
            {
                go.AddComponent<BatteryJumpModule>();
            }

            if (go.GetComponent<StaminaRechargeModule>() == null)
            {
                go.AddComponent<StaminaRechargeModule>();
            }
        }

        private static void PatchJumpHandlerUpdateIfPossible(Harmony harmony, Assembly asm)
        {
            var jumpHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.JumpHandler", throwOnError: false);
            if (jumpHandlerType == null)
                return;

            s_jumpHandlerJumpBufferField = AccessTools.Field(jumpHandlerType, "jumpBufferTimer");
            if (s_jumpHandlerJumpBufferField == null)
                return;

            var updateMethod = AccessTools.Method(jumpHandlerType, "Update");
            if (updateMethod == null)
                return;

            var prefix = typeof(BatteryJumpPatchModule).GetMethod(nameof(JumpHandler_Update_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(updateMethod, prefix: new HarmonyMethod(prefix));
        }

        private static bool JumpHandler_Update_Prefix(MonoBehaviour __instance)
        {
            if (__instance == null || s_jumpHandlerJumpBufferField == null)
                return true;

            if (s_jumpHandlerJumpBufferField.GetValue(__instance) is not float buffer || buffer <= 0f)
                return true;

            if (!FeatureFlags.BatteryJumpEnabled)
                return true;

            var module = __instance.gameObject.GetComponent<BatteryJumpModule>();
            if (module == null)
                return true;

            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (allowance.allowed)
                return true;

            s_jumpHandlerJumpBufferField.SetValue(__instance, 0f);
            module.NotifyJumpBlocked(allowance.currentEnergy, allowance.reference, allowance.readyFlag);
            return false;
        }
    }
}
