#nullable enable

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch(typeof(EnemyHidden), "StatePlayerMove")]
    internal static class LastChanceMonstersHiddenDestinationModule
    {
        private static readonly MethodInfo? s_levelPointGetPlayerDistanceVanilla =
            AccessTools.Method(typeof(SemiFunc), "LevelPointGetPlayerDistance", new[] { typeof(Vector3), typeof(float), typeof(float), typeof(bool) });

        private static readonly MethodInfo? s_levelPointGetFurthestFromPlayerVanilla =
            AccessTools.Method(typeof(SemiFunc), "LevelPointGetFurthestFromPlayer", new[] { typeof(Vector3), typeof(float) });

        private static readonly MethodInfo? s_levelPointGetVanilla =
            AccessTools.Method(typeof(SemiFunc), "LevelPointGet", new[] { typeof(Vector3), typeof(float), typeof(float) });

        private static readonly MethodInfo? s_levelPointGetPlayerDistanceProxy =
            AccessTools.Method(typeof(LastChanceMonstersHiddenDestinationModule), nameof(LevelPointGetPlayerDistanceLastChanceAware));

        private static readonly MethodInfo? s_levelPointGetFurthestFromPlayerProxy =
            AccessTools.Method(typeof(LastChanceMonstersHiddenDestinationModule), nameof(LevelPointGetFurthestFromPlayerLastChanceAware));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (s_levelPointGetPlayerDistanceVanilla == null ||
                s_levelPointGetFurthestFromPlayerVanilla == null ||
                s_levelPointGetPlayerDistanceProxy == null ||
                s_levelPointGetFurthestFromPlayerProxy == null)
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

                if (called == s_levelPointGetPlayerDistanceVanilla)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_levelPointGetPlayerDistanceProxy;
                    continue;
                }

                if (called == s_levelPointGetFurthestFromPlayerVanilla)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_levelPointGetFurthestFromPlayerProxy;
                }
            }

            return list;
        }

        internal static LevelPoint? LevelPointGetPlayerDistanceLastChanceAware(Vector3 position, float minDistance, float maxDistance, bool includeTruck)
        {
            var point = SemiFunc.LevelPointGetPlayerDistance(position, minDistance, maxDistance, includeTruck);
            if (point != null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return point;
            }

            // In LastChance all players may be "disabled" for this selector.
            // Keep vanilla first, then fallback to a generic reachable point search.
            return s_levelPointGetVanilla != null
                ? SemiFunc.LevelPointGet(position, 8f, 999f)
                : point;
        }

        internal static LevelPoint? LevelPointGetFurthestFromPlayerLastChanceAware(Vector3 position, float minDistance)
        {
            var point = SemiFunc.LevelPointGetFurthestFromPlayer(position, minDistance);
            if (point != null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return point;
            }

            return s_levelPointGetVanilla != null
                ? SemiFunc.LevelPointGet(position, 8f, 999f)
                : point;
        }
    }
}
