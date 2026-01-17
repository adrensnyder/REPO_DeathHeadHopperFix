#nullable enable

using System.Reflection;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class ItemHelpers
    {
        internal static string? GetItemAssetName(UnityEngine.Object? itemAsset)
        {
            if (itemAsset == null)
                return null;

            var type = itemAsset.GetType();
            var prop = type.GetProperty("itemAssetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.PropertyType == typeof(string))
                return (string?)prop.GetValue(itemAsset, null) ?? itemAsset.name;

            var field = type.GetField("itemAssetName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(string))
                return (string?)field.GetValue(itemAsset) ?? itemAsset.name;

            return itemAsset.name;
        }
    }
}
