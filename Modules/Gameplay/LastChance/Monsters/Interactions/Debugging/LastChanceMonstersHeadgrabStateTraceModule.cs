#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions.Debugging
{
    [HarmonyPatch]
    internal static class LastChanceMonstersHeadgrabStateTraceModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Headgrab");
        private static readonly Dictionary<int, string> LastStateByEnemy = new();

        private static readonly FieldInfo? PlayerDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo? PlayerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo? PlayerDeathHeadPhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
        private static readonly FieldInfo? PhysGrabObjectPlayerGrabbingField = AccessTools.Field(typeof(PhysGrabObject), "playerGrabbing");

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
            var candidateNames = new[]
            {
                "Update",
                "UpdateState",
                "Logic",
                "PlayerTumbleLogic",
                "StatePlayerGoTo",
                "StatePlayerMove",
                "StatePlayerRelease",
                "StatePlayerPickup",
                "GrabPlayerAddRPC",
                "GrabPlayerRemoveRPC",
                "PlayerStateChangedRPC"
            };

            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || type.Name.IndexOf("Headgrab", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                for (var j = 0; j < candidateNames.Length; j++)
                {
                    var method = type.GetMethod(candidateNames[j], flags);
                    if (method == null || method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    methods.Add(method);
                }
            }

            return methods;
        }

        [HarmonyPrefix]
        private static void Prefix(MethodBase __originalMethod, object __instance)
        {
            if (!ShouldTrace(__instance))
            {
                return;
            }

            LogStateTransition(__instance);
            DebugLog(
                "Enter",
                $"enemy={__instance.GetType().Name} enemyId={GetInstanceKey(__instance)} method={__originalMethod?.Name ?? "n/a"} {BuildSnapshot(__instance)}");
        }

        [HarmonyPostfix]
        private static void Postfix(MethodBase __originalMethod, object __instance)
        {
            if (!ShouldTrace(__instance))
            {
                return;
            }

            DebugLog(
                "Exit",
                $"enemy={__instance.GetType().Name} enemyId={GetInstanceKey(__instance)} method={__originalMethod?.Name ?? "n/a"} {BuildSnapshot(__instance)}");
        }

        private static bool ShouldTrace(object? instance)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadgrabFlow || instance == null)
            {
                return false;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return false;
            }

            return instance.GetType().Name.IndexOf("Headgrab", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void LogStateTransition(object instance)
        {
            var key = GetInstanceKey(instance);
            var current = ReadState(instance);
            if (LastStateByEnemy.TryGetValue(key, out var previous) && string.Equals(previous, current, StringComparison.Ordinal))
            {
                return;
            }

            LastStateByEnemy[key] = current;
            Log.LogInfo($"[Headgrab][StateTransition] enemy={instance.GetType().Name} enemyId={key} from={(previous ?? "n/a")} to={current}");
        }

        private static string BuildSnapshot(object instance)
        {
            var player = ReadPlayerTarget(instance);
            var playerId = player != null ? (player.photonView != null ? player.photonView.ViewID : player.GetInstanceID()) : 0;
            var deadSet = player != null && (PlayerDeadSetField?.GetValue(player) as bool? ?? false);
            var isDisabled = player != null && (PlayerIsDisabledField?.GetValue(player) as bool? ?? false);
            var headProxy = player != null && LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);

            var grabCount = 0;
            string firstGrabber = "n/a";
            if (player?.playerDeathHead != null)
            {
                var phys = PlayerDeathHeadPhysGrabObjectField?.GetValue(player.playerDeathHead) as PhysGrabObject;
                if (phys != null && PhysGrabObjectPlayerGrabbingField?.GetValue(phys) is ICollection grabbing)
                {
                    grabCount = grabbing.Count;
                    if (grabCount > 0)
                    {
                        foreach (var element in grabbing)
                        {
                            if (element == null)
                            {
                                continue;
                            }

                            firstGrabber = element.GetType().Name;
                            break;
                        }
                    }
                }
            }

            return
                $"state={ReadState(instance)} " +
                $"target={(player != null ? playerId.ToString() : "n/a")} " +
                $"deadSet={deadSet} isDisabled={isDisabled} " +
                $"alive={(!deadSet && !isDisabled)} headProxyActive={headProxy} " +
                $"headGrabbers={grabCount} firstGrabber={firstGrabber}";
        }

        private static PlayerAvatar? ReadPlayerTarget(object instance)
        {
            var field = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(instance.GetType(), "playerTarget");
            return field?.GetValue(instance) as PlayerAvatar;
        }

        private static string ReadState(object instance)
        {
            var field = LastChanceMonstersReflectionHelper.FindFieldInHierarchy(instance.GetType(), "currentState");
            return field?.GetValue(instance)?.ToString() ?? "n/a";
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceHeadgrabFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceHeadgrabVerbose && !LogLimiter.ShouldLog($"Headgrab.{reason}", 10))
            {
                return;
            }

            Log.LogInfo($"[Headgrab][{reason}] {detail}");
        }

        private static int GetInstanceKey(object instance)
        {
            if (instance is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID();
            }

            return instance.GetHashCode();
        }
    }
}
