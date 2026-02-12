#nullable enable

using System;
using System.Collections;
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
    internal static class LastChanceMonstersGasCaptureModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.HeartHugger");
        private static readonly Dictionary<Type, GasCheckerReflection> ReflectionCache = new();
        private static readonly FieldInfo? EnemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");

        private sealed class GasCheckerReflection
        {
            internal FieldInfo? PrevCheckPosField;
            internal FieldInfo? CheckTimerField;
            internal FieldInfo? PlayersCollidingField;
            internal FieldInfo? GasGuiderPrefabField;
            internal FieldInfo? OwnerField;
            internal MethodInfo? PlayerInGasMethod;
            internal MethodInfo? PlayerIsOnCooldownMethod;
            internal MethodInfo? PlayerInGasCheckMethod;
            internal FieldInfo? OwnerEnemyField;
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
                if (type == null || type.Name.IndexOf("GasChecker", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var update = type.GetMethod("Update", flags, null, Type.EmptyTypes, null);
                if (update == null || update.GetMethodBody() == null)
                {
                    continue;
                }

                var cache = GetReflection(type);
                if (cache.PrevCheckPosField == null || cache.CheckTimerField == null || cache.PlayersCollidingField == null || cache.OwnerField == null || cache.PlayerInGasMethod == null || cache.PlayerIsOnCooldownMethod == null || cache.PlayerInGasCheckMethod == null)
                {
                    continue;
                }

                methods.Add(update);
            }

            return methods;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            if (__instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            var cache = GetReflection(__instance.GetType());
            if (cache.PrevCheckPosField == null || cache.CheckTimerField == null || cache.PlayersCollidingField == null || cache.OwnerField == null || cache.PlayerInGasMethod == null || cache.PlayerIsOnCooldownMethod == null || cache.PlayerInGasCheckMethod == null)
            {
                return;
            }

            if (cache.CheckTimerField.GetValue(__instance) is not float checkTimer || checkTimer < 0.95f)
            {
                return;
            }

            var owner = cache.OwnerField.GetValue(__instance);
            if (owner == null)
            {
                return;
            }

            var playersColliding = cache.PlayersCollidingField.GetValue(__instance) as IList;
            if (playersColliding == null)
            {
                return;
            }

            var ownerEnemy = cache.OwnerEnemyField?.GetValue(owner) as Enemy;
            var ownerVision = ownerEnemy != null ? EnemyVisionField?.GetValue(ownerEnemy) as EnemyVision : null;
            var visionOrigin = ownerVision?.VisionTransform;

            var prev = cache.PrevCheckPosField.GetValue(__instance) as Vector3? ?? default;
            var current = (__instance as MonoBehaviour)?.transform.position ?? prev;
            var travel = current - prev;

            var direction = travel.normalized;
            var distance = travel.magnitude;
            var radius = Mathf.Max((__instance as MonoBehaviour)?.transform.localScale.z * 0.5f ?? 0.3f, 0.2f);

            if (distance <= 0.01f)
            {
                DebugLog("Gas.NoTravel", $"overlap fallback radius={radius:0.00}");
                var overlap = Physics.OverlapSphere(current, radius, ~0, QueryTriggerInteraction.Ignore);
                var processed = 0;
                for (var i = 0; i < overlap.Length; i++)
                {
                    var col = overlap[i];
                    if (col == null)
                    {
                        continue;
                    }

                    if (ProcessCandidateCollider(cache, __instance, owner, playersColliding, ownerVision, col, current, radius))
                    {
                        processed++;
                    }
                }
                DebugLog("Gas.NoTravel.Result", $"overlap={overlap.Length} processed={processed}");

                return;
            }

            var hits = Physics.SphereCastAll(prev, radius, direction, distance, LayerMask.GetMask("Player", "PhysGrabObject"), QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }
                _ = ProcessCandidateCollider(cache, __instance, owner, playersColliding, ownerVision, collider, hits[i].point, radius);
            }
        }

        private static bool ProcessCandidateCollider(
            GasCheckerReflection cache,
            object checkerInstance,
            object owner,
            IList playersColliding,
            EnemyVision? ownerVision,
            Collider collider,
            Vector3 hitPoint,
            float radius)
        {
            if (cache.PlayerIsOnCooldownMethod == null || cache.PlayerInGasCheckMethod == null || cache.PlayerInGasMethod == null)
            {
                return false;
            }

            if (!TryResolvePlayer(collider, out var player) || player == null)
            {
                return false;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                DebugLog("Gas.Skip.NotHeadProxy", $"player={GetPlayerId(player)}");
                return false;
            }

            if (cache.PlayerIsOnCooldownMethod.Invoke(owner, new object[] { player }) as bool? == true)
            {
                DebugLog("Gas.Skip.Cooldown", $"player={GetPlayerId(player)}");
                return false;
            }

            if (cache.PlayerInGasCheckMethod.Invoke(owner, new object[] { player }) as bool? == true)
            {
                DebugLog("Gas.Skip.AlreadyInGas", $"player={GetPlayerId(player)}");
                return false;
            }

            var visionOrigin = ownerVision?.VisionTransform;
            if (visionOrigin != null && player.PlayerVisionTarget?.VisionTransform != null)
            {
                var target = player.PlayerVisionTarget.VisionTransform.position;
                var dir = target - visionOrigin.position;
                if (Physics.Raycast(visionOrigin.position, dir, dir.magnitude, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
                {
                    DebugLog("Gas.Skip.BlockedLOS", $"player={GetPlayerId(player)}");
                    return false;
                }
            }

            if (!ContainsPlayer(playersColliding, player))
            {
                playersColliding.Add(player);
            }

            cache.PlayerInGasMethod.Invoke(owner, new object[] { player });
            DebugLog("Gas.PlayerInGas", $"player={GetPlayerId(player)} radius={radius:0.00}");
            TrySpawnGasGuiderForHeadProxy(cache, checkerInstance, owner, player, hitPoint);
            return true;
        }

        private static GasCheckerReflection GetReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new GasCheckerReflection
            {
                PrevCheckPosField = AccessTools.Field(type, "prevCheckPos"),
                CheckTimerField = AccessTools.Field(type, "checkTimer"),
                PlayersCollidingField = AccessTools.Field(type, "playersColliding"),
                GasGuiderPrefabField = AccessTools.Field(type, "gasGuider")
            };

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < fields.Length; i++)
            {
                var ft = fields[i].FieldType;
                if (ft == null)
                {
                    continue;
                }

                var playerInGas = LastChanceMonstersReflectionHelper.FindMethodInHierarchy(ft, "PlayerInGas", new[] { typeof(PlayerAvatar) });
                var playerOnCooldown = LastChanceMonstersReflectionHelper.FindMethodInHierarchy(ft, "PlayerIsOnCooldown", new[] { typeof(PlayerAvatar) });
                var playerInGasCheck = LastChanceMonstersReflectionHelper.FindMethodInHierarchy(ft, "PlayerInGasCheck", new[] { typeof(PlayerAvatar) });
                if (playerInGas == null || playerOnCooldown == null || playerInGasCheck == null)
                {
                    continue;
                }

                built.OwnerField = fields[i];
                built.PlayerInGasMethod = playerInGas;
                built.PlayerIsOnCooldownMethod = playerOnCooldown;
                built.PlayerInGasCheckMethod = playerInGasCheck;
                built.OwnerEnemyField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(ft, "enemy");
                break;
            }

            ReflectionCache[type] = built;
            return built;
        }

        private static bool TryResolvePlayer(Collider collider, out PlayerAvatar? player)
            => LastChanceMonstersReflectionHelper.TryResolvePlayerFromCollider(collider, out player);

        private static bool ContainsPlayer(IList list, PlayerAvatar player)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], player))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrySpawnGasGuiderForHeadProxy(GasCheckerReflection cache, object checkerInstance, object owner, PlayerAvatar player, Vector3 hitPoint)
        {
            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTransform(player, out var headTransform) || headTransform == null)
            {
                DebugLog("Guider.SpawnSkip.NoHeadTransform", $"player={GetPlayerId(player)}");
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyPhysGrabObject(player, out var headPhys) || headPhys == null)
            {
                DebugLog("Guider.SpawnSkip.NoHeadPhys", $"player={GetPlayerId(player)}");
                return;
            }

            if (cache.GasGuiderPrefabField?.GetValue(checkerInstance) is not GameObject gasGuiderPrefab || gasGuiderPrefab == null)
            {
                DebugLog("Guider.SpawnSkip.NoPrefab", $"player={GetPlayerId(player)}");
                return;
            }

            var instance = UnityEngine.Object.Instantiate(gasGuiderPrefab, (checkerInstance as MonoBehaviour)?.transform.position ?? headTransform.position, Quaternion.identity);
            if (instance == null)
            {
                DebugLog("Guider.SpawnSkip.InstantiateNull", $"player={GetPlayerId(player)}");
                return;
            }

            var guider = instance.GetComponent("EnemyHeartHuggerGasGuider");
            if (guider == null)
            {
                DebugLog("Guider.SpawnSkip.NoComponent", $"player={GetPlayerId(player)} prefab={gasGuiderPrefab.name}");
                return;
            }

            var guiderType = guider.GetType();
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "playerTumble")?.SetValue(guider, player.tumble);
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "targetTransform")?.SetValue(guider, headTransform);
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "enemyHeartHugger")?.SetValue(guider, owner);
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "headTransform")?.SetValue(guider, headTransform);
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "startPosition")?.SetValue(guider, hitPoint);
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "physGrabObject")?.SetValue(guider, headPhys);
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(guiderType, "player")?.SetValue(guider, player);
            instance.SetActive(true);
            DebugLog("Guider.Spawned", $"player={GetPlayerId(player)} start={hitPoint} head={headTransform.position}");
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceHeartHuggerFlow || !LogLimiter.ShouldLog($"HeartHugger.{reason}", 30))
            {
                return;
            }

            Log.LogInfo($"[HeartHugger][{reason}] {detail}");
        }

        private static int GetPlayerId(PlayerAvatar player)
        {
            var view = player.photonView;
            return view != null ? view.ViewID : player.GetInstanceID();
        }
    }
}

