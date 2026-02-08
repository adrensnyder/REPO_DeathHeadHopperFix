#nullable enable

using System;
using System.Reflection;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    internal static class LastChanceSpectateHelper
    {
        private static readonly FieldInfo? s_playerIsDisabledField =
            AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private const string ForceSpectateLogKey = "LastChance.ForceDeathHeadSpectate";
        private const string DebugStateLogKey = "LastChance.SpectateState";
        private static bool s_warnedMissingDhh;
        private static bool s_forceComplete;
        private static Type? s_dhhFuncType;
        private static MethodInfo? s_localDeathHeadActiveMethod;
        private static MethodInfo? s_getLocalDeathHeadControllerMethod;
        private static MethodInfo? s_isDeathHeadSpectatableMethod;
        private static MethodInfo? s_setSpectatedMethod;
        private static MethodInfo? s_updateSpectatedMethod;
        private static FieldInfo? s_controllerSpectatedField;
        private static object? s_cachedController;
        private static FieldInfo? s_spectatePlayerField;
        private static bool s_checkedDhhAccessors;
        private static bool s_missingDhhAccessors;
        private static bool s_warnedAccessorFailure;

        internal static bool AllPlayersDisabled()
        {
            var director = GameDirector.instance;
            if (director == null || director.PlayerList == null || director.PlayerList.Count == 0)
            {
                return false;
            }

            if (s_playerIsDisabledField == null)
            {
                return false;
            }

            foreach (var player in director.PlayerList)
            {
                if (player == null)
                {
                    continue;
                }

                if (s_playerIsDisabledField.GetValue(player) is bool disabled && !disabled)
                {
                    return false;
                }
            }

            return true;
        }

        internal static void ForceDeathHeadSpectateIfPossible()
        {
            if (!FeatureFlags.LastChangeMode)
            {
                return;
            }

            if (s_forceComplete)
            {
                return;
            }

            if (!LogLimiter.ShouldLog(ForceSpectateLogKey, 30))
            {
                return;
            }

            var funcType = s_dhhFuncType ??= AccessTools.TypeByName("DeathHeadHopper.Helpers.DHHFunc");
            if (funcType == null)
            {
                if (FeatureFlags.DebugLogging && !s_warnedMissingDhh)
                {
                    s_warnedMissingDhh = true;
                    UnityEngine.Debug.LogWarning("[LastChance] DeathHeadHopper.Helpers.DHHFunc not found; cannot force spectate.");
                }
                return;
            }

            if (!s_checkedDhhAccessors)
            {
                s_checkedDhhAccessors = true;
                s_localDeathHeadActiveMethod = AccessTools.Method(funcType, "LocalDeathHeadActive", Type.EmptyTypes);
                s_getLocalDeathHeadControllerMethod = AccessTools.Method(funcType, "GetLocalDeathHeadController", Type.EmptyTypes);
                s_isDeathHeadSpectatableMethod = AccessTools.Method(funcType, "IsDeathHeadSpectatable", new[] { typeof(PlayerAvatar) });
                s_missingDhhAccessors = s_localDeathHeadActiveMethod == null || s_getLocalDeathHeadControllerMethod == null;
            }

            if (s_missingDhhAccessors)
            {
                return;
            }

            if (s_localDeathHeadActiveMethod != null &&
                InvokeDhhAccessor(s_localDeathHeadActiveMethod, null) is bool active &&
                !active)
            {
                return;
            }

            var localAvatar = PlayerAvatar.instance;
            if (localAvatar != null && s_isDeathHeadSpectatableMethod != null)
            {
                if (InvokeDhhAccessor(s_isDeathHeadSpectatableMethod, new object[] { localAvatar }) is bool spectatable && !spectatable)
                {
                    return;
                }
            }

            var controller = TryGetLocalDeathHeadController();
            if (controller == null)
            {
                return;
            }
            s_cachedController = controller;

            var controllerType = controller.GetType();
            s_setSpectatedMethod ??= AccessTools.Method(controllerType, "SetSpectated", new[] { typeof(bool) });
            s_updateSpectatedMethod ??= AccessTools.Method(controllerType, "UpdateSpectated", Type.EmptyTypes);
            s_controllerSpectatedField ??= AccessTools.Field(controllerType, "spectated");

            if (IsSpectated(controller))
            {
                s_forceComplete = true;
                return;
            }

            var spectate = SpectateCamera.instance;
            if (spectate != null)
            {
                s_spectatePlayerField ??= AccessTools.Field(typeof(SpectateCamera), "player");
                if (s_spectatePlayerField != null && PlayerAvatar.instance != null)
                {
                    s_spectatePlayerField.SetValue(spectate, PlayerAvatar.instance);
                }
            }

            s_setSpectatedMethod?.Invoke(controller, new object[] { true });
            s_updateSpectatedMethod?.Invoke(controller, null);

            if (IsSpectated(controller))
            {
                s_forceComplete = true;
            }
        }

        internal static void ResetForceState()
        {
            s_forceComplete = false;
        }

        private static bool IsSpectated(object controller)
        {
            if (s_controllerSpectatedField != null &&
                s_controllerSpectatedField.GetValue(controller) is bool spectated &&
                spectated)
            {
                return true;
            }

            return false;
        }

        internal static bool IsDeathHeadSpectated()
        {
            var controller = s_cachedController ?? TryGetLocalDeathHeadController();
            if (controller == null)
            {
                return false;
            }

            s_cachedController = controller;
            var controllerType = controller.GetType();
            s_controllerSpectatedField ??= AccessTools.Field(controllerType, "spectated");

            return IsSpectated(controller);
        }

        internal static bool IsManualSwitchInputDown()
        {
            return SemiFunc.InputDown(InputKey.Jump) ||
                   SemiFunc.InputDown(InputKey.SpectateNext) ||
                   SemiFunc.InputDown(InputKey.SpectatePrevious);
        }

        internal static void EnsureSpectatePlayerLocal(SpectateCamera spectate)
        {
            if (spectate == null)
            {
                return;
            }

            s_spectatePlayerField ??= AccessTools.Field(typeof(SpectateCamera), "player");
            var local = PlayerAvatar.instance;
            if (s_spectatePlayerField != null && local != null)
            {
                s_spectatePlayerField.SetValue(spectate, local);
            }
        }

        internal static void DebugLogState(SpectateCamera? spectate)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            if (!LogLimiter.ShouldLog(DebugStateLogKey, 120))
            {
                return;
            }

            var local = PlayerAvatar.instance;
            var spectatePlayer = (spectate != null && s_spectatePlayerField != null)
                ? s_spectatePlayerField.GetValue(spectate) as PlayerAvatar
                : null;
            var isSpectateLocal = spectatePlayer != null && local != null && ReferenceEquals(spectatePlayer, local);

            bool? localActive = null;
            bool? spectatable = null;
            bool? spectated = null;

            if (s_localDeathHeadActiveMethod != null)
            {
                var activeObj = InvokeDhhAccessor(s_localDeathHeadActiveMethod, null);
                if (activeObj is bool activeBool)
                {
                    localActive = activeBool;
                }
            }

            if (local != null && s_isDeathHeadSpectatableMethod != null)
            {
                var spectObj = InvokeDhhAccessor(s_isDeathHeadSpectatableMethod, new object[] { local });
                if (spectObj is bool spectBool)
                {
                    spectatable = spectBool;
                }
            }

            var controller = s_cachedController ?? TryGetLocalDeathHeadController();
            if (controller != null)
            {
                s_cachedController = controller;
                var controllerType = controller.GetType();
                s_controllerSpectatedField ??= AccessTools.Field(controllerType, "spectated");
                if (s_controllerSpectatedField != null &&
                    s_controllerSpectatedField.GetValue(controller) is bool spectatedBool)
                {
                    spectated = spectatedBool;
                }
            }

            var spName = spectatePlayer != null ? spectatePlayer.GetType().Name : "null";
            var lpName = local != null ? local.GetType().Name : "null";
            UnityEngine.Debug.Log(
                $"[LastChance] SpectateState: spectatePlayer={spName} local={lpName} isSpectateLocal={isSpectateLocal} " +
                $"DHH.LocalActive={localActive} DHH.Spectatable={spectatable} DHH.Spectated={spectated}");
        }

        internal static bool ShouldForceLocalDeathHeadSpectate()
        {
            // If dead-player spectate is explicitly enabled in Always mode,
            // do not continuously re-force local spectate while everyone is disabled.
            if (FeatureFlags.SpectateDeadPlayers)
            {
                var mode = (FeatureFlags.SpectateDeadPlayersMode ?? string.Empty).Trim();
                if (mode.Equals("Always", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static object? TryGetLocalDeathHeadController()
        {
            var local = PlayerAvatar.instance;
            if (local == null || local.playerDeathHead == null)
            {
                return null;
            }

            return InvokeDhhAccessor(s_getLocalDeathHeadControllerMethod, null);
        }

        private static object? InvokeDhhAccessor(MethodInfo? method, object?[]? args)
        {
            if (method == null || s_missingDhhAccessors)
            {
                return null;
            }

            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException ex)
            {
                HandleAccessorFailure(ex);
            }
            catch (Exception ex)
            {
                HandleAccessorFailure(ex);
            }

            return null;
        }

        private static void HandleAccessorFailure(Exception? ex)
        {
            if (s_missingDhhAccessors)
            {
                return;
            }

            s_missingDhhAccessors = true;
            if (!s_warnedAccessorFailure && FeatureFlags.DebugLogging && LogLimiter.ShouldLog(DebugStateLogKey, 120))
            {
                s_warnedAccessorFailure = true;
                var message = ex?.InnerException?.Message ?? ex?.Message ?? "unknown";
                UnityEngine.Debug.LogWarning($"[LastChance] DHHSpectate helper failed: {message}");
            }
        }
    }

    [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
    internal static class SpectateCameraStateNormalLastChancePatch
    {
        [HarmonyPostfix]
        private static void Postfix(SpectateCamera __instance)
        {
            if (!FeatureFlags.LastChangeMode)
            {
                return;
            }

            if (!LastChanceTimerController.IsActive)
            {
                LastChanceSpectateHelper.ResetForceState();
                return;
            }

            if (!LastChanceSpectateHelper.AllPlayersDisabled())
            {
                LastChanceSpectateHelper.ResetForceState();
                return;
            }

            if (LastChanceSpectateHelper.ShouldForceLocalDeathHeadSpectate())
            {
                if (__instance != null)
                {
                    LastChanceSpectateHelper.EnsureSpectatePlayerLocal(__instance);
                }
                LastChanceSpectateHelper.ForceDeathHeadSpectateIfPossible();
            }
            LastChanceSpectateHelper.DebugLogState(__instance);
        }
    }

    [HarmonyPatch(typeof(SpectateCamera), "PlayerSwitch")]
    internal static class SpectateCameraPlayerSwitchLastChancePatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (!FeatureFlags.LastChangeMode)
            {
                return true;
            }

            if (LastChanceSpectateHelper.IsManualSwitchInputDown())
            {
                return true;
            }

            if (!LastChanceTimerController.IsActive)
            {
                return true;
            }

            if (!LastChanceSpectateHelper.AllPlayersDisabled())
            {
                return true;
            }

            if (LastChanceSpectateHelper.IsDeathHeadSpectated())
            {
                return false;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SpectateCamera), "UpdateState")]
    internal static class SpectateCameraUpdateStateLastChanceHeadBlockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SpectateCamera __instance, SpectateCamera.State _state)
        {
            if (!FeatureFlags.LastChangeMode || __instance == null)
            {
                return true;
            }

            if (_state != SpectateCamera.State.Head)
            {
                return true;
            }

            // During LastChance keep vanilla Head state disabled, even if disabled flags flicker.
            if (LastChanceTimerController.IsActive)
            {
                return false;
            }

            // Fallback: if all players are disabled outside active timer setup, keep old behavior.
            return !LastChanceSpectateHelper.AllPlayersDisabled();
        }
    }
}
