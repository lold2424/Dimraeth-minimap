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
    using Mouse = UnityEngine.InputSystem.Mouse;

    /// <summary>
    /// In-game settings, opened with a key and driven by keyboard or mouse.
    /// Mouse handling is our own hit-testing (no dependency on the game's event system to function). Separately,
    /// the panel is a raycast target on a canvas with a GraphicRaycaster, so game code that asks "is the pointer
    /// over UI?" before attacking sees the panel - best effort: whether the game asks is up to the game.
    /// Values are applied live; the config file is written once, when the panel closes.
    /// </summary>
    internal sealed class SettingsPanel
    {
        private sealed class Row
        {
            public Func<string> Label;
            public Func<string> Value;
            public Action<int> Change; // -1 / +1
            public TextMeshProUGUI LabelText, ValueText;
            public Image Background, Minus, Plus;
        }

        private static readonly Color PanelColor = new Color(0.06f, 0.05f, 0.08f, 0.92f);
        private static readonly Color SelectedColor = new Color(1f, 0.82f, 0.3f, 0.22f);
        private static readonly Color TextColor = new Color(0.93f, 0.9f, 0.84f, 1f);
        private static readonly Color DimTextColor = new Color(0.93f, 0.9f, 0.84f, 0.6f);

        private readonly RectTransform _panel;
        private readonly List<Row> _rows = new List<Row>();
        private readonly TextMeshProUGUI _title, _hint, _hint2;
        private int _selected;
        private float _nextRepeat;
        private int _builtForHeight;
        private bool _builtKorean;

        /// <summary>Korean or English, following the Language setting (Auto = the system language).</summary>
        internal static bool Korean =>
            Plugin.Language.Value == UiLanguage.Korean ||
            (Plugin.Language.Value == UiLanguage.Auto && Application.systemLanguage == SystemLanguage.Korean);

        private static string T(string korean, string english) => Korean ? korean : english;
        private Image _close;
        private float _nextMouseRepeat;
        private bool _mouseBroken;

        private static readonly Color ButtonColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color ButtonHotColor = new Color(1f, 0.82f, 0.3f, 0.55f);

        public bool IsOpen { get; private set; }

        public SettingsPanel(Transform canvas, TMP_FontAsset font)
        {
            _panel = NewRect("Settings", canvas);
            // Give the settings their own draw order above the quest HUD, without raising the minimap.
            var settingsCanvas = _panel.gameObject.AddComponent<Canvas>();
            settingsCanvas.overrideSorting = true;
            settingsCanvas.sortingOrder = short.MaxValue;
            _panel.gameObject.AddComponent<GraphicRaycaster>();
            var background = _panel.gameObject.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = true; // lets the game see "pointer over UI" while the mouse is on the panel
            // The minimap's opacity setting fades its whole canvas; the panel must stay readable.
            var group = _panel.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.blocksRaycasts = true;
            group.interactable = false;

            _title = NewText("Title", _panel, font, TextColor, TextAlignmentOptions.Left);
            _hint = NewText("Hint", _panel, font, DimTextColor, TextAlignmentOptions.Left);
            _hint2 = NewText("Hint2", _panel, font, DimTextColor, TextAlignmentOptions.Left);

            AddRow(() => T("미니맵 표시", "Show minimap"), () => OnOff(Plugin.Enabled.Value), d => Plugin.Enabled.Value = !Plugin.Enabled.Value);
            AddRow(() => T("미니맵 크기", "Minimap size"), () => Percent(Plugin.SizeFraction.Value), d => Step(Plugin.SizeFraction, d * 0.01f, 0.08f, 0.6f));
            AddRow(() => T("보이는 범위", "Visible range"), () => "x" + Plugin.Zoom.Value.ToString("0.0"), d => Step(Plugin.Zoom, d * 0.25f, Plugin.ZoomMin.Value, Plugin.ZoomMax.Value));
            AddRow(() => T("아이콘 크기", "Icon size"), () => Percent(Plugin.IconScale.Value), d => Step(Plugin.IconScale, d * 0.01f, 0.04f, 0.3f));
            AddRow(() => T("위치", "Position"), () => CornerName(Plugin.Position.Value), d => Plugin.Position.Value = (Corner)((((int)Plugin.Position.Value + d) % 4 + 4) % 4));
            AddRow(() => T("좌우 간격", "Side margin"), () => Percent(Plugin.MarginFraction.Value, 1), d => Step(Plugin.MarginFraction, d * 0.005f, 0f, 0.5f));
            AddRow(() => T("상하 간격", "Vertical margin"), () => Percent(Plugin.MarginVertical.Value, 1), d => Step(Plugin.MarginVertical, d * 0.005f, 0f, 0.5f));
            AddRow(() => T("투명도", "Opacity"), () => Percent(Plugin.Opacity.Value), d => Step(Plugin.Opacity, d * 0.05f, 0.1f, 1f));
            AddRow(() => T("모양", "Shape"), () => Plugin.Circular.Value ? T("원형", "Round") : T("사각형", "Square"), d => Plugin.Circular.Value = !Plugin.Circular.Value);
            AddRow(() => T("길 안내 점선", "Guidance routes"), () => OnOff(Plugin.ShowRoutes.Value), d => Plugin.ShowRoutes.Value = !Plugin.ShowRoutes.Value);
            AddRow(() => "언어 / Language", () => LanguageName(Plugin.Language.Value), d => Plugin.Language.Value = (UiLanguage)((((int)Plugin.Language.Value + d) % 3 + 3) % 3));
            AddRow(() => T("캐릭터 게이지", "Character bars"), () => OnOff(VitalBarsConfig.Enabled.Value), d => VitalBarsConfig.Enabled.Value = !VitalBarsConfig.Enabled.Value);
            AddRow(() => T("기력 표시", "Show stamina"), () => OnOff(VitalBarsConfig.ShowStamina.Value), d => VitalBarsConfig.ShowStamina.Value = !VitalBarsConfig.ShowStamina.Value);
            AddRow(() => T("마력 표시", "Show mana"), () => OnOff(VitalBarsConfig.ShowConcentration.Value), d => VitalBarsConfig.ShowConcentration.Value = !VitalBarsConfig.ShowConcentration.Value);
            AddRow(() => T("게이지 크기", "Bar size"), () => Percent(VitalBarsConfig.Scale.Value), d => Step(VitalBarsConfig.Scale, d * 0.1f, 0.6f, 2f));
            AddRow(() => T("게이지 높이", "Bar offset"), () => VitalBarsConfig.OffsetY.Value.ToString("0"), d => Step(VitalBarsConfig.OffsetY, d * 5f, 0f, 220f));
            AddRow(() => T("게이지 투명도", "Bar opacity"), () => Percent(VitalBarsConfig.Opacity.Value), d => Step(VitalBarsConfig.Opacity, d * 0.05f, 0.2f, 1f));
            AddRow(() => T("게이지 숫자", "Bar numbers"), () => OnOff(VitalBarsConfig.ShowNumbers.Value), d => VitalBarsConfig.ShowNumbers.Value = !VitalBarsConfig.ShowNumbers.Value);

            foreach (var row in _rows)
            {
                var rect = NewRect("Row", _panel);
                row.Background = rect.gameObject.AddComponent<Image>();
                row.Background.raycastTarget = false;
                row.LabelText = NewText("Label", rect, font, TextColor, TextAlignmentOptions.Left);
                row.ValueText = NewText("Value", rect, font, TextColor, TextAlignmentOptions.Center);
                row.Minus = NewButton("Minus", rect, font, "<");
                row.Plus = NewButton("Plus", rect, font, ">");
            }
            _close = NewButton("Close", _panel, font, "X");

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

        public void HandleMouse()
        {
            if (!IsOpen || _mouseBroken) return;
            try
            {
                var mouse = Mouse.current;
                if (mouse == null) return;
                Vector2 p = mouse.position.ReadValue();
                bool down = mouse.leftButton.wasPressedThisFrame, held = mouse.leftButton.isPressed;

                _close.color = Over(_close, p) ? ButtonHotColor : ButtonColor;
                if (down && Over(_close, p)) { Toggle(); return; }

                for (int i = 0; i < _rows.Count; i++)
                {
                    var row = _rows[i];
                    bool overMinus = Over(row.Minus, p), overPlus = Over(row.Plus, p);
                    row.Minus.color = overMinus ? ButtonHotColor : ButtonColor;
                    row.Plus.color = overPlus ? ButtonHotColor : ButtonColor;
                    if (!Over(row.Background, p)) continue;
                    _selected = i; // hovering a row selects it, so keyboard and mouse stay in step
                    if ((overMinus || overPlus) && Click(down, held)) row.Change(overPlus ? +1 : -1);
                }
            }
            catch (Exception e)
            {
                _mouseBroken = true;
                Plugin.Logger.LogWarning("Mouse control of the settings panel is unavailable; the arrow keys still work. " + e.Message);
            }
        }

        private static bool Over(Image image, Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(image.rectTransform, screenPoint, null);
        }

        private bool Click(bool down, bool held)
        {
            float now = Time.unscaledTime;
            if (down) { _nextMouseRepeat = now + 0.4f; return true; }
            if (held && now >= _nextMouseRepeat) { _nextMouseRepeat = now + 0.06f; return true; }
            return false;
        }

        /// <summary>Places the panel next to the minimap (above it in the bottom corners, below it in the top ones).</summary>
        public void Layout(Corner corner, float minimapPx, float marginX, float marginY, bool minimapVisible)
        {
            if (!IsOpen) return;

            int h = Screen.height;
            float rowH = Mathf.Round(h * 0.034f), pad = Mathf.Round(h * 0.012f), width = Mathf.Round(h * 0.34f);
            float fontSize = rowH * 0.52f;
            float height = pad * 2f + rowH * (_rows.Count + 2.7f); // title + rows + two hint lines

            bool right = corner == Corner.TopRight || corner == Corner.BottomRight;
            bool top = corner == Corner.TopLeft || corner == Corner.TopRight;
            var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
            float offsetY = marginY + (minimapVisible ? minimapPx + pad : 0f);
            offsetY = Mathf.Clamp(offsetY, pad, Mathf.Max(pad, h - height - pad));
            float offsetX = Mathf.Clamp(marginX, pad, Mathf.Max(pad, Screen.width - width - pad));
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = anchor;
            _panel.sizeDelta = new Vector2(width, height);
            _panel.anchoredPosition = new Vector2(right ? -offsetX : offsetX, top ? -offsetY : offsetY);

            if (_builtForHeight != h || _builtKorean != Korean)
            {
                _builtForHeight = h;
                _builtKorean = Korean;
                _title.text = T("미니맵 / 게이지 설정", "Minimap / bar settings");
                foreach (var row in _rows) row.LabelText.text = row.Label();
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
                    // Right side of the row:  [<]  value  [>]
                    float button = Mathf.Round(rowH * 0.82f), valueWidth = Mathf.Round(width * 0.30f);
                    PlaceRight(row.Plus.rectTransform, pad * 0.5f, button, button, fontSize);
                    PlaceRight(row.ValueText.rectTransform, pad * 0.5f + button, valueWidth, rowH, 0f);
                    PlaceRight(row.Minus.rectTransform, pad * 0.5f + button + valueWidth, button, button, fontSize);
                    row.LabelText.fontSize = row.ValueText.fontSize = fontSize;
                }
                Place(_hint.rectTransform, _rows.Count + 1, rowH, pad, width);
                Place(_hint2.rectTransform, _rows.Count + 1, rowH, pad, width);
                _hint2.rectTransform.anchoredPosition += new Vector2(0f, -rowH * 0.8f);
                _hint.fontSize = _hint2.fontSize = fontSize * 0.85f;
                _hint.text = T("키보드:  ↑ ↓ 항목 선택    ← → 값 조절", "Keyboard:  ↑ ↓ select    ← → adjust");
                _hint2.text = T($"마우스:  < > 클릭    닫기:  {Plugin.SettingsKey.Value} 또는 X", $"Mouse:  click < >    Close:  {Plugin.SettingsKey.Value} or X");
                float closeSize = Mathf.Round(rowH * 0.82f);
                var closeRect = _close.rectTransform;
                closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1f, 1f);
                closeRect.sizeDelta = new Vector2(closeSize, closeSize);
                closeRect.anchoredPosition = new Vector2(-pad, -pad);
                SetButtonFont(_close, fontSize);
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

        private void AddRow(Func<string> label, Func<string> value, Action<int> change)
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
        private static string OnOff(bool on) => on ? T("켜짐", "On") : T("꺼짐", "Off");

        private static string LanguageName(UiLanguage language)
        {
            switch (language)
            {
                case UiLanguage.Korean: return "한국어";
                case UiLanguage.English: return "English";
                default: return T("자동", "Auto");
            }
        }

        private static string CornerName(Corner corner)
        {
            switch (corner)
            {
                case Corner.TopLeft: return T("왼쪽 위", "Top left");
                case Corner.TopRight: return T("오른쪽 위", "Top right");
                case Corner.BottomLeft: return T("왼쪽 아래", "Bottom left");
                default: return T("오른쪽 아래", "Bottom right");
            }
        }

        private static void Place(RectTransform rect, int line, float rowH, float pad, float width)
        {
            rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-pad * 3f, rowH);
            rect.anchoredPosition = new Vector2(0f, -(pad + rowH * line));
        }

        private static void PlaceRight(RectTransform rect, float fromRight, float w, float h, float buttonFont)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(-fromRight, 0f);
            if (buttonFont > 0f) SetButtonFont(rect.GetComponent<Image>(), buttonFont);
        }

        private static void SetButtonFont(Image button, float fontSize)
        {
            var label = button.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
            if (label != null) label.fontSize = fontSize;
        }

        private static Image NewButton(string name, Transform parent, TMP_FontAsset font, string caption)
        {
            var rect = NewRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = ButtonColor;
            image.raycastTarget = false;
            var label = NewText("Caption", rect, font, TextColor, TextAlignmentOptions.Center);
            label.text = caption;
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero; labelRect.offsetMax = Vector2.zero;
            return image;
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
