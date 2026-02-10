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
        private const BindingFlags AnyInstanceField = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? s_playerIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool>? s_playerIsDisabledGetter =
            s_playerIsDisabledField != null ? AccessTools.FieldRefAccess<PlayerAvatar, bool>(s_playerIsDisabledField.Name) : null;
        private static readonly FieldInfo? s_enemyParentSpawnedField = typeof(EnemyParent).GetField("Spawned", AnyInstanceField);
        private static readonly FieldInfo? s_enemyParentForceLeaveField = typeof(EnemyParent).GetField("forceLeave", AnyInstanceField);
        private static readonly FieldInfo? s_enemyParentEnemyField =
            typeof(EnemyParent).GetField("Enemy", AnyInstanceField) ??
            typeof(EnemyParent).GetField("enemy", AnyInstanceField);
        private static readonly ManualLogSource Log = Logger.CreateLogSource("DeathHeadHopperFix.LastChance.MonstersSearch");
        private static readonly HashSet<MethodBase> s_patchedMethods = new HashSet<MethodBase>();
        private static Harmony? s_harmony;
        private static bool s_assemblyLoadHooked;
        private static float s_runtimeStateCachedAt;
        private static bool s_runtimeStateEnabled;
        private static bool s_loggedActivationSnapshot;

        internal static void Apply(Harmony harmony, Assembly asm)
        {
            if (s_harmony != null || harmony == null || s_playerIsDisabledField == null)
            {
                return;
            }

            s_harmony = new Harmony(PatchId);
            PatchAllLoadedAssemblies();

            if (!s_assemblyLoadHooked)
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                s_assemblyLoadHooked = true;
            }
        }

        private static void PatchAllLoadedAssemblies()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                TryPatchAssembly(assemblies[i]);
            }
        }

        private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
        {
            TryPatchAssembly(args.LoadedAssembly);
        }

        private static void TryPatchAssembly(Assembly? asm)
        {
            if (asm == null || asm.IsDynamic || s_harmony == null || s_playerIsDisabledField == null)
            {
                return;
            }

            List<MethodBase> methods;
            try
            {
                methods = CollectEnemyMethodsUsingIsDisabled(asm);
            }
            catch
            {
                return;
            }

            if (methods.Count == 0)
            {
                return;
            }

            var transpiler = new HarmonyMethod(typeof(LastChanceMonstersSearchModule), nameof(ReplaceDisabledChecksTranspiler));
            var patchedNow = 0;
            for (var i = 0; i < methods.Count; i++)
            {
                var method = methods[i];
                if (method == null || s_patchedMethods.Contains(method))
                {
                    continue;
                }

                s_harmony.Patch(method, transpiler: transpiler);
                s_patchedMethods.Add(method);
                patchedNow++;
            }

            if (patchedNow > 0 && FeatureFlags.DebugLogging)
            {
                Log.LogInfo($"[LastChance] MonstersSearch patched methods in {asm.GetName().Name}: {patchedNow}.");
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
                if (type == null || !IsMonsterRelatedType(type))
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

        private static bool IsMonsterRelatedType(Type type)
        {
            if (typeof(Enemy).IsAssignableFrom(type) || typeof(EnemyParent).IsAssignableFrom(type))
            {
                return true;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = type.GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
            {
                var fieldType = fields[i].FieldType;
                if (typeof(Enemy).IsAssignableFrom(fieldType) || typeof(EnemyParent).IsAssignableFrom(fieldType))
                {
                    return true;
                }
            }

            var properties = type.GetProperties(flags);
            for (var i = 0; i < properties.Length; i++)
            {
                var propertyType = properties[i].PropertyType;
                if (typeof(Enemy).IsAssignableFrom(propertyType) || typeof(EnemyParent).IsAssignableFrom(propertyType))
                {
                    return true;
                }
            }

            return false;
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
                    var op = il[i];
                    if (op != 0x7B && op != 0x7C) // ldfld / ldflda
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
                // IL probe is optional and can fail on non-standard method bodies.
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
                if ((instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Ldflda) &&
                    instruction.operand is FieldInfo field &&
                    field == s_playerIsDisabledField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = remapMethod;
                }
            }

            return list;
        }

        private static bool RemapMonsterDisabledCheck(PlayerAvatar? player)
        {
            if (player == null || s_playerIsDisabledGetter == null)
            {
                return false;
            }

            if (IsMonstersSearchRuntimeEnabled())
            {
                return false;
            }

            return s_playerIsDisabledGetter(player);
        }

        private static bool IsMonstersSearchRuntimeEnabled()
        {
            // Fast cache for the hot enemy-AI path; refresh often enough for responsive toggles.
            var now = Time.unscaledTime;
            if (now - s_runtimeStateCachedAt < 0.1f)
            {
                return s_runtimeStateEnabled;
            }

            s_runtimeStateCachedAt = now;
            var wasEnabled = s_runtimeStateEnabled;
            s_runtimeStateEnabled =
                FeatureFlags.LastChanceMonstersSearchEnabled &&
                FeatureFlags.LastChangeMode &&
                LastChanceTimerController.IsActive;

            if (!s_runtimeStateEnabled)
            {
                s_loggedActivationSnapshot = false;
            }
            else if (!wasEnabled && !s_loggedActivationSnapshot)
            {
                TryLogActivationSnapshot();
            }

            return s_runtimeStateEnabled;
        }

        private static void TryLogActivationSnapshot()
        {
            if (!FeatureFlags.DebugLogging || !FeatureFlags.LastChanceMonstersSearchEnabled)
            {
                return;
            }

            s_loggedActivationSnapshot = true;

            var director = EnemyDirector.instance;
            if (director == null || director.enemiesSpawned == null)
            {
                Log.LogInfo("[LastChance] MonstersSearch activation snapshot: EnemyDirector/enemiesSpawned not available.");
                return;
            }

            var enemies = director.enemiesSpawned;
            Log.LogInfo($"[LastChance] MonstersSearch activation snapshot: total={enemies.Count}.");
            for (var i = 0; i < enemies.Count; i++)
            {
                var parent = enemies[i];
                if (parent == null)
                {
                    Log.LogInfo($"[LastChance] MonstersSearch enemy[{i}] = null");
                    continue;
                }

                var enemy = s_enemyParentEnemyField?.GetValue(parent);
                var typeName = GetConcreteEnemyTypeName(enemy);
                var spawned = s_enemyParentSpawnedField != null && s_enemyParentSpawnedField.GetValue(parent) is bool spawnedValue && spawnedValue;
                var forceLeave = s_enemyParentForceLeaveField != null && s_enemyParentForceLeaveField.GetValue(parent) is bool forceLeaveValue && forceLeaveValue;
                Log.LogInfo($"[LastChance] MonstersSearch enemy[{i}] type={typeName} spawned={spawned} forceLeave={forceLeave}");
            }
        }

        private static string GetConcreteEnemyTypeName(object? enemyObj)
        {
            if (enemyObj == null)
            {
                return "null";
            }

            if (enemyObj is not Component component)
            {
                return enemyObj.GetType().Name;
            }

            var baseName = enemyObj.GetType().Name;
            var behaviours = component.GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                var name = behaviour.GetType().Name;
                if (name.StartsWith("Enemy", StringComparison.Ordinal) && !string.Equals(name, "Enemy", StringComparison.Ordinal))
                {
                    return $"{baseName}/{name}";
                }
            }

            return baseName;
        }
    }
}
