#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersCarryProxyModule
    {
        private static readonly FieldInfo? DeathHeadPhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
        private static readonly FieldInfo? PhysGrabObjectRbField = AccessTools.Field(typeof(PhysGrabObject), "rb");
        private static readonly FieldInfo? PhysGrabObjectCenterPointField = AccessTools.Field(typeof(PhysGrabObject), "centerPoint");
        private static readonly FieldInfo? PhysGrabObjectPlayerGrabbingField = AccessTools.Field(typeof(PhysGrabObject), "playerGrabbing");
        private static readonly Dictionary<Type, CarryReflection> ReflectionCache = new();

        private sealed class CarryReflection
        {
            internal FieldInfo? CurrentStateField;
            internal FieldInfo? PlayerTargetField;
            internal FieldInfo? PlayerPickupTransformField;
        }

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var results = new List<MethodBase>();
            Type[] types;
            try
            {
                types = typeof(Enemy).Assembly.GetTypes();
            }
            catch
            {
                return results;
            }

            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                const BindingFlags methodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var method = type.GetMethod("PlayerTumbleLogic", methodFlags, null, Type.EmptyTypes, null);
                if (method == null || method.GetMethodBody() == null)
                {
                    continue;
                }

                var cache = GetCarryReflection(type);
                if (cache.PlayerTargetField == null || cache.PlayerPickupTransformField == null || cache.CurrentStateField == null)
                {
                    continue;
                }

                // Only keep methods that reference tumble in IL, to stay on carry-like pipelines.
                if (!MethodMentionsTumble(method))
                {
                    continue;
                }

                results.Add(method);
            }

            return results;
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            if (__instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return true;
            }

            var cache = GetCarryReflection(__instance.GetType());
            var player = cache.PlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            if (player == null || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                return true;
            }

            if (!IsCarryState(__instance, cache))
            {
                return true;
            }

            var head = player.playerDeathHead;
            var phys = head != null ? DeathHeadPhysGrabObjectField?.GetValue(head) as PhysGrabObject : null;
            var rb = phys != null ? PhysGrabObjectRbField?.GetValue(phys) as Rigidbody : null;
            var centerPoint = phys != null && PhysGrabObjectCenterPointField?.GetValue(phys) is Vector3 center ? center : head != null ? head.transform.position : Vector3.zero;
            var pickupTransform = cache.PlayerPickupTransformField?.GetValue(__instance) as Transform;
            if (head == null || phys == null || rb == null || pickupTransform == null)
            {
                return true;
            }

            player.FallDamageResetSet(0.1f);
            phys.OverrideMass(1f, 0.1f);
            phys.OverrideAngularDrag(2f, 0.1f);
            phys.OverrideDrag(1f, 0.1f);

            var strength = 1f;
            if (PhysGrabObjectPlayerGrabbingField?.GetValue(phys) is System.Collections.ICollection grabbing && grabbing.Count > 0)
            {
                strength = 0.5f;
            }
            else if (IsState(__instance, cache, "PlayerRelease") || IsState(__instance, cache, "PlayerPickup"))
            {
                strength = 0.75f;
            }

            var followPos = SemiFunc.PhysFollowPosition(centerPoint, pickupTransform.position, rb.velocity, 10f * strength);
            rb.AddForce(followPos * (10f * Time.fixedDeltaTime * strength), ForceMode.Impulse);

            var followRot = SemiFunc.PhysFollowRotation(head.transform, pickupTransform.rotation, rb, 0.2f * strength);
            rb.AddTorque(followRot * (1f * Time.fixedDeltaTime * strength), ForceMode.Impulse);

            // We handled hidden carry logic for head-proxy target; skip vanilla tumble-only path.
            return false;
        }

        private static CarryReflection GetCarryReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new CarryReflection
            {
                CurrentStateField = AccessTools.Field(type, "currentState"),
                PlayerTargetField = AccessTools.Field(type, "playerTarget"),
                PlayerPickupTransformField = AccessTools.Field(type, "playerPickupTransform")
            };

            ReflectionCache[type] = built;
            return built;
        }

        private static bool MethodMentionsTumble(MethodBase method)
        {
            try
            {
                var body = method.GetMethodBody();
                var il = body?.GetILAsByteArray();
                if (il == null || il.Length == 0)
                {
                    return false;
                }

                var tumbleField = AccessTools.Field(typeof(PlayerAvatar), "tumble");
                if (tumbleField == null)
                {
                    return false;
                }

                var token = tumbleField.MetadataToken;
                for (var i = 0; i <= il.Length - 5; i++)
                {
                    var op = il[i];
                    if (op != OpCodes.Ldfld.Value && op != OpCodes.Ldflda.Value)
                    {
                        continue;
                    }

                    if (BitConverter.ToInt32(il, i + 1) == token)
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

        private static bool IsCarryState(object instance, CarryReflection cache)
        {
            return IsState(instance, cache, "PlayerPickup") ||
                   IsState(instance, cache, "PlayerMove") ||
                   IsState(instance, cache, "PlayerRelease");
        }

        private static bool IsState(object instance, CarryReflection cache, string stateName)
        {
            if (instance == null || cache.CurrentStateField == null || string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            var value = cache.CurrentStateField.GetValue(instance);
            var current = value?.ToString();
            return string.Equals(current, stateName, StringComparison.Ordinal);
        }
    }
}

