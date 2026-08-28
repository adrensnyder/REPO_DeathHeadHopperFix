#nullable enable

using System.Collections.Generic;
using BepInEx.Logging;
using DeathHeadHopper.DeathHead.Handlers;
using DeathHeadHopper.Helpers;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class JumpForceModule
    {
        private static readonly HashSet<JumpHandler> JumpHandlers = new();
        private static readonly HashSet<HopHandler> HopHandlers = new();
        private static bool s_applied;

        internal static void Apply(Harmony harmony, ManualLogSource? log)
        {
            if (s_applied || harmony == null)
                return;

            var jumpForceGetter = AccessTools.PropertyGetter(typeof(JumpHandler), nameof(JumpHandler.JumpForce));
            var hopJumpForceGetter = AccessTools.PropertyGetter(typeof(HopHandler), nameof(HopHandler.JumpForce));
            var hopMoveForceGetter = AccessTools.PropertyGetter(typeof(HopHandler), nameof(HopHandler.MoveForce));
            var jumpAwake = AccessTools.Method(typeof(JumpHandler), nameof(JumpHandler.Awake));
            var hopAwake = AccessTools.Method(typeof(HopHandler), nameof(HopHandler.Awake));
            var jumpHead = AccessTools.Method(typeof(JumpHandler), nameof(JumpHandler.JumpHead), new[] { typeof(UnityEngine.Vector3) });

            if (jumpForceGetter == null || hopJumpForceGetter == null || hopMoveForceGetter == null ||
                jumpAwake == null || hopAwake == null || jumpHead == null)
            {
                log?.LogWarning("DHH jump tuning disabled: one or more required publicized members are unavailable.");
                return;
            }

            harmony.Patch(jumpForceGetter,
                prefix: new HarmonyMethod(typeof(JumpForceModule), nameof(JumpForceGetterPrefix)));
            harmony.Patch(hopJumpForceGetter,
                prefix: new HarmonyMethod(typeof(JumpForceModule), nameof(HopJumpForceGetterPrefix)));
            harmony.Patch(hopMoveForceGetter,
                prefix: new HarmonyMethod(typeof(JumpForceModule), nameof(HopMoveForceGetterPrefix)));
            harmony.Patch(jumpAwake,
                postfix: new HarmonyMethod(typeof(JumpForceModule), nameof(JumpHandlerAwakePostfix)));
            harmony.Patch(hopAwake,
                postfix: new HarmonyMethod(typeof(JumpForceModule), nameof(HopHandlerAwakePostfix)));
            harmony.Patch(jumpHead,
                postfix: new HarmonyMethod(typeof(JumpForceModule), nameof(JumpHeadPostfix)));

            ConfigManager.HostControlledChanged += ApplyAll;
            s_applied = true;
        }

        private static void JumpHandlerAwakePostfix(JumpHandler __instance)
        {
            if (__instance == null)
                return;

            JumpHandlers.Add(__instance);
            ApplyJumpFields(__instance);
        }

        private static void HopHandlerAwakePostfix(HopHandler __instance)
        {
            if (__instance == null)
                return;

            HopHandlers.Add(__instance);
            ApplyHopFields(__instance);
        }

        private static void ApplyAll()
        {
            JumpHandlers.RemoveWhere(handler => handler == null);
            HopHandlers.RemoveWhere(handler => handler == null);

            foreach (var handler in JumpHandlers)
                ApplyJumpFields(handler);
            foreach (var handler in HopHandlers)
                ApplyHopFields(handler);
        }

        private static void ApplyJumpFields(JumpHandler handler)
        {
            handler.forceIncrease = FeatureFlags.DHHJumpForceIncreasePerLevel;
            handler.jumpVertical = FeatureFlags.DHHJumpVertical;
            handler.rotationForce = FeatureFlags.DHHJumpRotationForce;
            handler.jumpCooldown = FeatureFlags.DHHJumpCooldown;
        }

        private static void ApplyHopFields(HopHandler handler)
        {
            handler.jumpIncrease = FeatureFlags.DHHHopJumpIncreasePerLevel;
            handler.moveIncrease = FeatureFlags.DHHHopMoveIncreasePerLevel;
            handler.rotationForce = FeatureFlags.DHHHopRotationForce;
            handler.damping = FeatureFlags.DHHHopDamping;
            handler.angleThreshold = FeatureFlags.DHHHopAngleThreshold;
            handler.velocityThreshold = FeatureFlags.DHHHopVelocityThreshold;
            handler.cooldown = FeatureFlags.DHHHopCooldown;
            handler.moveDelay = FeatureFlags.DHHHopMoveDelay;
        }

        private static bool JumpForceGetterPrefix(JumpHandler __instance, ref float __result)
        {
            __result = DHHFunc.StatWithDiminishingReturns(
                FeatureFlags.DHHJumpForceBaseValue,
                FeatureFlags.DHHJumpForceIncreasePerLevel,
                __instance.PowerLevel + 1,
                FeatureFlags.DHHJumpForceThresholdLevel,
                FeatureFlags.DHHJumpForceDiminishingFactor);
            return false;
        }

        private static bool HopJumpForceGetterPrefix(HopHandler __instance, ref float __result)
        {
            __result = DHHFunc.StatWithDiminishingReturns(
                FeatureFlags.DHHHopJumpBaseValue,
                FeatureFlags.DHHHopJumpIncreasePerLevel,
                __instance.PowerLevel + 1,
                FeatureFlags.DHHHopJumpThresholdLevel,
                FeatureFlags.DHHHopJumpDiminishingFactor);
            return false;
        }

        private static bool HopMoveForceGetterPrefix(HopHandler __instance, ref float __result)
        {
            var level = __instance.PowerLevel;
            if (level <= 0)
            {
                __result = 0f;
                return false;
            }

            __result = DHHFunc.StatWithDiminishingReturns(
                FeatureFlags.DHHHopMoveBaseValue,
                FeatureFlags.DHHHopMoveIncreasePerLevel,
                level,
                FeatureFlags.DHHHopMoveThresholdLevel,
                FeatureFlags.DHHHopMoveDiminishingFactor);
            return false;
        }

        private static void JumpHeadPostfix(JumpHandler __instance)
        {
            if (__instance != null)
                __instance.jumpBufferTimer = FeatureFlags.DHHJumpBufferDuration;
        }
    }
}
