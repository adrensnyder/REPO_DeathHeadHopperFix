#nullable enable

using BepInEx;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core.Abilities;
using DeathHeadHopperFix.Modules.Gameplay.Core.Audio;
using DeathHeadHopperFix.Modules.Gameplay.Core.Bootstrap;
using DeathHeadHopperFix.Modules.Gameplay.Core.Input;
using DeathHeadHopperFix.Modules.Gameplay.Core.Interop;
using DeathHeadHopperFix.Modules.Gameplay.Core.Runtime;
using DeathHeadHopperFix.Modules.Gameplay.Stun;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using System;
using System.Reflection;

namespace DeathHeadHopperFix
{
    [BepInPlugin("AdrenSnyder.DeathHeadHopperFix", "Death Head Hopper - Fix", "0.2.8")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string TargetAssemblyName = "DeathHeadHopper";

        internal static Plugin? Instance { get; private set; }

        private Harmony? _harmony;
        private bool _patched;
        private bool _patchAttempted;
        private Assembly? _targetAssembly;
        private static ManualLogSource? _log;

        private void Awake()
        {
            Instance = this;
            _log = Logger;
            ConfigManager.Initialize(Config, Logger);
            WarnUnsafeDebugFlagsInRelease();
            _harmony = new Harmony("AdrenSnyder.DeathHeadHopperFix");
            DhhPatchRegistryModule.Initialize(Logger);

            if (FeatureFlags.DebugLogging)
                Logger.LogInfo("[Fix:StartupDiag] Fix Harmony.PatchAll begin.");

            try
            {
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception ex)
            {
                Logger.LogError("[Fix:StartupDiag] Fix Harmony.PatchAll failed.");
                Logger.LogError(ex);
                throw;
            }

            if (FeatureFlags.DebugLogging)
                Logger.LogInfo("[Fix:StartupDiag] Fix Harmony.PatchAll complete.");

            DHHApiGuardModule.DetectGameApiChanges();
            ApplyEarlyPatches();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchIfTargetAssembly(asm);
        }

        private void OnDestroy()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            if (Instance == this)
                Instance = null;
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryPatchIfTargetAssembly(args.LoadedAssembly);
        }

        private void ApplyEarlyPatches()
        {
            if (_harmony == null)
                return;

            StatsModule.ApplyHooks(_harmony);
            ItemUpgradeModule.Apply(_harmony);
        }

        private void TryPatchIfTargetAssembly(Assembly asm)
        {
            if (_patched || _patchAttempted || asm == null)
                return;

            var name = asm.GetName().Name;
            if (!string.Equals(name, TargetAssemblyName, StringComparison.OrdinalIgnoreCase))
                return;

            _patchAttempted = true;
            try
            {
                _log?.LogInfo($"Detected {TargetAssemblyName} assembly load. Applying patches...");

                var harmony = _harmony;
                if (harmony == null)
                    throw new InvalidOperationException("Harmony instance is null.");

                DHHStatsBootstrapModule.Apply(harmony, asm, _log);
                PrefabModule.Apply(harmony, asm, _log);
                AudioModule.Apply(harmony, asm, _log);
                var shopHooksApplied = DHHShopVanillaPoolModule.Apply(harmony, asm, _log);
                _targetAssembly = asm;
                DHHApiGuardModule.Apply(harmony, asm);
                BatteryJumpPatchModule.Apply(harmony, _log);
                JumpForceModule.Apply(harmony, _log);
                ChargeAbilityTuningModule.Apply(harmony, asm);
                ChargeHoldReleaseModule.Apply(harmony, asm, _log);

                InputModule.Apply(harmony, asm, _log);

                AbilityModule.ApplyAbilitySpotLabelOverlay(harmony);
                AbilityModule.ApplyAbilityManagerHooks(harmony);

                _patched = true;
                if (shopHooksApplied)
                    _log?.LogInfo("Patches applied successfully.");
                else
                    _log?.LogWarning("DHH patch setup completed with incomplete shop hook registration; see [Fix:Shop] messages.");
            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }
        }

        private void WarnUnsafeDebugFlagsInRelease()
        {
            if (UnityEngine.Debug.isDebugBuild)
            {
                return;
            }

            if (!InternalDebugFlags.DisableBatteryModule &&
                !InternalDebugFlags.DisableAbilityPatches &&
                !InternalDebugFlags.DisableSpectateChecks)
            {
                return;
            }

            _log?.LogWarning(
                "[DebugSafety] Internal debug bypass flags are enabled in a non-debug build. " +
                $"DisableBatteryModule={InternalDebugFlags.DisableBatteryModule}, " +
                $"DisableAbilityPatches={InternalDebugFlags.DisableAbilityPatches}, " +
                $"DisableSpectateChecks={InternalDebugFlags.DisableSpectateChecks}");
        }
    }
}
