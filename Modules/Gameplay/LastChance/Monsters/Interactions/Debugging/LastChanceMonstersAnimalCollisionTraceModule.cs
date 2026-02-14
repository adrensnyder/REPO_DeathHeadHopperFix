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

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Interactions.Debugging
{
    [HarmonyPatch]
    internal static class LastChanceMonstersAnimalCollisionTraceModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.AnimalCollision");

        private static readonly FieldInfo? s_attackField = AccessTools.Field(typeof(EnemyTriggerAttack), "Attack");
        private static readonly FieldInfo? s_enemyStateLookUnderField = AccessTools.Field(typeof(Enemy), "StateLookUnder");
        private static readonly FieldInfo? s_enemyStateChaseField = AccessTools.Field(typeof(Enemy), "StateChase");
        private static readonly FieldInfo? s_enemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");
        private static readonly FieldInfo? s_enemyStateInvestigateField = AccessTools.Field(typeof(Enemy), "StateInvestigate");
        private static readonly FieldInfo? s_enemyStateInvestigateTriggeredPositionField = AccessTools.Field(typeof(EnemyStateInvestigate), "onInvestigateTriggeredPosition");
        private static readonly FieldInfo? s_enemyAnimalEnemyField = AccessTools.Field(typeof(EnemyAnimal), "enemy");
        private static readonly FieldInfo? s_enemyAnimalCurrentStateField = AccessTools.Field(typeof(EnemyAnimal), "currentState");
        private static readonly FieldInfo? s_enemyAnimalPlayerTargetField = AccessTools.Field(typeof(EnemyAnimal), "playerTarget");
        private static readonly FieldInfo? s_enemyAnimalStateTimerField = AccessTools.Field(typeof(EnemyAnimal), "stateTimer");
        private static readonly FieldInfo? s_enemyAnimalAgentDestinationField = AccessTools.Field(typeof(EnemyAnimal), "agentDestination");
        private static readonly FieldInfo? s_stateLookUnderWaitDoneField = AccessTools.Field(typeof(EnemyStateLookUnder), "WaitDone");
        private static readonly FieldInfo? s_playerNameField = AccessTools.Field(typeof(PlayerAvatar), "playerName");

        private static readonly Dictionary<int, float> s_logCooldownUntil = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> s_animalHeartbeatNextAt = new Dictionary<int, float>();
        private static bool s_runtimeLast;
        private static bool s_runtimeLastInitialized;

        [HarmonyPatch(typeof(GameDirector), "Update")]
        [HarmonyPostfix]
        private static void GameDirectorUpdatePostfix()
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow)
            {
                s_runtimeLastInitialized = false;
                return;
            }

            var runtimeEnabled = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();
            if (!s_runtimeLastInitialized || runtimeEnabled != s_runtimeLast)
            {
                s_runtimeLast = runtimeEnabled;
                s_runtimeLastInitialized = true;
                Log.LogInfo($"[Animal][Debug] runtimeEnabled changed -> {runtimeEnabled}");
            }
        }

        [HarmonyPatch(typeof(EnemyAnimal), "Update")]
        [HarmonyPostfix]
        private static void EnemyAnimalUpdatePostfix(EnemyAnimal __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow || __instance == null)
            {
                return;
            }

            var enemy = ResolveEnemy(__instance);
            if (enemy == null)
            {
                return;
            }

            var key = enemy.GetInstanceID();
            var now = Time.unscaledTime;
            if (s_animalHeartbeatNextAt.TryGetValue(key, out var nextAt) && now < nextAt)
            {
                return;
            }

            s_animalHeartbeatNextAt[key] = now + (InternalDebugFlags.DebugLastChanceAnimalCollisionVerbose ? 0.25f : 1f);
            var runtimeEnabled = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();
            var currentState = s_enemyAnimalCurrentStateField?.GetValue(__instance)?.ToString() ?? "n/a";
            var stateTimer = s_enemyAnimalStateTimerField?.GetValue(__instance) as float? ?? -1f;
            var destination = s_enemyAnimalAgentDestinationField?.GetValue(__instance) as Vector3? ?? Vector3.zero;
            var target = s_enemyAnimalPlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            var targetName = GetPlayerNameOrNone(target);
            var targetDisabled = target != null && LastChanceMonstersTargetProxyHelper.IsDisabled(target);
            var targetHeadProxy = target != null && LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(target);
            var bodyDist = target != null ? Vector3.Distance(enemy.transform.position, target.transform.position) : -1f;
            var headDist = target != null && LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(target, out var headCenter)
                ? Vector3.Distance(enemy.transform.position, headCenter)
                : -1f;

            TryLog(
                key + 100000,
                $"[Animal][Update] runtime={runtimeEnabled} state={currentState} timer={stateTimer:F2} target={targetName} targetDisabled={targetDisabled} " +
                $"targetHeadProxy={targetHeadProxy} bodyDist={(bodyDist >= 0f ? bodyDist.ToString("F2") : "n/a")} headDist={(headDist >= 0f ? headDist.ToString("F2") : "n/a")} " +
                $"enemyPos={enemy.transform.position} dest={destination}");
        }

        [HarmonyPatch(typeof(EnemyAnimal), "UpdateState")]
        [HarmonyPrefix]
        private static void EnemyAnimalUpdateStatePrefix(EnemyAnimal __instance, object _nextState)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow || __instance == null)
            {
                return;
            }

            var enemy = ResolveEnemy(__instance);
            if (enemy == null)
            {
                return;
            }

            var fromState = s_enemyAnimalCurrentStateField?.GetValue(__instance)?.ToString() ?? "n/a";
            var toState = _nextState?.ToString() ?? "n/a";
            var target = s_enemyAnimalPlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            TryLog(
                enemy.GetInstanceID() + 200000,
                $"[Animal][StateTransition] from={fromState} to={toState} target={GetPlayerNameOrNone(target)}");
        }

        [HarmonyPatch(typeof(EnemyAnimal), "OnVision")]
        [HarmonyPrefix]
        private static void EnemyAnimalOnVisionPrefix(EnemyAnimal __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow || __instance == null)
            {
                return;
            }

            var enemy = ResolveEnemy(__instance);
            if (enemy == null)
            {
                return;
            }

            var player = s_enemyAnimalPlayerTargetField?.GetValue(__instance) as PlayerAvatar;
            var playerName = GetPlayerNameOrNone(player);
            var disabled = player != null && LastChanceMonstersTargetProxyHelper.IsDisabled(player);
            var headProxy = player != null && LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player);
            TryLog(enemy.GetInstanceID() + 300000, $"[Animal][OnVision] player={playerName} disabled={disabled} headProxy={headProxy}");
        }

        [HarmonyPatch(typeof(EnemyAnimal), "OnInvestigate")]
        [HarmonyPrefix]
        private static void EnemyAnimalOnInvestigatePrefix(EnemyAnimal __instance)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow || __instance == null)
            {
                return;
            }

            var enemy = ResolveEnemy(__instance);
            if (enemy == null)
            {
                return;
            }

            var investigate = s_enemyStateInvestigateField?.GetValue(enemy) as EnemyStateInvestigate;
            var pos = s_enemyStateInvestigateTriggeredPositionField?.GetValue(investigate) as Vector3? ?? Vector3.zero;
            TryLog(enemy.GetInstanceID() + 400000, $"[Animal][OnInvestigate] pos={pos}");
        }

        [HarmonyPatch(typeof(Enemy), "SetChaseTarget")]
        [HarmonyPrefix]
        private static void EnemySetChaseTargetPrefix(Enemy __instance, PlayerAvatar playerAvatar)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow ||
                __instance == null ||
                __instance.GetComponent<EnemyAnimal>() == null)
            {
                return;
            }

            var playerName = GetPlayerNameOrNone(playerAvatar);
            var disabled = playerAvatar != null && LastChanceMonstersTargetProxyHelper.IsDisabled(playerAvatar);
            var headProxy = playerAvatar != null && LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(playerAvatar);
            TryLog(__instance.GetInstanceID() + 500000, $"[Animal][SetChaseTarget] player={playerName} disabled={disabled} headProxy={headProxy}");
        }

        [HarmonyPatch(typeof(EnemyTriggerAttack), "OnTriggerStay")]
        [HarmonyPostfix]
        private static void EnemyTriggerAttackOnTriggerStayPostfix(EnemyTriggerAttack __instance, Collider other)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow ||
                __instance == null ||
                other == null)
            {
                return;
            }

            var enemy = __instance.Enemy;
            if (enemy == null || enemy.GetComponent<EnemyAnimal>() == null)
            {
                return;
            }

            var playerTrigger = other.GetComponent<PlayerTrigger>();
            if (playerTrigger == null)
            {
                if (LastChanceMonstersTargetProxyHelper.TryGetPlayerFromDeathHeadCollider(other, out var headPlayer) && headPlayer != null)
                {
                    TryLog(enemy.GetInstanceID(), $"[Animal][Trigger] collider='{other.name}' mappedToHeadPlayer='{GetPlayerName(headPlayer)}' tag='{other.tag}' layer={LayerMask.LayerToName(other.gameObject.layer)}");
                }
                else if (InternalDebugFlags.DebugLastChanceAnimalCollisionVerbose)
                {
                    TryLog(enemy.GetInstanceID(), $"[Animal][Trigger] collider='{other.name}' without PlayerTrigger tag='{other.tag}' layer={LayerMask.LayerToName(other.gameObject.layer)}");
                }
                return;
            }

            var player = playerTrigger.PlayerAvatar;
            if (player == null)
            {
                TryLog(enemy.GetInstanceID(), $"[Animal][Trigger] playerTrigger without PlayerAvatar. collider='{other.name}'");
                return;
            }

            var runtimeEnabled = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();

            var attack = s_attackField?.GetValue(__instance) as bool? == true;
            if (attack)
            {
                TryLog(enemy.GetInstanceID(), $"[Animal][Trigger] Attack=TRUE runtime={runtimeEnabled} target='{GetPlayerName(player)}' collider='{other.name}'");
                return;
            }

            var state = enemy.CurrentState;
            var disabled = LastChanceMonstersTargetProxyHelper.IsDisabled(player);
            var lookUnder = s_enemyStateLookUnderField?.GetValue(enemy) as EnemyStateLookUnder;
            var lookUnderReady = state == EnemyState.LookUnder &&
                                 lookUnder != null &&
                                 s_stateLookUnderWaitDoneField?.GetValue(lookUnder) as bool? == true;
            var viewId = player.photonView != null ? player.photonView.ViewID : -1;
            var vision = s_enemyVisionField?.GetValue(enemy) as EnemyVision;
            var stateChase = s_enemyStateChaseField?.GetValue(enemy) as EnemyStateChase;
            var visionTriggered = vision != null &&
                                  viewId >= 0 &&
                                  vision.VisionTriggered.TryGetValue(viewId, out var trig) &&
                                  trig;
            var chaseCanReach = stateChase != null && stateChase.ChaseCanReach;

            TryLog(
                enemy.GetInstanceID(),
                $"[Animal][Trigger] Attack=FALSE runtime={runtimeEnabled} state={state} player='{GetPlayerName(player)}' disabled={disabled} " +
                $"visionTriggered={visionTriggered} lookUnderReady={lookUnderReady} chaseCanReach={chaseCanReach} collider='{other.name}'");
        }

        [HarmonyPatch(typeof(HurtCollider), "PlayerHurt")]
        [HarmonyPrefix]
        private static void HurtColliderPlayerHurtPrefix(HurtCollider __instance, PlayerAvatar _player)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow ||
                __instance == null ||
                _player == null)
            {
                return;
            }

            var host = __instance.enemyHost;
            if (host == null || host.GetComponent<EnemyAnimal>() == null)
            {
                return;
            }

            var disabled = LastChanceMonstersTargetProxyHelper.IsDisabled(_player);
            var headProxy = LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(_player);
            var runtimeEnabled = LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled();
            TryLog(
                host.GetInstanceID(),
                $"[Animal][HurtCollider] PlayerHurt invoked runtime={runtimeEnabled} player='{GetPlayerName(_player)}' disabled={disabled} headProxy={headProxy} " +
                $"damage={__instance.playerDamage} force={__instance.playerHitForce}");
        }

        private static void TryLog(int key, string message)
        {
            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceAnimalCollisionVerbose &&
                !LogLimiter.ShouldLog($"AnimalCollision.{key}", 10))
            {
                return;
            }

            var now = Time.unscaledTime;
            if (s_logCooldownUntil.TryGetValue(key, out var until) && now < until)
            {
                return;
            }

            s_logCooldownUntil[key] = now + 0.5f;
            Log.LogInfo(message);
        }

        private static string GetPlayerName(PlayerAvatar player)
        {
            var reflected = s_playerNameField?.GetValue(player) as string;
            if (!string.IsNullOrWhiteSpace(reflected))
            {
                return reflected!;
            }

            return player.photonView != null ? $"view:{player.photonView.ViewID}" : "view:-1";
        }

        private static string GetPlayerNameOrNone(PlayerAvatar? player)
        {
            return player == null ? "n/a" : GetPlayerName(player);
        }

        private static Enemy? ResolveEnemy(EnemyAnimal animal)
        {
            return s_enemyAnimalEnemyField?.GetValue(animal) as Enemy;
        }
    }
}
