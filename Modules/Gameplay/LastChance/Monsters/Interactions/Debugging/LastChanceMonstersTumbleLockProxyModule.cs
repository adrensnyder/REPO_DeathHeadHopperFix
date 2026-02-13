#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Utilities;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions.Debugging
{
    [HarmonyPatch]
    internal static class LastChanceMonstersTumbleLockProxyModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Spinny");
        private static readonly FieldInfo? PlayerTumbleField = AccessTools.Field(typeof(PlayerAvatar), "tumble");
        private static readonly FieldInfo? PlayerIsTumblingField = AccessTools.Field(typeof(PlayerAvatar), "isTumbling");
        private static readonly Dictionary<Type, LockReflection> ReflectionCache = new();
        private static readonly Dictionary<int, string> LastStateByInstance = new();
        private static bool? s_lastRuntimeEnabled;
        private sealed class LockReflection
        {
            internal FieldInfo? PlayerTargetField;
            internal FieldInfo? PlayerLockPointField;
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

                var method = type.GetMethod("LockInPlayer", flags, null, new[] { typeof(bool), typeof(bool) }, null);
                if (method == null || method.GetMethodBody() == null)
                {
                    continue;
                }

                var cache = GetReflection(type);
                if (cache.PlayerTargetField == null || cache.PlayerLockPointField == null)
                {
                    continue;
                }

                if (!MethodMentionsTumble(method))
                {
                    continue;
                }

                methods.Add(method);
            }

            return methods;
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance, bool _horizontalPull = false, bool _fixedUpdate = false)
        {
            var runtimeEnabled = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();
            if (__instance == null || !runtimeEnabled)
            {
                if (InternalDebugFlags.DebugLastChanceSpinnyVerbose)
                {
                    if (!s_lastRuntimeEnabled.HasValue || s_lastRuntimeEnabled.Value != runtimeEnabled || LogLimiter.ShouldLog("Spinny.Skip.Early.Runtime", 5))
                    {
                        DebugLog("Skip.Early", $"instanceNull={__instance == null} runtime={runtimeEnabled}");
                    }
                }
                s_lastRuntimeEnabled = runtimeEnabled;
                return true;
            }
            s_lastRuntimeEnabled = runtimeEnabled;

            var cache = GetReflection(__instance.GetType());
            LogStateTransition(__instance, cache);
            var player = cache.PlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            if (player == null || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                DebugLog(
                    "Skip.NoHeadProxyTarget",
                    $"state={GetStateName(__instance, cache)} playerNull={player == null} headProxyActive={(player != null && LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))}");
                return true;
            }

            if (PlayerTumbleField == null)
            {
                DebugLog("Skip.NoTumbleField", $"state={GetStateName(__instance, cache)}");
                return true;
            }

            var tumbleObject = PlayerTumbleField.GetValue(player);
            if (tumbleObject == null)
            {
                DebugLog("Skip.NoTumbleObject", $"state={GetStateName(__instance, cache)}");
                return true;
            }

            var playerId = player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
            var avatarIsTumbling = ReadBoolField(player, PlayerIsTumblingField, "isTumbling");
            var tumbleIsTumbling = ReadBoolField(tumbleObject, "isTumbling");
            DebugLog(
                "Prefix.Enter",
                $"enemy={__instance.GetType().Name} enemyId={GetInstanceKey(__instance)} playerId={playerId} state={GetStateName(__instance, cache)} " +
                $"fixed={_fixedUpdate} horizontal={_horizontalPull} avatarIsTumbling={(avatarIsTumbling.HasValue ? avatarIsTumbling.Value.ToString() : "n/a")} tumbleIsTumbling={(tumbleIsTumbling.HasValue ? tumbleIsTumbling.Value.ToString() : "n/a")}");
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, bool _horizontalPull = false, bool _fixedUpdate = false)
        {
            if (__instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return;
            }

            var cache = GetReflection(__instance.GetType());
            var player = cache.PlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            if (player == null || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player) || PlayerTumbleField == null)
            {
                return;
            }

            var tumbleObject = PlayerTumbleField.GetValue(player);
            var playerId = player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
            var avatarIsTumbling = ReadBoolField(player, PlayerIsTumblingField, "isTumbling");
            var tumbleIsTumbling = tumbleObject != null ? ReadBoolField(tumbleObject, "isTumbling") : null;
            DebugLog(
                "Postfix.Exit",
                $"enemy={__instance.GetType().Name} enemyId={GetInstanceKey(__instance)} playerId={playerId} state={GetStateName(__instance, cache)} " +
                $"fixed={_fixedUpdate} horizontal={_horizontalPull} avatarIsTumbling={(avatarIsTumbling.HasValue ? avatarIsTumbling.Value.ToString() : "n/a")} tumbleIsTumbling={(tumbleIsTumbling.HasValue ? tumbleIsTumbling.Value.ToString() : "n/a")}");
        }

        private static LockReflection GetReflection(Type type)
        {
            if (ReflectionCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var built = new LockReflection
            {
                PlayerTargetField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerTarget"),
                PlayerLockPointField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "playerLockPoint"),
                CurrentStateField = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(type, "currentState")
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
                    if (op != 0x7B && op != 0x7C)
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

        private static string GetStateName(object instance, LockReflection cache)
        {
            return cache.CurrentStateField?.GetValue(instance)?.ToString() ?? "n/a";
        }

        private static void LogStateTransition(object instance, LockReflection cache)
        {
            if (!InternalDebugFlags.DebugLastChanceSpinnyFlow)
            {
                return;
            }

            var key = GetInstanceKey(instance);
            var current = GetStateName(instance, cache);
            if (LastStateByInstance.TryGetValue(key, out var previous) && string.Equals(previous, current, StringComparison.Ordinal))
            {
                return;
            }

            LastStateByInstance[key] = current;
            Log.LogInfo($"[Spinny][StateTransition] enemy={instance.GetType().Name} enemyId={key} from={(previous ?? "n/a")} to={current}");
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceSpinnyFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceSpinnyVerbose && !LogLimiter.ShouldLog($"Spinny.{reason}", 10))
            {
                return;
            }

            Log.LogInfo($"[Spinny][{reason}] {detail}");
        }

        private static int GetInstanceKey(object instance)
        {
            if (instance is UnityEngine.Object uo)
            {
                return uo.GetInstanceID();
            }

            return instance.GetHashCode();
        }

        private static bool? ReadBoolField(object obj, string fieldName)
        {
            try
            {
                var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(bool))
                {
                    return null;
                }

                return (bool)field.GetValue(obj)!;
            }
            catch
            {
                return null;
            }
        }

        private static bool? ReadBoolField(object obj, FieldInfo? field, string fallbackFieldName)
        {
            if (field != null && field.FieldType == typeof(bool))
            {
                try
                {
                    return (bool)field.GetValue(obj)!;
                }
                catch
                {
                }
            }

            return ReadBoolField(obj, fallbackFieldName);
        }
    }
}
