#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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
    internal static class LastChanceMonstersCarryProxyModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.HiddenCarry");

        private sealed class CarryAnchorState
        {
            internal int PlayerId;
            internal Vector3 PickupOrigin;
            internal bool HasOrigin;
            internal string LastState = string.Empty;
        }

        private static readonly FieldInfo? DeathHeadPhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
        private static readonly FieldInfo? PhysGrabObjectRbField = AccessTools.Field(typeof(PhysGrabObject), "rb");
        private static readonly FieldInfo? PhysGrabObjectCenterPointField = AccessTools.Field(typeof(PhysGrabObject), "centerPoint");
        private static readonly FieldInfo? PhysGrabObjectPlayerGrabbingField = AccessTools.Field(typeof(PhysGrabObject), "playerGrabbing");
        private static readonly Dictionary<Type, CarryReflection> ReflectionCache = new();
        private static readonly Dictionary<int, CarryAnchorState> AnchorByCarrier = new();

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
                ClearPickupOrigin(__instance);
                return true;
            }

            if (!IsCarryState(__instance, cache))
            {
                ClearPickupOrigin(__instance);
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

            UpdatePickupOrigin(__instance, player, GetCurrentStateName(__instance, cache), centerPoint);

            if (InternalDebugFlags.DebugLastChanceHiddenCarryFlow && LogLimiter.ShouldLog("HiddenCarry.PrefixState", 120))
            {
                Log.LogInfo(
                    $"[HiddenCarry][Prefix] state={GetCurrentStateName(__instance, cache)} " +
                    $"headCenter={centerPoint} bodyPos={player.transform.position} pickupPos={pickupTransform.position}");
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

            if (rb.isKinematic)
            {
                rb.position = Vector3.Lerp(centerPoint, pickupTransform.position, 0.35f);
                rb.rotation = Quaternion.Slerp(head.transform.rotation, pickupTransform.rotation, 0.2f * strength);
                return false;
            }

            var followPos = SemiFunc.PhysFollowPosition(centerPoint, pickupTransform.position, rb.velocity, 10f * strength);
            rb.AddForce(followPos * (10f * Time.fixedDeltaTime * strength), ForceMode.Impulse);

            var followRot = SemiFunc.PhysFollowRotation(head.transform, pickupTransform.rotation, rb, 0.2f * strength);
            rb.AddTorque(followRot * (1f * Time.fixedDeltaTime * strength), ForceMode.Impulse);

            // We handled hidden carry logic for head-proxy target; skip vanilla tumble-only path.
            return false;
        }

        internal static bool TryGetPickupOrigin(object carrierInstance, PlayerAvatar player, out Vector3 origin)
        {
            origin = default;
            if (carrierInstance == null || player == null)
            {
                return false;
            }

            var key = GetCarrierKey(carrierInstance);
            if (!AnchorByCarrier.TryGetValue(key, out var state) || state == null || !state.HasOrigin)
            {
                return false;
            }

            var playerId = GetPlayerId(player);
            if (state.PlayerId != playerId)
            {
                return false;
            }

            origin = state.PickupOrigin;
            return true;
        }

        private static CarryReflection GetCarryReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new CarryReflection
            {
                CurrentStateField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "currentState"),
                PlayerTargetField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerTarget"),
                PlayerPickupTransformField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerPickupTransform")
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

        private static string GetCurrentStateName(object instance, CarryReflection cache)
        {
            if (instance == null || cache.CurrentStateField == null)
            {
                return string.Empty;
            }

            return cache.CurrentStateField.GetValue(instance)?.ToString() ?? string.Empty;
        }

        private static void UpdatePickupOrigin(object carrierInstance, PlayerAvatar player, string stateName, Vector3 currentHeadCenter)
        {
            var key = GetCarrierKey(carrierInstance);
            if (!AnchorByCarrier.TryGetValue(key, out var state) || state == null)
            {
                state = new CarryAnchorState();
                AnchorByCarrier[key] = state;
            }

            var playerId = GetPlayerId(player);
            var enteringPickup = string.Equals(stateName, "PlayerPickup", StringComparison.Ordinal) &&
                                 !string.Equals(state.LastState, "PlayerPickup", StringComparison.Ordinal);
            var playerChanged = state.PlayerId != 0 && state.PlayerId != playerId;

            if (!state.HasOrigin || enteringPickup || playerChanged)
            {
                state.PickupOrigin = currentHeadCenter;
                state.HasOrigin = true;
                state.PlayerId = playerId;
                if (InternalDebugFlags.DebugLastChanceHiddenCarryFlow && LogLimiter.ShouldLog("HiddenCarry.PickupOriginSet", 120))
                {
                    Log.LogInfo($"[HiddenCarry][OriginSet] state={stateName} origin={state.PickupOrigin} playerId={state.PlayerId}");
                }
            }

            state.LastState = stateName;
        }

        private static void ClearPickupOrigin(object carrierInstance)
        {
            if (carrierInstance == null)
            {
                return;
            }

            AnchorByCarrier.Remove(GetCarrierKey(carrierInstance));
        }

        private static int GetCarrierKey(object carrierInstance)
        {
            if (carrierInstance is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID();
            }

            return carrierInstance.GetHashCode();
        }

        private static int GetPlayerId(PlayerAvatar player)
        {
            var photonView = player.photonView;
            if (photonView != null)
            {
                return photonView.ViewID;
            }

            return player.GetInstanceID();
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

