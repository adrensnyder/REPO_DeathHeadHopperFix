#nullable enable

using HarmonyLib;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    [HarmonyPatch(typeof(EnemyHeartHuggerGasGuider), "FixedUpdate")]
    internal static class LastChanceMonstersGasGuiderHeadProxyModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.HeartHugger");
        private static readonly FieldInfo? s_playerField =
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(typeof(EnemyHeartHuggerGasGuider), "player");
        private static readonly FieldInfo? s_physGrabObjectField =
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(typeof(EnemyHeartHuggerGasGuider), "physGrabObject");
        private static readonly FieldInfo? s_startPositionField =
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(typeof(EnemyHeartHuggerGasGuider), "startPosition");
        private static readonly FieldInfo? s_enemyHeartHuggerField =
            LastChanceMonstersReflectionHelper.FindFieldInHierarchy(typeof(EnemyHeartHuggerGasGuider), "enemyHeartHugger");

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            if (__instance == null || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() || !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return true;
            }

            var player = s_playerField?.GetValue(__instance) as PlayerAvatar;
            if (player == null || !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
            {
                return true;
            }

            var phys = s_physGrabObjectField?.GetValue(__instance) as PhysGrabObject;
            if (phys?.rb == null)
            {
                DebugLog("Guider.Fixed.SkipNoPhys", $"player={GetPlayerId(player)}");
                return true;
            }

            var enemyHeartHugger = s_enemyHeartHuggerField?.GetValue(__instance) as EnemyHeartHugger;
            if (enemyHeartHugger?.headCenterTransform == null || __instance is not Component guiderComponent)
            {
                DebugLog("Guider.Fixed.SkipNoEnemyHead", $"player={GetPlayerId(player)}");
                return true;
            }

            var rb = phys.rb;
            var start = s_startPositionField?.GetValue(__instance) as Vector3? ?? rb.position;
            var dirToHead = (enemyHeartHugger.headCenterTransform.position - start).normalized;
            var from = rb.position;
            var to = guiderComponent.transform.position;

            phys.OverrideZeroGravity(0.1f);
            if (rb.isKinematic)
            {
                rb.position = Vector3.Lerp(from, to, 0.3f);
                var targetRot = Quaternion.LookRotation(dirToHead.sqrMagnitude > 0.0001f ? dirToHead : rb.transform.forward, Vector3.up);
                rb.rotation = Quaternion.Slerp(rb.rotation, targetRot, 0.25f);
                DebugLog("Guider.Fixed.Kinematic", $"player={GetPlayerId(player)} rbPos={from} target={to}");
                return false;
            }

            var torque = SemiFunc.PhysFollowDirection(rb.transform, dirToHead, rb, 0.5f);
            rb.AddTorque(torque / Mathf.Max(rb.mass, 0.0001f), ForceMode.Force);
            var force = SemiFunc.PhysFollowPosition(from, to, rb.velocity, 5f);
            rb.AddForce(force, ForceMode.Acceleration);
            DebugLog("Guider.Fixed.Apply", $"player={GetPlayerId(player)} rbPos={from} target={to} forceMag={force.magnitude:0.00}");
            return false;
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
