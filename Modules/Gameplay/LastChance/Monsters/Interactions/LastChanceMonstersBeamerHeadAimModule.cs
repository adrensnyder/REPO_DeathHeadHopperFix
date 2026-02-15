#nullable enable

using System.Reflection;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersBeamerHeadAimModule
    {
        private static readonly FieldInfo? s_playerTargetField = AccessTools.Field(typeof(EnemyBeamer), "playerTarget");
        private static readonly FieldInfo? s_enemyField = AccessTools.Field(typeof(EnemyBeamer), "enemy");
        private static readonly FieldInfo? s_aimHorizontalTargetField = AccessTools.Field(typeof(EnemyBeamer), "aimHorizontalTarget");
        private static readonly FieldInfo? s_laserRayTransformField = AccessTools.Field(typeof(EnemyBeamer), "laserRayTransform");
        private static readonly FieldInfo? s_aimVerticalTargetField = AccessTools.Field(typeof(EnemyBeamer), "aimVerticalTarget");
        private static readonly FieldInfo? s_aimVerticalTransformField = AccessTools.Field(typeof(EnemyBeamer), "aimVerticalTransform");

        [HarmonyPatch(typeof(EnemyBeamer), "StateAttackStart")]
        [HarmonyPostfix]
        private static void StateAttackStartPostfix(EnemyBeamer __instance)
        {
            if (!TryGetAimContext(__instance, out var enemy, out var player, out var targetPoint))
            {
                return;
            }

            var from = enemy.CenterTransform != null ? enemy.CenterTransform.position : enemy.transform.position;
            var dir = targetPoint - from;
            if (dir.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var aim = Quaternion.LookRotation(dir);
            aim = Quaternion.Euler(0f, aim.eulerAngles.y, 0f);
            s_aimHorizontalTargetField?.SetValue(__instance, aim);
        }

        [HarmonyPatch(typeof(EnemyBeamer), "VerticalAimLogic")]
        [HarmonyPostfix]
        private static void VerticalAimLogicPostfix(EnemyBeamer __instance)
        {
            if (!TryGetAimContext(__instance, out _, out _, out var targetPoint))
            {
                return;
            }

            var laserRayTransform = s_laserRayTransformField?.GetValue(__instance) as Transform;
            if (laserRayTransform == null)
            {
                return;
            }

            var aimVerticalTransform = s_aimVerticalTransformField?.GetValue(__instance) as Transform;
            var dir = targetPoint - laserRayTransform.position;
            if (dir.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var worldAim = Quaternion.LookRotation(dir);
            var currentRotation = laserRayTransform.rotation;
            laserRayTransform.rotation = worldAim;
            var localAim = laserRayTransform.localRotation;
            localAim = Quaternion.Euler(laserRayTransform.eulerAngles.x, 0f, 0f);
            laserRayTransform.rotation = currentRotation;

            s_aimVerticalTargetField?.SetValue(__instance, localAim);
            laserRayTransform.localRotation = localAim;
            if (aimVerticalTransform != null)
            {
                aimVerticalTransform.localRotation = localAim;
            }
        }

        private static bool TryGetAimContext(EnemyBeamer beamer, out Enemy enemy, out PlayerAvatar player, out Vector3 targetPoint)
        {
            enemy = null!;
            player = null!;
            targetPoint = default;

            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return false;
            }

            var enemyValue = s_enemyField?.GetValue(beamer) as Enemy;
            var playerValue = s_playerTargetField?.GetValue(beamer) as PlayerAvatar;
            if (enemyValue == null || playerValue == null)
            {
                return false;
            }
            enemy = enemyValue;
            player = playerValue;

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyVisionTarget(player, out targetPoint))
            {
                return false;
            }

            return true;
        }
    }
}
