#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Utilities;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersPathBlockingModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Tricycle");
        private const float DefaultBlockingRadius = 0.5f;
        private const float DefaultBlockingDistance = 3f;
        private const float DefaultNavmeshRadius = 1f;
        private static float s_lastTargetSnapshotAt;

        private static readonly Dictionary<Type, BlockingReflection> ReflectionCache = new();
        private static readonly FieldInfo? EnemyNavMeshAgentField = AccessTools.Field(typeof(Enemy), "NavMeshAgent");

        private sealed class BlockingReflection
        {
            internal FieldInfo? EnemyField;
            internal FieldInfo? AgentDirectionField;
            internal FieldInfo? IsBlockedByPlayerField;
            internal FieldInfo? IsBlockedByPlayerAvatarField;
            internal FieldInfo? PlayerTargetField;
            internal FieldInfo? CurrentStateField;
        }

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
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var method = type.GetMethod("IsPlayerBlockingNavmeshPath", flags, null, Type.EmptyTypes, null);
                if (method == null || method.ReturnType != typeof(bool) || method.GetMethodBody() == null)
                {
                    continue;
                }

                var cache = GetReflection(type);
                if (cache.EnemyField == null || cache.AgentDirectionField == null || cache.IsBlockedByPlayerField == null || cache.IsBlockedByPlayerAvatarField == null)
                {
                    continue;
                }

                methods.Add(method);
            }

            return methods;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, ref bool __result)
        {
            var tricycle = IsTricycleType(__instance?.GetType());
            if (__result || __instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                if (tricycle)
                {
                    DebugLog("Skip.Early", $"result={__result} instanceNull={__instance == null} runtime={LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled()} master={LastChanceMonstersTargetProxyHelper.IsMasterContext()}");
                }
                return;
            }

            var cache = GetReflection(__instance.GetType());
            var enemy = cache.EnemyField?.GetValue(__instance) as Enemy;
            if (enemy == null || enemy.CenterTransform == null)
            {
                if (tricycle)
                {
                    DebugLog("Skip.NoEnemy", "enemy or center transform missing");
                }
                return;
            }

            var navMeshAgent = EnemyNavMeshAgentField?.GetValue(enemy);
            if (navMeshAgent == null)
            {
                if (tricycle)
                {
                    DebugLog("Skip.NoNavMeshAgent", $"enemy={enemy.name}");
                }
                return;
            }

            var heading = cache.AgentDirectionField?.GetValue(__instance) as Vector3? ?? Vector3.zero;
            if (heading.sqrMagnitude <= 0.0001f)
            {
                if (tricycle)
                {
                    DebugLog("Skip.NoHeading", $"enemy={enemy.name} heading={heading}");
                }
                return;
            }

            if (tricycle)
            {
                var blockedByPlayer = cache.IsBlockedByPlayerField?.GetValue(__instance) as bool? ?? false;
                var blockedAvatar = cache.IsBlockedByPlayerAvatarField?.GetValue(__instance) as PlayerAvatar;
                var playerTarget = cache.PlayerTargetField?.GetValue(__instance) as PlayerAvatar;
                var currentState = cache.CurrentStateField?.GetValue(__instance)?.ToString() ?? "n/a";
                var targetDistBody = playerTarget != null ? Vector3.Distance(enemy.CenterTransform.position, playerTarget.transform.position) : -1f;
                var targetDistHead = playerTarget != null && LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(playerTarget, out var targetHead)
                    ? Vector3.Distance(enemy.CenterTransform.position, targetHead)
                    : -1f;
                var targetHorzBody = -1f;
                var targetHorzHead = -1f;
                var deltaYBody = 0f;
                var deltaYHead = 0f;
                var headingDotBody = 0f;
                var headingDotHead = 0f;
                var losBody = false;
                var losHead = false;
                var hasBody = false;
                var hasHead = false;
                if (playerTarget != null)
                {
                    var center = enemy.CenterTransform.position;
                    var bodyPos = playerTarget.transform.position;
                    var bodyDelta = bodyPos - center;
                    var bodyFlat = new Vector3(bodyDelta.x, 0f, bodyDelta.z);
                    targetHorzBody = bodyFlat.magnitude;
                    deltaYBody = bodyDelta.y;
                    hasBody = bodyDelta.sqrMagnitude > 0.0001f;
                    headingDotBody = hasBody ? Vector3.Dot(heading.normalized, bodyDelta.normalized) : 0f;
                    losBody = HasLineOfSight(center, bodyPos, enemy.transform);

                    if (LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(playerTarget, out var headPos))
                    {
                        var headDelta = headPos - center;
                        var headFlat = new Vector3(headDelta.x, 0f, headDelta.z);
                        targetHorzHead = headFlat.magnitude;
                        deltaYHead = headDelta.y;
                        hasHead = headDelta.sqrMagnitude > 0.0001f;
                        headingDotHead = hasHead ? Vector3.Dot(heading.normalized, headDelta.normalized) : 0f;
                        losHead = HasLineOfSight(center, headPos, enemy.transform);
                    }
                }
                DebugLog(
                    "State",
                    $"enemy={enemy.name} state={currentState} currentBlocked={blockedByPlayer} blockedAvatar={GetPlayerIdOrNone(blockedAvatar)} " +
                    $"playerTarget={GetPlayerIdOrNone(playerTarget)} targetBodyDist={(targetDistBody >= 0f ? targetDistBody.ToString("F2") : "n/a")} " +
                    $"targetHeadDist={(targetDistHead >= 0f ? targetDistHead.ToString("F2") : "n/a")} " +
                    $"targetBodyH={(targetHorzBody >= 0f ? targetHorzBody.ToString("F2") : "n/a")} targetHeadH={(targetHorzHead >= 0f ? targetHorzHead.ToString("F2") : "n/a")} " +
                    $"deltaYBody={(hasBody ? deltaYBody.ToString("F2") : "n/a")} deltaYHead={(hasHead ? deltaYHead.ToString("F2") : "n/a")} " +
                    $"dotBody={(hasBody ? headingDotBody.ToString("F2") : "n/a")} dotHead={(hasHead ? headingDotHead.ToString("F2") : "n/a")} " +
                    $"losBody={losBody} losHead={losHead} probeDist={DefaultBlockingDistance:F2} headingMag={heading.magnitude:F2}");
                TryLogTargetSnapshot(enemy);
            }

            var hits = Physics.SphereCastAll(
                enemy.CenterTransform.position,
                DefaultBlockingRadius,
                heading.normalized,
                DefaultBlockingDistance,
                LayerMask.GetMask("Player", "PhysGrabObject"),
                QueryTriggerInteraction.Ignore);
            if (tricycle)
            {
                DebugLog("Probe", $"enemy={enemy.name} hits={hits.Length} center={enemy.CenterTransform.position} heading={heading.normalized}");
            }

            for (var i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (tricycle)
                {
                    DebugLog(
                        "Hit",
                        $"idx={i} collider={collider.name} dist={hits[i].distance:F2} point={hits[i].point} layer={LayerMask.LayerToName(collider.gameObject.layer)}");
                }

                if (!TryResolvePlayerFromBlockingCollider(collider, out var player) || player == null)
                {
                    if (tricycle)
                    {
                        LastChanceMonstersTargetProxyHelper.TryGetPlayerFromDeathHeadCollider(collider, out var deathHeadPlayer);
                        var self = collider.transform.IsChildOf(enemy.transform);
                        var root = collider.transform.root != null ? collider.transform.root.name : "n/a";
                        DebugLog(
                            "Hit.Skip.NoPlayer",
                            $"collider={collider.name} layer={LayerMask.LayerToName(collider.gameObject.layer)} root={root} self={self} deathHeadPlayer={GetPlayerIdOrNone(deathHeadPlayer)}");
                    }
                    continue;
                }

                if (!TryResolveNavmeshPoint(player, out var candidatePoint))
                {
                    if (tricycle)
                    {
                        DebugLog("Hit.Skip.NoPoint", $"player={GetPlayerId(player)}");
                    }
                    continue;
                }

                if (!InvokeOnNavmesh(navMeshAgent, candidatePoint, DefaultNavmeshRadius, true))
                {
                    if (tricycle)
                    {
                        var fromCenter = Vector3.Distance(enemy.CenterTransform.position, candidatePoint);
                        DebugLog("Hit.Skip.NotOnNavmesh", $"player={GetPlayerId(player)} point={candidatePoint} distFromEnemy={fromCenter:F2}");
                    }
                    continue;
                }

                if (tricycle)
                {
                    // Keep diagnostics only for Tricycle; no behavior override.
                    DebugLog("Blocked.WouldSet", $"player={GetPlayerId(player)} point={candidatePoint}");
                    continue;
                }

                cache.IsBlockedByPlayerAvatarField?.SetValue(__instance, player);
                cache.IsBlockedByPlayerField?.SetValue(__instance, true);
                __result = true;
                if (tricycle)
                {
                    DebugLog("Blocked.Set", $"player={GetPlayerId(player)} point={candidatePoint}");
                }
                return;
            }

            if (tricycle)
            {
                DebugLog("Probe.NoBlock", $"enemy={enemy.name}");
            }
        }

        private static BlockingReflection GetReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new BlockingReflection
            {
                EnemyField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "enemy"),
                AgentDirectionField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "agentDirection"),
                IsBlockedByPlayerField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "isBlockedByPlayer"),
                IsBlockedByPlayerAvatarField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "isBlockedByPlayerAvatar"),
                PlayerTargetField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerTarget"),
                CurrentStateField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "currentState")
            };

            ReflectionCache[type] = built;
            return built;
        }

        private static bool InvokeOnNavmesh(object navMeshAgent, Vector3 position, float radius, bool requirePath)
        {
            var method = AccessTools.Method(navMeshAgent.GetType(), "OnNavmesh", new[] { typeof(Vector3), typeof(float), typeof(bool) });
            if (method == null)
            {
                return false;
            }

            return method.Invoke(navMeshAgent, new object[] { position, radius, requirePath }) as bool? ?? false;
        }

        private static bool TryResolvePlayerFromBlockingCollider(Collider collider, out PlayerAvatar? player)
        {
            return LastChanceMonstersReflectionHelper.TryResolvePlayerFromCollider(collider, out player);
        }

        private static bool TryResolveNavmeshPoint(PlayerAvatar player, out Vector3 point)
        {
            if (LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                point = headCenter;
                return true;
            }

            point = player.transform.position;
            return true;
        }

        private static bool IsTricycleType(Type? type)
        {
            if (type == null)
            {
                return false;
            }

            return type.Name.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (type.FullName?.IndexOf("Tricycle", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private static int GetPlayerId(PlayerAvatar player)
        {
            return player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
        }

        private static string GetPlayerIdOrNone(PlayerAvatar? player)
        {
            return player == null ? "n/a" : GetPlayerId(player).ToString();
        }

        private static bool HasLineOfSight(Vector3 from, Vector3 to, Transform? enemyRoot)
        {
            var delta = to - from;
            var dist = delta.magnitude;
            if (dist <= 0.001f)
            {
                return true;
            }

            var hits = Physics.RaycastAll(from, delta / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var c = hits[i].collider;
                if (c == null)
                {
                    continue;
                }

                var t = c.transform;
                if (enemyRoot != null && t.IsChildOf(enemyRoot))
                {
                    continue;
                }

                if (LastChanceMonstersReflectionHelper.TryResolvePlayerFromCollider(c, out var p) && p != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void TryLogTargetSnapshot(Enemy enemy)
        {
            if (!InternalDebugFlags.DebugLastChanceTricycleFlow)
            {
                return;
            }

            var now = Time.unscaledTime;
            if (now - s_lastTargetSnapshotAt < 1.5f)
            {
                return;
            }

            s_lastTargetSnapshotAt = now;
            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0)
            {
                DebugLog("Targets", "no players in GameDirector list");
                return;
            }

            var center = enemy.CenterTransform.position;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null)
                {
                    continue;
                }

                var bodyDist = Vector3.Distance(center, p.transform.position);
                var headProxy = LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(p, out var headCenter);
                var headDist = headProxy ? Vector3.Distance(center, headCenter) : -1f;
                var disabled = LastChanceMonstersTargetProxyHelper.IsDisabled(p);
                var active = LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(p);
                DebugLog(
                    "Targets",
                    $"player={GetPlayerId(p)} disabled={disabled} headProxyActive={active} headProxy={headProxy} bodyDist={bodyDist:F2} headDist={(headDist >= 0f ? headDist.ToString("F2") : "n/a")} bodyPos={p.transform.position} headPos={(headProxy ? headCenter.ToString() : "n/a")}");
            }
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceTricycleFlow || !LogLimiter.ShouldLog($"Tricycle.{reason}", 30))
            {
                return;
            }

            Log.LogInfo($"[Tricycle][{reason}] {detail}");
        }
    }
}

