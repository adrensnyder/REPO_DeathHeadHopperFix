#nullable enable

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch]
    internal static class LastChanceMonstersSharedChaseTargetPointModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Headman");
        private static readonly MethodInfo? s_transformGetPositionMethod =
            AccessTools.PropertyGetter(typeof(Transform), nameof(Transform.position));

        private static readonly MethodInfo? s_effectiveTransformPositionMethod =
            AccessTools.Method(typeof(LastChanceMonstersSharedChaseTargetPointModule), nameof(GetEffectiveTransformPosition));

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();

            AddIfFound(methods, typeof(EnemyStateChase), "Update");
            AddIfFound(methods, typeof(EnemyStateChaseBegin), "Update");
            AddIfFound(methods, typeof(EnemyStateChaseSlow), "Update");

            return methods;
        }

        [HarmonyPrepare]
        private static bool Prepare()
        {
            return s_transformGetPositionMethod != null && s_effectiveTransformPositionMethod != null;
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
                if (InternalDebugFlags.DebugLastChanceHeadmanFlow)
                {
                    var key = $"Headman.TargetPointProxy.{player.GetInstanceID()}";
                    if (InternalDebugFlags.DebugLastChanceHeadmanVerbose || LogLimiter.ShouldLog(key, 15))
                    {
                        Log.LogInfo(
                            $"[Headman][TargetPointProxy] runtime=True player={player.name} playerId={player.GetInstanceID()} " +
                            $"body={transform.position} head={headCenter}");
                    }
                }
                return headCenter;
            }

            return transform.position;
        }

        private static void AddIfFound(List<MethodBase> methods, System.Type type, string methodName)
        {
            var method = AccessTools.Method(type, methodName);
            if (method != null)
            {
                methods.Add(method);
            }
        }
    }
}
