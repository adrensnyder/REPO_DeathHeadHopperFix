#nullable enable

using System;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Config;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Gameplay.Core.Bootstrap
{
    internal static class DhhPatchRegistryModule
    {
        private sealed class HookRegistration
        {
            internal HookRegistration(string name, Type patchType, int hookCount)
            {
                Name = name;
                PatchType = patchType;
                HookCount = hookCount;
            }

            internal string Name { get; }
            internal Type PatchType { get; }
            internal int HookCount { get; }
        }

        private static readonly HookRegistration[] Registrations =
        {
            new("GameDirector.gameStateLoad", typeof(GameDirectorGameStateLoadPatch), 1),
            new("GameDirector.gameStateStart", typeof(GameDirectorGameStateStartPatch), 1),
            new("GameDirector.Revive", typeof(GameDirectorRevivePatch), 1),
            new("SemiIconMaker.CreateIconFromRenderTexture", typeof(SemiIconMakerCreateIconPatch), 1),
            new(
                "SpectateCameraPatch: LateUpdate, StateNormal, PlayerSwitch and StateNormal transpilers",
                typeof(global::DeathHeadHopper.Patches.SpectateCameraPatch),
                4)
        };

        private static ManualLogSource? s_log;
        private static bool s_registeringBridgePatch;
        private static bool s_registryStarted;

        internal static void Initialize(ManualLogSource log)
        {
            s_log = log;
        }

        internal static bool ShouldProcessBridgePatch => s_registeringBridgePatch;

        internal static void RegisterSelectedHooks(global::DeathHeadHopper.DeathHeadHopper? plugin)
        {
            if (s_registryStarted)
            {
                LogDebug("DHH.Patch was called again; the selected registry has already been attempted.");
                return;
            }

            s_registryStarted = true;
            if (plugin == null)
            {
                s_log?.LogError("[Fix:DHHRegistry] DHH.PatchAll was suppressed, but the plugin instance was unavailable; no DHH hooks were registered.");
                return;
            }

            try
            {
                var harmony = plugin.Harmony;
                if (harmony == null)
                {
                    harmony = new Harmony(plugin.Info.Metadata.GUID);
                    plugin.Harmony = harmony;
                }

                s_log?.LogInfo(
                    "[Fix:DHHRegistry] Replacing DHH assembly-wide PatchAll with an explicit hook registry. " +
                    "The legacy custom-shop and item-dictionary patches are excluded; the Fix owns the vanilla item pool.");

                var installedGroups = 0;
                var registeredHooks = 0;
                var failedGroups = 0;
                foreach (var registration in Registrations)
                {
                    if (FeatureFlags.DebugLogging)
                        LogDebug($"hook={registration.Name} stage=register.begin");

                    try
                    {
                        s_registeringBridgePatch = true;
                        harmony.CreateClassProcessor(registration.PatchType).Patch();
                        installedGroups++;
                        registeredHooks += registration.HookCount;
                        if (FeatureFlags.DebugLogging)
                            LogDebug($"hook={registration.Name} stage=register.complete");
                    }
                    catch (Exception ex)
                    {
                        failedGroups++;
                        s_log?.LogWarning(
                            $"[Fix:DHHRegistry] Could not register DHH hook '{registration.Name}': " +
                            $"{ex.GetType().Name}: {ex.Message}.");
                        if (FeatureFlags.DebugLogging)
                        {
                            try
                            {
                                s_log?.LogError(ex);
                            }
                            catch
                            {
                                // Diagnostics must not alter patch registration.
                            }
                        }
                    }
                    finally
                    {
                        s_registeringBridgePatch = false;
                    }
                }

                s_log?.LogInfo(
                    $"[Fix:DHHRegistry] Explicit DHH hook registration finished: installedGroups={installedGroups}, " +
                    $"failedGroups={failedGroups}, registeredHooks={registeredHooks}, " +
                    $"totalGroups={Registrations.Length}, totalHooks=8. Skipped legacy hooks: PunManagerPatch.ShopPopulateItemVolumes, " +
                    "ShopManagerPatch custom-shelf/item-removal hooks, LevelGeneratorPatch.ItemSetup.");

                if (failedGroups > 0)
                    s_log?.LogWarning("[Fix:DHHRegistry] DHH is partially initialized; inspect the failed hook entries above.");
            }
            catch (Exception ex)
            {
                s_registeringBridgePatch = false;
                s_log?.LogError(
                    $"[Fix:DHHRegistry] DHH.PatchAll was suppressed, but registry preparation failed: " +
                    $"{ex.GetType().Name}: {ex.Message}.");
                if (FeatureFlags.DebugLogging)
                {
                    try
                    {
                        s_log?.LogError(ex);
                    }
                    catch
                    {
                        // Diagnostics must not alter DHH initialization.
                    }
                }
            }
        }

        private static void LogDebug(string message)
        {
            if (!FeatureFlags.DebugLogging)
                return;

            try
            {
                s_log?.LogInfo($"[Fix:DHHRegistryDiag] {message}");
            }
            catch
            {
                // Diagnostics must not alter DHH initialization.
            }
        }

    }

    [HarmonyPatch(typeof(global::DeathHeadHopper.DeathHeadHopper), nameof(global::DeathHeadHopper.DeathHeadHopper.Patch))]
    internal static class DhhPatchRegistryInterceptor
    {
        [HarmonyPrefix]
        private static bool Prefix(global::DeathHeadHopper.DeathHeadHopper __instance)
        {
            DhhPatchRegistryModule.RegisterSelectedHooks(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.gameStateLoad))]
    internal static class GameDirectorGameStateLoadPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => DhhPatchRegistryModule.ShouldProcessBridgePatch;

        [HarmonyPrefix]
        private static void Prefix() => global::DeathHeadHopper.Patches.GameDirectorPatch.GameStateLoad_Prefix();
    }

    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.gameStateStart))]
    internal static class GameDirectorGameStateStartPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => DhhPatchRegistryModule.ShouldProcessBridgePatch;

        [HarmonyPostfix]
        private static void Postfix() => global::DeathHeadHopper.Patches.GameDirectorPatch.GameStateStart_Postfix();
    }

    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.Revive))]
    internal static class GameDirectorRevivePatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => DhhPatchRegistryModule.ShouldProcessBridgePatch;

        [HarmonyPrefix]
        private static void Prefix() => global::DeathHeadHopper.Patches.GameDirectorPatch.Revive_Prefix();
    }

    [HarmonyPatch(typeof(SemiIconMaker), nameof(SemiIconMaker.CreateIconFromRenderTexture), typeof(string), typeof(Action<byte[]>))]
    internal static class SemiIconMakerCreateIconPatch
    {
        [HarmonyPrepare]
        private static bool Prepare() => DhhPatchRegistryModule.ShouldProcessBridgePatch;

        [HarmonyPrefix]
        private static void Prefix(SemiIconMaker __instance)
        {
            if (__instance == null || __instance.iconCamera == null)
                return;

            global::DeathHeadHopper.Patches.SemiIconMakerPatch.CreateIconFromRenderTexture_Prefix(__instance);
        }
    }

}
