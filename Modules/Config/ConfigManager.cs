#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Config
{
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class FeatureConfigEntryAttribute : Attribute
    {
        public FeatureConfigEntryAttribute(string section, string description)
        {
            Section = section;
            Description = description;
        }

        public string Section { get; }
        public string Description { get; }
        public string Key { get; set; } = string.Empty;
        public float Min { get; set; } = float.NaN;
        public float Max { get; set; } = float.NaN;
        public bool HostControlled { get; set; } = true;

        public bool HasRange => !float.IsNaN(Min) && !float.IsNaN(Max);
    }

    internal static class ConfigManager
    {
        private struct RangeF { public float Min, Max; }
        private struct RangeI { public int Min, Max; }

        private static bool s_initialized;
        private static readonly char[] ColorSeparators = { ',', ';' };
        private static readonly Dictionary<string, RangeF> s_floatRanges = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, RangeI> s_intRanges = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, FieldInfo> s_hostControlledFields = new(StringComparer.Ordinal);

        internal static event Action? HostControlledChanged;

        internal static void Initialize(ConfigFile config)
        {
            if (s_initialized || config == null)
            {
                return;
            }

            s_initialized = true;
            BindConfigEntries(config, typeof(FeatureFlags), "General");
        }

        private static void BindConfigEntries(ConfigFile config, Type targetType, string defaultSection)
        {
            foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = field.GetCustomAttribute<FeatureConfigEntryAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                var section = string.IsNullOrWhiteSpace(attribute.Section) ? defaultSection : attribute.Section;
                var key = string.IsNullOrWhiteSpace(attribute.Key) ? field.Name : attribute.Key;
                var rangeKey = BuildRangeKey(section, key);
                var description = attribute.Description ?? string.Empty;

                if (field.FieldType == typeof(bool))
                {
                    var defaultValue = (bool)field.GetValue(null)!;
                    var entry = config.Bind(section, key, defaultValue, description);
                    RegisterHostControlledField(attribute, key, field);
                    ApplyAndWatch(entry, rangeKey, value => field.SetValue(null, value), attribute.HostControlled);
                    continue;
                }

                if (field.FieldType == typeof(int))
                {
                    var defaultValue = (int)field.GetValue(null)!;
                    ConfigEntry<int> entry;
                    if (attribute.HasRange)
                    {
                        var min = GetIntRangeStart(attribute);
                        var max = GetIntRangeEnd(attribute);
                        entry = config.Bind(section, key, defaultValue,
                            new ConfigDescription(description, new AcceptableValueRange<int>(min, max)));
                        RegisterIntRange(rangeKey, min, max);
                    }
                    else
                    {
                        entry = config.Bind(section, key, defaultValue, description);
                    }

                    ApplyAndWatch(entry, rangeKey, value => field.SetValue(null, value), attribute.HostControlled);
                    RegisterHostControlledField(attribute, key, field);
                    continue;
                }

                if (field.FieldType == typeof(float))
                {
                    var defaultValue = (float)field.GetValue(null)!;
                    ConfigEntry<float> entry;
                    if (attribute.HasRange)
                    {
                        var min = Math.Min(attribute.Min, attribute.Max);
                        var max = Math.Max(attribute.Min, attribute.Max);
                        entry = config.Bind(section, key, defaultValue,
                            new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
                        RegisterFloatRange(rangeKey, min, max);
                    }
                    else
                    {
                        entry = config.Bind(section, key, defaultValue, description);
                    }

                    ApplyAndWatch(entry, rangeKey, value => field.SetValue(null, value), attribute.HostControlled);
                    RegisterHostControlledField(attribute, key, field);
                    continue;
                }

                if (field.FieldType == typeof(string))
                {
                    var defaultValue = field.GetValue(null) as string ?? string.Empty;
                    var entry = config.Bind(section, key, defaultValue, description);
                    RegisterHostControlledField(attribute, key, field);
                    ApplyAndWatch(entry, rangeKey, value => field.SetValue(null, value), attribute.HostControlled);
                    continue;
                }

                if (field.FieldType == typeof(Color))
                {
                    var defaultValue = (Color)field.GetValue(null)!;
                    var entry = config.Bind(section, key, ColorToString(defaultValue), description);
                    RegisterHostControlledField(attribute, key, field);
                    ApplyAndWatch(entry, ColorFromString, value => field.SetValue(null, value), attribute.HostControlled);
                    continue;
                }
            }
        }

        private static int GetIntRangeStart(FeatureConfigEntryAttribute attribute)
        {
            ValidateIntegerRange(attribute);
            return (int)Math.Min(attribute.Min, attribute.Max);
        }

        private static int GetIntRangeEnd(FeatureConfigEntryAttribute attribute)
        {
            ValidateIntegerRange(attribute);
            return (int)Math.Max(attribute.Min, attribute.Max);
        }

        private static void ValidateIntegerRange(FeatureConfigEntryAttribute attribute)
        {
            if (!IsWholeNumber(attribute.Min) || !IsWholeNumber(attribute.Max))
            {
                throw new InvalidOperationException("FeatureConfigEntryAttribute integer range values must be whole numbers.");
            }
        }

        private static bool IsWholeNumber(float value)
        {
            double truncated = Math.Truncate(value);
            return Math.Abs(value - truncated) < float.Epsilon;
        }

        private static void ApplyAndWatch<T>(ConfigEntry<T> entry, string rangeKey, Action<T> setter, bool notifyHostControlled)
        {
            if (entry == null || setter == null)
            {
                return;
            }

            void Update()
            {
                setter(SanitizeValue(entry.Value, rangeKey));
                if (notifyHostControlled)
                {
                    HostControlledChanged?.Invoke();
                }
            }

            Update();
            entry.SettingChanged += (_, _) => Update();
        }

        private static void ApplyAndWatch(ConfigEntry<string> entry, Func<string, Color> parser, Action<Color> setter, bool notifyHostControlled)
        {
            if (entry == null || parser == null || setter == null)
            {
                return;
            }

            setter(parser(entry.Value));
            if (notifyHostControlled)
            {
                HostControlledChanged?.Invoke();
            }
            entry.SettingChanged += (_, _) =>
            {
                setter(parser(entry.Value));
                if (notifyHostControlled)
                {
                    HostControlledChanged?.Invoke();
                }
            };
        }

        private static void RegisterHostControlledField(FeatureConfigEntryAttribute attribute, string key, FieldInfo field)
        {
            if (!attribute.HostControlled)
            {
                return;
            }

            s_hostControlledFields[key] = field;
        }

        internal static Dictionary<string, string> SnapshotHostControlled()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kvp in s_hostControlledFields)
            {
                var field = kvp.Value;
                var value = field.GetValue(null);
                snapshot[kvp.Key] = SerializeValue(value, field.FieldType);
            }

            return snapshot;
        }

        internal static void ApplyHostSnapshot(Dictionary<string, string> snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            foreach (var kvp in snapshot)
            {
                if (!s_hostControlledFields.TryGetValue(kvp.Key, out var field))
                {
                    continue;
                }

                var parsed = DeserializeValue(kvp.Value, field.FieldType);
                if (parsed != null)
                {
                    field.SetValue(null, parsed);
                }
            }
        }

        private static string SerializeValue(object? value, Type fieldType)
        {
            if (fieldType == typeof(bool))
            {
                return ((bool)(value ?? false)).ToString(CultureInfo.InvariantCulture);
            }

            if (fieldType == typeof(int))
            {
                return ((int)(value ?? 0)).ToString(CultureInfo.InvariantCulture);
            }

            if (fieldType == typeof(float))
            {
                return ((float)(value ?? 0f)).ToString(CultureInfo.InvariantCulture);
            }

            if (fieldType == typeof(string))
            {
                return value as string ?? string.Empty;
            }

            if (fieldType == typeof(Color))
            {
                return ColorToString((Color)(value ?? Color.black));
            }

            return value?.ToString() ?? string.Empty;
        }

        private static object? DeserializeValue(string value, Type fieldType)
        {
            if (fieldType == typeof(bool))
            {
                return bool.TryParse(value, out var b) && b;
            }

            if (fieldType == typeof(int))
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
            }

            if (fieldType == typeof(float))
            {
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
            }

            if (fieldType == typeof(string))
            {
                return value ?? string.Empty;
            }

            if (fieldType == typeof(Color))
            {
                return ColorFromString(value ?? string.Empty);
            }

            return null;
        }

        private static string ColorToString(Color input)
        {
            return string.Join(",",
                input.r.ToString(CultureInfo.InvariantCulture),
                input.g.ToString(CultureInfo.InvariantCulture),
                input.b.ToString(CultureInfo.InvariantCulture),
                input.a.ToString(CultureInfo.InvariantCulture));
        }

        private static Color ColorFromString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return Color.black;
            }

            var segments = input.Split(ColorSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return Color.black;
            }

            float r = 0f, g = 0f, b = 0f, a = 1f;
            TryParseComponent(segments, 0, ref r);
            TryParseComponent(segments, 1, ref g);
            TryParseComponent(segments, 2, ref b);
            TryParseComponent(segments, 3, ref a);

            return new Color(r, g, b, a);
        }

        private static void TryParseComponent(string[] segments, int index, ref float slot)
        {
            if (index >= segments.Length)
            {
                return;
            }

            var trimmed = segments[index].Trim();
            if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                slot = parsed;
            }
        }

        private static T SanitizeValue<T>(T value, string key)
        {
            if (value is float f && s_floatRanges.TryGetValue(key, out var floatRange))
            {
                var clamped = Math.Min(floatRange.Max, Math.Max(floatRange.Min, f));
                return (T)(object)clamped;
            }

            if (value is int i && s_intRanges.TryGetValue(key, out var intRange))
            {
                var clamped = Math.Min(intRange.Max, Math.Max(intRange.Min, i));
                return (T)(object)clamped;
            }

            return value;
        }


        private static void RegisterFloatRange(string key, float min, float max)
        {
            s_floatRanges[key] = new RangeF
            {
                Min = min,
                Max = max
            };
        }

        private static void RegisterIntRange(string key, int min, int max)
        {
            s_intRanges[key] = new RangeI
            {
                Min = min,
                Max = max
            };
        }


        private static string BuildRangeKey(string section, string key)
        {
            return $"{section}:{key}";
        }

    }
}
