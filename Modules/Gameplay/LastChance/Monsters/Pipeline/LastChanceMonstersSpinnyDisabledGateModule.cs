#nullable enable

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Adapters;
using DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Support;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.Monsters.Pipeline
{
    [HarmonyPatch]
    internal static class LastChanceMonstersSpinnyDisabledGateModule
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.Spinny");
        private static readonly FieldInfo? PlayerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");

        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod()
        {
            var type = AccessTools.TypeByName("EnemySpinny");
            return type == null ? null : AccessTools.Method(type, "Update");
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (PlayerIsDisabledField == null)
            {
                return instructions;
            }

            var remapMethod = AccessTools.Method(typeof(LastChanceMonstersSpinnyDisabledGateModule), nameof(RemapSpinnyDisabledCheck));
            if (remapMethod == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            for (var i = 0; i < list.Count; i++)
            {
                var instruction = list[i];
                if ((instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldflda) &&
                    instruction.operand is FieldInfo field &&
                    field == PlayerIsDisabledField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = remapMethod;
                }
            }

            return list;
        }

        private static bool RemapSpinnyDisabledCheck(PlayerAvatar? player)
        {
            if (player == null)
            {
                return true;
            }

            var isDisabled = LastChanceMonstersTargetProxyHelper.IsDisabled(player);
            if (!isDisabled)
            {
                return false;
            }

            if (LastChanceMonstersDisabledGateHelper.ShouldTreatDisabledAsActive(player))
            {
                DebugLog("DisabledCheck.OverrideFalse", $"player={GetPlayerId(player)} rawDisabled={isDisabled}");
                return false;
            }

            return true;
        }

        private static void DebugLog(string reason, string detail)
        {
            if (!InternalDebugFlags.DebugLastChanceSpinnyFlow)
            {
                return;
            }

            if (!InternalDebugFlags.DebugLastChanceSpinnyVerbose && !LogLimiter.ShouldLog($"Spinny.DisabledGate.{reason}", 10))
            {
                return;
            }

            Log.LogInfo($"[Spinny][{reason}] {detail}");
        }

        private static int GetPlayerId(PlayerAvatar player)
        {
            return player.photonView != null ? player.photonView.ViewID : player.GetInstanceID();
        }
    }
}
