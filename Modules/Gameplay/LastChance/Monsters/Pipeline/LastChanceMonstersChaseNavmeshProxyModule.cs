#nullable enable

using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch(typeof(EnemyStateChase), "Update")]
    internal static class LastChanceMonstersChaseNavmeshProxyModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Headman");

        private static readonly FieldInfo? s_enemyField = AccessTools.Field(typeof(EnemyStateChase), "Enemy");
        private static readonly FieldInfo? s_targetPlayerField = AccessTools.Field(typeof(Enemy), "TargetPlayerAvatar");

        private static readonly FieldInfo? s_lastNavmeshPositionField = AccessTools.Field(typeof(PlayerAvatar), "LastNavmeshPosition");
        private static readonly FieldInfo? s_lastNavmeshPositionTimerField = AccessTools.Field(typeof(PlayerAvatar), "LastNavMeshPositionTimer");

        [HarmonyPrefix]
        private static void Prefix(EnemyStateChase __instance)
        {
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return;
            }

            var enemy = s_enemyField?.GetValue(__instance) as Enemy;
            if (enemy == null || enemy.CurrentState != EnemyState.Chase)
            {
                return;
            }

            var player = s_targetPlayerField?.GetValue(enemy) as PlayerAvatar;
            if (player == null)
            {
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                return;
            }

            s_lastNavmeshPositionField?.SetValue(player, headCenter);
            s_lastNavmeshPositionTimerField?.SetValue(player, 0f);

            if (InternalDebugFlags.DebugLastChanceHeadmanFlow)
            {
                var key = $"Headman.NavmeshProxy.{enemy.GetInstanceID()}";
                if (InternalDebugFlags.DebugLastChanceHeadmanVerbose || LogLimiter.ShouldLog(key, 10))
                {
                    Log.LogInfo(
                        $"[Headman][NavmeshProxy] enemyId={enemy.GetInstanceID()} player={player.name} " +
                        $"head={headCenter} body={player.transform.position}");
                }
            }
        }
    }
}

