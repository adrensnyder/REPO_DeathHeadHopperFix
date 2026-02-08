#nullable enable

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Gameplay.Spectate;
using DeathHeadHopperFix.Modules.Stamina;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core;
using DeathHeadHopperFix.Modules.Gameplay.LastChance;
using DeathHeadHopperFix.Modules.Gameplay.Spectate.Patches;
using DeathHeadHopperFix.Modules.Utilities;

namespace DeathHeadHopperFix
{


    [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
    internal static class SpectateCameraStateNormalPatchMarker
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SpectateCameraStateNormalPatch.Transpiler(instructions);
    }


    [BepInPlugin("AdrenSnyder.DeathHeadHopperFix", "Death Head Hopper - Fix", "0.1.9")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string TargetAssemblyName = "DeathHeadHopper";
        private const string HopForceLogKey = "Fix:Hop.JumpForce";
        private const string JumpForceLogKey = "Fix:Jump.HeadJumpForce";
        private const string ChargeStrengthLogKey = "Fix:Charge.Strength";

        private Harmony? _harmony;
        private bool _patched;
        private static ManualLogSource? _log;
        private static bool _guardMissingLocalCameraPosition;
        private static FieldInfo? _jumpHandlerJumpBufferField;
        private static FieldInfo? s_playerAvatarDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
        private static FieldInfo? s_abilityEnergyHandlerControllerField;
        private static FieldInfo? s_deathHeadControllerDeathHeadField;
        private static FieldInfo? s_playerDeathHeadAvatarField;
        private static FieldInfo? s_playerAvatarIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static MethodInfo? s_hopHandlerPowerLevelGetter;
        private static MethodInfo? s_jumpHandlerPowerLevelGetter;
        private static FieldInfo? s_chargeHandlerChargeStrengthField;
        private static MethodInfo? s_chargeHandlerAbilityLevelGetter;
        private static FieldInfo? s_chargeHandlerImpactDetectorField;
        private static FieldInfo? s_chargeHandlerControllerField;
        private static FieldInfo? s_deathHeadControllerAudioHandlerField;
        private static MethodInfo? s_audioHandlerStopWindupMethod;
        private static FieldInfo? s_impactDetectorPhysGrabObjectField;
        private static FieldInfo? s_cachedPhysGrabObjectGrabbedField;

        private void Awake()
        {
            _log = Logger;
            ConfigManager.Initialize(Config);
            AllPlayersDeadGuard.EnsureEnabled();
            _harmony = new Harmony("AdrenSnyder.DeathHeadHopperFix");

            // Local patches from this assembly, e.g. PlayerDeathHeadUpdatePatch.
            // NOTE: we no longer patch SpectateCamera.UpdateState/StateHead because they were fragile
            // across versions and produced IL compile errors.
            _harmony.PatchAll(typeof(Plugin).Assembly);

            DetectGameApiChanges();
            ApplyEarlyPatches();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            SceneManager.sceneLoaded += OnSceneLoaded;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchIfTargetAssembly(asm);
        }


        private void OnDestroy()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryPatchIfTargetAssembly(args.LoadedAssembly);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            LastChanceTimerController.OnLevelLoaded();
        }

        private void Update()
        {
        }

        private void ApplyEarlyPatches()
        {
            if (_harmony == null)
                return;

            StatsModule.ApplyHooks(_harmony);
            ItemUpgradeModule.Apply(_harmony);
        }


