#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersPathBlockingModule
    {
        private const float DefaultBlockingRadius = 0.5f;
        private const float DefaultBlockingDistance = 3f;
        private const float DefaultNavmeshRadius = 1f;

        private static readonly Dictionary<Type, BlockingReflection> ReflectionCache = new();
        private static readonly FieldInfo? EnemyNavMeshAgentField = AccessTools.Field(typeof(Enemy), "NavMeshAgent");

        private sealed class BlockingReflection
        {
            internal FieldInfo? EnemyField;
            internal FieldInfo? AgentDirectionField;
            internal FieldInfo? IsBlockedByPlayerField;
            internal FieldInfo? IsBlockedByPlayerAvatarField;
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();

            Type[] types;
            try
            {
                types = typeof(Enemy).Assembly.GetTypes();
            }
            catch
            {
                return methods;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var method = type.GetMethod("IsPlayerBlockingNavmeshPath", flags, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(bool) || method.GetMethodBody() == null)
                {
                    continue;
                }

                var cache = GetReflection(type);
                if (cache.EnemyField == null || cache.AgentDirectionField == null || cache.IsBlockedByPlayerField == null || cache.IsBlockedByPlayerAvatarField == null)
                {
                    continue;
                }

                methods.Add(method);
            }

            return methods;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, ref bool __result)
        {
            if (__result || __instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            var cache = GetReflection(__instance.GetType());
            var enemy = cache.EnemyField?.GetValue(__instance) as Enemy;
            if (enemy == null || enemy.CenterTransform == null)
            {
                return;
            }

            var navMeshAgent = EnemyNavMeshAgentField?.GetValue(enemy);
            if (navMeshAgent == null)
            {
                return;
            }

            var heading = cache.AgentDirectionField?.GetValue(__instance) as Vector3? ?? Vector3.zero;
            if (heading.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var hits = Physics.SphereCastAll(
                enemy.CenterTransform.position,
                DefaultBlockingRadius,
                heading.normalized,
                DefaultBlockingDistance,
                LayerMask.GetMask("Player", "PhysGrabObject"),
                QueryTriggerInteraction.Ignore);

            for (var i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (!TryResolvePlayerFromBlockingCollider(collider, out var player) || player == null)
                {
                    continue;
                }

                if (!TryResolveNavmeshPoint(player, out var candidatePoint))
                {
                    continue;
                }

                if (!InvokeOnNavmesh(navMeshAgent, candidatePoint, DefaultNavmeshRadius, true))
                {
                    continue;
                }

                cache.IsBlockedByPlayerAvatarField?.SetValue(__instance, player);
                cache.IsBlockedByPlayerField?.SetValue(__instance, true);
                __result = true;
                return;
            }
        }

        private static BlockingReflection GetReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new BlockingReflection
            {
                EnemyField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "enemy"),
                AgentDirectionField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "agentDirection"),
                IsBlockedByPlayerField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "isBlockedByPlayer"),
                IsBlockedByPlayerAvatarField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "isBlockedByPlayerAvatar")
            };

            ReflectionCache[type] = built;
            return built;
        }

        private static bool InvokeOnNavmesh(object navMeshAgent, Vector3 position, float radius, bool requirePath)
        {
            var method = AccessTools.Method(navMeshAgent.GetType(), "OnNavmesh", new[] { typeof(Vector3), typeof(float), typeof(bool) });
            if (method == null)
            {
                return false;
            }

            return method.Invoke(navMeshAgent, new object[] { position, radius, requirePath }) as bool? ?? false;
        }

        private static bool TryResolvePlayerFromBlockingCollider(Collider collider, out PlayerAvatar? player)
        {
            return LastChanceMonstersReflectionHelper.TryResolvePlayerFromCollider(collider, out player);
        }

        private static bool TryResolveNavmeshPoint(PlayerAvatar player, out Vector3 point)
        {
            if (LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                point = headCenter;
                return true;
            }

            point = player.transform.position;
            return true;
        }
    }
}

