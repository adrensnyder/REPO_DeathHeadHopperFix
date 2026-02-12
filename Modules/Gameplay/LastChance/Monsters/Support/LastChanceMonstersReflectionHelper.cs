#nullable enable

using System;
using System.Reflection;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support
{
    internal static class LastChanceMonstersReflectionHelper
    {
        internal static FieldInfo? FindFieldInHierarchy(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, flags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        internal static MethodInfo? FindMethodInHierarchy(Type type, string name, Type[] parameterTypes)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var current = type; current != null; current = current.BaseType)
            {
                var method = current.GetMethod(name, flags, null, parameterTypes, null);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        internal static bool TryResolvePlayerFromCollider(Collider collider, out PlayerAvatar? player)
        {
            player = null;

            var controller = collider.GetComponentInParent<PlayerController>();
            if (controller != null && controller.playerAvatarScript != null)
            {
                player = controller.playerAvatarScript;
                return true;
            }

            var avatar = collider.GetComponentInParent<PlayerAvatar>();
            if (avatar != null)
            {
                player = avatar;
                return true;
            }

            var tumble = collider.GetComponentInParent<PlayerTumble>();
            if (tumble != null && tumble.playerAvatar != null)
            {
                player = tumble.playerAvatar;
                return true;
            }

            var deathHead = collider.GetComponentInParent<PlayerDeathHead>();
            if (deathHead != null && deathHead.playerAvatar != null)
            {
                player = deathHead.playerAvatar;
                return true;
            }

            return false;
        }
    }
}
