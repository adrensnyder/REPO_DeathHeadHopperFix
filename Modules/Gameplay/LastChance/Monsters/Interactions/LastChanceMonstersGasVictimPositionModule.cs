#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersGasVictimPositionModule
    {
        private static readonly MethodInfo? s_componentGetTransform =
            AccessTools.PropertyGetter(typeof(Component), nameof(Component.transform));

        private static readonly MethodInfo? s_transformGetPosition =
            AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));

        private static readonly MethodInfo? s_getEffectivePosition =
            AccessTools.Method(typeof(LastChanceMonstersGasVictimPositionModule), nameof(GetEffectivePlayerPosition));

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types;
            try
            {
                types = typeof(Enemy).Assembly.GetTypes();
            }
            catch
            {
                yield break;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                // Behavior-based selection:
                // methods that maintain gas victim tracking and are expected to read PlayerAvatar.transform.position.
                var playersInGasLogic = type.GetMethod("PlayersInGasLogic", flags, null, System.Type.EmptyTypes, null);
                if (playersInGasLogic?.GetMethodBody() != null)
                {
                    yield return playersInGasLogic;
                }

                var playerInGas = type.GetMethod("PlayerInGas", flags, null, new[] { typeof(PlayerAvatar) }, null);
                if (playerInGas?.GetMethodBody() != null)
                {
                    yield return playerInGas;
                }
            }
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (s_componentGetTransform == null || s_transformGetPosition == null || s_getEffectivePosition == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i <= list.Count - 2; i++)
            {
                var a = list[i];
                var b = list[i + 1];
                if (!CallsMethod(a, s_componentGetTransform) || !CallsMethod(b, s_transformGetPosition))
                {
                    continue;
                }

                a.opcode = OpCodes.Nop;
                a.operand = null;
                b.opcode = OpCodes.Call;
                b.operand = s_getEffectivePosition;
            }

            return list;
        }

        private static bool CallsMethod(CodeInstruction instruction, MethodInfo method)
        {
            return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                   instruction.operand is MethodInfo called &&
                   called == method;
        }

        internal static Vector3 GetEffectivePlayerPosition(PlayerAvatar? player)
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
