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
using UnityEngine.SceneManagement;

namespace DeathHeadHopperFix
{
    [BepInPlugin("AdrenSnyder.DeathHeadHopperFix", "Death Head Hopper - Fix", "0.1.9")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string TargetAssemblyName = "DeathHeadHopper";

        private Harmony? _harmony;
        private bool _patched;
        private Assembly? _targetAssembly;
        private static ManualLogSource? _log;

        private void Awake()
        {
            _log = Logger;
            ConfigManager.Initialize(Config);
            BundleAssetLoader.EnsureBundleLoaded();
            WarnUnsafeDebugFlagsInRelease();
            AllPlayersDeadGuard.EnsureEnabled();
            _harmony = new Harmony("AdrenSnyder.DeathHeadHopperFix");

            _harmony.PatchAll(typeof(Plugin).Assembly);
            LastChanceTimerController.PrewarmGlobalAssetsAtBoot();

            DHHApiGuardModule.DetectGameApiChanges();
            ApplyEarlyPatches();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ConfigManager.HostControlledChanged += OnHostControlledChanged;
            LastChanceTimerController.ActiveStateChanged += OnLastChanceActiveStateChanged;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchIfTargetAssembly(asm);
        }

        private void OnDestroy()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ConfigManager.HostControlledChanged -= OnHostControlledChanged;
            LastChanceTimerController.ActiveStateChanged -= OnLastChanceActiveStateChanged;
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryPatchIfTargetAssembly(args.LoadedAssembly);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            LastChanceTimerController.OnLevelLoaded();
            ConfigSyncManager.RequestHostSnapshotBroadcast();
            ReconcileConditionalMonsterPatches();
        }

        private void OnHostControlledChanged()
        {
            LastChanceTimerController.OnHostControlledConfigChanged();
            ReconcileConditionalMonsterPatches();
        }

        private void OnLastChanceActiveStateChanged()
        {
            ReconcileConditionalMonsterPatches();
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
            if (_patched || asm == null)
                return;

            var name = asm.GetName().Name;
            if (!string.Equals(name, TargetAssemblyName, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                _log?.LogInfo($"Detected {TargetAssemblyName} assembly load. Applying patches...");

                var harmony = _harmony;
                if (harmony == null)
                    throw new InvalidOperationException("Harmony instance is null.");

                PrefabModule.Apply(harmony, asm, _log);
                AudioModule.Apply(harmony, asm, _log);
                DHHShopModule.Apply(harmony, asm, _log);
                _targetAssembly = asm;
                ReconcileConditionalMonsterPatches();
                DHHApiGuardModule.Apply(harmony, asm);
                BatteryJumpPatchModule.Apply(harmony, asm);
                JumpForceModule.Apply(harmony, asm, _log);
                ChargeAbilityTuningModule.Apply(harmony, asm);
                ChargeHoldReleaseModule.Apply(harmony, asm, _log);

                InputModule.Apply(harmony, asm, _log);

                AbilityModule.ApplyAbilitySpotLabelOverlay(harmony, asm);
                AbilityModule.ApplyAbilityManagerHooks(harmony, asm);

                _patched = true;
                _log?.LogInfo("Patches applied successfully.");
            }
            catch (Exception ex)
            {
                _log?.LogError(ex);
            }
        }

        private void ReconcileConditionalMonsterPatches()
        {
            var harmony = _harmony;
            var asm = _targetAssembly;
            if (harmony == null || asm == null)
            {
                return;
            }

            var enableMonsterPipelinePatches =
                FeatureFlags.LastChangeMode &&
                FeatureFlags.LastChanceMonstersSearchEnabled &&
                LastChanceTimerController.IsActive;

            if (enableMonsterPipelinePatches)
            {
                LastChanceMonstersSearchModule.Apply(harmony, asm);
                LastChanceMonstersNoiseAggroModule.Apply(harmony, asm);
                LastChanceMonstersCameraForceLockModule.Apply();
                LastChanceMonstersPlayerVisionCheckModule.Apply();
                return;
            }

            LastChanceMonstersNoiseAggroModule.Unapply();
            LastChanceMonstersSearchModule.Unapply();
            LastChanceMonstersCameraForceLockModule.Unapply();
            LastChanceMonstersPlayerVisionCheckModule.Unapply();
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
