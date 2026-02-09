#nullable enable

using BepInEx;
using BepInEx.Logging;
using DeathHeadHopperFix.Modules.Battery;
using DeathHeadHopperFix.Modules.Config;
using DeathHeadHopperFix.Modules.Gameplay.Core;
using DeathHeadHopperFix.Modules.Gameplay.LastChance;
using DeathHeadHopperFix.Modules.Gameplay.Spectate.Patches;
using DeathHeadHopperFix.Modules.Gameplay.Stun;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace DeathHeadHopperFix
{
    [HarmonyPatch(typeof(SpectateCamera), "StateNormal")]
    internal static class SpectateCameraStateNormalPatchMarker
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            => SpectateCameraStateNormalPatch.Transpiler(instructions);
    }

    [BepInPlugin("AdrenSnyder.DeathHeadHopperFix", "Death Head Hopper - Fix", "0.1.9")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private const string TargetAssemblyName = "DeathHeadHopper";

        private Harmony? _harmony;
        private bool _patched;
        private static ManualLogSource? _log;

        private void Awake()
        {
            _log = Logger;
            ConfigManager.Initialize(Config);
            AllPlayersDeadGuard.EnsureEnabled();
            _harmony = new Harmony("AdrenSnyder.DeathHeadHopperFix");

            _harmony.PatchAll(typeof(Plugin).Assembly);

            DHHApiGuardModule.DetectGameApiChanges();
            ApplyEarlyPatches();

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            SceneManager.sceneLoaded += OnSceneLoaded;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchIfTargetAssembly(asm);
        }

        private void OnDestroy()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryPatchIfTargetAssembly(args.LoadedAssembly);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            LastChanceTimerController.OnLevelLoaded();
            ConfigSyncManager.RequestHostSnapshotBroadcast();
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
                LastChanceMonstersSearchModule.Apply(harmony, asm);

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
    }
}
