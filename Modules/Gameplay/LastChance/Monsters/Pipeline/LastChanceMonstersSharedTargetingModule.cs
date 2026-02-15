#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch]
    internal static class LastChanceMonstersSharedPlayerSearchModule
    {
        private static readonly MethodInfo? s_getAllVanillaMethod = AccessTools.Method(
            typeof(SemiFunc),
            "PlayerGetAllPlayerAvatarWithinRange",
            new[] { typeof(float), typeof(Vector3), typeof(bool), typeof(LayerMask) });

        private static readonly MethodInfo? s_getNearestVanillaMethod = AccessTools.Method(
            typeof(SemiFunc),
            "PlayerGetNearestPlayerAvatarWithinRange",
            new[] { typeof(float), typeof(Vector3), typeof(bool), typeof(LayerMask) });

        private static readonly MethodInfo? s_getAllProxyMethod =
            AccessTools.Method(typeof(LastChanceMonstersSharedPlayerSearchModule), nameof(GetAllPlayersWithinRangeLastChanceAware));

        private static readonly MethodInfo? s_getNearestProxyMethod =
            AccessTools.Method(typeof(LastChanceMonstersSharedPlayerSearchModule), nameof(GetNearestPlayerWithinRangeLastChanceAware));

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

            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || !IsMonsterRelatedType(type) || IsTricycleType(type))
                {
                    continue;
                }

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var typeMethods = type.GetMethods(flags);
                for (var m = 0; m < typeMethods.Length; m++)
                {
                    var method = typeMethods[m];
                    if (method == null || method.IsAbstract || method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    if (MethodCallsSharedPlayerSearch(method))
                    {
                        methods.Add(method);
                    }
                }
            }

            return methods;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ReplaceSharedPlayerSearchCalls(IEnumerable<CodeInstruction> instructions)
        {
            if (s_getAllVanillaMethod == null || s_getNearestVanillaMethod == null || s_getAllProxyMethod == null || s_getNearestProxyMethod == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (ins.operand is not MethodInfo called)
                {
                    continue;
                }

                if (called == s_getAllVanillaMethod)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_getAllProxyMethod;
                    continue;
                }

                if (called == s_getNearestVanillaMethod)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_getNearestProxyMethod;
                }
            }

            return list;
        }

        private static bool IsMonsterRelatedType(Type type)
        {
            if (type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (typeof(Enemy).IsAssignableFrom(type) || typeof(EnemyParent).IsAssignableFrom(type))
            {
                return true;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = type.GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
            {
                var fieldType = fields[i].FieldType;
                if (typeof(Enemy).IsAssignableFrom(fieldType) || typeof(EnemyParent).IsAssignableFrom(fieldType))
                {
                    return true;
                }
            }

            var properties = type.GetProperties(flags);
            for (var i = 0; i < properties.Length; i++)
            {
                var propertyType = properties[i].PropertyType;
                if (typeof(Enemy).IsAssignableFrom(propertyType) || typeof(EnemyParent).IsAssignableFrom(propertyType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTricycleType(Type type)
        {
            return type.Name.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (type.FullName?.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                   type.Name.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (type.FullName?.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private static bool MethodCallsSharedPlayerSearch(MethodBase method)
        {
            if (method == null || s_getAllVanillaMethod == null || s_getNearestVanillaMethod == null)
            {
                return false;
            }

            try
            {
                var body = method.GetMethodBody();
                var il = body?.GetILAsByteArray();
                if (il == null || il.Length < 5)
                {
                    return false;
                }

                var allToken = s_getAllVanillaMethod.MetadataToken;
                var nearestToken = s_getNearestVanillaMethod.MetadataToken;

                for (var i = 0; i <= il.Length - 5; i++)
                {
                    var op = il[i];
                    if (op != 0x28 && op != 0x6F)
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    if (token == allToken || token == nearestToken)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static List<PlayerAvatar> GetAllPlayersWithinRangeLastChanceAware(float range, Vector3 position, bool doRaycastCheck, LayerMask layerMask)
        {
            var list = SemiFunc.PlayerGetAllPlayerAvatarWithinRange(range, position, doRaycastCheck, layerMask) ?? new List<PlayerAvatar>();
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return list;
            }

            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0)
            {
                return list;
            }

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || list.Contains(player))
                {
                    continue;
                }

                if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
                {
                    continue;
                }

                var dist = Vector3.Distance(position, headCenter);
                if (dist > range)
                {
                    continue;
                }

                if (doRaycastCheck && IsWallBlocking(position, headCenter, dist, layerMask))
                {
                    continue;
                }

                list.Add(player);
            }

            return list;
        }

        private static PlayerAvatar? GetNearestPlayerWithinRangeLastChanceAware(float range, Vector3 position, bool doRaycastCheck, LayerMask layerMask)
        {
            var list = GetAllPlayersWithinRangeLastChanceAware(range, position, doRaycastCheck, layerMask);
            if (list.Count == 0)
            {
                return null;
            }

            var bestDistance = range;
            PlayerAvatar? bestPlayer = null;
            for (var i = 0; i < list.Count; i++)
            {
                var player = list[i];
                if (player == null)
                {
                    continue;
                }

                var point = ResolveDistancePoint(player);
                var dist = Vector3.Distance(position, point);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestPlayer = player;
                }
            }

            return bestPlayer;
        }

        private static bool IsWallBlocking(Vector3 origin, Vector3 target, float distance, LayerMask layerMask)
        {
            var direction = target - origin;
            var hits = Physics.RaycastAll(origin, direction, distance, layerMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var hitTransform = hits[i].collider?.transform;
                if (hitTransform != null && hitTransform.CompareTag("Wall"))
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector3 ResolveDistancePoint(PlayerAvatar player)
        {
            if (LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                return headCenter;
            }

            var vision = player.PlayerVisionTarget?.VisionTransform;
            if (vision != null)
            {
                return vision.position;
            }

            return player.transform.position;
        }
    }

    [HarmonyPatch]
    internal static class LastChanceMonstersEffectiveTargetPointModule
    {
        private static readonly MethodInfo? s_transformGetPositionMethod =
            AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));

        private static readonly MethodInfo? s_effectiveTransformPositionMethod =
            AccessTools.Method(typeof(LastChanceMonstersEffectiveTargetPointModule), nameof(GetEffectiveTransformPosition));

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

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || !IsMonsterRelatedType(type) || IsTricycleType(type))
                {
                    continue;
                }

                var typeMethods = type.GetMethods(flags);
                for (var m = 0; m < typeMethods.Length; m++)
                {
                    var method = typeMethods[m];
                    if (method == null || method.IsAbstract || method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    if (MethodUsesPlayerTargetTransformPosition(method))
                    {
                        methods.Add(method);
                    }
                }
            }

            return methods;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            foreach (var _ in TargetMethods())
            {
                return true;
            }

            return false;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ReplaceTargetPositionReads(IEnumerable<CodeInstruction> instructions)
        {
            if (s_transformGetPositionMethod == null || s_effectiveTransformPositionMethod == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if ((ins.opcode != OpCodes.Call && ins.opcode != OpCodes.Callvirt) || ins.operand is not MethodInfo called)
                {
                    continue;
                }

                if (called == s_transformGetPositionMethod)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_effectiveTransformPositionMethod;
                }
            }

            return list;
        }

        private static bool MethodUsesPlayerTargetTransformPosition(MethodBase method)
        {
            if (method == null || s_transformGetPositionMethod == null)
            {
                return false;
            }

            try
            {
                var body = method.GetMethodBody();
                var il = body?.GetILAsByteArray();
                if (il == null || il.Length < 5)
                {
                    return false;
                }

                var positionToken = s_transformGetPositionMethod.MetadataToken;
                var hasPositionRead = false;
                for (var i = 0; i <= il.Length - 5; i++)
                {
                    var op = il[i];
                    if (op != 0x28 && op != 0x6F)
                    {
                        continue;
                    }

                    if (BitConverter.ToInt32(il, i + 1) == positionToken)
                    {
                        hasPositionRead = true;
                        break;
                    }
                }

                if (!hasPositionRead)
                {
                    return false;
                }

                var fields = method.DeclaringType?.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fields == null)
                {
                    return false;
                }

                for (var i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    if (typeof(PlayerAvatar).IsAssignableFrom(f.FieldType))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static Vector3 GetEffectiveTransformPosition(Transform transform)
        {
            if (transform == null)
            {
                return Vector3.zero;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return transform.position;
            }

            LastChanceMonstersTargetProxyHelper.TryResolvePlayerAvatarFromTransform(transform, out var player);
            if (player != null && LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                return headCenter;
            }

            return transform.position;
        }

        private static bool IsMonsterRelatedType(Type type)
        {
            if (type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (typeof(Enemy).IsAssignableFrom(type) || typeof(EnemyParent).IsAssignableFrom(type))
            {
                return true;
            }

            return false;
        }

        private static bool IsTricycleType(Type type)
        {
            return type.Name.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (type.FullName?.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                   type.Name.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (type.FullName?.IndexOf("Animal", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

    }
}
