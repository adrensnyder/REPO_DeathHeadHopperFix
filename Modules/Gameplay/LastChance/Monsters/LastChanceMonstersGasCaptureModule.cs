#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters
{
    [HarmonyPatch]
    internal static class LastChanceMonstersGasCaptureModule
    {
        private static readonly Dictionary<Type, GasCheckerReflection> ReflectionCache = new();
        private static readonly FieldInfo? EnemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");

        private sealed class GasCheckerReflection
        {
            internal FieldInfo? PrevCheckPosField;
            internal FieldInfo? CheckTimerField;
            internal FieldInfo? PlayersCollidingField;
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
            if (travel.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var direction = travel.normalized;
            var distance = travel.magnitude;
            var radius = Mathf.Max((__instance as MonoBehaviour)?.transform.localScale.z * 0.5f ?? 0.3f, 0.2f);
            var hits = Physics.SphereCastAll(prev, radius, direction, distance, LayerMask.GetMask("Player", "PhysGrabObject"), QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                if (!TryResolvePlayer(collider, out var player) || player == null)
                {
                    continue;
                }

                if (!LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
                {
                    continue;
                }

                if (cache.PlayerIsOnCooldownMethod.Invoke(owner, new object[] { player }) as bool? == true)
                {
                    continue;
                }

                if (cache.PlayerInGasCheckMethod.Invoke(owner, new object[] { player }) as bool? == true)
                {
                    continue;
                }

                if (visionOrigin != null && player.PlayerVisionTarget?.VisionTransform != null)
                {
                    var target = player.PlayerVisionTarget.VisionTransform.position;
                    var dir = target - visionOrigin.position;
                    if (Physics.Raycast(visionOrigin.position, dir, dir.magnitude, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                }

                if (!ContainsPlayer(playersColliding, player))
                {
                    playersColliding.Add(player);
                }

                cache.PlayerInGasMethod.Invoke(owner, new object[] { player });
            }
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
                PlayersCollidingField = AccessTools.Field(type, "playersColliding")
            };

            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < fields.Length; i++)
            {
                var ft = fields[i].FieldType;
                if (ft == null)
                {
                    continue;
                }

                var playerInGas = AccessTools.Method(ft, "PlayerInGas", new[] { typeof(PlayerAvatar) });
                var playerOnCooldown = AccessTools.Method(ft, "PlayerIsOnCooldown", new[] { typeof(PlayerAvatar) });
                var playerInGasCheck = AccessTools.Method(ft, "PlayerInGasCheck", new[] { typeof(PlayerAvatar) });
                if (playerInGas == null || playerOnCooldown == null || playerInGasCheck == null)
                {
                    continue;
                }

                built.OwnerField = fields[i];
                built.PlayerInGasMethod = playerInGas;
                built.PlayerIsOnCooldownMethod = playerOnCooldown;
                built.PlayerInGasCheckMethod = playerInGasCheck;
                built.OwnerEnemyField = AccessTools.Field(ft, "enemy");
                break;
            }

            ReflectionCache[type] = built;
            return built;
        }

        private static bool TryResolvePlayer(Collider collider, out PlayerAvatar? player)
        {
            player = null;

            var controller = collider.GetComponentInParent<PlayerController>();
            if (controller != null && controller.playerAvatarScript != null)
            {
                player = controller.playerAvatarScript;
                return true;
            }

            var avatar = collider.GetComponentInParent<PlayerAvatar>();
            if (avatar != null)
            {
                player = avatar;
                return true;
            }

            var tumble = collider.GetComponentInParent<PlayerTumble>();
            if (tumble != null && tumble.playerAvatar != null)
            {
                player = tumble.playerAvatar;
                return true;
            }

            var deathHead = collider.GetComponentInParent<PlayerDeathHead>();
            if (deathHead != null && deathHead.playerAvatar != null)
            {
                player = deathHead.playerAvatar;
                return true;
            }

            return false;
        }

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
    }
}
