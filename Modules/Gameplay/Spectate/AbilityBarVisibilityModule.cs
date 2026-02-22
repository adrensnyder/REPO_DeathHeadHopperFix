#nullable enable

using System;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    [HarmonyPatch(typeof(SpectateCamera), "LateUpdate")]
    internal static class AbilityBarVisibilityModule
    {
        private static Type? s_dhhAbilityManagerType;
        private static PropertyInfo? s_dhhAbilityManagerInstanceProperty;
        private static MethodInfo? s_hasEquippedAbilityMethod;

        private static Type? s_abilityUiType;
        private static FieldInfo? s_abilityUiInstanceField;
        private static MethodInfo? s_abilityUiShowMethod;

        private static Type? s_abilityEnergyUiType;
        private static FieldInfo? s_abilityEnergyUiInstanceField;
        private static MethodInfo? s_abilityEnergyUiShowMethod;

        private static bool? s_lastShouldShow;

        [HarmonyPostfix]
        private static void LateUpdatePostfix()
        {
            if (InternalDebugFlags.DisableAbilityPatches)
            {
                return;
            }

            if (!ShouldEvaluateInCurrentContext())
            {
                s_lastShouldShow = null;
                return;
            }

            var shouldShow = ShouldShowAbilityBar();
            if (!shouldShow)
            {
                if (FeatureFlags.DebugLogging && s_lastShouldShow == true)
                {
                    UnityEngine.Debug.Log("[Fix:AbilityBar] hidden by policy (native=false, external=false).");
                }

                s_lastShouldShow = false;
                return;
            }

            if (FeatureFlags.DebugLogging && s_lastShouldShow != true)
            {
                var native = HasNativeEquippedAbility();
                var external = HasExternalAbilityUiDemand();
                UnityEngine.Debug.Log($"[Fix:AbilityBar] show by policy (native={native}, external={external}).");
            }

            TryShowAbilityUi();
            TryShowAbilityEnergyUi();
            s_lastShouldShow = true;
        }

        private static bool ShouldShowAbilityBar()
        {
            return HasNativeEquippedAbility() || HasExternalAbilityUiDemand();
        }

        private static bool HasExternalAbilityUiDemand()
        {
            return AbilityBarVisibilityAnchor.HasExternalDemand();
        }

        private static bool ShouldEvaluateInCurrentContext()
        {
            if (!SemiFunc.RunIsLevel() && !SemiFunc.RunIsShop())
            {
                return false;
            }

            return SpectateContextHelper.IsSpectatingLocalDeathHead();
        }

        private static bool HasNativeEquippedAbility()
        {
            ResolveDhhAbilityManagerReflection();
            if (s_dhhAbilityManagerInstanceProperty == null || s_hasEquippedAbilityMethod == null)
            {
                return false;
            }

            var instance = s_dhhAbilityManagerInstanceProperty.GetValue(null);
            if (instance == null)
            {
                return false;
            }

            return s_hasEquippedAbilityMethod.Invoke(instance, null) as bool? ?? false;
        }

        private static void TryShowAbilityUi()
        {
            ResolveAbilityUiReflection();
            var instance = s_abilityUiInstanceField?.GetValue(null);
            if (instance == null || s_abilityUiShowMethod == null)
            {
                return;
            }

            try
            {
                s_abilityUiShowMethod.Invoke(instance, null);
            }
            catch
            {
                // UI references can be rebuilt while transitioning scenes/menus.
            }
        }

        private static void TryShowAbilityEnergyUi()
        {
            ResolveAbilityEnergyUiReflection();
            var instance = s_abilityEnergyUiInstanceField?.GetValue(null);
            if (instance == null || s_abilityEnergyUiShowMethod == null)
            {
                return;
            }

            try
            {
                s_abilityEnergyUiShowMethod.Invoke(instance, null);
            }
            catch
            {
                // UI references can be rebuilt while transitioning scenes/menus.
            }
        }

        private static void ResolveDhhAbilityManagerReflection()
        {
            s_dhhAbilityManagerType ??= AccessTools.TypeByName("DeathHeadHopper.Managers.DHHAbilityManager");
            if (s_dhhAbilityManagerType == null)
            {
                return;
            }

            s_dhhAbilityManagerInstanceProperty ??=
                s_dhhAbilityManagerType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            s_hasEquippedAbilityMethod ??=
                s_dhhAbilityManagerType.GetMethod("HasEquippedAbility", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void ResolveAbilityUiReflection()
        {
            s_abilityUiType ??= AccessTools.TypeByName("DeathHeadHopper.UI.AbilityUI");
            if (s_abilityUiType == null)
            {
                return;
            }

            s_abilityUiInstanceField ??=
                s_abilityUiType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            s_abilityUiShowMethod ??=
                s_abilityUiType.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static void ResolveAbilityEnergyUiReflection()
        {
            s_abilityEnergyUiType ??= AccessTools.TypeByName("DeathHeadHopper.UI.AbilityEnergyUI");
            if (s_abilityEnergyUiType == null)
            {
                return;
            }

            s_abilityEnergyUiInstanceField ??=
                s_abilityEnergyUiType.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            s_abilityEnergyUiShowMethod ??=
                s_abilityEnergyUiType.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}
