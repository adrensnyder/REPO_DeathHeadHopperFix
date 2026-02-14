#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions.Debugging
{
    [HarmonyPatch]
    internal static class LastChanceMonstersHeadmanStateTraceModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Headman");

        private static readonly FieldInfo? s_headControllerEnemyField = AccessTools.Field(typeof(EnemyHeadController), "Enemy");
        private static readonly FieldInfo? s_enemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");
        private static readonly FieldInfo? s_enemyTargetPlayerAvatarField = AccessTools.Field(typeof(Enemy), "TargetPlayerAvatar");
        private static readonly FieldInfo? s_enemyNavMeshAgentField = AccessTools.Field(typeof(Enemy), "NavMeshAgent");
        private static readonly FieldInfo? s_enemyStateChaseField = AccessTools.Field(typeof(Enemy), "StateChase");
        private static readonly FieldInfo? s_visionTriggeredPlayerField = AccessTools.Field(typeof(EnemyVision), "onVisionTriggeredPlayer");
        private static readonly FieldInfo? s_visionTriggeredDistanceField = AccessTools.Field(typeof(EnemyVision), "onVisionTriggeredDistance");
        private static readonly FieldInfo? s_visionTriggeredCulledField = AccessTools.Field(typeof(EnemyVision), "onVisionTriggeredCulled");
        private static readonly FieldInfo? s_visionTriggeredNearField = AccessTools.Field(typeof(EnemyVision), "onVisionTriggeredNear");

        private static readonly FieldInfo? s_stateChaseEnemyField = AccessTools.Field(typeof(EnemyStateChase), "Enemy");
        private static readonly FieldInfo? s_stateChaseVisionTimerField = AccessTools.Field(typeof(EnemyStateChase), "VisionTimer");
        private static readonly FieldInfo? s_stateChaseCantReachTimeField = AccessTools.Field(typeof(EnemyStateChase), "CantReachTime");
        private static readonly FieldInfo? s_stateChaseChaseCanReachField = AccessTools.Field(typeof(EnemyStateChase), "ChaseCanReach");
        private static readonly FieldInfo? s_stateChaseStateTimerField = AccessTools.Field(typeof(EnemyStateChase), "StateTimer");

        private static readonly FieldInfo? s_stateChaseBeginEnemyField = AccessTools.Field(typeof(EnemyStateChaseBegin), "Enemy");
        private static readonly FieldInfo? s_stateChaseBeginStateTimerField = AccessTools.Field(typeof(EnemyStateChaseBegin), "StateTimer");
        private static readonly FieldInfo? s_stateChaseBeginTargetPlayerField = AccessTools.Field(typeof(EnemyStateChaseBegin), "TargetPlayer");

        private static readonly FieldInfo? s_stateChaseSlowEnemyField = AccessTools.Field(typeof(EnemyStateChaseSlow), "Enemy");
        private static readonly FieldInfo? s_stateChaseSlowStateTimerField = AccessTools.Field(typeof(EnemyStateChaseSlow), "StateTimer");

        [HarmonyPatch(typeof(EnemyHeadController), "VisionTriggered")]
        [HarmonyPrefix]
        private static void EnemyHeadVisionTriggeredPrefix(EnemyHeadController __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadmanFlow || __instance == null)
            {
                return;
            }

            var enemy = s_headControllerEnemyField?.GetValue(__instance) as Enemy;
            LogDecision(
                "VisionTriggered",
                enemy,
                $"state={enemy?.CurrentState} target={GetPlayerName(GetVisionPlayer(enemy))} " +
                $"dist={GetVisionDistance(enemy):F2} culled={GetVisionCulled(enemy)} near={GetVisionNear(enemy)}");
        }

        [HarmonyPatch(typeof(EnemyStateChaseBegin), "Update")]
        [HarmonyPostfix]
        private static void EnemyStateChaseBeginUpdatePostfix(EnemyStateChaseBegin __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadmanFlow || __instance == null)
            {
                return;
            }

            var enemy = s_stateChaseBeginEnemyField?.GetValue(__instance) as Enemy;
            if (enemy == null || enemy.CurrentState != EnemyState.ChaseBegin)
            {
                return;
            }

            var target = s_stateChaseBeginTargetPlayerField?.GetValue(__instance) as PlayerAvatar;
            var timer = s_stateChaseBeginStateTimerField?.GetValue(__instance) as float? ?? -1f;
            LogHeartbeat(
                "ChaseBegin.Update",
                enemy,
                $"stateTimer={timer:F2} target={GetPlayerName(target)} targetDisabled={LastChanceMonstersTargetProxyHelper.IsDisabled(target)} " +
                $"headProxy={LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(target)} bodyDist={GetDistance(enemy, target, useHead: false)} " +
                $"headDist={GetDistance(enemy, target, useHead: true)}");
        }

        [HarmonyPatch(typeof(EnemyStateChase), "Update")]
        [HarmonyPostfix]
        private static void EnemyStateChaseUpdatePostfix(EnemyStateChase __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadmanFlow || __instance == null)
            {
                return;
            }

            var enemy = s_stateChaseEnemyField?.GetValue(__instance) as Enemy;
            if (enemy == null || enemy.CurrentState != EnemyState.Chase)
            {
                return;
            }

            var target = s_enemyTargetPlayerAvatarField?.GetValue(enemy) as PlayerAvatar;
            var visionTimer = s_stateChaseVisionTimerField?.GetValue(__instance) as float? ?? -1f;
            var cantReach = s_stateChaseCantReachTimeField?.GetValue(__instance) as float? ?? -1f;
            var chaseCanReach = s_stateChaseChaseCanReachField?.GetValue(__instance) as bool? ?? false;
            var stateTimer = s_stateChaseStateTimerField?.GetValue(__instance) as float? ?? -1f;

            LogHeartbeat(
                "Chase.Update",
                enemy,
                $"stateTimer={stateTimer:F2} visionTimer={visionTimer:F2} cantReach={cantReach:F2} canReach={chaseCanReach} " +
                $"target={GetPlayerName(target)} targetDisabled={LastChanceMonstersTargetProxyHelper.IsDisabled(target)} " +
                $"headProxy={LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(target)} bodyDist={GetDistance(enemy, target, useHead: false)} " +
                $"headDist={GetDistance(enemy, target, useHead: true)} chasePos={GetChasePosition(enemy)}");
        }

        [HarmonyPatch(typeof(EnemyStateChaseSlow), "Update")]
        [HarmonyPostfix]
        private static void EnemyStateChaseSlowUpdatePostfix(EnemyStateChaseSlow __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadmanFlow || __instance == null)
            {
                return;
            }

            var enemy = s_stateChaseSlowEnemyField?.GetValue(__instance) as Enemy;
            if (enemy == null || enemy.CurrentState != EnemyState.ChaseSlow)
            {
                return;
            }

            var target = s_enemyTargetPlayerAvatarField?.GetValue(enemy) as PlayerAvatar;
            var timer = s_stateChaseSlowStateTimerField?.GetValue(__instance) as float? ?? -1f;
            var destination = GetNavDestination(enemy);
            LogHeartbeat(
                "ChaseSlow.Update",
                enemy,
                $"stateTimer={timer:F2} target={GetPlayerName(target)} targetDisabled={LastChanceMonstersTargetProxyHelper.IsDisabled(target)} " +
                $"headProxy={LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(target)} bodyDist={GetDistance(enemy, target, useHead: false)} " +
                $"headDist={GetDistance(enemy, target, useHead: true)} destination={destination}");
        }

        private static string GetPlayerName(PlayerAvatar? player)
        {
            return player == null ? "n/a" : player.name;
        }

        private static string GetDistance(Enemy enemy, PlayerAvatar? player, bool useHead)
        {
            if (enemy == null || player == null)
            {
                return "n/a";
            }

            var from = enemy.CenterTransform != null ? enemy.CenterTransform.position : enemy.transform.position;
            var to = player.transform.position;
            if (useHead && LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                to = headCenter;
            }

            return Vector3.Distance(from, to).ToString("F2");
        }

        private static PlayerAvatar? GetVisionPlayer(Enemy? enemy)
        {
            var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
            return s_visionTriggeredPlayerField?.GetValue(vision) as PlayerAvatar;
        }

        private static float GetVisionDistance(Enemy? enemy)
        {
            var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
            return s_visionTriggeredDistanceField?.GetValue(vision) as float? ?? -1f;
        }

        private static bool GetVisionCulled(Enemy? enemy)
        {
            var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
            return s_visionTriggeredCulledField?.GetValue(vision) as bool? ?? false;
        }

        private static bool GetVisionNear(Enemy? enemy)
        {
            var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
            return s_visionTriggeredNearField?.GetValue(vision) as bool? ?? false;
        }

        private static Vector3 GetChasePosition(Enemy? enemy)
        {
            var stateChase = s_enemyStateChaseField?.GetValue(enemy) as EnemyStateChase;
            return stateChase?.ChasePosition ?? Vector3.zero;
        }

        private static Vector3 GetNavDestination(Enemy? enemy)
        {
            var nav = s_enemyNavMeshAgentField?.GetValue(enemy) as EnemyNavMeshAgent;
            return nav?.GetDestination() ?? Vector3.zero;
        }

        private static void LogDecision(string reason, Enemy? enemy, string message)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadmanFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceHeadmanVerbose && !LogLimiter.ShouldLog($"Headman.{reason}", 10))
            {
                return;
            }

            var runtime = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();
            var enemyInfo = enemy == null ? "enemy=n/a id=n/a" : $"enemy={enemy.name} id={enemy.GetInstanceID()}";
            Log.LogInfo($"[Headman][{reason}] runtime={runtime} {enemyInfo} {message}");
        }

        private static void LogHeartbeat(string reason, Enemy? enemy, string message)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadmanFlow)
            {
                return;
            }

            var key = enemy == null
                ? $"Headman.{reason}.none"
                : $"Headman.{reason}.{enemy.GetInstanceID()}";

            var interval = InternalDebugFlags.DebugLastChanceHeadmanVerbose ? 3 : 15;
            if (!LogLimiter.ShouldLog(key, interval))
            {
                return;
            }

            var runtime = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();
            var enemyInfo = enemy == null ? "enemy=n/a id=n/a" : $"enemy={enemy.name} id={enemy.GetInstanceID()}";
            Log.LogInfo($"[Headman][{reason}] runtime={runtime} {enemyInfo} {message}");
        }
    }
}
