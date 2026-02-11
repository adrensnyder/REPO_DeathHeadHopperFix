#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersVisualCouplingDecoupleModule
    {
        private static readonly FieldInfo? s_tumbleWingPinkTimerField =
            AccessTools.Field(typeof(ItemUpgradePlayerTumbleWingsLogic), "tumbleWingPinkTimer");

        private static readonly MethodInfo? s_upgradeTumbleWingsVisualsActiveMethod =
            AccessTools.Method(typeof(PlayerAvatar), "UpgradeTumbleWingsVisualsActive", new[] { typeof(bool), typeof(bool) });

        private static readonly MethodInfo? s_setTumbleWingPinkTimerSafeMethod =
            AccessTools.Method(typeof(LastChanceMonstersVisualCouplingDecoupleModule), nameof(SetTumbleWingPinkTimerSafe));

        private static readonly MethodInfo? s_upgradeTumbleWingsVisualsActiveSafeMethod =
            AccessTools.Method(typeof(LastChanceMonstersVisualCouplingDecoupleModule), nameof(UpgradeTumbleWingsVisualsActiveSafe));

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            if (s_tumbleWingPinkTimerField == null && s_upgradeTumbleWingsVisualsActiveMethod == null)
            {
                yield break;
            }

            Type[] types;
            try
            {
                types = typeof(Enemy).Assembly.GetTypes();
            }
            catch
            {
                yield break;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var methods = type.GetMethods(flags);
                for (var m = 0; m < methods.Length; m++)
                {
                    var method = methods[m];
                    if (method == null || method.IsAbstract || method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    if (MethodUsesTumbleWingPinkTimer(method) || MethodCallsTumbleWingsVisuals(method))
                    {
                        yield return method;
                    }
                }
            }
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);

            if (s_tumbleWingPinkTimerField != null && s_setTumbleWingPinkTimerSafeMethod != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var ins = list[i];
                    if (ins.opcode == OpCodes.Stfld && ins.operand is FieldInfo field && field == s_tumbleWingPinkTimerField)
                    {
                        ins.opcode = OpCodes.Call;
                        ins.operand = s_setTumbleWingPinkTimerSafeMethod;
                    }
                }
            }

            if (s_upgradeTumbleWingsVisualsActiveMethod != null && s_upgradeTumbleWingsVisualsActiveSafeMethod != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var ins = list[i];
                    if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) &&
                        ins.operand is MethodInfo method &&
                        method == s_upgradeTumbleWingsVisualsActiveMethod)
                    {
                        ins.opcode = OpCodes.Call;
                        ins.operand = s_upgradeTumbleWingsVisualsActiveSafeMethod;
                    }
                }
            }

            return list;
        }

        private static bool MethodUsesTumbleWingPinkTimer(MethodBase method)
        {
            return MethodContainsCallOrFieldToken(method, s_tumbleWingPinkTimerField?.MetadataToken ?? -1, requireStfld: true);
        }

        private static bool MethodCallsTumbleWingsVisuals(MethodBase method)
        {
            return MethodContainsCallOrFieldToken(method, s_upgradeTumbleWingsVisualsActiveMethod?.MetadataToken ?? -1, requireStfld: false);
        }

        private static bool MethodContainsCallOrFieldToken(MethodBase method, int targetToken, bool requireStfld)
        {
            if (method == null || targetToken < 0)
            {
                return false;
            }

            try
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null || il.Length < 5)
                {
                    return false;
                }

                for (var i = 0; i <= il.Length - 5; i++)
                {
                    var op = il[i];
                    if (requireStfld)
                    {
                        if (op != 0x7D)
                        {
                            continue;
                        }
                    }
                    else if (op != 0x28 && op != 0x6F)
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    if (token == targetToken)
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

        private static void SetTumbleWingPinkTimerSafe(ItemUpgradePlayerTumbleWingsLogic? logic, float value)
        {
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                if (logic == null)
                {
                    throw new System.NullReferenceException("upgradeTumbleWingsLogic is null");
                }

                s_tumbleWingPinkTimerField?.SetValue(logic, value);
                return;
            }

            if (logic == null)
            {
                return;
            }

            s_tumbleWingPinkTimerField?.SetValue(logic, value);
        }

        private static void UpgradeTumbleWingsVisualsActiveSafe(PlayerAvatar player, bool visualsActive, bool pink)
        {
            if (player == null)
            {
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                player.UpgradeTumbleWingsVisualsActive(visualsActive, pink);
                return;
            }

            if (player.upgradeTumbleWingsLogic == null)
            {
                return;
            }

            player.UpgradeTumbleWingsVisualsActive(visualsActive, pink);
        }
    }
}

