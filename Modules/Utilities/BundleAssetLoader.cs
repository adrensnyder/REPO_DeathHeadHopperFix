#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using DeathHeadHopperFix.Modules.Config;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class BundleAssetLoader
    {
        private const string BundleFileName = "deathheadhopperfix";

        private static readonly object Sync = new();
        private static bool s_initialized;
        private static AssetBundle? s_bundle;
        private static string s_bundlePath = string.Empty;
        private static HashSet<string>? s_assetNames;
        private static readonly Dictionary<string, Sprite> SpriteCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> AudioCache = new(StringComparer.OrdinalIgnoreCase);
        private static Sprite[]? s_allSprites;
        private static AudioClip[]? s_allAudioClips;

        internal static bool EnsureBundleLoaded()
        {
            lock (Sync)
            {
                if (s_initialized)
                {
                    return s_bundle != null;
                }

                s_initialized = true;
                s_bundlePath = ResolveBundlePath();
                var pluginDir = GetPluginDirectory();
                var probePath = Path.IsPathRooted(s_bundlePath) ? s_bundlePath : Path.Combine(pluginDir, s_bundlePath);
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.Ensure", 240))
                {
                    Debug.Log($"[Bundle] EnsureBundleLoaded pluginDir='{pluginDir}' requestedPath='{s_bundlePath}' probePath='{probePath}'");
                }
                if (string.IsNullOrWhiteSpace(s_bundlePath) || !File.Exists(probePath))
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.NotFound", 240))
                    {
                        Debug.LogWarning($"[Bundle] Bundle file not found. requestedPath='{s_bundlePath}' probePath='{probePath}' pluginDir='{pluginDir}'");
                    }
                    return false;
                }

                try
                {
                    var previousCurrentDirectory = Environment.CurrentDirectory;
                    Environment.CurrentDirectory = pluginDir;
                    try
                    {
                        // Load using relative path from plugin DLL directory as requested.
                        s_bundle = AssetBundle.LoadFromFile(s_bundlePath);
                    }
                    finally
                    {
                        Environment.CurrentDirectory = previousCurrentDirectory;
                    }
                    if (s_bundle == null)
                    {
                        if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.LoadNull", 240))
                        {
                            Debug.LogWarning($"[Bundle] AssetBundle.LoadFromFile returned null for '{s_bundlePath}'.");
                        }
                        return false;
                    }

                    var names = s_bundle.GetAllAssetNames();
                    s_assetNames = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    LogBundleContents(names);
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.Loaded", 240))
                    {
                        Debug.Log($"[Bundle] Loaded bundle: {s_bundlePath} assets={s_assetNames.Count}");
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.LoadFailed", 240))
                    {
                        Debug.LogWarning($"[Bundle] Failed to load bundle: {s_bundlePath}. {ex}");
                    }
                    s_bundle = null;
                    s_assetNames = null;
                    return false;
                }
            }
        }

        internal static bool TryLoadSprite(string fileName, out Sprite? sprite, out string resolvedPath)
        {
            sprite = null;
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (SpriteCache.TryGetValue(fileName, out var cached) && cached != null)
            {
                sprite = cached;
                resolvedPath = $"bundle:{s_bundlePath}";
                return true;
            }

            if (!EnsureBundleLoaded() || s_bundle == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.SpriteUnavailable", 240))
                {
                    Debug.LogWarning($"[Bundle] Sprite load skipped: bundle unavailable. file='{fileName}'");
                }
                return false;
            }

            foreach (var candidate in EnumerateCandidates(fileName))
            {
                try
                {
                    var loadedSprite = s_bundle.LoadAsset<Sprite>(candidate);
                    if (loadedSprite != null)
                    {
                        loadedSprite.name = Path.GetFileNameWithoutExtension(fileName);
                        SpriteCache[fileName] = loadedSprite;
                        sprite = loadedSprite;
                        resolvedPath = $"bundle:{s_bundlePath}::{candidate}";
                        return true;
                    }

                    var texture = s_bundle.LoadAsset<Texture2D>(candidate);
                    if (texture == null)
                    {
                        continue;
                    }

                    var created = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    created.name = Path.GetFileNameWithoutExtension(fileName);
                    SpriteCache[fileName] = created;
                    sprite = created;
                    resolvedPath = $"bundle:{s_bundlePath}::{candidate}";
                    return true;
                }
                catch
                {
                    // Ignore and keep trying alternate candidates.
                }
            }

            // Some bundles expose stable asset object names but not predictable internal paths.
            var targetName = Path.GetFileNameWithoutExtension(fileName);
            foreach (var loadedSprite in GetAllSprites())
            {
                if (loadedSprite == null)
                {
                    continue;
                }

                if (!string.Equals(loadedSprite.name, targetName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(loadedSprite.name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SpriteCache[fileName] = loadedSprite;
                sprite = loadedSprite;
                resolvedPath = $"bundle:{s_bundlePath}::name:{loadedSprite.name}";
                return true;
            }

            return false;
        }

        internal static bool TryLoadAudioClip(string fileName, out AudioClip? clip, out string resolvedPath)
        {
            clip = null;
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (AudioCache.TryGetValue(fileName, out var cached) && cached != null)
            {
                clip = cached;
                resolvedPath = $"bundle:{s_bundlePath}";
                return true;
            }

            if (!EnsureBundleLoaded() || s_bundle == null)
            {
                if (FeatureFlags.DebugLogging && LogLimiter.ShouldLog("BundleAssetLoader.AudioUnavailable", 240))
                {
                    Debug.LogWarning($"[Bundle] Audio load skipped: bundle unavailable. file='{fileName}'");
                }
                return false;
            }

            foreach (var candidate in EnumerateCandidates(fileName))
            {
                try
                {
                    var loaded = s_bundle.LoadAsset<AudioClip>(candidate);
                    if (loaded == null)
                    {
                        continue;
                    }

                    loaded.name = Path.GetFileNameWithoutExtension(fileName);
                    AudioCache[fileName] = loaded;
                    clip = loaded;
                    resolvedPath = $"bundle:{s_bundlePath}::{candidate}";
                    return true;
                }
                catch
                {
                    // Ignore and keep trying alternate candidates.
                }
            }

            var targetName = Path.GetFileNameWithoutExtension(fileName);
            foreach (var loadedClip in GetAllAudioClips())
            {
                if (loadedClip == null)
                {
                    continue;
                }

                if (!string.Equals(loadedClip.name, targetName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(loadedClip.name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AudioCache[fileName] = loadedClip;
                clip = loadedClip;
                resolvedPath = $"bundle:{s_bundlePath}::name:{loadedClip.name}";
                return true;
            }

            return false;
        }

        private static Sprite[] GetAllSprites()
        {
            if (s_allSprites != null)
            {
                return s_allSprites;
            }

            if (!EnsureBundleLoaded() || s_bundle == null)
            {
                s_allSprites = Array.Empty<Sprite>();
                return s_allSprites;
            }

            try
            {
                s_allSprites = s_bundle.LoadAllAssets<Sprite>() ?? Array.Empty<Sprite>();
            }
            catch
            {
                s_allSprites = Array.Empty<Sprite>();
            }

            return s_allSprites;
        }

        private static AudioClip[] GetAllAudioClips()
        {
            if (s_allAudioClips != null)
            {
                return s_allAudioClips;
            }

            if (!EnsureBundleLoaded() || s_bundle == null)
            {
                s_allAudioClips = Array.Empty<AudioClip>();
                return s_allAudioClips;
            }

            try
            {
                s_allAudioClips = s_bundle.LoadAllAssets<AudioClip>() ?? Array.Empty<AudioClip>();
            }
            catch
            {
                s_allAudioClips = Array.Empty<AudioClip>();
            }

            return s_allAudioClips;
        }

        private static IEnumerable<string> EnumerateCandidates(string fileName)
        {
            var normalized = Normalize(fileName);
            if (s_assetNames == null || s_assetNames.Count == 0)
            {
                yield break;
            }

            foreach (var name in s_assetNames)
            {
                if (name.EndsWith(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    yield return name;
                }
            }
        }

        private static string ResolveBundlePath()
        {
            var dllDirectory = GetPluginDirectory();
            var direct = Path.Combine(dllDirectory, BundleFileName);
            if (File.Exists(direct))
            {
                return BundleFileName;
            }

            var repoBundle = direct + ".repobundle";
            if (File.Exists(repoBundle))
            {
                return BundleFileName + ".repobundle";
            }

            return BundleFileName;
        }

        internal static string GetPluginDirectory()
        {
            try
            {
                var codeBase = typeof(Plugin).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(codeBase))
                {
                    var dir = Path.GetDirectoryName(codeBase);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        return dir;
                    }
                }
            }
            catch
            {
                // Ignore and use fallback path.
            }

            return Path.Combine(Paths.PluginPath, "AdrenSnyder-DeathHeadHopperFix");
        }

        private static string Normalize(string fileName)
        {
            return fileName.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        }

        private static void LogBundleContents(string[]? names)
        {
            if (!FeatureFlags.DebugLogging)
            {
                return;
            }

            if (!LogLimiter.ShouldLog("BundleAssetLoader.ContentDump", 600))
            {
                return;
            }

            if (names == null || names.Length == 0)
            {
                Debug.LogWarning("[Bundle] Bundle loaded but GetAllAssetNames() returned empty.");
                return;
            }

            var folders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++)
            {
                var assetName = names[i] ?? string.Empty;
                var normalized = assetName.Replace('\\', '/').Trim('/');
                var folder = "(root)";
                var lastSlash = normalized.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    folder = normalized.Substring(0, lastSlash);
                }

                if (folders.TryGetValue(folder, out var count))
                {
                    folders[folder] = count + 1;
                }
                else
                {
                    folders[folder] = 1;
                }
            }

            Debug.Log($"[Bundle] Asset list start ({names.Length} entries)");
            for (var i = 0; i < names.Length; i++)
            {
                Debug.Log($"[Bundle] Asset[{i}]={names[i]}");
            }
            Debug.Log("[Bundle] Asset list end");

            Debug.Log($"[Bundle] Folder summary start ({folders.Count} groups)");
            foreach (var kvp in folders)
            {
                Debug.Log($"[Bundle] Folder='{kvp.Key}' Count={kvp.Value}");
            }
            Debug.Log("[Bundle] Folder summary end");
        }
    }
}
