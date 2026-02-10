using System.Collections.Generic;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate.Patches
{
    [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
    internal static class SpectateCameraStateNormalPatchMarker
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return SpectateCameraStateNormalPatch.Transpiler(instructions);
        }
    }
}
