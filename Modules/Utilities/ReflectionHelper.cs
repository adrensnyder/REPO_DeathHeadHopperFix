#nullable enable

using System;
using System.Reflection;
using HarmonyLib;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class ReflectionHelper
    {
        internal static object? GetStaticInstanceByName(string typeName)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                return null;
            return GetStaticInstanceValue(type, "instance");
        }

        internal static object? GetStaticInstanceValue(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(null);

            var prop = type.GetProperty(name, flags);
            if (prop != null)
                return prop.GetValue(null, null);

            return null;
        }
    }
}
