#nullable enable

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters
{
    [HarmonyPatch(typeof(EnemyTriggerAttack), "OnTriggerStay")]
    internal static class LastChanceMonstersTriggerAttackModule
    {
        private static readonly FieldInfo? s_triggerCheckTimerSetField = AccessTools.Field(typeof(EnemyTriggerAttack), "TriggerCheckTimerSet");
        private static readonly FieldInfo? s_triggerCheckTimerField = AccessTools.Field(typeof(EnemyTriggerAttack), "TriggerCheckTimer");
        private static readonly FieldInfo? s_enemyStateLookUnderField = AccessTools.Field(typeof(Enemy), "StateLookUnder");
        private static readonly FieldInfo? s_enemyStateChaseField = AccessTools.Field(typeof(Enemy), "StateChase");
        private static readonly FieldInfo? s_enemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");
        private static readonly FieldInfo? s_enemyTriggerAttackAttackField = AccessTools.Field(typeof(EnemyTriggerAttack), "Attack");
        private static readonly FieldInfo? s_stateLookUnderWaitDoneField = AccessTools.Field(typeof(EnemyStateLookUnder), "WaitDone");

        [HarmonyPrefix]
        private static bool OnTriggerStayPrefix(EnemyTriggerAttack __instance, Collider other)
        {
            if (__instance == null || other == null)
            {
                return true;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsMasterContext() || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return true;
            }

            // Keep vanilla path for regular body trigger.
            if (other.GetComponent<PlayerTrigger>() != null)
            {
                return true;
            }

            if (!LastChanceMonstersTargetProxyHelper.TryGetPlayerFromDeathHeadCollider(other, out var player) || player == null)
            {
                return true;
            }

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                return true;
            }

            if (!LevelGenerator.Instance.Generated)
            {
                return false;
            }

            var timer = s_triggerCheckTimerField?.GetValue(__instance) as float? ?? 0f;
            if (timer > 0f)
            {
                return false;
            }

            s_triggerCheckTimerSetField?.SetValue(__instance, true);

            var enemy = __instance.Enemy;
            if (enemy == null)
            {
                return false;
            }

            if (enemy.CurrentState != EnemyState.Chase && enemy.CurrentState != EnemyState.LookUnder)
            {
                return false;
            }

            var lookUnder = s_enemyStateLookUnderField?.GetValue(enemy) as EnemyStateLookUnder;
            var chase = s_enemyStateChaseField?.GetValue(enemy) as EnemyStateChase;
            var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
            var lookUnderReady = enemy.CurrentState == EnemyState.LookUnder &&
                                 lookUnder != null &&
                                 s_stateLookUnderWaitDoneField?.GetValue(lookUnder) as bool? == true;
            var chaseCanReach = chase != null && chase.ChaseCanReach;

            var viewId = player.photonView.ViewID;
            var visionTriggered = vision != null && vision.VisionTriggered.TryGetValue(viewId, out var triggered) && triggered;
            if (!visionTriggered)
            {
                var near = vision != null && Vector3.Distance(__instance.VisionTransform.position, headCenter) <= vision.VisionDistanceClose;
                if (vision != null)
                {
                    LastChanceMonstersTargetProxyHelper.EnsureVisionTriggered(vision, player, near);
                }
                visionTriggered = true;
            }

            if (!visionTriggered && !lookUnderReady)
            {
                return false;
            }

            var allowAttack = chaseCanReach && !lookUnderReady;
            var fallbackCanAttack = !chaseCanReach || lookUnderReady;

            var blocked = !LastChanceMonstersTargetProxyHelper.IsLineOfSightToHead(__instance.VisionTransform, headCenter, __instance.VisionMask, player);
            if (blocked)
            {
                if (!fallbackCanAttack)
                {
                    allowAttack = false;
                }
            }
            else if (fallbackCanAttack)
            {
                allowAttack = true;
            }

            if (allowAttack)
            {
                s_enemyTriggerAttackAttackField?.SetValue(__instance, true);
            }

            return false;
        }
    }
}