        private void TryPatchIfTargetAssembly(Assembly asm)
        {
            if (_patched) return;
            if (asm == null) return;

            var name = asm.GetName().Name;
            if (!string.Equals(name, TargetAssemblyName, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                _log?.LogInfo($"Detected {TargetAssemblyName} assembly load. Applying patches...");

                var harmony = _harmony;
                if (harmony == null)
                    throw new InvalidOperationException("Harmony instance is null.");

                // Runtime-only target patches:
                // These hooks are intentionally applied with harmony.Patch(...) instead of PatchAll.
                // They target methods/types from the external DeathHeadHopper assembly, which is loaded
                // dynamically and resolved via reflection at runtime. Because those target symbols are
                // not compile-time-stable in this assembly, static [HarmonyPatch] declarations are not
                // a reliable fit here.
                PrefabModule.Apply(harmony, asm, _log);
                AudioModule.Apply(harmony, asm, _log);
                DHHShopModule.Apply(harmony, asm, _log);
                LastChanceMonstersSearchModule.Apply(harmony, asm);

                // 5) Guard DeathHeadController revive/update when localCameraPosition is missing
                PatchDeathHeadControllerGuardsIfPossible(harmony, asm);
                    PatchDeathHeadControllerModulesIfPossible(harmony, asm);
                    PatchJumpHandlerUpdateIfPossible(harmony, asm);
                    PatchChargeHandlerDamageModeIfPossible(harmony, asm);
                    PatchJumpHandlerJumpForceIfPossible(harmony, asm);
                    PatchHopHandlerJumpForceIfPossible(harmony, asm);

                // 6) Suppress false host warning and delay version check
                InputModule.Apply(harmony, asm, _log);

                // 7) Keep DHHPunManager PhotonView alive across scenes
                // handled by InputModule
                // 8-10) Stats/SemiFunc patches are applied during Awake via ApplyEarlyPatches
                // 11) Allow Photon.DefaultPool to instantiate mod prefabs
                // handled by PrefabModule
                PatchAbilityEnergyHandlerRechargeSoundIfPossible(harmony, asm);

                AbilityModule.ApplyAbilitySpotLabelOverlay(harmony, asm);
                AbilityModule.ApplyAbilityManagerHooks(harmony, asm);
                PatchChargeAbilityGettersIfPossible(harmony, asm);

                _patched = true;
                _log?.LogInfo("Patches applied successfully.");
            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }
        }

        private static void PatchAbilityEnergyHandlerRechargeSoundIfPossible(Harmony harmony, Assembly asm)
        {
            var tAbilityEnergyHandler = asm.GetType("DeathHeadHopper.DeathHead.Handlers.AbilityEnergyHandler", throwOnError: false);
            if (tAbilityEnergyHandler == null)
                return;
            s_abilityEnergyHandlerControllerField ??= tAbilityEnergyHandler.GetField("controller", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

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

            var mPlayRechargeSound = AccessTools.Method(tAbilityEnergyHandler, "PlayRechargeSound", Type.EmptyTypes);
            if (mPlayRechargeSound == null)
                return;

            var prefix = typeof(Plugin).GetMethod(nameof(AbilityEnergyHandler_PlayRechargeSound_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mPlayRechargeSound, prefix: new HarmonyMethod(prefix));
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
            var tChargeAbility = asm.GetType("DeathHeadHopper.Abilities.Charge.ChargeAbility", throwOnError: false);
            if (tChargeAbility == null)
                return;

            var mGetCost = AccessTools.PropertyGetter(tChargeAbility, "EnergyCost");
            var mGetCooldown = AccessTools.PropertyGetter(tChargeAbility, "Cooldown");
            if (mGetCost == null)
                return;

            var postCost = typeof(Plugin).GetMethod(nameof(ChargeAbility_EnergyCost_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            var preCooldown = typeof(Plugin).GetMethod(nameof(ChargeAbility_Cooldown_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (postCost == null || preCooldown == null)
                return;

            harmony.Patch(mGetCost, postfix: new HarmonyMethod(postCost));
            if (mGetCooldown != null)
            {
                harmony.Patch(mGetCooldown, prefix: new HarmonyMethod(preCooldown));
            }
        }

        private static void ChargeAbility_EnergyCost_Postfix(UnityEngine.Object __instance, ref float __result)
        {
            if (__instance == null)
                return;
            if (FeatureFlags.DisableAbilityPatches)
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
            if (FeatureFlags.DisableAbilityPatches)
                return true;

            var customCooldown = Mathf.Max(0f, (float)FeatureFlags.ChargeAbilityCooldown);
            __result = customCooldown;

            if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("DHHCharge.Cooldown", 120))
            {
                Debug.Log($"[Fix:DHHCharge] Cooldown override: custom={customCooldown:F3}");
            }

            return false;
        }

        // *****--
        // Game API probes and guards
        // *****--
        private static void DetectGameApiChanges()
        {
            try
            {
                var tPlayerAvatar = AccessTools.TypeByName("PlayerAvatar");
                if (tPlayerAvatar == null)
                {
                    _guardMissingLocalCameraPosition = true;
                    return;
                }

                var f = tPlayerAvatar.GetField("localCameraPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _guardMissingLocalCameraPosition = (f == null);
            }
            catch
            {
                _guardMissingLocalCameraPosition = true;
            }
        }

        private static void PatchDeathHeadControllerGuardsIfPossible(Harmony harmony, Assembly asm)
        {
            if (!_guardMissingLocalCameraPosition)
                return;

            var tController = asm.GetType("DeathHeadHopper.DeathHead.DeathHeadController", throwOnError: false);
            if (tController == null) return;

            var mCancel = AccessTools.Method(tController, "CancelReviveInCart");
            if (mCancel != null)
            {
                var miPrefix = typeof(Plugin).GetMethod(nameof(DeathHeadController_CancelReviveInCart_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (miPrefix != null)
                    harmony.Patch(mCancel, prefix: new HarmonyMethod(miPrefix));
            }

            var mUpdate = AccessTools.Method(tController, "Update");
            if (mUpdate != null)
            {
                var miFinalizer = typeof(Plugin).GetMethod(nameof(DeathHeadController_Update_Finalizer),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (miFinalizer != null)
                    harmony.Patch(mUpdate, finalizer: new HarmonyMethod(miFinalizer));
            }
        }

        private static bool DeathHeadController_CancelReviveInCart_Prefix()
        {
            if (_guardMissingLocalCameraPosition)
                return false;
            return true;
        }

        private static Exception? DeathHeadController_Update_Finalizer(Exception? __exception)
        {
            if (__exception == null)
                return null;

            if (_guardMissingLocalCameraPosition && __exception is MissingFieldException)
                return null;

            return __exception;
        }

        private static void PatchDeathHeadControllerModulesIfPossible(Harmony harmony, Assembly asm)
        {
            var controllerType = asm.GetType("DeathHeadHopper.DeathHead.DeathHeadController", throwOnError: false);
            if (controllerType == null)
                return;

            var startMethod = AccessTools.Method(controllerType, "Start");
            if (startMethod == null)
                return;

            var postfix = typeof(Plugin).GetMethod(nameof(DeathHeadController_Start_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (postfix == null)
                return;

            harmony.Patch(startMethod, postfix: new HarmonyMethod(postfix));
        }

        private static void DeathHeadController_Start_Postfix(object __instance)
        {
            if (__instance is not MonoBehaviour mono)
                return;

            var go = mono.gameObject;
            if (go.GetComponent<BatteryJumpModule>() == null)
            {
                go.AddComponent<BatteryJumpModule>();
            }

            if (go.GetComponent<StaminaRechargeModule>() == null)
            {
                go.AddComponent<StaminaRechargeModule>();
            }
        }

        private static void PatchJumpHandlerUpdateIfPossible(Harmony harmony, Assembly asm)
        {
            var jumpHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.JumpHandler", throwOnError: false);
            if (jumpHandlerType == null)
                return;

            _jumpHandlerJumpBufferField = AccessTools.Field(jumpHandlerType, "jumpBufferTimer");
            if (_jumpHandlerJumpBufferField == null)
                return;

            var mUpdate = AccessTools.Method(jumpHandlerType, "Update");
            if (mUpdate == null)
                return;

            var prefix = typeof(Plugin).GetMethod(nameof(JumpHandler_Update_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(mUpdate, prefix: new HarmonyMethod(prefix));
        }

        private static bool JumpHandler_Update_Prefix(MonoBehaviour __instance)
        {
            if (!FeatureFlags.BatteryJumpEnabled)
                return true;
            if (__instance == null || _jumpHandlerJumpBufferField == null)
                return true;

            if (_jumpHandlerJumpBufferField.GetValue(__instance) is not float buffer || buffer <= 0f)
                return true;

            var module = __instance.gameObject.GetComponent<BatteryJumpModule>();
            if (module == null)
                return true;

            var allowance = DHHBatteryHelper.EvaluateJumpAllowance();
            if (allowance.allowed)
                return true;

            _jumpHandlerJumpBufferField.SetValue(__instance, 0f);
            module.NotifyJumpBlocked(allowance.currentEnergy, allowance.reference, allowance.readyFlag);
            return false;
        }

        private static void PatchChargeHandlerDamageModeIfPossible(Harmony harmony, Assembly asm)
        {
            var chargeHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.ChargeHandler", throwOnError: false);
            if (chargeHandlerType == null)
                return;

            var mWindup = AccessTools.Method(chargeHandlerType, "ChargeWindup", new[] { typeof(Vector3) });
            var windupPrefix = typeof(Plugin).GetMethod(nameof(ChargeHandler_ChargeWindup_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mWindup != null && windupPrefix != null)
                harmony.Patch(mWindup, prefix: new HarmonyMethod(windupPrefix));

            var mReset = AccessTools.Method(chargeHandlerType, "ResetState", Type.EmptyTypes);
            if (s_chargeHandlerChargeStrengthField == null)
            {
                s_chargeHandlerChargeStrengthField = AccessTools.Field(chargeHandlerType, "chargeStrength");
            }
            if (s_chargeHandlerAbilityLevelGetter == null)
            {
                s_chargeHandlerAbilityLevelGetter = AccessTools.PropertyGetter(chargeHandlerType, "AbilityLevel");
            }
            if (s_chargeHandlerControllerField == null)
            {
                s_chargeHandlerControllerField = AccessTools.Field(chargeHandlerType, "controller");
            }
            var resetPostfix = typeof(Plugin).GetMethod(nameof(ChargeHandler_ResetState_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mReset != null && resetPostfix != null)
                harmony.Patch(mReset, postfix: new HarmonyMethod(resetPostfix));
            var mEndCharge = AccessTools.Method(chargeHandlerType, "EndCharge", Type.EmptyTypes);
            var endChargePostfix = typeof(Plugin).GetMethod(nameof(ChargeHandler_EndCharge_Postfix), BindingFlags.Static | BindingFlags.NonPublic);
            if (mEndCharge != null && endChargePostfix != null)
                harmony.Patch(mEndCharge, postfix: new HarmonyMethod(endChargePostfix));
        }

        private static void PatchJumpHandlerJumpForceIfPossible(Harmony harmony, Assembly asm)
        {
            var jumpHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.JumpHandler", throwOnError: false);
            if (jumpHandlerType == null)
                return;

            var jumpForceGetter = AccessTools.PropertyGetter(jumpHandlerType, "JumpForce");
            if (jumpForceGetter == null)
                return;

            if (s_jumpHandlerPowerLevelGetter == null)
            {
                s_jumpHandlerPowerLevelGetter = AccessTools.PropertyGetter(jumpHandlerType, "PowerLevel");
            }

            var prefix = typeof(Plugin).GetMethod(nameof(JumpHandler_JumpForce_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
                return;

            harmony.Patch(jumpForceGetter, prefix: new HarmonyMethod(prefix));
        }

        private static bool JumpHandler_JumpForce_Prefix(object __instance, ref float __result)
        {
            if (__instance == null || s_jumpHandlerPowerLevelGetter == null)
                return true;

            var levelObj = s_jumpHandlerPowerLevelGetter.Invoke(__instance, null);
            var level = levelObj is int value ? value : 0;
            var appliedLevel = level + 1;
            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHJumpForceBaseValue,
                FeatureFlags.DHHJumpForceIncreasePerLevel,
                appliedLevel,
                FeatureFlags.DHHJumpForceThresholdLevel,
                FeatureFlags.DHHJumpForceDiminishingFactor);

            __result = stat.FinalValue;
            LogJumpForce(__instance, level, appliedLevel, stat);
            return false;
        }

        private static bool ChargeHandler_ChargeWindup_Prefix(object __instance)
        {
            if (__instance == null)
                return true;

            if (IsChargeHandlerHeadGrabbed(__instance))
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("Fix:Charge.Grabbed", 30))
                {
                    Debug.Log("[Fix:Charge] Charge windup blocked because the head is grabbed.");
                }
                return false;
            }

            return true;
        }

        private static bool IsChargeHandlerHeadGrabbed(object chargeHandler)
        {
            var impactDetector = GetChargeImpactDetector(chargeHandler);
            if (impactDetector == null)
                return false;

            var physGrabObject = GetImpactPhysGrabObject(impactDetector);
            if (physGrabObject == null)
                return false;

            var grabbedField = s_cachedPhysGrabObjectGrabbedField;
            if (grabbedField == null || grabbedField.DeclaringType != physGrabObject.GetType())
            {
                grabbedField = AccessTools.Field(physGrabObject.GetType(), "grabbed");
                s_cachedPhysGrabObjectGrabbedField = grabbedField;
            }

            return grabbedField != null && grabbedField.GetValue(physGrabObject) is bool grabbed && grabbed;
        }

        private static object? GetChargeImpactDetector(object chargeHandler)
        {
            var field = s_chargeHandlerImpactDetectorField;
            if (field == null || field.DeclaringType != chargeHandler.GetType())
            {
                field = AccessTools.Field(chargeHandler.GetType(), "impactDetector");
                s_chargeHandlerImpactDetectorField = field;
            }

            return field?.GetValue(chargeHandler);
        }

        private static object? GetImpactPhysGrabObject(object impactDetector)
        {
            if (impactDetector == null)
                return null;

            var field = s_impactDetectorPhysGrabObjectField;
            if (field == null || field.DeclaringType != impactDetector.GetType())
            {
                field = AccessTools.Field(impactDetector.GetType(), "physGrabObject");
                s_impactDetectorPhysGrabObjectField = field;
            }

            return field?.GetValue(impactDetector);
        }

        private static void ChargeHandler_ResetState_Postfix(object __instance)
        {
            StopChargeWindupLoop(__instance);
            if (__instance == null || s_chargeHandlerChargeStrengthField == null || s_chargeHandlerAbilityLevelGetter == null)
                return;

            var levelObj = s_chargeHandlerAbilityLevelGetter.Invoke(__instance, null);
            var level = levelObj is int value ? value : 0;

            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHChargeStrengthBaseValue,
                FeatureFlags.DHHChargeStrengthIncreasePerLevel,
                level,
                FeatureFlags.DHHChargeStrengthThresholdLevel,
                FeatureFlags.DHHChargeStrengthDiminishingFactor);

            s_chargeHandlerChargeStrengthField.SetValue(__instance, stat.FinalValue);
            LogChargeStrength(__instance, stat);
        }

        private static void ChargeHandler_EndCharge_Postfix(object __instance)
        {
            StopChargeWindupLoop(__instance);
        }

        private static void StopChargeWindupLoop(object? chargeHandler)
        {
            if (chargeHandler == null)
                return;

            try
            {
                var controllerField = s_chargeHandlerControllerField ??= AccessTools.Field(chargeHandler.GetType(), "controller");
                if (controllerField == null)
                    return;

                var controller = controllerField.GetValue(chargeHandler);
                if (controller == null)
                    return;

                var audioField = s_deathHeadControllerAudioHandlerField ??= AccessTools.Field(controller.GetType(), "audioHandler");
                if (audioField == null)
                    return;

                var audioHandler = audioField.GetValue(controller);
                if (audioHandler == null)
                    return;

                var stopMethod = s_audioHandlerStopWindupMethod ??= AccessTools.Method(audioHandler.GetType(), "StopWindupSound", Type.EmptyTypes);
                stopMethod?.Invoke(audioHandler, null);
            }
            catch
            {
                // best effort
            }
        }

        private static void PatchHopHandlerJumpForceIfPossible(Harmony harmony, Assembly asm)
        {
            var hopHandlerType = asm.GetType("DeathHeadHopper.DeathHead.Handlers.HopHandler", throwOnError: false);
            if (hopHandlerType == null)
                return;

            var jumpForceGetter = AccessTools.PropertyGetter(hopHandlerType, "JumpForce");
            if (s_hopHandlerPowerLevelGetter == null)
            {
                s_hopHandlerPowerLevelGetter = AccessTools.PropertyGetter(hopHandlerType, "PowerLevel");
            }

            var prefix = typeof(Plugin).GetMethod(nameof(HopHandler_JumpForce_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (jumpForceGetter != null && prefix != null)
            {
                // Prefix (skip original) is more robust than postfix here: it guarantees the returned JumpForce
                // is the custom one, even if other code reads JumpForce multiple times or if other mods patch it.
                harmony.Patch(jumpForceGetter, prefix: new HarmonyMethod(prefix));
            }
        }

        private static bool HopHandler_JumpForce_Prefix(object __instance, ref float __result)
        {
            if (__instance == null || s_hopHandlerPowerLevelGetter == null)
                return true;

            var levelObj = s_hopHandlerPowerLevelGetter.Invoke(__instance, null);
            var level = levelObj is int value ? value : 0;
            var appliedLevel = level + 1;
            var stat = EvaluateStatWithDiminishingReturns(
                FeatureFlags.DHHHopJumpBaseValue,
                FeatureFlags.DHHHopJumpIncreasePerLevel,
                appliedLevel,
                FeatureFlags.DHHHopJumpThresholdLevel,
                FeatureFlags.DHHHopJumpDiminishingFactor);

            __result = stat.FinalValue;
            LogHopJumpForce(__instance, level, appliedLevel, stat);

            return false; // skip original getter
        }

        private static float StatWithDiminishingReturns(float baseValue, float increasePerLevel, int currentLevel, int thresholdLevel, float diminishingFactor)
        {
            return EvaluateStatWithDiminishingReturns(baseValue, increasePerLevel, currentLevel, thresholdLevel, diminishingFactor).FinalValue;
        }

        private static DiminishingReturnsResult EvaluateStatWithDiminishingReturns(float baseValue, float increasePerLevel, int currentLevel, int thresholdLevel, float diminishingFactor)
        {
            var appliedLevel = currentLevel;
            var normalizedLevel = Math.Max(0, appliedLevel - 1);
            var normalizedThreshold = Math.Max(0, thresholdLevel - 1);
            var linearLevels = Mathf.Min(normalizedLevel, normalizedThreshold);
            var extraLevels = Mathf.Max(0, normalizedLevel - normalizedThreshold);
            var diminishingComponent = extraLevels * Mathf.Pow(diminishingFactor, extraLevels);
            var linearContribution = increasePerLevel * linearLevels;
            var diminishingContribution = increasePerLevel * diminishingComponent;
            var finalValue = baseValue + linearContribution + diminishingContribution;

            return new DiminishingReturnsResult(
                baseValue,
                increasePerLevel,
                appliedLevel,
                thresholdLevel,
                diminishingFactor,
                linearLevels,
                extraLevels,
                linearContribution,
                diminishingContribution,
                diminishingComponent,
                finalValue);
        }

        private readonly struct DiminishingReturnsResult
        {
            public DiminishingReturnsResult(float baseValue, float increasePerLevel, int appliedLevel, int thresholdLevel, float diminishingFactor,
                int linearLevels, int extraLevels, float linearContribution, float diminishingContribution, float diminishingComponent, float finalValue)
            {
                BaseValue = baseValue;
                IncreasePerLevel = increasePerLevel;
                AppliedLevel = appliedLevel;
                ThresholdLevel = thresholdLevel;
                DiminishingFactor = diminishingFactor;
                LinearLevels = linearLevels;
                ExtraLevels = extraLevels;
                LinearContribution = linearContribution;
                DiminishingContribution = diminishingContribution;
                DiminishingComponent = diminishingComponent;
                FinalValue = finalValue;
            }

            public float BaseValue { get; }
            public float IncreasePerLevel { get; }
            public int AppliedLevel { get; }
            public int ThresholdLevel { get; }
            public float DiminishingFactor { get; }
            public int LinearLevels { get; }
            public int ExtraLevels { get; }
            public float LinearContribution { get; }
            public float DiminishingContribution { get; }
            public float DiminishingComponent { get; }
            public float FinalValue { get; }
        }
        private static void LogHopJumpForce(object hopHandler, int powerLevel, int appliedLevel, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(HopForceLogKey, 30))
                return;

            var label = GetHandlerLabel(hopHandler, "HopHandler");
            var message = $"[Fix:Hop] {label} JumpForce={stat.FinalValue:F3} powerLevel={powerLevel} appliedLevel={appliedLevel} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            _log?.LogInfo(message);
            Debug.Log(message);
        }

        private static void LogJumpForce(object jumpHandler, int powerLevel, int appliedLevel, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(JumpForceLogKey, 30))
                return;

            var label = GetHandlerLabel(jumpHandler, "JumpHandler");
            var message = $"[Fix:Jump] {label} JumpForce={stat.FinalValue:F3} powerLevel={powerLevel} appliedLevel={appliedLevel} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            _log?.LogInfo(message);
            Debug.Log(message);
        }

        private static string GetHandlerLabel(object? handler, string fallback)
        {
            if (handler is Component component)
            {
                return component.name ?? component.GetType().Name;
            }

            return handler?.GetType().Name ?? fallback;
        }

        private static void LogChargeStrength(object chargeHandler, DiminishingReturnsResult stat)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            if (!LogLimiter.ShouldLog(ChargeStrengthLogKey, 60))
                return;

            var label = GetHandlerLabel(chargeHandler, "ChargeHandler");
            var message = $"[Fix:Charge] {label} Strength={stat.FinalValue:F3} base={stat.BaseValue:F3} inc={stat.IncreasePerLevel:F3} level={stat.AppliedLevel} fullUpgrades={stat.LinearLevels} dimUpgrades={stat.ExtraLevels} linearDelta={stat.LinearContribution:F3} dimDelta={stat.DiminishingContribution:F3} thresh={stat.ThresholdLevel} dimFactor={stat.DiminishingFactor:F3}";
            _log?.LogInfo(message);
            Debug.Log(message);
        }


        private static void TryImpactEffect(object impactDetector, Vector3 contactPoint)
        {
            if (impactDetector == null)
                return;

            try
            {
                if (!SemiFunc.IsMultiplayer())
                {
                    var m = AccessTools.Method(impactDetector.GetType(), "ImpactEffectRPC");
                    if (m != null)
                    {
                        var pars = m.GetParameters();
                        if (pars.Length == 2)
                        {
                            var info = Activator.CreateInstance(pars[1].ParameterType);
                            m.Invoke(impactDetector, new object?[] { contactPoint, info });
                            return;
                        }
                    }
                }

                var fPv = AccessTools.Field(impactDetector.GetType(), "photonView");
                if (fPv?.GetValue(impactDetector) is Photon.Pun.PhotonView pv)
                    pv.RPC("ImpactEffectRPC", 0, new object[] { contactPoint });
            }
            catch
            {
                // ignore
            }
        }

        private static Vector3 CalculateEnemyBounceNormal(Transform? self, Vector3 enemyCenterPoint)
        {
            if (self == null)
                return Vector3.up;

            var frontPoint = self.TransformPoint(Vector3.up * 0.3f);
            var directionVector = frontPoint - enemyCenterPoint;
            return Vector3.ProjectOnPlane(directionVector, Vector3.up).normalized;
        }

    }
}


