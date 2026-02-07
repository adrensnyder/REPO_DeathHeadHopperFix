#nullable enable

using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DeathHeadHopperFix.Modules.Gameplay.LastChance.UI
{
    internal static class LastChanceTimerUI
    {
        private static readonly Type? LabelType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
        private static readonly PropertyInfo? TextProperty = LabelType?.GetProperty("text");
        private static readonly PropertyInfo? ColorProperty = LabelType?.GetProperty("color");
        private static readonly PropertyInfo? AlignmentProperty = LabelType?.GetProperty("alignment");
        private static readonly PropertyInfo? FontSizeProperty = LabelType?.GetProperty("fontSize");
        private static readonly PropertyInfo? AutoSizeProperty = LabelType?.GetProperty("enableAutoSizing");
        private static readonly PropertyInfo? WordWrapProperty = LabelType?.GetProperty("enableWordWrapping");
        private static readonly PropertyInfo? RichTextProperty = LabelType?.GetProperty("richText");
        private static readonly Type? AlignmentType = AccessTools.TypeByName("TMPro.TextAlignmentOptions");
        private static readonly object? CenterAlignment = AlignmentType != null
            ? Enum.Parse(AlignmentType, "Center")
            : null;
        private static readonly FieldInfo? CurrentMenuPageField = AccessTools.Field(typeof(MenuManager), "currentMenuPage");

        private static Component? s_label;
        private static RectTransform? s_rect;
        private static Component? s_hintLabel;
        private static RectTransform? s_hintRect;
        private static string s_defaultHintText = string.Empty;
        private static string s_lastTimerText = string.Empty;
        private static string s_lastHintText = string.Empty;
        private static bool s_isVisible;
        private static Transform? s_cachedUiParent;
        private static float s_nextVisibilityRefreshAt;
        private const float VisibilityRefreshIntervalSeconds = 0.5f;
        private const float SurrenderHintVerticalOffset = -45f;

        internal static void Show(string defaultHintText)
        {
            s_defaultHintText = defaultHintText;
            if (s_label != null)
            {
                ReparentToPreferredUiRoot();
                RefreshVisibility(force: true);
                return;
            }

            var parent = ResolvePreferredUiParent();
            if (LabelType == null || parent == null)
            {
                return;
            }

            var go = new GameObject("LastChanceTimer", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            s_rect = go.GetComponent<RectTransform>();
            s_rect.anchorMin = new Vector2(0.5f, 1f);
            s_rect.anchorMax = new Vector2(0.5f, 1f);
            s_rect.pivot = new Vector2(0.5f, 1f);
            s_rect.anchoredPosition = new Vector2(0f, -30f);
            s_rect.sizeDelta = new Vector2(600f, 60f);

            s_label = go.AddComponent(LabelType);
            SetDefaults(s_label);
            SetEnabled(false);

            var hintGo = new GameObject("LastChanceSurrenderHint", typeof(RectTransform));
            hintGo.transform.SetParent(go.transform, false);

            s_hintRect = hintGo.GetComponent<RectTransform>();
            if (s_hintRect != null)
            {
                s_hintRect.anchorMin = new Vector2(0.5f, 1f);
                s_hintRect.anchorMax = new Vector2(0.5f, 1f);
                s_hintRect.pivot = new Vector2(0.5f, 1f);
                s_hintRect.anchoredPosition = new Vector2(0f, SurrenderHintVerticalOffset);
                s_hintRect.sizeDelta = new Vector2(600f, 26f);
            }

            s_hintLabel = hintGo.AddComponent(LabelType);
            SetHintDefaults(s_hintLabel);
            SetSurrenderHintText(s_defaultHintText);
            RefreshVisibility(force: true);
        }

        internal static void UpdateText(string text)
        {
            if (s_label == null)
            {
                return;
            }

            RefreshVisibility(force: false);

            if (string.Equals(s_lastTimerText, text, StringComparison.Ordinal))
            {
                return;
            }
            s_lastTimerText = text;

            if (TextProperty != null)
            {
                TextProperty.SetValue(s_label, text);
            }
        }

        internal static void Hide()
        {
            if (s_label == null)
            {
                return;
            }

            SetEnabled(false);
        }

        private static void RefreshVisibility(bool force)
        {
            if (!force && Time.unscaledTime < s_nextVisibilityRefreshAt)
            {
                return;
            }

            s_nextVisibilityRefreshAt = Time.unscaledTime + VisibilityRefreshIntervalSeconds;
            SetEnabled(ShouldBeVisibleNow());
        }

        private static bool ShouldBeVisibleNow()
        {
            if (GetPreferredUiParentCached() == null)
            {
                return false;
            }

            if (MenuManager.instance != null && CurrentMenuPageField != null && CurrentMenuPageField.GetValue(MenuManager.instance) != null)
            {
                return false;
            }

            return true;
        }

        private static void ReparentToPreferredUiRoot()
        {
            if (s_label is not Component labelComponent)
            {
                return;
            }

            var parent = GetPreferredUiParentCached();
            if (parent == null)
            {
                return;
            }

            if (labelComponent.transform.parent != parent)
            {
                labelComponent.transform.SetParent(parent, false);
            }
        }

        private static Transform? ResolvePreferredUiParent()
        {
            // Prefer the same root used by DHH ability/energy UI (better compatibility with UI-mirroring mods, e.g. VR).
            var dhhUiMgrType = AccessTools.TypeByName("DeathHeadHopper.Managers.DHHUIManager");
            if (dhhUiMgrType != null)
            {
                var instanceProperty = dhhUiMgrType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var instance = instanceProperty?.GetValue(null);
                if (instance != null)
                {
                    var gameHudField = dhhUiMgrType.GetField("gameHUD", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (gameHudField?.GetValue(instance) is GameObject gameHud && gameHud != null)
                    {
                        return gameHud.transform;
                    }
                }
            }

            return HUDCanvas.instance != null ? HUDCanvas.instance.transform : null;
        }

        private static Transform? GetPreferredUiParentCached()
        {
            if (s_cachedUiParent != null)
            {
                return s_cachedUiParent;
            }

            s_cachedUiParent = ResolvePreferredUiParent();
            return s_cachedUiParent;
        }

        private static void SetDefaults(Component label)
        {
            if (label == null)
                return;

            if (ColorProperty != null)
                ColorProperty.SetValue(label, Color.white);
            if (FontSizeProperty != null)
                FontSizeProperty.SetValue(label, 24f);
            if (AutoSizeProperty != null)
                AutoSizeProperty.SetValue(label, false);
            if (WordWrapProperty != null)
                WordWrapProperty.SetValue(label, false);
            if (RichTextProperty != null)
                RichTextProperty.SetValue(label, true);
            if (AlignmentProperty != null && CenterAlignment != null)
                AlignmentProperty.SetValue(label, CenterAlignment);
        }

        private static void SetHintDefaults(Component label)
        {
            if (label == null)
                return;

            if (ColorProperty != null)
                ColorProperty.SetValue(label, Color.white);
            if (FontSizeProperty != null)
                FontSizeProperty.SetValue(label, 18f);
            if (AutoSizeProperty != null)
                AutoSizeProperty.SetValue(label, false);
            if (WordWrapProperty != null)
                WordWrapProperty.SetValue(label, false);
            if (RichTextProperty != null)
                RichTextProperty.SetValue(label, true);
            if (AlignmentProperty != null && CenterAlignment != null)
                AlignmentProperty.SetValue(label, CenterAlignment);
        }

        private static void SetEnabled(bool enabled)
        {
            if (s_label == null)
            {
                return;
            }

            if (s_isVisible == enabled)
            {
                return;
            }
            s_isVisible = enabled;

            if (s_label is Behaviour behaviour)
            {
                behaviour.enabled = enabled;
            }

            if (s_label is Component component)
            {
                component.gameObject.SetActive(enabled);
            }

            if (s_hintLabel is Behaviour hintBehaviour)
            {
                hintBehaviour.enabled = enabled;
            }

            if (s_hintLabel is Component hintComponent)
            {
                hintComponent.gameObject.SetActive(enabled);
            }
        }

        internal static void SetSurrenderHintText(string text)
        {
            if (s_hintLabel == null || TextProperty == null)
            {
                return;
            }

            if (string.Equals(s_lastHintText, text, StringComparison.Ordinal))
            {
                return;
            }
            s_lastHintText = text;
            TextProperty.SetValue(s_hintLabel, text);
        }

        internal static void ResetSurrenderHint()
        {
            SetSurrenderHintText(s_defaultHintText);
        }
    }
}
