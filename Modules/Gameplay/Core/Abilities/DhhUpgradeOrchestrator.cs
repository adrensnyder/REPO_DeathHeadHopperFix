#nullable enable

using DeathHeadHopper.Items;
using DeathHeadHopper.Managers;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class DhhUpgradeOrchestrator
    {
        private const float UpgradeDestroyDelaySeconds = 0.35f;
        private const float UpgradeReleaseDisableSeconds = 0.55f;

        internal static bool IsDhhUpgrade(GameObject prefab)
        {
            return prefab != null &&
                   (HasChargeUpgrade(prefab) || HasPowerUpgrade(prefab));
        }

        internal static bool HasChargeUpgrade(GameObject prefab)
        {
            return prefab != null &&
                   (prefab.GetComponent<DHHItemUpgradeCharge>() != null ||
                    prefab.GetComponentInChildren<DHHItemUpgradeCharge>(true) != null);
        }

        internal static bool HasPowerUpgrade(GameObject prefab)
        {
            return prefab != null &&
                   (prefab.GetComponent<DHHItemUpgradePower>() != null ||
                    prefab.GetComponentInChildren<DHHItemUpgradePower>(true) != null);
        }

        internal static void DisableLegacyToggleListeners(GameObject prefab)
        {
            if (!IsDhhUpgrade(prefab))
                return;

            var itemToggle = prefab.GetComponent<ItemToggle>() ?? prefab.GetComponentInChildren<ItemToggle>(true);
            if (itemToggle == null)
                return;

            itemToggle.onToggle = new UnityEvent();
        }

        internal static bool TryHandleToggle(ItemToggle toggle, int player)
        {
            if (toggle == null || !IsDhhUpgrade(toggle.gameObject))
                return false;

            if (GameManager.Multiplayer() && !PhotonNetwork.IsMasterClient)
                return true;

            var usedPower = TryRunPowerUpgrade(toggle, player);
            var usedCharge = TryRunChargeUpgrade(toggle, player);
            if (!usedPower && !usedCharge)
                return true;

            PlayUpgradeFx(toggle, player);
            RegisterConsumedUpgrade(toggle);
            ScheduleDestroyUpgradeItem(toggle);
            return true;
        }

        internal static void PlayAuthorizedLocalFeedback(string playerId, int newValue, int previousValue)
        {
            if (newValue <= previousValue || string.IsNullOrWhiteSpace(playerId))
                return;

            if (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient)
                return;

            var local = PlayerAvatar.instance;
            if (local == null || local.steamID != playerId)
                return;

            StatsUI.instance?.Fetch();
            StatsUI.instance?.ShowStats();
            CameraGlitch.Instance?.PlayUpgrade();
        }

        private static bool TryRunPowerUpgrade(ItemToggle toggle, int player)
        {
            if (toggle.GetComponent<DHHItemUpgradePower>() == null)
                return false;

            if (!TryGetToggleSteamId(toggle, player, out var playerId))
                return false;

            if (!EnsureLegacyDhhUpgradeKey(playerId, isChargeUpgrade: false))
                return false;

            toggle.GetComponent<DHHItemUpgradePower>()?.Upgrade();
            return true;
        }

        private static bool TryRunChargeUpgrade(ItemToggle toggle, int player)
        {
            if (toggle.GetComponent<DHHItemUpgradeCharge>() == null)
                return false;

            if (!TryGetToggleSteamId(toggle, player, out var playerId))
                return false;

            if (!EnsureLegacyDhhUpgradeKey(playerId, isChargeUpgrade: true))
                return false;

            toggle.GetComponent<DHHItemUpgradeCharge>()?.Upgrade();
            return true;
        }

        private static bool TryGetToggleSteamId(ItemToggle toggle, int player, out string playerId)
        {
            playerId = string.Empty;
            try
            {
                var playerAvatar = SemiFunc.PlayerAvatarGetFromPhotonID(player);
                if (playerAvatar == null && toggle != null)
                    playerAvatar = SemiFunc.PlayerAvatarGetFromPhotonID(toggle.playerTogglePhotonID);

                playerId = playerAvatar != null ? SemiFunc.PlayerGetSteamID(playerAvatar) : string.Empty;
                return !string.IsNullOrWhiteSpace(playerId);
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureLegacyDhhUpgradeKey(string playerId, bool isChargeUpgrade)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return false;

            var stats = DHHStatsManager.instance;
            if (stats == null)
                return false;

            var dictionary = isChargeUpgrade
                ? stats.playerUpgradeHeadCharge
                : stats.playerUpgradeHeadPower;

            if (!dictionary.ContainsKey(playerId))
                dictionary[playerId] = 0;

            return true;
        }

        private static void ScheduleDestroyUpgradeItem(ItemToggle toggle)
        {
            if (toggle == null)
                return;

            DisableConsumedToggle(toggle);
            ForceReleaseGrabbers(toggle);
            toggle.StartCoroutine(DestroyUpgradeItemAfterRelease(toggle));
        }

        private static IEnumerator DestroyUpgradeItemAfterRelease(ItemToggle toggle)
        {
            yield return new WaitForSeconds(UpgradeDestroyDelaySeconds);
            DestroyUpgradeItemNow(toggle);
        }

        private static void DisableConsumedToggle(ItemToggle toggle)
        {
            try
            {
                toggle.disabled = true;
                toggle.enabled = false;

                var physGrabObject = toggle.GetComponent<PhysGrabObject>()
                                     ?? toggle.GetComponentInChildren<PhysGrabObject>()
                                     ?? toggle.GetComponentInParent<PhysGrabObject>();

                if (physGrabObject != null)
                    physGrabObject.grabDisableTimer = Mathf.Max(physGrabObject.grabDisableTimer, UpgradeReleaseDisableSeconds);
            }
            catch
            {
                // Disabling interaction is best-effort; the authoritative stat update already happened.
            }
        }

        private static void DestroyUpgradeItemNow(ItemToggle toggle)
        {
            if (toggle == null)
                return;

            var impact = toggle.GetComponent<PhysGrabObjectImpactDetector>()
                         ?? toggle.GetComponentInChildren<PhysGrabObjectImpactDetector>();
            if (impact == null)
                impact = toggle.GetComponentInParent<PhysGrabObjectImpactDetector>();

            impact?.DestroyObject(false);
        }

        private static void ForceReleaseGrabbers(ItemToggle toggle)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer())
                return;

            var physGrabObject = toggle.GetComponent<PhysGrabObject>()
                                 ?? toggle.GetComponentInChildren<PhysGrabObject>()
                                 ?? toggle.GetComponentInParent<PhysGrabObject>();
            if (physGrabObject == null || physGrabObject.playerGrabbing == null || physGrabObject.playerGrabbing.Count == 0)
                return;

            var viewId = physGrabObject.photonView != null ? physGrabObject.photonView.ViewID : -1;
            foreach (var grabber in new List<PhysGrabber>(physGrabObject.playerGrabbing))
            {
                if (grabber == null)
                    continue;

                if (!SemiFunc.IsMultiplayer())
                {
                    grabber.ReleaseObject(viewId, UpgradeReleaseDisableSeconds);
                    continue;
                }

                grabber.photonView?.RPC("ReleaseObjectRPC", RpcTarget.All, false, UpgradeReleaseDisableSeconds, viewId);
            }
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
                    playerAvatar.playerHealth?.MaterialEffectOverride(PlayerHealth.Effect.Upgrade);
            }
            catch
            {
                // Upgrade VFX is best-effort and must not block the authoritative upgrade path.
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
            var attrs = toggle.GetComponent<ItemAttributes>()
                        ?? toggle.GetComponentInChildren<ItemAttributes>()
                        ?? toggle.GetComponentInParent<ItemAttributes>();

            if (attrs?.item != null && !string.IsNullOrWhiteSpace(attrs.item.name))
                return attrs.item.name;

            var fallback = toggle.name;
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }
    }
}
