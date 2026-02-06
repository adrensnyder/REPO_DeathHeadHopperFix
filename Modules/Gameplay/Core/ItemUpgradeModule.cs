#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    internal static class ItemUpgradeModule
    {
        private const string DhhUpgradePowerTypeName = "DeathHeadHopper.Items.DHHItemUpgradePower";
        private const string DhhUpgradeChargeTypeName = "DeathHeadHopper.Items.DHHItemUpgradeCharge";

        private static Type? _dhhUpgradePowerType;
        private static Type? _dhhUpgradeChargeType;
        private static MethodInfo? _dhhUpgradePowerMethod;
        private static MethodInfo? _dhhUpgradeChargeMethod;

        internal static void Apply(Harmony harmony)
        {
            PatchItemToggleUpgradeHook(harmony);
        }

        private static void PatchItemToggleUpgradeHook(Harmony harmony)
        {
            if (harmony == null)
                return;

            var tItemToggle = AccessTools.TypeByName("ItemToggle");
            if (tItemToggle == null)
                return;

            var method = AccessTools.Method(tItemToggle, "ToggleItemLogic", new[] { typeof(bool), typeof(int) });
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

            var usedPower = TryInvokeUpgrade(__instance, ref _dhhUpgradePowerType, ref _dhhUpgradePowerMethod, DhhUpgradePowerTypeName);
            var usedCharge = TryInvokeUpgrade(__instance, ref _dhhUpgradeChargeType, ref _dhhUpgradeChargeMethod, DhhUpgradeChargeTypeName);
            if (usedPower || usedCharge)
            {
                PlayUpgradeFx(__instance, player);
                DestroyUpgradeItem(__instance);
                RegisterConsumedUpgrade(__instance);
            }
        }

        private static bool TryInvokeUpgrade(ItemToggle toggle, ref Type? upgradeType, ref MethodInfo? upgradeMethod, string typeName)
        {
            var type = upgradeType ??= AccessTools.TypeByName(typeName);
            if (type == null)
                return false;

            var component = toggle.GetComponent(type);
            if (component == null)
                return false;

            var method = upgradeMethod ??= AccessTools.Method(type, "Upgrade");
            if (method == null)
                return false;

            method.Invoke(component, Array.Empty<object?>());
            return true;
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
                // ignore
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
                // ignore
            }

            var fallback = ItemHelpers.GetItemAssetName(toggle) ?? toggle.name;
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }
    }
}
