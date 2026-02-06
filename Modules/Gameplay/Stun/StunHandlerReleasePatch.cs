#nullable disable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using DeathHeadHopper.DeathHead.Handlers;

namespace DeathHeadHopperFix.Modules.Gameplay.Stun
{
    [HarmonyPatch(typeof(StunHandler), "HandleStun")]
    internal static class StunHandlerReleasePatch
    {
        private const int ReleaseObjectViewId = -1;

        private static readonly MethodInfo Replacement = AccessTools.Method(typeof(StunHandlerReleasePatch), nameof(CustomPhysObjectHurt));

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo target
                    && IsPhysObjectHurtCall(target))
                {
                    yield return new CodeInstruction(OpCodes.Call, Replacement);
                    continue;
                }

                yield return instruction;
            }
        }

        private static bool IsPhysObjectHurtCall(MethodInfo target)
        {
            if (target == null || target.Name != "PhysObjectHurt")
            {
                return false;
            }

            var parameters = target.GetParameters();
            if (parameters.Length != 3)
            {
                return false;
            }

            return parameters[0].ParameterType == typeof(PhysGrabObject)
                && parameters[1].ParameterType == typeof(HurtCollider.BreakImpact)
                && parameters[2].ParameterType == typeof(float);
        }

        public static void CustomPhysObjectHurt(StunHandler self, PhysGrabObject physGrabObject, HurtCollider.BreakImpact impact, float hitForce)
        {
            if (physGrabObject == null)
            {
                return;
            }

            switch (impact)
            {
                case HurtCollider.BreakImpact.Light:
                    physGrabObject.lightBreakImpulse = true;
                    break;
                case HurtCollider.BreakImpact.Medium:
                    physGrabObject.mediumBreakImpulse = true;
                    break;
                case HurtCollider.BreakImpact.Heavy:
                    physGrabObject.heavyBreakImpulse = true;
                    break;
            }

            if (hitForce >= 5f && physGrabObject.playerGrabbing.Count > 0)
            {
                foreach (PhysGrabber playerGrabber in physGrabObject.playerGrabbing.ToList())
                {
                    if (playerGrabber == null)
                    {
                        continue;
                    }

                    if (!SemiFunc.IsMultiplayer())
                    {
                        playerGrabber.ReleaseObjectRPC(true, 2f, ReleaseObjectViewId);
                    }
                    else
                    {
                        playerGrabber.photonView.RPC("ReleaseObjectRPC", RpcTarget.All, new object[]
                        {
                            false,
                            1f,
                            ReleaseObjectViewId
                        });
                    }
                }
            }
        }
    }
}
