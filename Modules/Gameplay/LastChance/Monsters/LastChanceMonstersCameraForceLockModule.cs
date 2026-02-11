#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters
{
    [HarmonyPatch]
    internal static class LastChanceMonstersCameraForceLockModule
    {
        private sealed class LockState
        {
            internal float LockStartAt = -1f;
            internal float LastSeenAt = -1f;
            internal float CooldownUntil = -1f;
            internal float LastTouchAt = -1f;
        }

        private static readonly Dictionary<int, LockState> s_lockBySource = new();
        private static float s_nextCleanupAt;

        private static readonly MethodInfo? s_aimTargetSoftSetVanilla =
            AccessTools.Method(typeof(CameraAim), "AimTargetSoftSet", new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(GameObject), typeof(int) });

        private static readonly MethodInfo? s_aimTargetSetVanilla =
            AccessTools.Method(typeof(CameraAim), "AimTargetSet", new[] { typeof(Vector3), typeof(float), typeof(float), typeof(GameObject), typeof(int) });

        private static readonly MethodInfo? s_aimTargetSoftSetProxy =
            AccessTools.Method(typeof(LastChanceMonstersCameraForceLockModule), nameof(AimTargetSoftSetLastChanceAware));

        private static readonly MethodInfo? s_aimTargetSetProxy =
            AccessTools.Method(typeof(LastChanceMonstersCameraForceLockModule), nameof(AimTargetSetLastChanceAware));

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
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
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

                    if (MethodCallsCameraAim(method))
                    {
                        methods.Add(method);
                    }
                }
            }

            return methods;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ReplaceCameraAimCalls(IEnumerable<CodeInstruction> instructions)
        {
            if (s_aimTargetSoftSetVanilla == null || s_aimTargetSetVanilla == null || s_aimTargetSoftSetProxy == null || s_aimTargetSetProxy == null)
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

                if (called == s_aimTargetSoftSetVanilla)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_aimTargetSoftSetProxy;
                    continue;
                }

                if (called == s_aimTargetSetVanilla)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_aimTargetSetProxy;
                }
            }

            return list;
        }

        private static bool MethodCallsCameraAim(MethodBase method)
        {
            if (method == null || s_aimTargetSoftSetVanilla == null || s_aimTargetSetVanilla == null)
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

                var softToken = s_aimTargetSoftSetVanilla.MetadataToken;
                var setToken = s_aimTargetSetVanilla.MetadataToken;
                for (var i = 0; i <= il.Length - 5; i++)
                {
                    var op = il[i];
                    if (op != 0x28 && op != 0x6F)
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    if (token == softToken || token == setToken)
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

        internal static void AimTargetSoftSetLastChanceAware(Vector3 position, float inSpeed, float outSpeed, float strengthNoAim, GameObject source, int prio)
        {
            if (!ShouldApplyCameraForce(source))
            {
                return;
            }

            CameraAim.Instance.AimTargetSoftSet(position, inSpeed, outSpeed, strengthNoAim, source, prio);
        }

        internal static void AimTargetSetLastChanceAware(Vector3 position, float inSpeed, float outSpeed, GameObject source, int prio)
        {
            if (!ShouldApplyCameraForce(source))
            {
                return;
            }

            CameraAim.Instance.AimTargetSet(position, inSpeed, outSpeed, source, prio);
        }

        private static bool ShouldApplyCameraForce(GameObject? source)
        {
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return true;
            }

            var local = PlayerAvatar.instance;
            if (!LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(local))
            {
                return true;
            }

            var now = Time.unscaledTime;
            CleanupOldStates(now);

            var key = source != null ? source.GetInstanceID() : 0;
            if (!s_lockBySource.TryGetValue(key, out var state))
            {
                state = new LockState();
                s_lockBySource[key] = state;
            }

            state.LastTouchAt = now;
            if (state.CooldownUntil > now)
            {
                return false;
            }

            var grace = Mathf.Max(0.05f, InternalConfig.LastChanceMonstersCameraLockKeepAliveGraceSeconds);
            if (state.LockStartAt < 0f || state.LastSeenAt < 0f || now - state.LastSeenAt > grace)
            {
                state.LockStartAt = now;
            }

            state.LastSeenAt = now;
            var maxLock = Mathf.Max(0.1f, InternalConfig.LastChanceMonstersCameraLockMaxSeconds);
            if (now - state.LockStartAt >= maxLock)
            {
                var cooldown = Mathf.Max(0.1f, InternalConfig.LastChanceMonstersCameraLockCooldownSeconds);
                state.CooldownUntil = now + cooldown;
                state.LockStartAt = -1f;
                state.LastSeenAt = -1f;
                return false;
            }

            // Gameplay stays active regardless; this only controls camera forcing.
            return InternalConfig.LastChanceMonstersForceCameraOnLock;
        }

        private static void CleanupOldStates(float now)
        {
            if (now < s_nextCleanupAt)
            {
                return;
            }

            s_nextCleanupAt = now + 5f;
            if (s_lockBySource.Count == 0)
            {
                return;
            }

            var stale = new List<int>();
            foreach (var kvp in s_lockBySource)
            {
                var state = kvp.Value;
                if (state == null)
                {
                    stale.Add(kvp.Key);
                    continue;
                }

                var lastRelevant = Mathf.Max(state.LastTouchAt, state.CooldownUntil);
                if (lastRelevant < 0f || now - lastRelevant > 30f)
                {
                    stale.Add(kvp.Key);
                }
            }

            for (var i = 0; i < stale.Count; i++)
            {
                s_lockBySource.Remove(stale[i]);
            }
        }

        internal static void ResetRuntimeState()
        {
            s_lockBySource.Clear();
            s_nextCleanupAt = 0f;
        }
    }
}
