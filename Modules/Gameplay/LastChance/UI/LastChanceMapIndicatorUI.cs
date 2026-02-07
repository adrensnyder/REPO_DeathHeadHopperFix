#nullable enable

using System;
using System.Reflection;
using DeathHeadHopperFix.Modules.Utilities;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.UI
{
    internal static class LastChanceMapIndicatorUI
    {
        private static readonly Type? RawImageType = AccessTools.TypeByName("UnityEngine.UI.RawImage");
        private static readonly PropertyInfo? TextureProperty = RawImageType?.GetProperty("texture", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo? ColorProperty = RawImageType?.GetProperty("color", BindingFlags.Instance | BindingFlags.Public);
        private static readonly PropertyInfo? RaycastTargetProperty = RawImageType?.GetProperty("raycastTarget", BindingFlags.Instance | BindingFlags.Public);

        private static Component? s_rawImage;
        private static RectTransform? s_rect;

        internal static bool Show(Texture? texture, bool debugLogging)
        {
            if (texture == null)
            {
                if (debugLogging)
                {
                    if (LogLimiter.ShouldLog("LastChance.MapUI.TextureNull", 1))
                    {
                        Debug.LogWarning("[LastChance] MapUI: texture is null.");
                    }
                }
                return false;
            }

            if (!EnsureCreated(debugLogging))
            {
                return false;
            }

            if (TextureProperty != null && s_rawImage != null)
            {
                TextureProperty.SetValue(s_rawImage, texture);
            }

            if (s_rawImage is Behaviour behaviour)
            {
                behaviour.enabled = true;
            }

            if (s_rawImage is Component component)
            {
                component.gameObject.SetActive(true);
            }

            return true;
        }

        internal static void Hide()
        {
            if (s_rawImage == null)
            {
                return;
            }

            if (s_rawImage is Behaviour behaviour)
            {
                behaviour.enabled = false;
            }

            if (s_rawImage is Component component)
            {
                component.gameObject.SetActive(false);
            }
        }

        private static bool EnsureCreated(bool debugLogging)
        {
            if (s_rawImage != null)
            {
                return true;
            }

            if (RawImageType == null || HUDCanvas.instance == null)
            {
                if (debugLogging)
                {
                    Debug.LogWarning($"[LastChance] MapUI unavailable: rawImageType={(RawImageType != null)} hudCanvas={(HUDCanvas.instance != null)}");
                }
                return false;
            }

            var go = new GameObject("LastChanceMapIndicator", typeof(RectTransform));
            go.transform.SetParent(HUDCanvas.instance.transform, false);

            s_rect = go.GetComponent<RectTransform>();
            if (s_rect != null)
            {
                s_rect.anchorMin = new Vector2(1f, 0f);
                s_rect.anchorMax = new Vector2(1f, 0f);
                s_rect.pivot = new Vector2(1f, 0f);
                s_rect.anchoredPosition = new Vector2(-24f, 24f);
                s_rect.sizeDelta = new Vector2(260f, 180f);
            }

            s_rawImage = go.AddComponent(RawImageType);
            if (RaycastTargetProperty != null && s_rawImage != null)
            {
                RaycastTargetProperty.SetValue(s_rawImage, false);
            }

            if (ColorProperty != null && s_rawImage != null)
            {
                ColorProperty.SetValue(s_rawImage, Color.white);
            }

            Hide();
            return true;
        }
    }
}
