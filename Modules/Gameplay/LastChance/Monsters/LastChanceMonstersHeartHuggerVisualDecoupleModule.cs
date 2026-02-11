#nullable enable

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters
{
    [HarmonyPatch]
    internal static class LastChanceMonstersHeartHuggerVisualDecoupleModule
    {
        private static readonly MethodInfo? s_playerInGasLogicMethod =
            AccessTools.Method(typeof(EnemyHeartHugger), "PlayersInGasLogic");

        private static readonly MethodInfo? s_gasCheckerUpdateMethod =
            AccessTools.Method(typeof(EnemyHeartHuggerGasChecker), "Update");

        private static readonly FieldInfo? s_tumbleWingPinkTimerField =
            AccessTools.Field(typeof(ItemUpgradePlayerTumbleWingsLogic), "tumbleWingPinkTimer");

        private static readonly MethodInfo? s_upgradeTumbleWingsVisualsActiveMethod =
            AccessTools.Method(typeof(PlayerAvatar), "UpgradeTumbleWingsVisualsActive", new[] { typeof(bool), typeof(bool) });

        private static readonly MethodInfo? s_setTumbleWingPinkTimerSafeMethod =
            AccessTools.Method(typeof(LastChanceMonstersHeartHuggerVisualDecoupleModule), nameof(SetTumbleWingPinkTimerSafe));

        private static readonly MethodInfo? s_upgradeTumbleWingsVisualsActiveSafeMethod =
            AccessTools.Method(typeof(LastChanceMonstersHeartHuggerVisualDecoupleModule), nameof(UpgradeTumbleWingsVisualsActiveSafe));

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            if (s_playerInGasLogicMethod != null)
            {
                yield return s_playerInGasLogicMethod;
            }

            if (s_gasCheckerUpdateMethod != null)
            {
                yield return s_gasCheckerUpdateMethod;
            }
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(MethodBase __originalMethod, IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);

            if (__originalMethod == s_playerInGasLogicMethod &&
                s_tumbleWingPinkTimerField != null &&
                s_setTumbleWingPinkTimerSafeMethod != null)
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

            if (__originalMethod == s_gasCheckerUpdateMethod &&
                s_upgradeTumbleWingsVisualsActiveMethod != null &&
                s_upgradeTumbleWingsVisualsActiveSafeMethod != null)
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
