#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersCarryDistanceModule
    {
        private const float MaxCarryDistance = 5f;

        private static readonly Dictionary<Type, CarryDistanceReflection> ReflectionCache = new();
        private static readonly FieldInfo? EnemyRigidbodyField = AccessTools.Field(typeof(Enemy), "Rigidbody");

        private sealed class CarryDistanceReflection
        {
            internal FieldInfo? EnemyField;
            internal FieldInfo? PlayerTargetField;
            internal FieldInfo? CurrentStateField;
            internal MethodInfo? UpdateStateMethod;
            internal Type? StateEnumType;
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();

            Type[] types;
            try
            {
                types = typeof(Enemy).Assembly.GetTypes();
            }
            catch
            {
                return results;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var reflection = GetReflection(type);
                if (reflection.EnemyField == null || reflection.PlayerTargetField == null || reflection.CurrentStateField == null || reflection.UpdateStateMethod == null || reflection.StateEnumType == null)
                {
                    continue;
                }

                var moveMethod = type.GetMethod("StatePlayerMove", flags, null, Type.EmptyTypes, null);
                if (moveMethod != null && moveMethod.GetMethodBody() != null)
                {
                    results.Add(moveMethod);
                }

                var releaseMethod = type.GetMethod("StatePlayerRelease", flags, null, Type.EmptyTypes, null);
                if (releaseMethod != null && releaseMethod.GetMethodBody() != null)
                {
                    results.Add(releaseMethod);
                }
            }

            return results;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, MethodBase __originalMethod)
        {
            if (__instance == null || __originalMethod == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            var reflection = GetReflection(__instance.GetType());
            if (reflection.UpdateStateMethod == null || reflection.StateEnumType == null)
            {
                return;
            }

            if (!ShouldKeepCarryByHeadDistance(__instance, reflection))
            {
                return;
            }

            var methodName = __originalMethod.Name;
            if (string.Equals(methodName, "StatePlayerMove", StringComparison.Ordinal) && IsState(__instance, reflection, "PlayerRelease"))
            {
                InvokeUpdateState(__instance, reflection, "PlayerMove");
                return;
            }

            if (string.Equals(methodName, "StatePlayerRelease", StringComparison.Ordinal) && IsState(__instance, reflection, "PlayerReleaseWait"))
            {
                InvokeUpdateState(__instance, reflection, "PlayerRelease");
            }
        }

        private static CarryDistanceReflection GetReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new CarryDistanceReflection
            {
                EnemyField = AccessTools.Field(type, "enemy"),
                PlayerTargetField = AccessTools.Field(type, "playerTarget"),
                CurrentStateField = AccessTools.Field(type, "currentState")
            };

            built.StateEnumType = built.CurrentStateField?.FieldType;
            if (built.StateEnumType != null && built.StateEnumType.IsEnum)
            {
                built.UpdateStateMethod = AccessTools.Method(type, "UpdateState", new[] { built.StateEnumType });
            }

            ReflectionCache[type] = built;
            return built;
        }

        private static bool ShouldKeepCarryByHeadDistance(object instance, CarryDistanceReflection reflection)
        {
            var player = reflection.PlayerTargetField?.GetValue(instance) as PlayerAvatar;
            if (player == null || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                return false;
            }

            var enemy = reflection.EnemyField?.GetValue(instance) as Enemy;
            var enemyRigid = enemy != null ? EnemyRigidbodyField?.GetValue(enemy) as EnemyRigidbody : null;
            var enemyPosition = enemyRigid != null ? enemyRigid.transform.position : (instance as MonoBehaviour)?.transform.position ?? Vector3.zero;

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var effectiveTarget))
            {
                return false;
            }

            return Vector3.Distance(enemyPosition, effectiveTarget) <= MaxCarryDistance;
        }

        private static bool IsState(object instance, CarryDistanceReflection reflection, string stateName)
        {
            if (reflection.CurrentStateField == null)
            {
                return false;
            }

            var value = reflection.CurrentStateField.GetValue(instance);
            return string.Equals(value?.ToString(), stateName, StringComparison.Ordinal);
        }

        private static void InvokeUpdateState(object instance, CarryDistanceReflection reflection, string stateName)
        {
            if (reflection.UpdateStateMethod == null || reflection.StateEnumType == null || !reflection.StateEnumType.IsEnum)
            {
                return;
            }

            try
            {
                var state = Enum.Parse(reflection.StateEnumType, stateName, ignoreCase: false);
                reflection.UpdateStateMethod.Invoke(instance, new[] { state });
            }
            catch
            {
            }
        }
    }
}

