#nullable enable

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch(typeof(HurtCollider), "Update")]
    internal static class LastChanceMonstersHurtColliderHeadProxyModule
    {
        private static readonly FieldInfo? s_boxColliderField = AccessTools.Field(typeof(HurtCollider), "BoxCollider");
        private static readonly FieldInfo? s_sphereColliderField = AccessTools.Field(typeof(HurtCollider), "SphereCollider");
        private static readonly FieldInfo? s_colliderIsBoxField = AccessTools.Field(typeof(HurtCollider), "ColliderIsBox");
        private static readonly FieldInfo? s_layerMaskField = AccessTools.Field(typeof(HurtCollider), "LayerMask");
        private static readonly MethodInfo? s_playerHurtMethod = AccessTools.Method(typeof(HurtCollider), "PlayerHurt", new[] { typeof(PlayerAvatar) });

        [HarmonyPostfix]
        private static void UpdatePostfix(HurtCollider __instance)
        {
            if (__instance == null || s_playerHurtMethod == null)
            {
                return;
            }

            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                return;
            }

            if (!__instance.isActiveAndEnabled || !__instance.playerLogic || __instance.playerDamageCooldown <= 0f)
            {
                return;
            }

            var layerMask = s_layerMaskField?.GetValue(__instance) as LayerMask? ?? default;
            var overlaps = CollectOverlaps(__instance, layerMask);
            if (overlaps == null || overlaps.Length == 0)
            {
                return;
            }

            var processedPlayers = new HashSet<int>();
            for (var i = 0; i < overlaps.Length; i++)
            {
                var collider = overlaps[i];
                if (collider == null || !collider.gameObject.CompareTag("Player"))
                {
                    continue;
                }

                var player = ResolvePlayer(collider);
                if (player == null)
                {
                    continue;
                }

                if (!LastChanceMonstersTargetProxyHelper.IsDisabled(player) ||
                    !LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player))
                {
                    continue;
                }

                var key = player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
                if (!processedPlayers.Add(key))
                {
                    continue;
                }

                s_playerHurtMethod.Invoke(__instance, new object[] { player });
            }
        }

        private static Collider[]? CollectOverlaps(HurtCollider instance, LayerMask mask)
        {
            var colliderIsBox = s_colliderIsBoxField?.GetValue(instance) as bool? ?? true;
            if (colliderIsBox)
            {
                var box = s_boxColliderField?.GetValue(instance) as BoxCollider;
                if (box == null)
                {
                    return null;
                }

                var center = instance.transform.TransformPoint(box.center);
                var scaledSize = new Vector3(
                    instance.transform.lossyScale.x * box.size.x,
                    instance.transform.lossyScale.y * box.size.y,
                    instance.transform.lossyScale.z * box.size.z);
                return Physics.OverlapBox(center, scaledSize * 0.5f, instance.transform.rotation, mask, QueryTriggerInteraction.Collide);
            }

            var sphere = s_sphereColliderField?.GetValue(instance) as SphereCollider;
            if (sphere == null)
            {
                return null;
            }

            var centerSphere = sphere.bounds.center;
            var radius = instance.transform.lossyScale.x * sphere.radius;
            return Physics.OverlapSphere(centerSphere, radius, mask, QueryTriggerInteraction.Collide);
        }

        private static PlayerAvatar? ResolvePlayer(Collider collider)
        {
            var avatar = collider.GetComponentInParent<PlayerAvatar>();
            if (avatar != null)
            {
                return avatar;
            }

            var controller = collider.GetComponentInParent<PlayerController>();
            if (controller != null && controller.playerAvatarScript != null)
            {
                return controller.playerAvatarScript;
            }

            var trigger = collider.GetComponent<PlayerTrigger>() ?? collider.GetComponentInParent<PlayerTrigger>();
            return trigger?.PlayerAvatar;
        }
    }
}
