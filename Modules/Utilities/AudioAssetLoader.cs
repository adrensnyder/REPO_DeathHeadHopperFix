#nullable enable

using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class AudioAssetLoader
    {
        internal static string GetDefaultAssetsDirectory()
        {
            return BundleAssetLoader.GetPluginDirectory();
        }

        internal static bool TryLoadAudioClip(string fileName, string? baseDirectory, out AudioClip? clip, out string resolvedPath)
        {
            clip = null;
            resolvedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (BundleAssetLoader.TryLoadAudioClip(fileName, out var bundledClip, out var bundledPath) && bundledClip != null)
            {
                clip = bundledClip;
                resolvedPath = bundledPath;
                return true;
            }

            return false;
        }
    }
}
