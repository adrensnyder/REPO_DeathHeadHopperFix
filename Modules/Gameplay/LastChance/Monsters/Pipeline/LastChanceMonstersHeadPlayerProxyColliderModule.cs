#nullable enable

using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch(typeof(GameDirector), "Update")]
    internal static class LastChanceMonstersHeadPlayerProxyColliderModule
    {
        [HarmonyPostfix]
        private static void UpdatePostfix(GameDirector __instance)
        {
            if (__instance == null)
            {
                return;
            }

            if (__instance.GetComponent<LastChanceHeadPlayerProxyColliderRuntime>() == null)
            {
                __instance.gameObject.AddComponent<LastChanceHeadPlayerProxyColliderRuntime>();
            }
        }
    }

    internal sealed class LastChanceHeadPlayerProxyColliderRuntime : MonoBehaviour
    {
        private readonly Dictionary<int, ProxyEntry> _entries = new Dictionary<int, ProxyEntry>();
        private const float ProxyRadius = 0.35f;

        private sealed class ProxyEntry
        {
            internal PlayerAvatar? Player;
            internal GameObject? ProxyObject;
            internal SphereCollider? Collider;
            internal PlayerTrigger? Trigger;
        }

        private void LateUpdate()
        {
            if (!LastChanceMonstersTargetProxyHelper.IsRuntimeEnabled())
            {
                DisableAll();
                return;
            }

            var players = GameDirector.instance?.PlayerList;
            if (players == null || players.Count == 0)
            {
                DisableAll();
                return;
            }

            var seen = new HashSet<int>();
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null)
                {
                    continue;
                }

                var id = player.GetInstanceID();
                seen.Add(id);

                var hasHeadCenter = LastChanceMonstersTargetProxyHelper.TryGetHeadCenter(player, out var headCenter);
                var shouldEnable = LastChanceMonstersTargetProxyHelper.IsDisabled(player) &&
                                   LastChanceMonstersTargetProxyHelper.IsHeadProxyActive(player) &&
                                   hasHeadCenter;

                var entry = GetOrCreateEntry(id, player);
                if (!shouldEnable)
                {
                    SetActive(entry, false);
                    continue;
                }

                if (entry.ProxyObject == null)
                {
                    continue;
                }

                entry.ProxyObject.transform.position = headCenter;
                entry.ProxyObject.transform.rotation = Quaternion.identity;
                entry.ProxyObject.transform.localScale = Vector3.one;
                SetActive(entry, true);
            }

            DisableMissing(seen);
        }

        private ProxyEntry GetOrCreateEntry(int id, PlayerAvatar player)
        {
            if (_entries.TryGetValue(id, out var existing))
            {
                existing.Player = player;
                return existing;
            }

            var entry = new ProxyEntry
            {
                Player = player
            };

            var proxy = new GameObject("LastChance_HeadPlayerProxyCollider");
            proxy.transform.SetParent(transform, worldPositionStays: true);
            proxy.tag = "Player";
            var playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0)
            {
                proxy.layer = playerLayer;
            }

            var sphere = proxy.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = ProxyRadius;

            var trigger = proxy.AddComponent<PlayerTrigger>();
            trigger.PlayerAvatar = player;

            proxy.SetActive(false);

            entry.ProxyObject = proxy;
            entry.Collider = sphere;
            entry.Trigger = trigger;

            _entries[id] = entry;
            return entry;
        }

        private void DisableAll()
        {
            foreach (var entry in _entries.Values)
            {
                SetActive(entry, false);
            }
        }

        private void DisableMissing(HashSet<int> seen)
        {
            foreach (var kvp in _entries)
            {
                if (!seen.Contains(kvp.Key))
                {
                    SetActive(kvp.Value, false);
                }
            }
        }

        private static void SetActive(ProxyEntry entry, bool enabled)
        {
            if (entry.ProxyObject == null)
            {
                return;
            }

            if (entry.ProxyObject.activeSelf != enabled)
            {
                entry.ProxyObject.SetActive(enabled);
            }
        }

        private void OnDestroy()
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.ProxyObject != null)
                {
                    Destroy(entry.ProxyObject);
                }
            }

            _entries.Clear();
        }
    }
}
