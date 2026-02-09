#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    internal static class JumpForceModule
    {
        private const string HopForceLogKey = "Fix:Hop.JumpForce";
        private const string JumpForceLogKey = "Fix:Jump.HeadJumpForce";

        private static MethodInfo? s_hopHandlerPowerLevelGetter;
        private static MethodInfo? s_jumpHandlerPowerLevelGetter;
        private static ManualLogSource? s_log;

        internal static void Apply(Harmony harmony, Assembly asm, ManualLogSource? log)
        {
            s_log = log;
            PatchJumpHandlerJumpForceIfPossible(harmony, asm);
            PatchHopHandlerJumpForceIfPossible(harmony, asm);
        }

        private static void PatchJumpHandlerJumpForceIfPossible(Harmony harmony, Assembly asm)
        {
            var jumpHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.JumpHandler", throwOnError: false);
            if (jumpHandlerType == null)
                return;

            var jumpForceGetter = AccessTools.PropertyGetter(jumpHandlerType, "JumpForce");
            if (jumpForceGetter == null)
                return;

            s_jumpHandlerPowerLevelGetter ??= AccessTools.PropertyGetter(jumpHandlerType, "PowerLevel");

            var prefix = typeof(JumpForceModule).GetMethod(nameof(JumpHandler_JumpForce_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(jumpForceGetter, prefix: new HarmonyMethod(prefix));
        }

        private static bool JumpHandler_JumpForce_Prefix(object __instance, ref float __result)
        {
            if (__instance == null || s_jumpHandlerPowerLevelGetter == null)
                return true;

            var levelObj = s_jumpHandlerPowerLevelGetter.Invoke(__instance, null);
            var level = levelObj is int value ? value : 0;
            var appliedLevel = level + 1;
            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHJumpForceBaseValue,
                FeatureFlags.DHHJumpForceIncreasePerLevel,
                appliedLevel,
                FeatureFlags.DHHJumpForceThresholdLevel,
                FeatureFlags.DHHJumpForceDiminishingFactor);

            __result = stat.FinalValue;
            LogJumpForce(__instance, level, appliedLevel, stat);
            return false;
        }

        private static void PatchHopHandlerJumpForceIfPossible(Harmony harmony, Assembly asm)
        {
            var hopHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.HopHandler", throwOnError: false);
            if (hopHandlerType == null)
                return;

            var jumpForceGetter = AccessTools.PropertyGetter(hopHandlerType, "JumpForce");
            s_hopHandlerPowerLevelGetter ??= AccessTools.PropertyGetter(hopHandlerType, "PowerLevel");

            var prefix = typeof(JumpForceModule).GetMethod(nameof(HopHandler_JumpForce_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (jumpForceGetter != null && prefix != null)
            {
                harmony.Patch(jumpForceGetter, prefix: new HarmonyMethod(prefix));
            }
        }

        private static bool HopHandler_JumpForce_Prefix(object __instance, ref float __result)
        {
            if (__instance == null || s_hopHandlerPowerLevelGetter == null)
                return true;

            var levelObj = s_hopHandlerPowerLevelGetter.Invoke(__instance, null);
            var level = levelObj is int value ? value : 0;
            var appliedLevel = level + 1;
            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHHopJumpBaseValue,
                FeatureFlags.DHHHopJumpIncreasePerLevel,
                appliedLevel,
                FeatureFlags.DHHHopJumpThresholdLevel,
                FeatureFlags.DHHHopJumpDiminishingFactor);

            __result = stat.FinalValue;
            LogHopJumpForce(__instance, level, appliedLevel, stat);

            return false;
        }

        private static DiminishingReturnsResult EvaluateStatWithDiminishingReturns(float baseValue, float increasePerLevel, int currentLevel, int thresholdLevel, float diminishingFactor)
        {
            var appliedLevel = currentLevel;
            var normalizedLevel = Math.Max(0, appliedLevel - 1);
            var normalizedThreshold = Math.Max(0, thresholdLevel - 1);
            var linearLevels = Mathf.Min(normalizedLevel, normalizedThreshold);
            var extraLevels = Mathf.Max(0, normalizedLevel - normalizedThreshold);
            var diminishingComponent = extraLevels * Mathf.Pow(diminishingFactor, extraLevels);
            var linearContribution = increasePerLevel * linearLevels;
            var diminishingContribution = increasePerLevel * diminishingComponent;
            var finalValue = baseValue + linearContribution + diminishingContribution;

            return new DiminishingReturnsResult(
                baseValue,
                increasePerLevel,
                appliedLevel,
                thresholdLevel,
                diminishingFactor,
                linearLevels,
                extraLevels,
                linearContribution,
                diminishingContribution,
                diminishingComponent,
                finalValue);
        }

        private readonly struct DiminishingReturnsResult
        {
            public DiminishingReturnsResult(float baseValue, float increasePerLevel, int appliedLevel, int thresholdLevel, float diminishingFactor,
                int linearLevels, int extraLevels, float linearContribution, float diminishingContribution, float diminishingComponent, float finalValue)
            {
                BaseValue = baseValue;
                IncreasePerLevel = increasePerLevel;
                AppliedLevel = appliedLevel;
                ThresholdLevel = thresholdLevel;
                DiminishingFactor = diminishingFactor;
                LinearLevels = linearLevels;
                ExtraLevels = extraLevels;
                LinearContribution = linearContribution;
                DiminishingContribution = diminishingContribution;
                DiminishingComponent = diminishingComponent;
                FinalValue = finalValue;
            }

            public float BaseValue { get; }
            public float IncreasePerLevel { get; }
            public int AppliedLevel { get; }
            public int ThresholdLevel { get; }
            public float DiminishingFactor { get; }
            public int LinearLevels { get; }
            public int ExtraLevels { get; }
            public float LinearContribution { get; }
            public float DiminishingContribution { get; }
            public float DiminishingComponent { get; }
            public float FinalValue { get; }
        }

        private static void LogHopJumpForce(object hopHandler, int powerLevel, int appliedLevel, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(HopForceLogKey, 30))
                return;

            var label = GetHandlerLabel(hopHandler, "HopHandler");
            var message = $"[Fix:Hop] {label} JumpForce={stat.FinalValue:F3} powerLevel={powerLevel} appliedLevel={appliedLevel} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            s_log?.LogInfo(message);
            Debug.Log(message);
        }

        private static void LogJumpForce(object jumpHandler, int powerLevel, int appliedLevel, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(JumpForceLogKey, 30))
                return;

            var label = GetHandlerLabel(jumpHandler, "JumpHandler");
            var message = $"[Fix:Jump] {label} JumpForce={stat.FinalValue:F3} powerLevel={powerLevel} appliedLevel={appliedLevel} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            s_log?.LogInfo(message);
            Debug.Log(message);
        }

        private static string GetHandlerLabel(object? handler, string fallback)
        {
            if (handler is Component component)
            {
                return component.name ?? component.GetType().Name;
            }

            return handler?.GetType().Name ?? fallback;
        }
    }
}
