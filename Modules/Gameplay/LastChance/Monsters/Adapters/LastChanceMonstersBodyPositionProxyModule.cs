#nullable enable

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters
{
    [HarmonyPatch(typeof(EnemyDirector), "Update")]
    internal static class LastChanceMonstersBodyPositionProxyModule
    {
        private static readonly Dictionary<Type, FieldInfo?> PlayerTargetFieldCache = new();

        [HarmonyPostfix]
        private static void Postfix()
        {
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0)
            {
                return;
            }

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null)
                {
                    continue;
                }

                if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
                {
                    continue;
                }

                // General safety: if any enemy is already tracking this player as target,
                // avoid rewriting the body position to prevent steering/path regressions.
                if (IsTargetedByAnyEnemy(player))
                {
                    continue;
                }

                player.transform.position = headCenter;
            }
        }

        private static bool IsTargetedByAnyEnemy(PlayerAvatar player)
        {
            if (player == null)
            {
                return false;
            }

            var enemies = UnityEngine.Object.FindObjectsOfType<Enemy>();
            for (var i = 0; i < enemies.Length; i++)
            {
                var enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                var type = enemy.GetType();
                if (TryGetPlayerTarget(enemy, type) == player)
                {
                    return true;
                }
            }

            return false;
        }

        private static PlayerAvatar? TryGetPlayerTarget(Enemy enemy, Type type)
        {
            if (!PlayerTargetFieldCache.TryGetValue(type, out var field))
            {
                field = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerTarget");
                PlayerTargetFieldCache[type] = field;
            }

            return field?.GetValue(enemy) as PlayerAvatar;
        }
    }
}

