#nullable enable

using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class ImageAssetLoader
    {
        internal static string GetDefaultAssetsDirectory()
        {
            return BundleAssetLoader.GetPluginDirectory();
        }

        internal static bool TryLoadTexture(string fileName, string? baseDirectory, out Texture2D? texture, out string resolvedPath)
        {
            texture = null;
            resolvedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (BundleAssetLoader.TryLoadSprite(fileName, out var bundledSprite, out var bundledPath) &&
                bundledSprite != null &&
                bundledSprite.texture != null)
            {
                texture = bundledSprite.texture;
                resolvedPath = bundledPath;
                return true;
            }
            return false;
        }

        internal static bool TryLoadSprite(
            string fileName,
            string? baseDirectory,
            out Sprite? sprite,
            out string resolvedPath,
            float pixelsPerUnit = 100f)
        {
            sprite = null;
            if (BundleAssetLoader.TryLoadSprite(fileName, out var bundledSprite, out resolvedPath) && bundledSprite != null)
            {
                sprite = bundledSprite;
                return true;
            }

            return false;
        }
    }
}
