#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance
{
    internal static class LastChanceMonstersSearchModule
    {
        private const string PatchId = "DeathHeadHopperFix.Gameplay.LastChance.MonstersSearch";
        private static readonly FieldInfo? s_playerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly FieldInfo? s_enemyParentSpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");
        private static readonly FieldInfo? s_enemyParentForceLeaveField = AccessTools.Field(typeof(EnemyParent), "forceLeave");
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.MonstersSearch");
        private static Harmony? s_harmony;

        internal static void Apply(Harmony harmony, Assembly asm)
        {
            if (s_harmony != null || harmony == null || asm == null || s_playerIsDisabledField == null)
            {
                return;
            }

            var methods = CollectEnemyMethodsUsingIsDisabled(asm);
            if (methods.Count == 0)
            {
                return;
            }

            s_harmony = new Harmony(PatchId);
            var transpiler = new HarmonyMethod(typeof(LastChanceMonstersSearchModule), nameof(ReplaceDisabledChecksTranspiler));
            var patched = 0;
            for (var i = 0; i < methods.Count; i++)
            {
                var method = methods[i];
                if (method == null)
                {
                    continue;
                }

                s_harmony.Patch(method, transpiler: transpiler);
                patched++;
            }

            if (FeatureFlags.DebugLogging)
            {
                Log.LogInfo($"[LastChance] MonstersSearch patched methods: {patched}.");
            }
        }

        internal static int GetAliveSearchMonsterCount()
        {
            if (!FeatureFlags.LastChanceMonstersSearchEnabled || !FeatureFlags.LastChangeMode || !LastChanceTimerController.IsActive)
            {
                return 0;
            }

            var director = EnemyDirector.instance;
            if (director == null || director.enemiesSpawned == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var enemyParent in director.enemiesSpawned)
            {
                if (enemyParent == null || !IsActiveEnemy(enemyParent))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private static bool IsActiveEnemy(EnemyParent enemyParent)
        {
            var spawned = s_enemyParentSpawnedField != null && s_enemyParentSpawnedField.GetValue(enemyParent) is bool spawnedValue && spawnedValue;
            if (!spawned)
            {
                return false;
            }

            var forceLeave = s_enemyParentForceLeaveField != null && s_enemyParentForceLeaveField.GetValue(enemyParent) is bool forceLeaveValue && forceLeaveValue;
            return !forceLeave;
        }

        private static List<MethodBase> CollectEnemyMethodsUsingIsDisabled(Assembly asm)
        {
            var methods = new List<MethodBase>();
            var types = asm.GetTypes();
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type == null || !type.Name.StartsWith("Enemy", StringComparison.Ordinal))
                {
                    continue;
                }

                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var typeMethods = type.GetMethods(flags);
                for (var j = 0; j < typeMethods.Length; j++)
                {
                    var method = typeMethods[j];
                    if (method == null || method.IsAbstract || method.GetMethodBody() == null)
                    {
                        continue;
                    }

                    if (!MethodReadsPlayerIsDisabled(method))
                    {
                        continue;
                    }

                    methods.Add(method);
                }
            }

            return methods;
        }

        private static bool MethodReadsPlayerIsDisabled(MethodBase method)
        {
            if (method == null || s_playerIsDisabledField == null)
            {
                return false;
            }

            try
            {
                var body = method.GetMethodBody();
                var il = body?.GetILAsByteArray();
                if (il == null || il.Length < 5)
                {
                    return false;
                }

                var targetToken = s_playerIsDisabledField.MetadataToken;
                for (var i = 0; i <= il.Length - 5; i++)
                {
                    if (il[i] != 0x7B)
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    if (token == targetToken)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignore: non-critical probe
            }

            return false;
        }

        private static IEnumerable<CodeInstruction> ReplaceDisabledChecksTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (s_playerIsDisabledField == null)
            {
                return instructions;
            }

            var list = new List<CodeInstruction>(instructions);
            var remapMethod = AccessTools.Method(typeof(LastChanceMonstersSearchModule), nameof(RemapMonsterDisabledCheck));
            if (remapMethod == null)
            {
                return list;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var instruction = list[i];
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field == s_playerIsDisabledField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = remapMethod;
                }
            }

            return list;
        }

        private static bool RemapMonsterDisabledCheck(PlayerAvatar? player)
        {
            if (player == null || s_playerIsDisabledField == null)
            {
                return false;
            }

            var isDisabled = s_playerIsDisabledField.GetValue(player) is bool value && value;
            if (!isDisabled)
            {
                return false;
            }

            if (!FeatureFlags.LastChanceMonstersSearchEnabled)
            {
                return true;
            }

            if (!FeatureFlags.LastChangeMode || !LastChanceTimerController.IsActive)
            {
                return true;
            }

            return false;
        }
    }
}
