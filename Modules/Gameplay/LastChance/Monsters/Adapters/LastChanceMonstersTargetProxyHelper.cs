#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters
{
    internal static class LastChanceMonstersTargetProxyHelper
    {
        private static readonly FieldInfo? s_playerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo? s_deathHeadTriggeredField = AccessTools.Field(typeof(PlayerDeathHead), "triggered");
        private static readonly FieldInfo? s_deathHeadPhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
        private static readonly FieldInfo? s_physGrabObjectCenterPointField = AccessTools.Field(typeof(PhysGrabObject), "centerPoint");
        private static readonly FieldInfo? s_deathHeadPlayerEyesField = AccessTools.Field(typeof(PlayerDeathHead), "playerEyes");

        internal static bool IsRuntimeEnabled()
        {
            return FeatureFlags.LastChanceMonstersSearchEnabled &&
                   FeatureFlags.LastChangeMode &&
                   LastChanceTimerController.IsActive;
        }

        internal static bool IsMasterContext()
        {
            return SemiFunc.IsMasterClientOrSingleplayer();
        }

        internal static bool IsDisabled(PlayerAvatar? player)
        {
            if (player == null || s_playerIsDisabledField == null)
            {
                return false;
            }

            return s_playerIsDisabledField.GetValue(player) is bool disabled && disabled;
        }

        internal static bool IsHeadProxyActive(PlayerAvatar? player)
        {
            if (player == null)
            {
                return false;
            }

            var head = player.playerDeathHead;
            if (head == null)
            {
                return false;
            }

            if (s_deathHeadTriggeredField != null && s_deathHeadTriggeredField.GetValue(head) is bool triggered)
            {
                return triggered;
            }

            // Fallback: if reflection fails, accept either disabled state or a valid head phys object.
            // This keeps compatibility with both vanilla-disabled flow and LastChance "alive + active head" flow.
            return IsDisabled(player) || s_deathHeadPhysGrabObjectField?.GetValue(head) is PhysGrabObject;
        }

        internal static bool TryGetHeadCenter(PlayerAvatar? player, out Vector3 center)
        {
            center = default;
            if (player == null)
            {
                return false;
            }

            var head = player.playerDeathHead;
            if (head == null)
            {
                return false;
            }

            var phys = s_deathHeadPhysGrabObjectField?.GetValue(head) as PhysGrabObject;
            if (phys != null && s_physGrabObjectCenterPointField != null)
            {
                var value = s_physGrabObjectCenterPointField.GetValue(phys);
                if (value is Vector3 p)
                {
                    center = p;
                    return true;
                }
            }

            center = head.transform.position;
            return true;
        }

        internal static bool TryGetHeadProxyTarget(PlayerAvatar? player, out Vector3 center)
        {
            center = default;
            return IsRuntimeEnabled() && IsHeadProxyActive(player) && TryGetHeadCenter(player, out center);
        }

        internal static bool TryGetHeadProxyVisionTarget(PlayerAvatar? player, out Vector3 point)
        {
            point = default;
            if (!IsRuntimeEnabled() || !IsHeadProxyActive(player) || player?.playerDeathHead == null)
            {
                return false;
            }

            var eyes = s_deathHeadPlayerEyesField?.GetValue(player.playerDeathHead);
            if (eyes is Component eyesComponent)
            {
                point = eyesComponent.transform.position;
                return true;
            }

            return TryGetHeadCenter(player, out point);
        }

        internal static bool TryGetPlayerFromDeathHeadCollider(Collider? other, out PlayerAvatar? player)
        {
            player = null;
            if (other == null)
            {
                return false;
            }

            var deathHead = other.GetComponentInParent<PlayerDeathHead>();
            if (deathHead == null)
            {
                return false;
            }

            player = deathHead.playerAvatar;
            return player != null;
        }

        internal static bool IsLineOfSightToHead(Transform origin, Vector3 headCenter, LayerMask visionMask, PlayerAvatar player)
        {
            var dir = headCenter - origin.position;
            var dist = dir.magnitude;
            if (dist <= 0.001f)
            {
                return true;
            }

            var hits = Physics.RaycastAll(origin.position, dir.normalized, dist, visionMask, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var t = hits[i].transform;
                if (t == null)
                {
                    continue;
                }

                if (t.CompareTag("Enemy"))
                {
                    continue;
                }

                // Consider the target DeathHead colliders transparent for LOS checks.
                var hitHead = t.GetComponentInParent<PlayerDeathHead>();
                if (hitHead != null && hitHead == player.playerDeathHead)
                {
                    continue;
                }

                if (t.GetComponentInParent<PlayerTumble>() != null)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        internal static void EnsureVisionTriggered(EnemyVision vision, PlayerAvatar player, bool near)
        {
            if (vision == null || player == null || player.photonView == null)
            {
                return;
            }

            var viewId = player.photonView.ViewID;
            if (!vision.VisionTriggered.ContainsKey(viewId))
            {
                vision.VisionTriggered[viewId] = false;
            }

            if (!vision.VisionsTriggered.ContainsKey(viewId))
            {
                vision.VisionsTriggered[viewId] = 0;
            }

            vision.VisionTrigger(viewId, player, culled: false, playerNear: near);
        }

        internal static IEnumerable<Enemy> EnumerateEnemies()
        {
            var all = UnityEngine.Object.FindObjectsOfType<Enemy>();
            for (var i = 0; i < all.Length; i++)
            {
                var enemy = all[i];
                if (enemy != null)
                {
                    yield return enemy;
                }
            }
        }
    }
}

