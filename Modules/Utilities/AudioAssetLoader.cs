#nullable enable

using System;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class AudioAssetLoader
    {
        private static readonly Type? WwWType = AccessTools.TypeByName("UnityEngine.WWW");
        private static readonly ConstructorInfo? WwWConstructor = WwWType?.GetConstructor(new[] { typeof(string) });
        private static readonly PropertyInfo? WwWIsDoneProperty = WwWType?.GetProperty("isDone", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo? WwWErrorProperty = WwWType?.GetProperty("error", BindingFlags.Instance | BindingFlags.Public);
        private static readonly MethodInfo? WwWGetAudioClipMethod = WwWType?.GetMethod("GetAudioClip", new[] { typeof(bool), typeof(bool), typeof(AudioType) });

        internal static string GetDefaultAssetsDirectory()
        {
            return Path.Combine(Paths.PluginPath, "AdrenSnyder-DeathHeadHopperFix", "Assets");
        }

        internal static bool TryLoadAudioClip(string fileName, string? baseDirectory, out AudioClip? clip, out string resolvedPath)
        {
            clip = null;
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

            try
            {
                if (!TryLoadLocalClip(path, out var loaded) || loaded == null)
                    return false;

                loaded.name = Path.GetFileNameWithoutExtension(path);
                clip = loaded;
                resolvedPath = path;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLoadLocalClip(string path, out AudioClip? clip)
        {
            clip = null;
            if (TryLoadWaveClip(path, out clip) && clip != null)
                return true;

            var audioType = GetAudioTypeFromExtension(path);
            if (audioType == AudioType.UNKNOWN)
                return false;

            return TryLoadClipViaWww(path, audioType, out clip);
        }

        private static bool TryLoadClipViaWww(string path, AudioType audioType, out AudioClip? clip)
        {
            clip = null;
            if (WwWType == null || WwWConstructor == null || WwWIsDoneProperty == null || WwWErrorProperty == null || WwWGetAudioClipMethod == null)
                return false;

            var uri = new Uri(path);
            var www = WwWConstructor.Invoke(new object[] { uri.AbsoluteUri });
            if (www == null)
                return false;

            try
            {
                while (!(WwWIsDoneProperty.GetValue(www) is bool done && done))
                {
                }

                var error = WwWErrorProperty.GetValue(www) as string;
                if (!string.IsNullOrEmpty(error))
                    return false;

                clip = WwWGetAudioClipMethod.Invoke(www, new object[] { false, false, audioType }) as AudioClip;
                return clip != null;
            }
            finally
            {
                if (www is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private static AudioType GetAudioTypeFromExtension(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension switch
            {
                ".wav" => AudioType.WAV,
                ".ogg" => AudioType.OGGVORBIS,
                ".mp3" => AudioType.MPEG,
                ".aiff" => AudioType.AIFF,
                ".aif" => AudioType.AIFF,
                ".mod" => AudioType.MOD,
                ".it" => AudioType.IT,
                ".s3m" => AudioType.S3M,
                ".xm" => AudioType.XM,
                _ => AudioType.UNKNOWN
            };
        }

        private static bool TryLoadWaveClip(string path, out AudioClip? clip)
        {
            clip = null;
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            if (extension != ".wav")
                return false;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44)
                return false;

            if (ReadAscii(bytes, 0, 4) != "RIFF" || ReadAscii(bytes, 8, 4) != "WAVE")
                return false;

            var offset = 12;
            var formatTag = (ushort)0;
            var channels = (ushort)0;
            var sampleRate = 0;
            var bitsPerSample = (ushort)0;
            byte[]? dataChunk = null;

            while (offset + 8 <= bytes.Length)
            {
                var chunkId = ReadAscii(bytes, offset, 4);
                var chunkSize = BitConverter.ToInt32(bytes, offset + 4);
                offset += 8;

                if (chunkSize < 0 || offset + chunkSize > bytes.Length)
                    return false;

                if (chunkId == "fmt ")
                {
                    if (chunkSize < 16)
                        return false;

                    formatTag = BitConverter.ToUInt16(bytes, offset + 0);
                    channels = BitConverter.ToUInt16(bytes, offset + 2);
                    sampleRate = BitConverter.ToInt32(bytes, offset + 4);
                    bitsPerSample = BitConverter.ToUInt16(bytes, offset + 14);
                }
                else if (chunkId == "data")
                {
                    dataChunk = new byte[chunkSize];
                    Buffer.BlockCopy(bytes, offset, dataChunk, 0, chunkSize);
                }

                offset += chunkSize;
                if ((chunkSize & 1) != 0 && offset < bytes.Length)
                    offset++;
            }

            if (dataChunk == null || channels == 0 || sampleRate <= 0)
                return false;

            var samples = DecodeSamples(dataChunk, formatTag, bitsPerSample, channels);
            if (samples == null || samples.Length == 0)
                return false;

            var sampleFrames = samples.Length / channels;
            if (sampleFrames <= 0)
                return false;

            clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), sampleFrames, channels, sampleRate, false);
            return clip.SetData(samples, 0);
        }

        private static string ReadAscii(byte[] bytes, int offset, int count)
        {
            return System.Text.Encoding.ASCII.GetString(bytes, offset, count);
        }

        private static float[]? DecodeSamples(byte[] data, ushort formatTag, ushort bitsPerSample, ushort channels)
        {
            if (channels == 0)
                return null;

            if (formatTag == 1) // PCM
            {
                if (bitsPerSample == 8)
                {
                    var result = new float[data.Length];
                    for (var i = 0; i < data.Length; i++)
                        result[i] = (data[i] - 128f) / 128f;
                    return result;
                }

                if (bitsPerSample == 16)
                {
                    var count = data.Length / 2;
                    var result = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        var sample = BitConverter.ToInt16(data, i * 2);
                        result[i] = sample / 32768f;
                    }
                    return result;
                }

                if (bitsPerSample == 24)
                {
                    var count = data.Length / 3;
                    var result = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        var baseIndex = i * 3;
                        var value = (data[baseIndex + 2] << 16) | (data[baseIndex + 1] << 8) | data[baseIndex];
                        if ((value & 0x800000) != 0)
                            value |= unchecked((int)0xFF000000);
                        result[i] = value / 8388608f;
                    }
                    return result;
                }

                if (bitsPerSample == 32)
                {
                    var count = data.Length / 4;
                    var result = new float[count];
                    for (var i = 0; i < count; i++)
                    {
                        var sample = BitConverter.ToInt32(data, i * 4);
                        result[i] = sample / 2147483648f;
                    }
                    return result;
                }
            }
            else if (formatTag == 3 && bitsPerSample == 32) // IEEE float
            {
                var count = data.Length / 4;
                var result = new float[count];
                for (var i = 0; i < count; i++)
                    result[i] = BitConverter.ToSingle(data, i * 4);
                return result;
            }

            return null;
        }
    }
}
