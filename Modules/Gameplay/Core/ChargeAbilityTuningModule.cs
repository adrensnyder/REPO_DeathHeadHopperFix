#nullable enable

using System;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.Core
{
    internal static class ChargeAbilityTuningModule
    {
        private static readonly FieldInfo? s_playerAvatarDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static readonly FieldInfo? s_playerAvatarIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static FieldInfo? s_abilityEnergyHandlerControllerField;
        private static FieldInfo? s_deathHeadControllerDeathHeadField;
        private static FieldInfo? s_playerDeathHeadAvatarField;

        internal static void Apply(Harmony harmony, Assembly asm)
        {
            PatchAbilityEnergyHandlerRechargeSoundIfPossible(harmony, asm);
            PatchChargeAbilityGettersIfPossible(harmony, asm);
        }

        private static void PatchAbilityEnergyHandlerRechargeSoundIfPossible(Harmony harmony, Assembly asm)
        {
            var abilityEnergyHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.AbilityEnergyHandler", throwOnError: false);
            if (abilityEnergyHandlerType == null)
                return;
            s_abilityEnergyHandlerControllerField ??= abilityEnergyHandlerType.GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            var deathHeadControllerType = asm.GetType("DeathHeadHopper.DeathHead.DeathHeadController", throwOnError: false);
            if (deathHeadControllerType != null)
            {
                s_deathHeadControllerDeathHeadField ??= deathHeadControllerType.GetField("deathHead", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }

            var playerDeathHeadType = asm.GetType("PlayerDeathHead", throwOnError: false);
            if (playerDeathHeadType != null)
            {
                s_playerDeathHeadAvatarField ??= playerDeathHeadType.GetField("playerAvatar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }

            var playRechargeSoundMethod = AccessTools.Method(abilityEnergyHandlerType, "PlayRechargeSound", Type.EmptyTypes);
            if (playRechargeSoundMethod == null)
                return;

            var prefix = typeof(ChargeAbilityTuningModule).GetMethod(nameof(AbilityEnergyHandler_PlayRechargeSound_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(playRechargeSoundMethod, prefix: new HarmonyMethod(prefix));
        }

        private static bool AbilityEnergyHandler_PlayRechargeSound_Prefix(object __instance)
        {
            if (SpectateContextHelper.IsSpectatingLocalDeathHead() || IsLocalPlayerDead())
                return false;

            var avatar = GetAbilityEnergyHandlerPlayerAvatar(__instance);
            if (IsAbilityPlayerDisabled(avatar))
                return false;

            return true;
        }

        private static PlayerAvatar? GetAbilityEnergyHandlerPlayerAvatar(object? handler)
        {
            if (handler == null || s_abilityEnergyHandlerControllerField == null)
                return null;

            var controller = s_abilityEnergyHandlerControllerField.GetValue(handler);
            if (controller == null || s_deathHeadControllerDeathHeadField == null)
                return null;

            var deathHead = s_deathHeadControllerDeathHeadField.GetValue(controller);
            if (deathHead == null || s_playerDeathHeadAvatarField == null)
                return null;

            return s_playerDeathHeadAvatarField.GetValue(deathHead) as PlayerAvatar;
        }

        private static bool IsAbilityPlayerDisabled(PlayerAvatar? avatar)
        {
            if (avatar == null || s_playerAvatarIsDisabledField == null)
                return false;

            return s_playerAvatarIsDisabledField.GetValue(avatar) is bool disabled && disabled;
        }

        private static bool IsLocalPlayerDead()
        {
            var avatar = PlayerAvatar.instance;
            if (avatar == null || s_playerAvatarDeadSetField == null)
                return false;

            return s_playerAvatarDeadSetField.GetValue(avatar) is bool dead && dead;
        }

        private static void PatchChargeAbilityGettersIfPossible(Harmony harmony, Assembly asm)
        {
            var chargeAbilityType = asm.GetType("DeathHeadHopper.Abilities.Charge.ChargeAbility", throwOnError: false);
            if (chargeAbilityType == null)
                return;

            var getCostMethod = AccessTools.PropertyGetter(chargeAbilityType, "EnergyCost");
            var getCooldownMethod = AccessTools.PropertyGetter(chargeAbilityType, "Cooldown");
            if (getCostMethod == null)
                return;

            var postCost = typeof(ChargeAbilityTuningModule).GetMethod(nameof(ChargeAbility_EnergyCost_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var preCooldown = typeof(ChargeAbilityTuningModule).GetMethod(nameof(ChargeAbility_Cooldown_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postCost == null || preCooldown == null)
                return;

            harmony.Patch(getCostMethod, postfix: new HarmonyMethod(postCost));
            if (getCooldownMethod != null)
            {
                harmony.Patch(getCooldownMethod, prefix: new HarmonyMethod(preCooldown));
            }
        }

        private static void ChargeAbility_EnergyCost_Postfix(UnityEngine.Object __instance, ref float __result)
        {
            if (__instance == null)
                return;
            if (InternalDebugFlags.DisableAbilityPatches)
                return;

            var abilityBaseCost = Mathf.Max(0f, __result);
            var customChargeCost = Mathf.Max(0f, (float)FeatureFlags.ChargeAbilityStaminaCost);
            if (customChargeCost <= 0f)
            {
                __result = abilityBaseCost;
                return;
            }

            __result = customChargeCost;

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHCharge.Cost", 120))
            {
                Debug.Log($"[Fix:DHHCharge] Charge cost override: custom={customChargeCost:F3} base={abilityBaseCost:F3}");
            }
        }

        private static bool ChargeAbility_Cooldown_Prefix(UnityEngine.Object __instance, ref float __result)
        {
            if (__instance == null)
                return true;
            if (InternalDebugFlags.DisableAbilityPatches)
                return true;

            var customCooldown = Mathf.Max(0f, (float)FeatureFlags.ChargeAbilityCooldown);
            __result = customCooldown;

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHCharge.Cooldown", 120))
            {
                Debug.Log($"[Fix:DHHCharge] Cooldown override: custom={customCooldown:F3}");
            }

            return false;
        }
    }
}
