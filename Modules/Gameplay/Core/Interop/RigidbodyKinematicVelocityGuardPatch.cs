#nullable enable

using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Interop
{
    [HarmonyPatch]
    internal static class RigidbodyKinematicVelocityGuardPatch
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.PhysicsGuard");

        [HarmonyPatch(typeof(Rigidbody), "set_velocity")]
        [HarmonyPrefix]
        private static bool Rigidbody_SetVelocity_Prefix(Rigidbody __instance)
        {
            return AllowVelocityWrite(__instance, "linear");
        }

        [HarmonyPatch(typeof(Rigidbody), "set_angularVelocity")]
        [HarmonyPrefix]
        private static bool Rigidbody_SetAngularVelocity_Prefix(Rigidbody __instance)
        {
            return AllowVelocityWrite(__instance, "angular");
        }

        private static bool AllowVelocityWrite(Rigidbody rb, string kind)
        {
            if (rb == null || !rb.isKinematic)
            {
                return true;
            }

            if (InternalDebugFlags.DebugPhysicsKinematicVelocityGuardLog &&
                LogLimiter.ShouldLog($"PhysicsGuard.{kind}.{rb.GetInstanceID()}", 120))
            {
                Log.LogInfo($"[PhysicsGuard] Skipped {kind} velocity write on kinematic body '{rb.name}' (id={rb.GetInstanceID()}).");
            }

            return false;
        }
    }
}
