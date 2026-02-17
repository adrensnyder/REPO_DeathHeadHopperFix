#nullable enable

using System;
using System.Reflection;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class LastChanceInteropBridge
    {
        private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type? s_timerControllerType;
        private static Type? s_spectateHelperType;
        private static Type? s_featureFlagsType;
        private static bool s_resolvedTypes;

        private static PropertyInfo? s_isActiveProperty;
        private static PropertyInfo? s_directionUiVisibleProperty;
        private static MethodInfo? s_isDirectionEnergySufficientMethod;
        private static MethodInfo? s_getDirectionPenaltyPreviewMethod;
        private static MethodInfo? s_getDirectionEnergyDebugSnapshotMethod;
        private static MethodInfo? s_isPlayerSurrenderedForDataMethod;

        private static MethodInfo? s_allPlayersDisabledMethod;
        private static MethodInfo? s_resetForceStateMethod;
        private static MethodInfo? s_shouldForceLocalDeathHeadSpectateMethod;
        private static MethodInfo? s_ensureSpectatePlayerLocalMethod;
        private static MethodInfo? s_forceDeathHeadSpectateIfPossibleMethod;
        private static MethodInfo? s_debugLogStateMethod;
        private static MethodInfo? s_isManualSwitchInputDownMethod;
        private static FieldInfo? s_lastChanceModeField;
        private static FieldInfo? s_spectateDeadPlayersField;
        private static FieldInfo? s_spectateDeadPlayersModeField;
        private static FieldInfo? s_lastChanceIndicatorsField;

        internal static bool IsLastChanceActive() =>
            TryGetBoolProperty(ref s_isActiveProperty, "DeathHeadHopperFix.Modules.Gameplay.LastChance.Runtime.LastChanceTimerController", "IsActive");

        internal static bool IsDirectionIndicatorUiVisible() =>
            TryGetBoolProperty(ref s_directionUiVisibleProperty, "DeathHeadHopperFix.Modules.Gameplay.LastChance.Runtime.LastChanceTimerController", "IsDirectionIndicatorUiVisible");

        internal static bool IsDirectionIndicatorEnergySufficientPreview()
        {
            ResolveMembers();
            if (s_isDirectionEnergySufficientMethod == null)
            {
                return false;
            }

            return s_isDirectionEnergySufficientMethod.Invoke(null, null) as bool? ?? false;
        }

        internal static float GetDirectionIndicatorPenaltySecondsPreview()
        {
            ResolveMembers();
            if (s_getDirectionPenaltyPreviewMethod == null)
            {
                return 0f;
            }

            return s_getDirectionPenaltyPreviewMethod.Invoke(null, null) as float? ?? 0f;
        }

        internal static void GetDirectionIndicatorEnergyDebugSnapshot(
            out bool visible,
            out float timerRemaining,
            out float penaltyPreview,
            out bool hasEnoughEnergy)
        {
            visible = false;
            timerRemaining = 0f;
            penaltyPreview = 0f;
            hasEnoughEnergy = false;

            ResolveMembers();
            if (s_getDirectionEnergyDebugSnapshotMethod == null)
            {
                return;
            }

            var args = new object[] { false, 0f, 0f, false };
            s_getDirectionEnergyDebugSnapshotMethod.Invoke(null, args);
            visible = args[0] as bool? ?? false;
            timerRemaining = args[1] as float? ?? 0f;
            penaltyPreview = args[2] as float? ?? 0f;
            hasEnoughEnergy = args[3] as bool? ?? false;
        }

        internal static bool IsPlayerSurrenderedForData(PlayerAvatar? player)
        {
            ResolveMembers();
            if (s_isPlayerSurrenderedForDataMethod == null)
            {
                return false;
            }

            return s_isPlayerSurrenderedForDataMethod.Invoke(null, new object?[] { player }) as bool? ?? false;
        }

        internal static bool AllPlayersDisabled()
        {
            ResolveMembers();
            if (s_allPlayersDisabledMethod == null)
            {
                return false;
            }

            return s_allPlayersDisabledMethod.Invoke(null, null) as bool? ?? false;
        }

        internal static void ResetSpectateForceState()
        {
            ResolveMembers();
            s_resetForceStateMethod?.Invoke(null, null);
        }

        internal static bool ShouldForceLocalDeathHeadSpectate()
        {
            ResolveMembers();
            if (s_shouldForceLocalDeathHeadSpectateMethod == null)
            {
                return false;
            }

            return s_shouldForceLocalDeathHeadSpectateMethod.Invoke(null, null) as bool? ?? false;
        }

        internal static void EnsureSpectatePlayerLocal(SpectateCamera? spectate)
        {
            ResolveMembers();
            s_ensureSpectatePlayerLocalMethod?.Invoke(null, new object?[] { spectate });
        }

        internal static void ForceDeathHeadSpectateIfPossible()
        {
            ResolveMembers();
            s_forceDeathHeadSpectateIfPossibleMethod?.Invoke(null, null);
        }

        internal static void DebugLogState(SpectateCamera? spectate)
        {
            ResolveMembers();
            s_debugLogStateMethod?.Invoke(null, new object?[] { spectate });
        }

        internal static bool IsManualSwitchInputDown()
        {
            ResolveMembers();
            if (s_isManualSwitchInputDownMethod == null)
            {
                return false;
            }

            return s_isManualSwitchInputDownMethod.Invoke(null, null) as bool? ?? false;
        }

        internal static bool IsLastChanceModeEnabled()
        {
            ResolveMembers();
            if (s_lastChanceModeField == null)
            {
                return false;
            }

            return s_lastChanceModeField.GetValue(null) as bool? ?? false;
        }

        internal static bool IsSpectateDeadPlayersEnabled()
        {
            ResolveMembers();
            if (s_spectateDeadPlayersField == null)
            {
                return false;
            }

            return s_spectateDeadPlayersField.GetValue(null) as bool? ?? false;
        }

        internal static string GetSpectateDeadPlayersMode()
        {
            ResolveMembers();
            if (s_spectateDeadPlayersModeField == null)
            {
                return string.Empty;
            }

            return s_spectateDeadPlayersModeField.GetValue(null) as string ?? string.Empty;
        }

        internal static string GetLastChanceIndicatorsMode()
        {
            ResolveMembers();
            if (s_lastChanceIndicatorsField == null)
            {
                return string.Empty;
            }

            return s_lastChanceIndicatorsField.GetValue(null) as string ?? string.Empty;
        }

        private static bool TryGetBoolProperty(ref PropertyInfo? property, string typeName, string propertyName)
        {
            ResolveMembers();
            if (property == null)
            {
                var type = ResolveType(typeName);
                property = type?.GetProperty(propertyName, StaticAny);
            }

            return property?.GetValue(null) as bool? ?? false;
        }

        private static void ResolveMembers()
        {
            if (!s_resolvedTypes)
            {
                s_timerControllerType = ResolveType("DeathHeadHopperFix.Modules.Gameplay.LastChance.Runtime.LastChanceTimerController");
                s_spectateHelperType = ResolveType("DeathHeadHopperFix.Modules.Gameplay.LastChance.Spectate.LastChanceSpectateHelper");
                s_featureFlagsType = ResolveType("DHHFLastChanceMode.Modules.Config.FeatureFlags");
                s_resolvedTypes = true;
            }

            if (s_timerControllerType != null)
            {
                s_isDirectionEnergySufficientMethod ??= s_timerControllerType.GetMethod("IsDirectionIndicatorEnergySufficientPreview", StaticAny);
                s_getDirectionPenaltyPreviewMethod ??= s_timerControllerType.GetMethod("GetDirectionIndicatorPenaltySecondsPreview", StaticAny);
                s_getDirectionEnergyDebugSnapshotMethod ??= s_timerControllerType.GetMethod("GetDirectionIndicatorEnergyDebugSnapshot", StaticAny);
                s_isPlayerSurrenderedForDataMethod ??= s_timerControllerType.GetMethod("IsPlayerSurrenderedForData", StaticAny);
            }

            if (s_spectateHelperType != null)
            {
                s_allPlayersDisabledMethod ??= s_spectateHelperType.GetMethod("AllPlayersDisabled", StaticAny);
                s_resetForceStateMethod ??= s_spectateHelperType.GetMethod("ResetForceState", StaticAny);
                s_shouldForceLocalDeathHeadSpectateMethod ??= s_spectateHelperType.GetMethod("ShouldForceLocalDeathHeadSpectate", StaticAny);
                s_ensureSpectatePlayerLocalMethod ??= s_spectateHelperType.GetMethod("EnsureSpectatePlayerLocal", StaticAny);
                s_forceDeathHeadSpectateIfPossibleMethod ??= s_spectateHelperType.GetMethod("ForceDeathHeadSpectateIfPossible", StaticAny);
                s_debugLogStateMethod ??= s_spectateHelperType.GetMethod("DebugLogState", StaticAny);
                s_isManualSwitchInputDownMethod ??= s_spectateHelperType.GetMethod("IsManualSwitchInputDown", StaticAny);
            }

            if (s_featureFlagsType != null)
            {
                s_lastChanceModeField ??= s_featureFlagsType.GetField("LastChangeMode", StaticAny);
                s_spectateDeadPlayersField ??= s_featureFlagsType.GetField("SpectateDeadPlayers", StaticAny);
                s_spectateDeadPlayersModeField ??= s_featureFlagsType.GetField("SpectateDeadPlayersMode", StaticAny);
                s_lastChanceIndicatorsField ??= s_featureFlagsType.GetField("LastChanceIndicators", StaticAny);
            }
        }

        private static Type? ResolveType(string fullName)
        {
            var type = Type.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
