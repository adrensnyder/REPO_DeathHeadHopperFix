#nullable enable

using System;
using System.Reflection;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    internal static class DHHApiGuardModule
    {
        private static bool s_guardMissingLocalCameraPosition;

        internal static void DetectGameApiChanges()
        {
            try
            {
                var tPlayerAvatar = AccessTools.TypeByName("PlayerAvatar");
                if (tPlayerAvatar == null)
                {
                    s_guardMissingLocalCameraPosition = true;
                    return;
                }

                var field = tPlayerAvatar.GetField("localCameraPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                s_guardMissingLocalCameraPosition = field == null;
            }
            catch
            {
                s_guardMissingLocalCameraPosition = true;
            }
        }

        internal static void Apply(Harmony harmony, Assembly asm)
        {
            if (!s_guardMissingLocalCameraPosition)
                return;

            var controllerType = asm.GetType("DeathHeadHopper.DeathHead.DeathHeadController", throwOnError: false);
            if (controllerType == null)
                return;

            var cancelMethod = AccessTools.Method(controllerType, "CancelReviveInCart");
            if (cancelMethod != null)
            {
                var prefix = typeof(DHHApiGuardModule).GetMethod(nameof(DeathHeadController_CancelReviveInCart_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix != null)
                    harmony.Patch(cancelMethod, prefix: new HarmonyMethod(prefix));
            }

            var updateMethod = AccessTools.Method(controllerType, "Update");
            if (updateMethod != null)
            {
                var finalizer = typeof(DHHApiGuardModule).GetMethod(nameof(DeathHeadController_Update_Finalizer), BindingFlags.Static | BindingFlags.NonPublic);
                if (finalizer != null)
                    harmony.Patch(updateMethod, finalizer: new HarmonyMethod(finalizer));
            }
        }

        private static bool DeathHeadController_CancelReviveInCart_Prefix()
        {
            return !s_guardMissingLocalCameraPosition;
        }

        private static Exception? DeathHeadController_Update_Finalizer(Exception? __exception)
        {
            if (__exception == null)
                return null;

            if (s_guardMissingLocalCameraPosition && __exception is MissingFieldException)
                return null;

            return __exception;
        }
    }
}

