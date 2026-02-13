#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions.Debugging
{
    [HarmonyPatch]
    internal static class LastChanceMonstersSpinnyStateTraceModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Spinny");

        private static readonly Type? EnemySpinnyType = AccessTools.TypeByName("EnemySpinny");
        private static readonly FieldInfo? CurrentStateField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "currentState") : null;
        private static readonly FieldInfo? PlayerTargetField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "playerTarget") : null;
        private static readonly FieldInfo? PlayerLockPointField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "playerLockPoint") : null;
        private static readonly FieldInfo? StateTimerField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "stateTimer") : null;
        private static readonly FieldInfo? LockPointTimerField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "lockPointTimer") : null;
        private static readonly FieldInfo? OffLockPointTimerField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "offLockPointTimer") : null;
        private static readonly FieldInfo? ReachedPointField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "reachedPoint") : null;
        private static readonly FieldInfo? EnemySpinnyAnimField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "enemySpinnyAnim") : null;
        private static readonly FieldInfo? EnemyField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "enemy") : null;
        private static readonly FieldInfo? PlayerIsTumblingField = AccessTools.Field(typeof(PlayerAvatar), "isTumbling");
        private static readonly FieldInfo? PlayerTumbleRbField = AccessTools.Field(typeof(PlayerTumble), "rb");
        private static readonly MethodInfo? EnemyIsStunnedMethod = AccessTools.Method(typeof(Enemy), "IsStunned");

        private static readonly MethodInfo? HasLineOfSightMethod = EnemySpinnyType != null ? AccessTools.Method(EnemySpinnyType, "HasLineOfSight") : null;
        private static readonly MethodInfo? UpdateStateMethod = EnemySpinnyType != null ? AccessTools.Method(EnemySpinnyType, "UpdateState") : null;
        private static readonly MethodInfo? StateWaitForRouletteMethod = EnemySpinnyType != null ? AccessTools.Method(EnemySpinnyType, "StateWaitForRoulette") : null;

        [HarmonyPatch]
        private static class UpdateStateTracePatch
        {
            [HarmonyTargetMethod]
            private static MethodBase? TargetMethod() => UpdateStateMethod;

            [HarmonyPrefix]
            private static void Prefix(object __instance, object _nextState)
            {
                if (!ShouldTrace(__instance))
                {
                    return;
                }

                DebugLog(
                    "UpdateState.Call",
                    $"enemyId={GetInstanceKey(__instance)} from={ReadStateName(__instance)} to={_nextState} {BuildSnapshot(__instance)}");

                // Focus log: immediate exit from WaitForRoulette is the critical branch we are diagnosing.
                var from = ReadStateName(__instance);
                var to = _nextState?.ToString() ?? "n/a";
                if (string.Equals(from, "WaitForRoulette", StringComparison.Ordinal) &&
                    string.Equals(to, "CloseMouth", StringComparison.Ordinal))
                {
                    DebugLog("WaitForRoulette.CloseMouthReason", $"enemyId={GetInstanceKey(__instance)} {BuildDecisionSnapshot(__instance)}");
                }
            }
        }

        [HarmonyPatch]
        private static class StateWaitForRouletteTracePatch
        {
            [HarmonyTargetMethod]
            private static MethodBase? TargetMethod() => StateWaitForRouletteMethod;

            [HarmonyPrefix]
            private static void Prefix(object __instance)
            {
                if (!ShouldTrace(__instance))
                {
                    return;
                }

                DebugLog("WaitForRoulette.Enter", $"enemyId={GetInstanceKey(__instance)} {BuildSnapshot(__instance)}");
            }

            [HarmonyPostfix]
            private static void Postfix(object __instance)
            {
                if (!ShouldTrace(__instance))
                {
                    return;
                }

                DebugLog("WaitForRoulette.Exit", $"enemyId={GetInstanceKey(__instance)} {BuildSnapshot(__instance)}");
                DebugLog("WaitForRoulette.Decision", $"enemyId={GetInstanceKey(__instance)} {BuildDecisionSnapshot(__instance)}");
            }
        }

        [HarmonyPatch]
        private static class HasLineOfSightTracePatch
        {
            [HarmonyTargetMethod]
            private static MethodBase? TargetMethod() => HasLineOfSightMethod;

            [HarmonyPostfix]
            private static void Postfix(object __instance, bool __result)
            {
                if (!ShouldTrace(__instance))
                {
                    return;
                }

                DebugLog("HasLineOfSight", $"enemyId={GetInstanceKey(__instance)} result={__result} {BuildSnapshot(__instance)}");
            }
        }

        private static bool ShouldTrace(object? instance)
        {
            if (!InternalDebugFlags.DebugLastChanceSpinnyFlow || instance == null)
            {
                return false;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return false;
            }

            var player = PlayerTargetField?.GetValue(instance) as PlayerAvatar;
            return player != null && LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
        }

        private static string BuildSnapshot(object instance)
        {
            var player = PlayerTargetField?.GetValue(instance) as PlayerAvatar;
            var lockPoint = PlayerLockPointField?.GetValue(instance) as Transform;
            var anim = EnemySpinnyAnimField?.GetValue(instance);
            var mouthOpened = ReadBoolField(anim, "mouthOpened");
            var playerIsTumbling = ReadBoolField(player, PlayerIsTumblingField, "isTumbling");
            var stateTimer = ReadFloatField(instance, StateTimerField, "stateTimer");
            var lockPointTimer = ReadFloatField(instance, LockPointTimerField, "lockPointTimer");
            var offLockPointTimer = ReadFloatField(instance, OffLockPointTimerField, "offLockPointTimer");
            var reachedPoint = ReadBoolField(instance, ReachedPointField, "reachedPoint");

            float? dist = null;
            var tumbleRb = player?.tumble != null && PlayerTumbleRbField != null ? PlayerTumbleRbField.GetValue(player.tumble) as Rigidbody : null;
            if (tumbleRb != null && lockPoint != null)
            {
                dist = Vector3.Distance(tumbleRb.position, lockPoint.position);
            }

            return
                $"state={ReadStateName(instance)} " +
                $"player={(player != null ? (player.photonView != null ? player.photonView.ViewID.ToString() : player.GetInstanceID().ToString()) : "n/a")} " +
                $"isTumbling={(playerIsTumbling.HasValue ? playerIsTumbling.Value.ToString() : "n/a")} " +
                $"stateTimer={(stateTimer.HasValue ? stateTimer.Value.ToString("F2") : "n/a")} " +
                $"lockPointTimer={(lockPointTimer.HasValue ? lockPointTimer.Value.ToString("F2") : "n/a")} " +
                $"offLockPointTimer={(offLockPointTimer.HasValue ? offLockPointTimer.Value.ToString("F2") : "n/a")} " +
                $"reachedPoint={(reachedPoint.HasValue ? reachedPoint.Value.ToString() : "n/a")} " +
                $"distToLock={(dist.HasValue ? dist.Value.ToString("F2") : "n/a")} " +
                $"mouthOpened={(mouthOpened.HasValue ? mouthOpened.Value.ToString() : "n/a")}";
        }

        private static string ReadStateName(object instance)
        {
            return CurrentStateField?.GetValue(instance)?.ToString() ?? "n/a";
        }

        private static string BuildDecisionSnapshot(object instance)
        {
            var player = PlayerTargetField?.GetValue(instance) as PlayerAvatar;
            var stateTimer = ReadFloatField(instance, StateTimerField, "stateTimer") ?? 0f;
            var offLockPointTimer = ReadFloatField(instance, OffLockPointTimerField, "offLockPointTimer") ?? 0f;
            var reachedPoint = ReadBoolField(instance, ReachedPointField, "reachedPoint") ?? false;
            var enemy = EnemyField?.GetValue(instance) as Enemy;
            var stunned = false;
            if (enemy != null && EnemyIsStunnedMethod != null)
            {
                try
                {
                    stunned = EnemyIsStunnedMethod.Invoke(enemy, null) is bool b && b;
                }
                catch
                {
                    stunned = false;
                }
            }

            var closeByTimeoutNoReach = stateTimer <= 0f && !reachedPoint;
            var closeByStunnedOrOffLock = stunned || offLockPointTimer > 1.5f;
            var canRouletteByGrab = ReadBoolField(EnemySpinnyAnimField?.GetValue(instance), "mouthOpened") ?? false;
            var playerId = player != null ? (player.photonView != null ? player.photonView.ViewID.ToString() : player.GetInstanceID().ToString()) : "n/a";
            return $"player={playerId} reachedPoint={reachedPoint} stateTimer={stateTimer:F2} offLockPointTimer={offLockPointTimer:F2} stunned={stunned} closeByTimeoutNoReach={closeByTimeoutNoReach} closeByStunnedOrOffLock={closeByStunnedOrOffLock} mouthOpened={canRouletteByGrab}";
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceSpinnyFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceSpinnyVerbose && !LogLimiter.ShouldLog($"Spinny.Trace.{reason}", 10))
            {
                return;
            }

            Log.LogInfo($"[Spinny][{reason}] {detail}");
        }

        private static int GetInstanceKey(object instance)
        {
            if (instance is UnityEngine.Object uo)
            {
                return uo.GetInstanceID();
            }

            return instance.GetHashCode();
        }

        private static bool? ReadBoolField(object? instance, FieldInfo? field, string fallbackFieldName)
        {
            if (instance == null)
            {
                return null;
            }

            if (field != null && field.FieldType == typeof(bool))
            {
                try
                {
                    return (bool)field.GetValue(instance)!;
                }
                catch
                {
                }
            }

            return ReadBoolField(instance, fallbackFieldName);
        }

        private static bool? ReadBoolField(object? instance, string fieldName)
        {
            if (instance == null)
            {
                return null;
            }

            try
            {
                var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool))
                {
                    return null;
                }

                return (bool)field.GetValue(instance)!;
            }
            catch
            {
                return null;
            }
        }

        private static float? ReadFloatField(object? instance, FieldInfo? field, string fallbackFieldName)
        {
            if (instance == null)
            {
                return null;
            }

            if (field != null && field.FieldType == typeof(float))
            {
                try
                {
                    return (float)field.GetValue(instance)!;
                }
                catch
                {
                }
            }

            try
            {
                var fallback = instance.GetType().GetField(fallbackFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fallback == null || fallback.FieldType != typeof(float))
                {
                    return null;
                }

                return (float)fallback.GetValue(instance)!;
            }
            catch
            {
                return null;
            }
        }
    }
}
