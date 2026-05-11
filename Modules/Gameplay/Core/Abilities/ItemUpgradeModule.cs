#nullable enable

using System;
using System.Reflection;
using DeathHeadHopper.Items;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class ItemUpgradeModule
    {
        internal static void Apply(Harmony harmony)
        {
            PatchItemToggleUpgradeHook(harmony);
        }

        private static void PatchItemToggleUpgradeHook(Harmony harmony)
        {
            if (harmony == null)
                return;

            var method = AccessTools.Method(typeof(ItemToggle), nameof(ItemToggle.ToggleItemLogic), new[] { typeof(bool), typeof(int) });
            if (method == null)
                return;

            var postfix = typeof(ItemUpgradeModule).GetMethod(nameof(ItemToggle_ToggleItemLogic_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
        }

        private static void ItemToggle_ToggleItemLogic_Postfix(ItemToggle __instance, bool toggle, int player)
        {
            if (!toggle || __instance == null)
                return;

            if (!SemiFunc.IsMasterClientOrSingleplayer())
                return;

            var usedPower = TryRunPowerUpgrade(__instance);
            var usedCharge = TryRunChargeUpgrade(__instance);
            if (usedPower || usedCharge)
            {
                PlayUpgradeFx(__instance, player);
                DestroyUpgradeItem(__instance);
                RegisterConsumedUpgrade(__instance);
            }
        }

        private static bool TryRunPowerUpgrade(ItemToggle toggle)
        {
            if (toggle.GetComponent<DHHItemUpgradePower>() == null)
                return false;

            if (!TryGetToggleSteamId(toggle, out var playerId))
                return false;

            return StatsModule.TryIncreaseDhhUpgrade(playerId, isChargeUpgrade: false, out _);
        }

        private static bool TryRunChargeUpgrade(ItemToggle toggle)
        {
            if (toggle.GetComponent<DHHItemUpgradeCharge>() == null)
                return false;

            if (!TryGetToggleSteamId(toggle, out var playerId))
                return false;

            return StatsModule.TryIncreaseDhhUpgrade(playerId, isChargeUpgrade: true, out _);
        }

        private static bool TryGetToggleSteamId(ItemToggle toggle, out string playerId)
        {
            playerId = string.Empty;
            try
            {
                var playerAvatar = SemiFunc.PlayerAvatarGetFromPhotonID(toggle.playerTogglePhotonID);
                playerId = playerAvatar != null ? SemiFunc.PlayerGetSteamID(playerAvatar) : string.Empty;
                return !string.IsNullOrWhiteSpace(playerId);
            }
            catch
            {
                return false;
            }
        }

        private static void DestroyUpgradeItem(ItemToggle toggle)
        {
            if (toggle == null)
                return;

            var impact = toggle.GetComponent<PhysGrabObjectImpactDetector>()
                         ?? toggle.GetComponentInChildren<PhysGrabObjectImpactDetector>();
            if (impact == null)
            {
                impact = toggle.GetComponentInParent<PhysGrabObjectImpactDetector>();
            }

            impact?.DestroyObject(false);
        }

        private static void PlayUpgradeFx(ItemToggle toggle, int player)
        {
            try
            {
                var playerAvatar = SemiFunc.PlayerAvatarGetFromPhotonID(player);
                if (playerAvatar == null)
                    return;

                var photonView = playerAvatar.photonView;
                var isLocal = !GameManager.Multiplayer() || (photonView != null && photonView.IsMine);

                if (isLocal)
                {
                    StatsUI.instance?.Fetch();
                    StatsUI.instance?.ShowStats();
                    CameraGlitch.Instance?.PlayUpgrade();
                }
                else
                {
                    GameDirector.instance?.CameraImpact?.ShakeDistance(5f, 1f, 6f, toggle.transform.position, 0.2f);
                }

                if (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient)
                {
                    playerAvatar.playerHealth?.MaterialEffectOverride(PlayerHealth.Effect.Upgrade);
                }
            }
            catch
            {
                // Upgrade VFX/stats refresh is non-critical; do not break item consumption flow.
            }
        }

        private static void RegisterConsumedUpgrade(ItemToggle toggle)
        {
            if (toggle == null)
                return;

            var itemName = TryGetStatsItemName(toggle);
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            StatsModule.EnsureStatsManagerKey(itemName!);

            var stats = StatsManager.instance;
            if (stats == null)
                return;

            stats.itemsPurchased[itemName] = Mathf.Max(stats.itemsPurchased[itemName] - 1, 0);
        }

        private static string? TryGetStatsItemName(ItemToggle toggle)
        {
            try
            {
                var attrs = toggle.GetComponent<ItemAttributes>()
                           ?? toggle.GetComponentInChildren<ItemAttributes>()
                           ?? toggle.GetComponentInParent<ItemAttributes>();

                if (attrs?.item != null && !string.IsNullOrWhiteSpace(attrs.item.name))
                    return attrs.item.name;
            }
            catch
            {
                // Reflection fallback: use helper-based asset name resolution.
            }

            var fallback = toggle.name;
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }
    }
}

