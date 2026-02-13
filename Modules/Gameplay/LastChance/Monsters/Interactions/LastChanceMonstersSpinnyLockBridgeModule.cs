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
            if (__instance == null ||
                !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() ||
                !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            if (!IsSpinnyLockState(__instance))
            {
                return;
            }

            var player = PlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            if (player == null || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                return;
            }

            var tumble = player.tumble;
            var tumbleRb = PlayerTumbleRbField?.GetValue(tumble) as Rigidbody;
            if (tumbleRb == null)
            {
                DebugLog("Bridge.Skip.NoTumbleRb", $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)}");
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyPhysGrabObject(player, out var headPhys) || headPhys?.rb == null)
            {
                DebugLog("Bridge.Skip.NoHeadPhys", $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)}");
                return;
            }

            var headRb = headPhys.rb;
            var targetPos = tumbleRb.position;
            var targetRot = tumbleRb.rotation;
            var dist = Vector3.Distance(headRb.position, targetPos);

            // Keep DeathHead physically coupled to the vanilla tumble lock path used by Spinny.
            if (dist > 2.5f)
            {
                headRb.position = targetPos;
                headRb.rotation = targetRot;
                headRb.velocity = tumbleRb.velocity;
                headRb.angularVelocity = tumbleRb.angularVelocity;
                DebugLog("Bridge.HardSnap", $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)} dist={dist:0.00}");
                return;
            }

            headPhys.OverrideZeroGravity(0.1f);

            var follow = SemiFunc.PhysFollowPosition(headRb.position, targetPos, headRb.velocity, 5f);
            var dir = targetPos - headRb.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                var torque = SemiFunc.PhysFollowDirection(headRb.transform, dir.normalized, headRb, 0.5f);
                headRb.AddTorque(torque / Mathf.Max(headRb.mass, 0.0001f), ForceMode.Force);
            }

            headRb.AddForce(follow, _fixedUpdate ? ForceMode.Acceleration : ForceMode.Force);
            headRb.velocity = Vector3.Lerp(headRb.velocity, tumbleRb.velocity, 0.35f);
            headRb.angularVelocity = Vector3.Lerp(headRb.angularVelocity, tumbleRb.angularVelocity, 0.35f);

            DebugLog(
                "Bridge.Apply",
                $"enemyId={GetInstanceKey(__instance)} player={GetPlayerId(player)} state={ReadState(__instance)} dist={dist:0.00} fixed={_fixedUpdate} horizontal={_horizontalPull}");
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
    }
}
