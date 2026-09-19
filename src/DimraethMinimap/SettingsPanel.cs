using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DimraethMinimap
{
    // Aliased: the game has its own global types with these names.
    using Keyboard = UnityEngine.InputSystem.Keyboard;
    using Key = UnityEngine.InputSystem.Key;

    /// <summary>
    /// In-game settings, opened with a key. Driven by the keyboard on purpose: a mouse-driven panel would need
    /// to take part in the game's UI event system, and every click on it would also reach the game (attacks).
    /// Values are applied live; the config file is written once, when the panel closes.
    /// </summary>
    internal sealed class SettingsPanel
    {
        private sealed class Row
        {
            public string Label;
            public Func<string> Value;
            public Action<int> Change; // -1 / +1
            public TextMeshProUGUI LabelText, ValueText;
            public Image Background;
        }

        private static readonly Color PanelColor = new Color(0.06f, 0.05f, 0.08f, 0.92f);
        private static readonly Color SelectedColor = new Color(1f, 0.82f, 0.3f, 0.22f);
        private static readonly Color TextColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        private static readonly Color DimTextColor = new Color(0.93f, 0.9f, 0.84f, 0.6f);

        private readonly RectTransform _panel;
        private readonly List<Row> _rows = new List<Row>();
        private readonly TextMeshProUGUI _title, _hint;
        private int _selected;
        private float _nextRepeat;
        private int _builtForHeight;

        public bool IsOpen { get; private set; }

        public SettingsPanel(Transform canvas, TMP_FontAsset font)
        {
            _panel = NewRect("Settings", canvas);
            var background = _panel.gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;
            // The minimap's opacity setting fades its whole canvas; the panel must stay readable.
            var group = _panel.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.blocksRaycasts = false;
            group.interactable = false;

            _title = NewText("Title", _panel, font, TextColor, TextAlignmentOptions.Left);
            _title.text = "미니맵 설정";
            _hint = NewText("Hint", _panel, font, DimTextColor, TextAlignmentOptions.Left);

            AddRow("미니맵 표시", () => Plugin.Enabled.Value ? "켜짐" : "꺼짐", d => Plugin.Enabled.Value = !Plugin.Enabled.Value);
            AddRow("미니맵 크기", () => Percent(Plugin.SizeFraction.Value), d => Step(Plugin.SizeFraction, d * 0.01f, 0.08f, 0.6f));
            AddRow("보이는 범위", () => "x" + Plugin.Zoom.Value.ToString("0.0"), d => Step(Plugin.Zoom, d * 0.25f, Plugin.ZoomMin.Value, Plugin.ZoomMax.Value));
            AddRow("아이콘 크기", () => Percent(Plugin.IconScale.Value), d => Step(Plugin.IconScale, d * 0.01f, 0.04f, 0.3f));
            AddRow("위치", () => CornerName(Plugin.Position.Value), d => Plugin.Position.Value = (Corner)((((int)Plugin.Position.Value + d) % 4 + 4) % 4));
            AddRow("좌우 간격", () => Percent(Plugin.MarginFraction.Value, 1), d => Step(Plugin.MarginFraction, d * 0.005f, 0f, 0.5f));
            AddRow("상하 간격", () => Percent(Plugin.MarginVertical.Value, 1), d => Step(Plugin.MarginVertical, d * 0.005f, 0f, 0.5f));
            AddRow("투명도", () => Percent(Plugin.Opacity.Value), d => Step(Plugin.Opacity, d * 0.05f, 0.1f, 1f));
            AddRow("모양", () => Plugin.Circular.Value ? "원형" : "사각형", d => Plugin.Circular.Value = !Plugin.Circular.Value);
            AddRow("길 안내 점선", () => OnOff(Plugin.ShowRoutes.Value), d => Plugin.ShowRoutes.Value = !Plugin.ShowRoutes.Value);

            foreach (var row in _rows)
            {
                var rect = NewRect("Row", _panel);
                row.Background = rect.gameObject.AddComponent<Image>();
                row.Background.raycastTarget = false;
                row.LabelText = NewText("Label", rect, font, TextColor, TextAlignmentOptions.Left);
                row.LabelText.text = row.Label;
                row.ValueText = NewText("Value", rect, font, TextColor, TextAlignmentOptions.Right);
            }

            _panel.gameObject.SetActive(false);
        }

        public void Toggle()
        {
            IsOpen = !IsOpen;
            _panel.gameObject.SetActive(IsOpen);
            // Holding an arrow changes a value many times a second; write the file once at the end instead.
            Plugin.Settings.SaveOnConfigSet = !IsOpen;
            if (!IsOpen) Plugin.Settings.Save();
        }

        public void HandleKeys(Keyboard kb)
        {
            if (!IsOpen) return;
            if (Pressed(kb, Key.UpArrow)) _selected = (_selected + _rows.Count - 1) % _rows.Count;
            if (Pressed(kb, Key.DownArrow)) _selected = (_selected + 1) % _rows.Count;
            if (Pressed(kb, Key.LeftArrow)) _rows[_selected].Change(-1);
            if (Pressed(kb, Key.RightArrow)) _rows[_selected].Change(+1);
        }

        /// <summary>Places the panel next to the minimap (above it in the bottom corners, below it in the top ones).</summary>
        public void Layout(Corner corner, float minimapPx, float marginX, float marginY, bool minimapVisible)
        {
            if (!IsOpen) return;

            int h = Screen.height;
            float rowH = Mathf.Round(h * 0.034f), pad = Mathf.Round(h * 0.012f), width = Mathf.Round(h * 0.34f);
            float fontSize = rowH * 0.52f;
            float height = pad * 2f + rowH * (_rows.Count + 2); // title + rows + hint

            bool right = corner == Corner.TopRight || corner == Corner.BottomRight;
            bool top = corner == Corner.TopLeft || corner == Corner.TopRight;
            var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
            float offsetY = marginY + (minimapVisible ? minimapPx + pad : 0f);
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = anchor;
            _panel.sizeDelta = new Vector2(width, height);
            _panel.anchoredPosition = new Vector2(right ? -marginX : marginX, top ? -offsetY : offsetY);

            if (_builtForHeight != h)
            {
                _builtForHeight = h;
                Place(_title.rectTransform, 0, rowH, pad, width);
                _title.fontSize = fontSize * 1.1f;
                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    var rect = row.Background.rectTransform;
                    rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(0.5f, 1f);
                    rect.sizeDelta = new Vector2(-pad, rowH);
                    rect.anchoredPosition = new Vector2(0f, -(pad + rowH * (i + 1)));
                    Fill(row.LabelText.rectTransform, pad);
                    Fill(row.ValueText.rectTransform, pad);
                    row.LabelText.fontSize = row.ValueText.fontSize = fontSize;
                }
                Place(_hint.rectTransform, _rows.Count + 1, rowH, pad, width);
                _hint.fontSize = fontSize * 0.85f;
                _hint.text = $"↑↓ 선택   ←→ 조절   {Plugin.SettingsKey.Value} 닫기";
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                row.Background.color = i == _selected ? SelectedColor : Color.clear;
                string value = row.Value();
                if (row.ValueText.text != value) row.ValueText.text = value;
            }
        }

        // -------------------------------------------------------------- helpers

        private void AddRow(string label, Func<string> value, Action<int> change)
        {
            _rows.Add(new Row { Label = label, Value = value, Change = change });
        }

        private bool Pressed(Keyboard kb, Key key)
        {
            var control = kb[key];
            if (control == null) return false;
            float now = Time.unscaledTime;
            if (control.wasPressedThisFrame) { _nextRepeat = now + 0.4f; return true; }
            if (control.isPressed && now >= _nextRepeat) { _nextRepeat = now + 0.06f; return true; }
            return false;
        }

        private static void Step(BepInEx.Configuration.ConfigEntry<float> entry, float delta, float min, float max)
        {
            entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + delta) * 1000f) / 1000f, min, max);
        }

        private static string Percent(float fraction, int decimals = 0) => (fraction * 100f).ToString(decimals == 0 ? "0" : "0.0") + "%";
        private static string OnOff(bool on) => on ? "켜짐" : "꺼짐";

        private static string CornerName(Corner corner)
        {
            switch (corner)
            {
                case Corner.TopLeft: return "왼쪽 위";
                case Corner.TopRight: return "오른쪽 위";
                case Corner.BottomLeft: return "왼쪽 아래";
                default: return "오른쪽 아래";
            }
        }

        private static void Place(RectTransform rect, int line, float rowH, float pad, float width)
        {
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-pad * 3f, rowH);
            rect.anchoredPosition = new Vector2(0f, -(pad + rowH * line));
        }

        private static void Fill(RectTransform rect, float pad)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(pad, 0f); rect.offsetMax = new Vector2(-pad, 0f);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = 5;
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, Color color, TextAlignmentOptions alignment)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.richText = false;
            return text;
        }
    }
}
