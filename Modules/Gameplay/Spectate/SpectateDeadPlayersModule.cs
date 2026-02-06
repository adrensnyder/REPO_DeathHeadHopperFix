#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using DeathHeadHopperFix.Modules.Config;

namespace DeathHeadHopperFix.Modules.Gameplay.Spectate
{
    internal static class SpectateDeadPlayersModule
    {
        private const string ModuleId = "DeathHeadHopperFix.Spectate.DeadPlayers";

        private static readonly FieldInfo? s_playerIsDisabledField =
            AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly MethodInfo? s_shouldSkipMethod =
            AccessTools.Method(typeof(SpectateDeadPlayersModule), nameof(ShouldSkipSpectateTarget),
                Array.Empty<Type>(), Array.Empty<Type>());

        private static bool s_applied;

        internal static void Apply(Harmony harmony)
        {
            if (s_applied || harmony == null)
            {
                return;
            }

            var target = AccessTools.Method(typeof(SpectateCamera), "PlayerSwitch", Array.Empty<Type>(), Array.Empty<Type>());
            if (target == null || s_playerIsDisabledField == null || s_shouldSkipMethod == null)
            {
                return;
            }

            var transpiler = AccessTools.Method(typeof(SpectateDeadPlayersModule), nameof(PlayerSwitchTranspiler),
                new[] { typeof(IEnumerable<CodeInstruction>) }, Array.Empty<Type>());
            if (transpiler == null)
            {
                return;
            }

            harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
            s_applied = true;
        }

        private static IEnumerable<CodeInstruction> PlayerSwitchTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (s_playerIsDisabledField == null || s_shouldSkipMethod == null)
            {
                return instructions;
            }

            var codes = instructions.ToList();
            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo field && field == s_playerIsDisabledField)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call, s_shouldSkipMethod);
                    break;
                }
            }

            return codes;
        }

        private static bool ShouldSkipSpectateTarget(PlayerAvatar player)
        {
            if (player == null)
            {
                return true;
            }

            if (FeatureFlags.SpectateDeadPlayers)
            {
                return false;
            }

            if (s_playerIsDisabledField == null)
            {
                return true;
            }

            return s_playerIsDisabledField.GetValue(player) is bool disabled && disabled;
        }
    }
}
