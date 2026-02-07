#nullable enable

using System;
using System.Drawing;
using System.IO;
using BepInEx;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class ImageAssetLoader
    {
        internal static string GetDefaultAssetsDirectory()
        {
            return Path.Combine(Paths.PluginPath, "AdrenSnyder-DeathHeadHopperFix", "Assets");
        }

        internal static bool TryLoadTexture(string fileName, string? baseDirectory, out Texture2D? texture, out string resolvedPath)
        {
            texture = null;
            resolvedPath = string.Empty;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var root = string.IsNullOrWhiteSpace(baseDirectory) ? GetDefaultAssetsDirectory() : baseDirectory!;
            var path = Path.IsPathRooted(fileName) ? fileName : Path.Combine(root, fileName);
            if (!File.Exists(path))
            {
                return false;
            }

            Bitmap? bitmap = null;
            try
            {
                bitmap = new Bitmap(path);
                var width = bitmap.Width;
                var height = bitmap.Height;
                var pixels = new Color32[width * height];
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var c = bitmap.GetPixel(x, y);
                        var unityY = height - 1 - y;
                        pixels[(unityY * width) + x] = new Color32(c.R, c.G, c.B, c.A);
                    }
                }

                var loaded = new Texture2D(width, height, TextureFormat.RGBA32, false);
                loaded.SetPixels32(pixels);
                loaded.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                loaded.name = Path.GetFileNameWithoutExtension(path);
                texture = loaded;
                resolvedPath = path;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        internal static bool TryLoadSprite(
            string fileName,
            string? baseDirectory,
            out Sprite? sprite,
            out string resolvedPath,
            float pixelsPerUnit = 100f)
        {
            sprite = null;
            if (!TryLoadTexture(fileName, baseDirectory, out var texture, out resolvedPath) || texture == null)
            {
                return false;
            }

            var rect = new Rect(0f, 0f, texture.width, texture.height);
            var pivot = new Vector2(0.5f, 0.5f);
            sprite = Sprite.Create(texture, rect, pivot, pixelsPerUnit);
            sprite.name = texture.name;
            return true;
        }
    }
}
