#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions
{
    internal static class LastChanceMonstersNoiseAggroModule
    {
        private const string PatchId = "DeathHeadHopperFix.Gameplay.LastChance.Monsters.NoiseAggro";
        private const float DefaultAggroRadius = 18f;
        private const float DefaultAggroCooldown = 0.75f;

        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Monsters.NoiseAggro");
        private static readonly Dictionary<int, float> s_lastAggroByPlayerViewId = new();
        private static Harmony? s_harmony;
        private static bool s_applied;

        private static readonly Type? s_chargeHandlerType = AccessTools.TypeByName("DeathHeadHopper.DeathHead.Handlers.ChargeHandler");
        private static readonly Type? s_deathHeadControllerType = AccessTools.TypeByName("DeathHeadHopper.DeathHead.DeathHeadController");
        private static readonly FieldInfo? s_chargeHandlerControllerField = s_chargeHandlerType == null ? null : AccessTools.Field(s_chargeHandlerType, "controller");
        private static readonly FieldInfo? s_deathHeadControllerDeathHeadField = s_deathHeadControllerType == null ? null : AccessTools.Field(s_deathHeadControllerType, "deathHead");
        private static readonly FieldInfo? s_enemyHasStateInvestigateField = AccessTools.Field(typeof(Enemy), "HasStateInvestigate");
        private static readonly FieldInfo? s_enemyStateInvestigateField = AccessTools.Field(typeof(Enemy), "StateInvestigate");
        private static readonly FieldInfo? s_enemyHasVisionField = AccessTools.Field(typeof(Enemy), "HasVision");
        private static readonly FieldInfo? s_enemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");

        internal static void ResetRuntimeState()
        {
            s_lastAggroByPlayerViewId.Clear();
        }

        internal static void Apply(Harmony harmony, Assembly asm)
        {
            if (s_applied || harmony == null || asm == null)
            {
                return;
            }

            var chargeHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.ChargeHandler", throwOnError: false);
            var windupMethod = chargeHandlerType == null
                ? null
                : AccessTools.Method(chargeHandlerType, "ChargeWindup", new[] { typeof(Vector3) });

            if (windupMethod == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.NoiseAggro.Missing", 120))
                {
                    Log.LogWarning("[LastChance] NoiseAggro skipped: ChargeHandler.ChargeWindup not found.");
                }
                return;
            }

            s_harmony = new Harmony(PatchId);
            var postfix = new HarmonyMethod(typeof(LastChanceMonstersNoiseAggroModule), nameof(ChargeWindupPostfix));
            s_harmony.Patch(windupMethod, postfix: postfix);
            s_applied = true;
        }

        private static void ChargeWindupPostfix(object __instance)
        {
            if (__instance == null ||
                !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled() ||
                !LastChanceMonstersTargetProxyHelper.IsMasterContext())
            {
                return;
            }

            var player = TryGetOwnerPlayer(__instance);
            if (player == null)
            {
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
            {
                return;
            }

            var viewId = player.photonView.ViewID;
            var now = Time.unscaledTime;
            if (s_lastAggroByPlayerViewId.TryGetValue(viewId, out var last) && now - last < DefaultAggroCooldown)
            {
                return;
            }

            s_lastAggroByPlayerViewId[viewId] = now;

            foreach (var enemy in LastChanceMonstersTargetProxyHelper.EnumerateEnemies())
            {
                if (enemy == null)
                {
                    continue;
                }

                var dist = Vector3.Distance(enemy.transform.position, headCenter);
                if (dist > DefaultAggroRadius)
                {
                    continue;
                }

                var hasInvestigate = s_enemyHasStateInvestigateField?.GetValue(enemy) as bool? ?? false;
                var investigate = s_enemyStateInvestigateField?.GetValue(enemy) as EnemyStateInvestigate;
                if (hasInvestigate && investigate != null)
                {
                    investigate.Set(headCenter, false);
                }

                var hasVision = s_enemyHasVisionField?.GetValue(enemy) as bool? ?? false;
                var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
                if (hasVision && vision != null)
                {
                    var near = dist <= vision.VisionDistanceClose;
                    LastChanceMonstersTargetProxyHelper.EnsureVisionTriggered(vision, player, near);
                }

                // SetChaseTarget has internal disabled checks remapped by MonstersSearch module during LastChance.
                enemy.SetChaseTarget(player);
            }

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("LastChance.NoiseAggro.Trigger", 30))
            {
                Log.LogInfo($"[LastChance] NoiseAggro triggered by charge windup. player={player.photonView.ViewID} pos={headCenter}");
            }
        }

        private static PlayerAvatar? TryGetOwnerPlayer(object chargeHandler)
        {
            var controller = s_chargeHandlerControllerField?.GetValue(chargeHandler);
            if (controller == null)
            {
                return null;
            }

            var deathHead = s_deathHeadControllerDeathHeadField?.GetValue(controller) as PlayerDeathHead;
            return deathHead?.playerAvatar;
        }
    }
}

