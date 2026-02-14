#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Utilities;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch]
    internal static class LastChanceMonstersPlayerVisionCheckModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.CeilingEye");

        private sealed class ContinuousLockState
        {
            internal float LockStartAt = -1f;
            internal float LastSeenAt = -1f;
            internal float CooldownUntil = -1f;
            internal float LastTouchAt = -1f;
        }

        private static readonly Dictionary<long, ContinuousLockState> s_lockBySourceAndPlayer = new();
        private static float s_nextCleanupAt;

        internal static void ResetRuntimeState()
        {
            s_lockBySourceAndPlayer.Clear();
            s_nextCleanupAt = 0f;
        }

        private static readonly MethodInfo? s_playerVisionCheckVanilla = AccessTools.Method(
            typeof(SemiFunc),
            "PlayerVisionCheck",
            new[] { typeof(Vector3), typeof(float), typeof(PlayerAvatar), typeof(bool) });

        private static readonly MethodInfo? s_playerVisionCheckPositionVanilla = AccessTools.Method(
            typeof(SemiFunc),
            "PlayerVisionCheckPosition",
            new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(PlayerAvatar), typeof(bool) });

        private static readonly MethodInfo? s_playerVisionCheckProxy = AccessTools.Method(
            typeof(LastChanceMonstersPlayerVisionCheckModule),
            nameof(PlayerVisionCheckLastChanceAware));

        private static readonly MethodInfo? s_playerVisionCheckPositionProxy = AccessTools.Method(
            typeof(LastChanceMonstersPlayerVisionCheckModule),
            nameof(PlayerVisionCheckPositionLastChanceAware));

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
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
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

                    if (MethodCallsVisionChecks(method))
                    {
                        methods.Add(method);
                    }
                }
            }

            return methods;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ReplaceVisionChecks(IEnumerable<CodeInstruction> instructions)
        {
            if (s_playerVisionCheckVanilla == null || s_playerVisionCheckPositionVanilla == null || s_playerVisionCheckProxy == null || s_playerVisionCheckPositionProxy == null)
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

                if (called == s_playerVisionCheckVanilla)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_playerVisionCheckProxy;
                    continue;
                }

                if (called == s_playerVisionCheckPositionVanilla)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = s_playerVisionCheckPositionProxy;
                }
            }

            return list;
        }

        private static bool MethodCallsVisionChecks(MethodBase method)
        {
            if (method == null || s_playerVisionCheckVanilla == null || s_playerVisionCheckPositionVanilla == null)
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

                var checkToken = s_playerVisionCheckVanilla.MetadataToken;
                var checkPosToken = s_playerVisionCheckPositionVanilla.MetadataToken;
                for (var i = 0; i <= il.Length - 5; i++)
                {
                    var op = il[i];
                    if (op != 0x28 && op != 0x6F)
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    if (token == checkToken || token == checkPosToken)
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

        internal static bool PlayerVisionCheckLastChanceAware(Vector3 position, float range, PlayerAvatar player, bool previouslySeen)
        {
            if (LastChanceMonstersTargetProxyHelper.TryGetHeadProxyVisionTarget(player, out var headCenter))
            {
                return PlayerVisionCheckPositionLastChanceAware(position, headCenter, range, player, previouslySeen);
            }

            return SemiFunc.PlayerVisionCheck(position, range, player, previouslySeen);
        }

        internal static bool PlayerVisionCheckPositionLastChanceAware(Vector3 startPosition, Vector3 endPosition, float range, PlayerAvatar player, bool previouslySeen)
        {
            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyVisionTarget(player, out var headCenter))
            {
                return SemiFunc.PlayerVisionCheckPosition(startPosition, endPosition, range, player, previouslySeen);
            }

            endPosition = headCenter;
            var now = Time.unscaledTime;
            CleanupOldStates(now);

            var key = BuildLockKey(startPosition, player);
            if (!s_lockBySourceAndPlayer.TryGetValue(key, out var state))
            {
                state = new ContinuousLockState();
                s_lockBySourceAndPlayer[key] = state;
            }

            state.LastTouchAt = now;
            if (state.CooldownUntil > now)
            {
                DebugVision("CooldownActive", startPosition, endPosition, player, state, now, false);
                return false;
            }

            var seen = HeadProxyVisionCheckPosition(startPosition, endPosition, range, player);
            var keepAliveGrace = Mathf.Max(0.05f, InternalConfig.LastChanceMonstersCameraLockKeepAliveGraceSeconds);
            if (!seen)
            {
                if (state.LastSeenAt < 0f || now - state.LastSeenAt > keepAliveGrace)
                {
                    state.LockStartAt = -1f;
                }
                DebugVision("NotSeen", startPosition, endPosition, player, state, now, false);
                return false;
            }

            if (state.LockStartAt < 0f || state.LastSeenAt < 0f || now - state.LastSeenAt > keepAliveGrace)
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
                DebugVision("ReachedMaxLock_SetCooldown", startPosition, endPosition, player, state, now, false);
                return false;
            }

            DebugVision("SeenAndAllowed", startPosition, endPosition, player, state, now, true);
            return true;
        }


        private static bool HeadProxyVisionCheckPosition(Vector3 startPosition, Vector3 endPosition, float range, PlayerAvatar player)
        {
            // Ceiling-eye specific resilience: test a few vertical samples so near-floor head proxies
            // are still detectable when floor geometry clips the direct center ray.
            var candidatePoints = new[]
            {
                endPosition,
                endPosition + Vector3.up * 0.2f,
                endPosition + Vector3.up * 0.45f
            };

            for (var i = 0; i < candidatePoints.Length; i++)
            {
                if (HeadProxyVisionCheckPositionSingle(startPosition, candidatePoints[i], range, player))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HeadProxyVisionCheckPositionSingle(Vector3 startPosition, Vector3 endPosition, float range, PlayerAvatar player)
        {
            var direction = endPosition - startPosition;
            var distance = direction.magnitude;
            if (distance > range)
            {
                return false;
            }

            if (distance <= 0.001f)
            {
                return true;
            }

            var hits = Physics.RaycastAll(startPosition, direction.normalized, distance, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var t = hits[i].transform;
                if (t == null)
                {
                    continue;
                }

                if (t.CompareTag("Enemy"))
                {
                    continue;
                }

                var hitHead = t.GetComponentInParent<PlayerDeathHead>();
                if (hitHead != null && player != null && hitHead == player.playerDeathHead)
                {
                    continue;
                }

                var hitAvatar = t.GetComponentInParent<PlayerAvatar>();
                if (hitAvatar != null && hitAvatar == player)
                {
                    continue;
                }

                if (t.GetComponentInParent<PlayerTumble>() != null)
                {
                    continue;
                }

                // If the obstruction is extremely close to target point, treat it as endpoint grazing
                // (common when target is on/near floor).
                var hitToTarget = Vector3.Distance(hits[i].point, endPosition);
                if (hitToTarget <= 0.35f)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void DebugVision(
            string reason,
            Vector3 startPosition,
            Vector3 endPosition,
            PlayerAvatar player,
            ContinuousLockState state,
            float now,
            bool decision)
        {
            if (!InternalDebugFlags.DebugLastChanceCeilingEyeFlow)
            {
                return;
            }

            var playerId = player != null && player.photonView != null ? player.photonView.ViewID : (player?.GetInstanceID() ?? 0);
            if (!LogLimiter.ShouldLog($"CeilingEye.Vision.{reason}.{playerId}", 90))
            {
                return;
            }

            Log.LogInfo(
                $"[CeilingEye][Vision][{reason}] playerId={playerId} decision={decision} " +
                $"start={startPosition} end={endPosition} now={now:F2} lockStart={state.LockStartAt:F2} lastSeen={state.LastSeenAt:F2} cooldownUntil={state.CooldownUntil:F2}");
        }

        private static long BuildLockKey(Vector3 startPosition, PlayerAvatar player)
        {
            var sourceBucketSize = Mathf.Max(0.1f, InternalConfig.LastChanceMonstersVisionLockSourceBucketSize);
            var px = Mathf.RoundToInt(startPosition.x / sourceBucketSize);
            var py = Mathf.RoundToInt(startPosition.y / sourceBucketSize);
            var pz = Mathf.RoundToInt(startPosition.z / sourceBucketSize);
            var sourceHash = ((px * 73856093) ^ (py * 19349663) ^ (pz * 83492791));
            var playerId = player != null && player.photonView != null ? player.photonView.ViewID : (player?.GetInstanceID() ?? 0);
            return ((long)sourceHash << 32) ^ (uint)playerId;
        }

        private static void CleanupOldStates(float now)
        {
            if (now < s_nextCleanupAt)
            {
                return;
            }

            s_nextCleanupAt = now + 5f;
            if (s_lockBySourceAndPlayer.Count == 0)
            {
                return;
            }

            var stale = new List<long>();
            foreach (var kvp in s_lockBySourceAndPlayer)
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
                s_lockBySourceAndPlayer.Remove(stale[i]);
            }
        }
    }
}

