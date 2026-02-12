#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersCarryTargetPositionModule
    {
        private static readonly MethodInfo? s_componentGetTransform =
            AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));

        private static readonly MethodInfo? s_transformGetPosition =
            AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));

        private static readonly MethodInfo? s_getEffectiveTargetPosition =
            AccessTools.Method(typeof(LastChanceMonstersCarryTargetPositionModule), nameof(GetEffectivePlayerTargetPosition));

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
            var names = new[] { "StatePlayerGoTo", "StatePlayerMove", "StatePlayerRelease" };
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var playerTargetField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerTarget");
                if (playerTargetField == null || !typeof(PlayerAvatar).IsAssignableFrom(playerTargetField.FieldType))
                {
                    continue;
                }

                for (var n = 0; n < names.Length; n++)
                {
                    var method = type.GetMethod(names[n], flags, null, Type.EmptyTypes, null);
                    if (method == null || method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    methods.Add(method);
                }
            }

            return methods;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ReplacePlayerTargetPositionReads(MethodBase __originalMethod, IEnumerable<CodeInstruction> instructions)
        {
            if (__originalMethod == null || s_componentGetTransform == null || s_transformGetPosition == null || s_getEffectiveTargetPosition == null)
            {
                return instructions;
            }

            var playerTargetField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(__originalMethod.DeclaringType!, "playerTarget");
            if (playerTargetField == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i <= list.Count - 3; i++)
            {
                var a = list[i];
                var b = list[i + 1];
                var c = list[i + 2];

                if ((a.opcode != OpCodes.Ldfld && a.opcode != OpCodes.Ldflda) || a.operand is not FieldInfo loadedField || loadedField != playerTargetField)
                {
                    continue;
                }

                if (!CallsMethod(b, s_componentGetTransform))
                {
                    continue;
                }

                if (!CallsMethod(c, s_transformGetPosition))
                {
                    continue;
                }

                b.opcode = OpCodes.Nop;
                b.operand = null;
                c.opcode = OpCodes.Call;
                c.operand = s_getEffectiveTargetPosition;
            }

            return list;
        }

        private static bool CallsMethod(CodeInstruction instruction, MethodInfo target)
        {
            return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                   instruction.operand is MethodInfo called &&
                   called == target;
        }

        internal static Vector3 GetEffectivePlayerTargetPosition(PlayerAvatar? player)
        {
            if (player == null)
            {
                return Vector3.zero;
            }

            if (LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                return headCenter;
            }

            return player.transform.position;
        }
    }
}
