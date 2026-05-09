#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DeathHeadHopper.DeathHead.Handlers;
using HarmonyLib;
using Photon.Pun;

namespace DeathHeadHopperFix.Modules.Gameplay.Stun
{
    [HarmonyPatch(typeof(StunHandler), nameof(StunHandler.HandleStun))]
    internal static class StunHandlerReleasePatch
    {
        private const int ReleaseObjectViewId = -1;
        private static readonly MethodInfo? s_targetCall = AccessTools.Method(
            typeof(StunHandler),
            nameof(StunHandler.PhysObjectHurt),
            new[] { typeof(PhysGrabObject), typeof(HurtCollider.BreakImpact), typeof(float) });

        private static readonly MethodInfo? s_replacement = AccessTools.Method(
            typeof(StunHandlerReleasePatch),
            nameof(CustomPhysObjectHurt),
            new[] { typeof(StunHandler), typeof(PhysGrabObject), typeof(HurtCollider.BreakImpact), typeof(float) });

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (s_targetCall != null && s_replacement != null && instruction.Calls(s_targetCall))
                {
                    yield return new CodeInstruction(OpCodes.Call, s_replacement);
                    continue;
                }

                yield return instruction;
            }
        }

        private static void CustomPhysObjectHurt(StunHandler self, PhysGrabObject physGrabObject, HurtCollider.BreakImpact impact, float hitForce)
        {
            if (physGrabObject == null)
                return;

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
                foreach (var playerGrabber in physGrabObject.playerGrabbing.ToList())
                {
                    if (playerGrabber == null)
                        continue;

                    if (!SemiFunc.IsMultiplayer())
                    {
                        playerGrabber.ReleaseObjectRPC(true, 2f, ReleaseObjectViewId);
                    }
                    else
                    {
                        playerGrabber.photonView.RPC("ReleaseObjectRPC", RpcTarget.All, false, 1f, ReleaseObjectViewId);
                    }
                }
            }
        }
    }
}
