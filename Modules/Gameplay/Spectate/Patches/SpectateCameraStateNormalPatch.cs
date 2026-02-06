using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate.Patches
{
    internal static class SpectateCameraStateNormalPatch
    {
        private static readonly MethodInfo s_inputDown =
            AccessTools.Method(typeof(SemiFunc), "InputDown", (Type[])null, (Type[])null);

        private static readonly MethodInfo s_playerSwitch =
            AccessTools.Method(typeof(SpectateCamera), "PlayerSwitch", (Type[])null, (Type[])null);

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var targetIndex = codes.FindIndex(ci => CodeInstructionExtensions.Calls(ci, s_inputDown)) - 1;
            if (targetIndex <= 0 || codes[targetIndex].opcode != OpCodes.Ldc_I4_1)
            {
                return instructions;
            }

            var playerSwitchIndex = codes.FindIndex(targetIndex,
                ci => CodeInstructionExtensions.Calls(ci, s_playerSwitch));
            if (playerSwitchIndex == -1)
            {
                return instructions;
            }

            codes[targetIndex].opcode = OpCodes.Nop;
            codes.RemoveRange(targetIndex + 1, playerSwitchIndex - targetIndex);
            return codes.AsEnumerable();
        }
    }
}
