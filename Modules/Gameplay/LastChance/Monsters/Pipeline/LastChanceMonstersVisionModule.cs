#nullable enable

using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch(typeof(EnemyVision), "Awake")]
    internal static class LastChanceMonstersVisionModule
    {
        [HarmonyPostfix]
        private static void AwakePostfix(EnemyVision __instance)
        {
            if (__instance == null)
            {
                return;
            }

            if (__instance.GetComponent<LastChanceEnemyVisionHeadProxyRuntime>() == null)
            {
                __instance.gameObject.AddComponent<LastChanceEnemyVisionHeadProxyRuntime>();
            }
        }
    }

    internal sealed class LastChanceEnemyVisionHeadProxyRuntime : MonoBehaviour
    {
        private static readonly FieldInfo? s_enemyVisionMaskField = AccessTools.Field(typeof(Enemy), "VisionMask");
        private EnemyVision? _vision;
        private Enemy? _enemy;
        private float _tick;

        private void Awake()
        {
            _vision = GetComponent<EnemyVision>();
            _enemy = GetComponent<Enemy>();
            _tick = Random.Range(0f, 0.25f);
        }

        private void Update()
        {
            if (_vision == null || _enemy == null)
            {
                return;
            }

            if (_enemy.GetComponent<EnemyAnimal>() != null)
            {
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsMasterContext() || !LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return;
            }

            _tick -= Time.deltaTime;
            if (_tick > 0f)
            {
                return;
            }

            _tick = 0.25f;

            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0)
            {
                return;
            }

            var origin = _vision.VisionTransform;
            if (origin == null)
            {
                return;
            }

            var visionMask = s_enemyVisionMaskField?.GetValue(_enemy) as LayerMask?;
            if (visionMask == null)
            {
                return;
            }

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!LastChanceMonstersTargetProxyHelper.TryGetHeadProxyTarget(player, out var headCenter))
                {
                    continue;
                }

                var dir = headCenter - origin.position;
                var dist = dir.magnitude;
                if (dist > _vision.VisionDistance)
                {
                    continue;
                }

                var dot = Vector3.Dot(origin.forward, dir.normalized);
                var near = dist <= _vision.VisionDistanceClose;
                var inCone = dot >= _vision.VisionDotStanding || near;
                if (!inCone)
                {
                    continue;
                }

                if (!LastChanceMonstersTargetProxyHelper.IsLineOfSightToHead(origin, headCenter, visionMask.Value, player!))
                {
                    continue;
                }

                LastChanceMonstersTargetProxyHelper.EnsureVisionTriggered(_vision, player!, near);
            }
        }
    }
}

