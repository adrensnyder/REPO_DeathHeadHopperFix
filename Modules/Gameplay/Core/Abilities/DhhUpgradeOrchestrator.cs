#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DeathHeadHopper.Items;
using DeathHeadHopperFix.Modules.Gameplay.Core.Interop;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Events;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Abilities
{
    internal static class DhhUpgradeOrchestrator
    {
        private const float UpgradeDestroyDelaySeconds = 0.35f;
        private const float UpgradeReleaseDisableSeconds = 0.55f;
        private static readonly ConditionalWeakTable<ItemToggle, UpgradeUseState> UpgradeUseStates = new();

        private sealed class UpgradeUseState
        {
            public UpgradeUseState()
            {
            }

            internal bool InProgress;
            internal bool Consumed;
        }

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

            var correlation = toggle.GetInstanceID().ToString();
            var itemName = TryGetStatsItemName(toggle) ?? "<unknown>";
            var role = GetRuntimeRole();

            if (!TryResolvePhysicalUpgrade(toggle, out var upgradeId, out var upgradeKind))
            {
                DhhRepolibUpgradeBridge.LogError(
                    $"Physical DHH item correlation={correlation} item='{itemName}' could not be mapped to exactly one " +
                    "DHH upgrade component on the ItemToggle GameObject; the item was left unconsumed.");
                return true;
            }

            if (GameManager.Multiplayer() && !PhotonNetwork.IsMasterClient)
            {
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-toggle correlation={correlation} upgrade={upgradeId} kind={upgradeKind} item='{itemName}' " +
                    $"requestedPhotonId={player} role={role} guard=not-entered decision=skip reason=non-authority");
                return true;
            }

            if (!TryGetToggleSteamId(toggle, player, out var playerId))
            {
                DhhRepolibUpgradeBridge.LogError(
                    $"Physical DHH item correlation={correlation} upgrade={upgradeId} item='{itemName}' could not resolve " +
                    $"a Steam ID from requested Photon ID {player}; the item was left unconsumed.");
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-toggle correlation={correlation} upgrade={upgradeId} kind={upgradeKind} item='{itemName}' " +
                    $"requestedPhotonId={player} resolvedSteamId=<missing> role={role} guard=not-entered " +
                    "decision=reject reason=target-resolution-failed");
                return true;
            }

            if (!DhhRepolibUpgradeBridge.TryGetOwnedUpgrade(upgradeId, out var playerUpgrade, out var registrationFailure) ||
                playerUpgrade == null)
            {
                DhhRepolibUpgradeBridge.LogError(
                    $"Physical DHH item correlation={correlation} upgrade={upgradeId} target='{playerId}' cannot use the " +
                    $"DHHFix-owned REPOLib registration ({registrationFailure}); the item was left unconsumed and no legacy fallback was used.");
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-toggle correlation={correlation} upgrade={upgradeId} kind={upgradeKind} item='{itemName}' " +
                    $"requestedPhotonId={player} resolvedSteamId={playerId} role={role} guard=not-entered " +
                    $"decision=reject reason={registrationFailure}");
                return true;
            }

            int before;
            try
            {
                before = playerUpgrade.GetLevel(playerId);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogError(
                    $"Physical DHH item correlation={correlation} upgrade={upgradeId} target='{playerId}' could not read the " +
                    $"current REPOLib level before mutation: {ex.Message}. The item was left unconsumed.");
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-toggle correlation={correlation} upgrade={upgradeId} kind={upgradeKind} item='{itemName}' " +
                    $"requestedPhotonId={player} resolvedSteamId={playerId} role={role} guard=not-entered " +
                    "decision=reject reason=pre-mutation-level-read-failed");
                return true;
            }

            if (!TryEnterUseGuard(toggle, correlation, upgradeId, upgradeKind, itemName, player, playerId, role))
                return true;

            int? returnedLevel = null;
            Exception? mutationException = null;

            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-mutation correlation={correlation} upgrade={upgradeId} target={playerId} before={before} " +
                "requestedDelta=+1 action=AddLevel begin");

            try
            {
                returnedLevel = playerUpgrade.AddLevel(playerId, 1);
            }
            catch (Exception ex)
            {
                mutationException = ex;
            }

            int? observedAfter = null;
            Exception? postMutationReadException = null;
            try
            {
                observedAfter = playerUpgrade.GetLevel(playerId);
            }
            catch (Exception ex)
            {
                postMutationReadException = ex;
            }

            // A successfully returned AddLevel value is authoritative even if a later diagnostic read fails.
            // When AddLevel threw and no post-state can be observed, do not guess that the mutation succeeded.
            var after = observedAfter ?? returnedLevel;
            if (!after.HasValue)
            {
                ReleaseUseGuardAfterFailure(
                    toggle,
                    correlation,
                    upgradeId,
                    null,
                    before,
                    "post-mutation-level-unavailable");
                DhhRepolibUpgradeBridge.LogError(
                    $"REPOLib mutation outcome is unknown for physical {upgradeId} item correlation={correlation}, target='{playerId}': " +
                    $"AddLevel exception={(mutationException != null ? mutationException.GetType().Name : "none")}, " +
                    $"post-read exception={(postMutationReadException != null ? postMutationReadException.GetType().Name : "none")}. " +
                    "The item was left unconsumed and its interaction guard was released.");
                return true;
            }

            if (postMutationReadException != null)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation}, target='{playerId}' could not read the REPOLib level " +
                    $"after AddLevel ({postMutationReadException.GetType().Name}: {postMutationReadException.Message}); " +
                    $"the returned level {after.Value} was used to verify the mutation.");
            }

            var mutationSucceeded = after.Value > before;
            var expectedSingleStep = after.Value == before + 1;

            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-mutation correlation={correlation} upgrade={upgradeId} target={playerId} before={before} " +
                $"requestedDelta=+1 returned={(returnedLevel.HasValue ? returnedLevel.Value.ToString() : "<exception>")} after={after.Value} " +
                $"mutationSucceeded={mutationSucceeded} expectedSingleStep={expectedSingleStep} " +
                $"mutationException={(mutationException != null ? mutationException.GetType().Name : "none")} " +
                $"postReadException={(postMutationReadException != null ? postMutationReadException.GetType().Name : "none")}");

            if (!mutationSucceeded)
            {
                ReleaseUseGuardAfterFailure(toggle, correlation, upgradeId, after.Value, before, "level-not-increased");
                var exceptionSuffix = mutationException != null
                    ? $" AddLevel threw {mutationException.GetType().Name}: {mutationException.Message}"
                    : string.Empty;
                DhhRepolibUpgradeBridge.LogError(
                    $"REPOLib mutation failed for physical {upgradeId} item correlation={correlation}, target='{playerId}', " +
                    $"before={before}, after={after.Value}.{exceptionSuffix} The item was left unconsumed.");
                return true;
            }

            if (mutationException != null)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"REPOLib AddLevel for physical {upgradeId} item correlation={correlation}, target='{playerId}' threw " +
                    $"{mutationException.GetType().Name} after the authoritative level had already advanced {before}->{after.Value}. " +
                    "The item will still be consumed to prevent a second upgrade from the same physical item.");
            }

            if (!expectedSingleStep)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation}, target='{playerId}' produced an unexpected REPOLib " +
                    $"level change {before}->{after.Value}; expected exactly +1. The changed state is authoritative, so the item will be consumed once.");
            }

            CommitUseGuard(toggle, correlation, upgradeId, playerId);

            PlayUpgradeFx(toggle, player, correlation, upgradeId, playerId);
            RegisterConsumedUpgrade(toggle, itemName, correlation, upgradeId);
            ScheduleDestroyUpgradeItem(toggle, correlation, upgradeId);
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

            var statsUiAvailable = StatsUI.instance != null;
            var cameraGlitchAvailable = CameraGlitch.Instance != null;

            StatsUI.instance?.Fetch();
            StatsUI.instance?.ShowStats();
            CameraGlitch.Instance?.PlayUpgrade();
            DhhRepolibUpgradeBridge.DebugLog(
                $"target-feedback target={playerId} old={previousValue} new={newValue} role=client decision=applied " +
                $"statsUiAvailable={statsUiAvailable} cameraGlitchAvailable={cameraGlitchAvailable}");
        }

        private static bool TryResolvePhysicalUpgrade(ItemToggle toggle, out string upgradeId, out string upgradeKind)
        {
            var hasCharge = toggle.GetComponent<DHHItemUpgradeCharge>() != null;
            var hasPower = toggle.GetComponent<DHHItemUpgradePower>() != null;

            if (hasCharge == hasPower)
            {
                upgradeId = string.Empty;
                upgradeKind = hasCharge ? "ambiguous" : "missing-direct-component";
                return false;
            }

            if (hasCharge)
            {
                upgradeId = DhhRepolibUpgradeBridge.HeadChargeUpgradeId;
                upgradeKind = "Charge";
                return true;
            }

            upgradeId = DhhRepolibUpgradeBridge.HeadPowerUpgradeId;
            upgradeKind = "Power";
            return true;
        }

        private static bool TryEnterUseGuard(
            ItemToggle toggle,
            string correlation,
            string upgradeId,
            string upgradeKind,
            string itemName,
            int requestedPhotonId,
            string playerId,
            string role)
        {
            var state = UpgradeUseStates.GetOrCreateValue(toggle);
            if (state.InProgress || state.Consumed || toggle.disabled)
            {
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-toggle correlation={correlation} upgrade={upgradeId} kind={upgradeKind} item='{itemName}' " +
                    $"requestedPhotonId={requestedPhotonId} resolvedSteamId={playerId} role={role} " +
                    $"guard=inProgress:{state.InProgress},consumed:{state.Consumed},toggleDisabled:{toggle.disabled} " +
                    "decision=reject reason=already-disabled-in-progress-or-consumed");
                return false;
            }

            state.InProgress = true;
            toggle.disabled = true;
            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-toggle correlation={correlation} upgrade={upgradeId} kind={upgradeKind} item='{itemName}' " +
                $"requestedPhotonId={requestedPhotonId} resolvedSteamId={playerId} role={role} " +
                "guard=inProgress:True,consumed:False,toggleDisabled:True decision=accept");
            return true;
        }

        private static void CommitUseGuard(ItemToggle toggle, string correlation, string upgradeId, string playerId)
        {
            var state = UpgradeUseStates.GetOrCreateValue(toggle);
            state.InProgress = false;
            state.Consumed = true;
            toggle.disabled = true;

            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-guard correlation={correlation} upgrade={upgradeId} target={playerId} " +
                "guard=inProgress:False,consumed:True,toggleDisabled:True decision=commit " +
                "reason=authoritative-level-increased");
        }

        private static void ReleaseUseGuardAfterFailure(
            ItemToggle toggle,
            string correlation,
            string upgradeId,
            int? after,
            int before,
            string reason)
        {
            var state = UpgradeUseStates.GetOrCreateValue(toggle);
            state.InProgress = false;
            toggle.disabled = false;

            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-guard correlation={correlation} upgrade={upgradeId} " +
                "guard=inProgress:False,consumed:False,toggleDisabled:False state=released " +
                $"decision=retry-allowed reason={reason} before={before} " +
                $"after={(after.HasValue ? after.Value.ToString() : "<unavailable>")}");
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

        private static void ScheduleDestroyUpgradeItem(ItemToggle toggle, string correlation, string upgradeId)
        {
            if (toggle == null)
                return;

            DisableConsumedToggle(toggle, correlation, upgradeId);
            try
            {
                ForceReleaseGrabbers(toggle, correlation, upgradeId);
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation} could not fully release its grabbers after " +
                    $"the authoritative upgrade: {ex.Message}");
            }

            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-destroy correlation={correlation} upgrade={upgradeId} action=scheduled delaySeconds={UpgradeDestroyDelaySeconds:0.###}");
            try
            {
                toggle.StartCoroutine(DestroyUpgradeItemAfterRelease(toggle, correlation, upgradeId));
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation} could not schedule delayed destruction: {ex.Message}. " +
                    "Immediate destruction will be attempted.");
                DestroyUpgradeItemNow(toggle, correlation, upgradeId);
            }
        }

        private static IEnumerator DestroyUpgradeItemAfterRelease(ItemToggle toggle, string correlation, string upgradeId)
        {
            yield return new WaitForSeconds(UpgradeDestroyDelaySeconds);
            DestroyUpgradeItemNow(toggle, correlation, upgradeId);
        }

        private static void DisableConsumedToggle(ItemToggle toggle, string correlation, string upgradeId)
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

                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-consume correlation={correlation} upgrade={upgradeId} action=disable-toggle " +
                    $"grabDisableSeconds={UpgradeReleaseDisableSeconds:0.###}");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation} could not fully disable interaction after its " +
                    $"authoritative upgrade: {ex.Message}");
            }
        }

        private static void DestroyUpgradeItemNow(ItemToggle toggle, string correlation, string upgradeId)
        {
            if (toggle == null)
                return;

            try
            {
                var impact = toggle.GetComponent<PhysGrabObjectImpactDetector>()
                             ?? toggle.GetComponentInChildren<PhysGrabObjectImpactDetector>();
                if (impact == null)
                    impact = toggle.GetComponentInParent<PhysGrabObjectImpactDetector>();

                if (impact == null)
                {
                    DhhRepolibUpgradeBridge.LogWarning(
                        $"Physical {upgradeId} item correlation={correlation} was upgraded and disabled, but no " +
                        "PhysGrabObjectImpactDetector was available to request destruction.");
                    return;
                }

                impact.DestroyObject(false);
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-destroy correlation={correlation} upgrade={upgradeId} action=destroy-requested");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation} could not request destruction after " +
                    $"the authoritative upgrade: {ex.Message}");
            }
        }

        private static void ForceReleaseGrabbers(ItemToggle toggle, string correlation, string upgradeId)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer())
                return;

            var physGrabObject = toggle.GetComponent<PhysGrabObject>()
                                 ?? toggle.GetComponentInChildren<PhysGrabObject>()
                                 ?? toggle.GetComponentInParent<PhysGrabObject>();
            if (physGrabObject == null || physGrabObject.playerGrabbing == null || physGrabObject.playerGrabbing.Count == 0)
            {
                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-release correlation={correlation} upgrade={upgradeId} action=skip reason=no-active-grabbers");
                return;
            }

            var viewId = physGrabObject.photonView != null ? physGrabObject.photonView.ViewID : -1;
            var released = 0;
            foreach (var grabber in new List<PhysGrabber>(physGrabObject.playerGrabbing))
            {
                if (grabber == null)
                    continue;

                if (!SemiFunc.IsMultiplayer())
                {
                    grabber.ReleaseObject(viewId, UpgradeReleaseDisableSeconds);
                    released++;
                    continue;
                }

                var grabberView = grabber.photonView;
                if (grabberView == null)
                    continue;

                grabberView.RPC("ReleaseObjectRPC", RpcTarget.All, false, UpgradeReleaseDisableSeconds, viewId);
                released++;
            }

            DhhRepolibUpgradeBridge.DebugLog(
                $"physical-release correlation={correlation} upgrade={upgradeId} action=requested grabbers={released} viewId={viewId}");
        }

        private static void PlayUpgradeFx(ItemToggle toggle, int player, string correlation, string upgradeId, string playerId)
        {
            try
            {
                var playerAvatar = SemiFunc.PlayerAvatarGetFromPhotonID(player);
                if (playerAvatar == null)
                {
                    DhhRepolibUpgradeBridge.DebugLog(
                        $"physical-feedback correlation={correlation} upgrade={upgradeId} target={playerId} " +
                        "decision=skip reason=avatar-unavailable");
                    return;
                }

                var photonView = playerAvatar.photonView;
                var isLocal = !GameManager.Multiplayer() || (photonView != null && photonView.IsMine);

                var statsUiAvailable = StatsUI.instance != null;
                var cameraGlitchAvailable = CameraGlitch.Instance != null;
                var cameraImpactAvailable = GameDirector.instance?.CameraImpact != null;
                var materialEffectAvailable = playerAvatar.playerHealth != null;

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

                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-feedback correlation={correlation} upgrade={upgradeId} target={playerId} " +
                    $"localTarget={isLocal} role={GetRuntimeRole()} decision=applied " +
                    $"statsUiAvailable={statsUiAvailable} cameraGlitchAvailable={cameraGlitchAvailable} " +
                    $"cameraImpactAvailable={cameraImpactAvailable} materialEffectAvailable={materialEffectAvailable}");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation} upgraded successfully, but authority-side feedback failed: {ex.Message}");
            }
        }

        private static void RegisterConsumedUpgrade(ItemToggle toggle, string itemName, string correlation, string upgradeId)
        {
            if (toggle == null)
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(itemName) || string.Equals(itemName, "<unknown>", StringComparison.Ordinal))
                {
                    DhhRepolibUpgradeBridge.LogWarning(
                        $"Physical {upgradeId} item correlation={correlation} upgraded successfully, but its stats item name could not be resolved; " +
                        "itemsPurchased could not be decremented.");
                    return;
                }

                var stats = StatsManager.instance;
                if (stats == null)
                {
                    DhhRepolibUpgradeBridge.LogWarning(
                        $"Physical {upgradeId} item correlation={correlation} upgraded successfully, but StatsManager is unavailable; " +
                        "itemsPurchased could not be decremented.");
                    return;
                }

                StatsModule.EnsureStatsManagerKey(itemName);
                var before = stats.itemsPurchased.TryGetValue(itemName, out var current) ? current : 0;
                var after = Mathf.Max(before - 1, 0);
                stats.itemsPurchased[itemName] = after;

                DhhRepolibUpgradeBridge.DebugLog(
                    $"physical-consume correlation={correlation} upgrade={upgradeId} item='{itemName}' " +
                    $"itemsPurchasedBefore={before} itemsPurchasedAfter={after} action=decrement");
            }
            catch (Exception ex)
            {
                DhhRepolibUpgradeBridge.LogWarning(
                    $"Physical {upgradeId} item correlation={correlation} upgraded successfully, but itemsPurchased bookkeeping failed: {ex.Message}");
            }
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

        private static string GetRuntimeRole()
        {
            if (!GameManager.Multiplayer())
                return "singleplayer";

            return PhotonNetwork.IsMasterClient ? "master" : "client";
        }
    }
}
