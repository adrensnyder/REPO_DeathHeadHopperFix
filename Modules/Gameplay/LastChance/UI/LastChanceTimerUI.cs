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

        private static Component? s_label;
        private static RectTransform? s_rect;
        private static Component? s_hintLabel;
        private static RectTransform? s_hintRect;
        private static string s_defaultHintText = string.Empty;
        private const float SurrenderHintVerticalOffset = -45f;

        internal static void Show(string defaultHintText)
        {
            s_defaultHintText = defaultHintText;
            if (s_label != null)
            {
                SetEnabled(true);
                return;
            }

            if (LabelType == null || HUDCanvas.instance == null)
            {
                return;
            }

            var go = new GameObject("LastChanceTimer", typeof(RectTransform));
            go.transform.SetParent(HUDCanvas.instance.transform, false);

            s_rect = go.GetComponent<RectTransform>();
            s_rect.anchorMin = new Vector2(0.5f, 1f);
            s_rect.anchorMax = new Vector2(0.5f, 1f);
            s_rect.pivot = new Vector2(0.5f, 1f);
            s_rect.anchoredPosition = new Vector2(0f, -30f);
            s_rect.sizeDelta = new Vector2(600f, 60f);

            s_label = go.AddComponent(LabelType);
            SetDefaults(s_label);
            SetEnabled(true);

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
        }

        internal static void UpdateText(string text)
        {
            if (s_label == null)
            {
                return;
            }

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

            TextProperty.SetValue(s_hintLabel, text);
        }

        internal static void ResetSurrenderHint()
        {
            SetSurrenderHintText(s_defaultHintText);
        }
    }
}
