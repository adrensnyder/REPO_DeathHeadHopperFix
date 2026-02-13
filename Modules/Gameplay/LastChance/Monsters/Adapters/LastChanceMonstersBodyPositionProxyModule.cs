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
        private static readonly Dictionary<Type, FieldInfo?> TricycleTargetFieldCache = new();

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

                // Keep Tricycle as vanilla as possible: do not rewrite body position
                // for players currently targeted by any Tricycle controller.
                if (IsTargetedByActiveTricycle(player))
                {
                    continue;
                }

                player.transform.position = headCenter;
            }
        }

        private static bool IsTargetedByActiveTricycle(PlayerAvatar player)
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
                if (!IsTricycleType(type))
                {
                    continue;
                }

                if (TryGetTricyclePlayerTarget(enemy, type) == player)
                {
                    return true;
                }
            }

            return false;
        }

        private static PlayerAvatar? TryGetTricyclePlayerTarget(Enemy enemy, Type type)
        {
            if (!TricycleTargetFieldCache.TryGetValue(type, out var field))
            {
                field = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerTarget");
                TricycleTargetFieldCache[type] = field;
            }

            return field?.GetValue(enemy) as PlayerAvatar;
        }

        private static bool IsTricycleType(Type type)
        {
            return type.Name.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (type.FullName?.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }
    }
}

