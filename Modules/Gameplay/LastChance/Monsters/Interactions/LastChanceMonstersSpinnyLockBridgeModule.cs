#nullable enable

using System;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch]
    internal static class LastChanceMonstersSpinnyLockBridgeModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Spinny");
        private static readonly Type? EnemySpinnyType = AccessTools.TypeByName("EnemySpinny");
        private static readonly FieldInfo? PlayerTargetField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "playerTarget") : null;
        private static readonly FieldInfo? CurrentStateField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "currentState") : null;
        private static readonly FieldInfo? PlayerLockPointField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "playerLockPoint") : null;
        private static readonly FieldInfo? OffLockPointTimerField = EnemySpinnyType != null ? AccessTools.Field(EnemySpinnyType, "offLockPointTimer") : null;
        private static readonly FieldInfo? PlayerTumbleRbField = AccessTools.Field(typeof(PlayerTumble), "rb");

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod()
        {
            if (EnemySpinnyType == null)
            {
                return null;
            }

            return AccessTools.Method(EnemySpinnyType, "LockInPlayer", new[] { typeof(bool), typeof(bool) });
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, bool _horizontalPull = false, bool _fixedUpdate = false)
        {
            if (__instance == null)
            {
                return;
            }

            if (!IsSpinnyLockState(__instance))
            {
                return;
            }

            var player = PlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            if (!LastChanceMonstersLockBridgeCore.IsHeadProxyRuntimeApplicable(player))
            {
                return;
            }

            var tumble = player!.tumble;
            var tumbleRb = PlayerTumbleRbField?.GetValue(tumble) as Rigidbody;
            if (tumbleRb == null)
            {
                DebugLog("Bridge.Skip.NoTumbleRb", $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)}");
                return;
            }

            StabilizeTumbleAtLockPoint(__instance, tumbleRb, _fixedUpdate);

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyPhysGrabObject(player, out var headPhys) || headPhys?.rb == null)
            {
                DebugLog("Bridge.Skip.NoHeadPhys", $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)}");
                return;
            }

            var headRb = headPhys.rb;
            var couple = LastChanceMonstersLockBridgeCore.CoupleHeadToTarget(headPhys, headRb, tumbleRb, _fixedUpdate);
            if (couple.HardSnap)
            {
                DebugLog("Bridge.HardSnap", $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)} dist={couple.Distance:0.00}");
                return;
            }

            DebugLog(
                "Bridge.Apply",
                $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)} state={ReadState(__instance)} dist={couple.Distance:0.00} follow={couple.ForceMagnitude:0.00} fixed={_fixedUpdate} horizontal={_horizontalPull}");
        }

        private static void StabilizeTumbleAtLockPoint(object instance, Rigidbody tumbleRb, bool fixedUpdate)
        {
            if (!fixedUpdate || !string.Equals(ReadState(instance), "WaitForRoulette", StringComparison.Ordinal))
            {
                return;
            }

            var lockPoint = PlayerLockPointField?.GetValue(instance) as Transform;
            if (lockPoint == null)
            {
                return;
            }

            var result = LastChanceMonstersLockBridgeCore.StabilizeTargetAtLockPoint(
                tumbleRb,
                lockPoint,
                ReadFloat(instance, OffLockPointTimerField),
                fixedUpdate,
                isPrimaryLockState: true);
            if (!result.Applied)
            {
                return;
            }

            if (result.Kind == LastChanceMonstersLockBridgeCore.LockStabilizeKind.Emergency)
            {
                DebugLog("Bridge.LockStabilizeEmergency", $"enemyId={GetInstanceKey(instance)} dist={result.Distance:0.00} offLock={result.OffLockTimer:0.00}");
                return;
            }

            if (result.Kind == LastChanceMonstersLockBridgeCore.LockStabilizeKind.Snap)
            {
                DebugLog("Bridge.LockStabilizeSnap", $"enemyId={GetInstanceKey(instance)} dist={result.Distance:0.00}");
                return;
            }

            if (result.Kind == LastChanceMonstersLockBridgeCore.LockStabilizeKind.Force)
            {
                DebugLog("Bridge.LockStabilizeForce", $"enemyId={GetInstanceKey(instance)} dist={result.Distance:0.00} force={result.ForceMagnitude:0.00}");
            }
        }

        private static bool IsSpinnyLockState(object instance)
        {
            var state = ReadState(instance);
            return string.Equals(state, "WaitForRoulette", StringComparison.Ordinal) ||
                   string.Equals(state, "Roulette", StringComparison.Ordinal) ||
                   string.Equals(state, "RouletteEndPause", StringComparison.Ordinal) ||
                   string.Equals(state, "RouletteEnd", StringComparison.Ordinal) ||
                   string.Equals(state, "RouletteEffect", StringComparison.Ordinal);
        }

        private static string ReadState(object instance)
        {
            return CurrentStateField?.GetValue(instance)?.ToString() ?? "n/a";
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceSpinnyFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceSpinnyVerbose && !LogLimiter.ShouldLog($"Spinny.Bridge.{reason}", 8))
            {
                return;
            }

            Log.LogInfo($"[Spinny][{reason}] {detail}");
        }

        private static int GetPlayerId(PlayerAvatar player)
        {
            return player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
        }

        private static int GetInstanceKey(object instance)
        {
            if (instance is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID();
            }

            return instance.GetHashCode();
        }

        private static float ReadFloat(object instance, FieldInfo? field)
        {
            if (field == null || field.FieldType != typeof(float))
            {
                return 0f;
            }

            try
            {
                return (float)field.GetValue(instance)!;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
