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
            WarnUnsafeDebugFlagsInRelease();
            _harmony = new Harmony("AdrenSnyder.DeathHeadHopperFix");

            _harmony.PatchAll(typeof(Plugin).Assembly);

            DHHApiGuardModule.DetectGameApiChanges();
            ApplyEarlyPatches();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchIfTargetAssembly(asm);
        }

        private void OnDestroy()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
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
