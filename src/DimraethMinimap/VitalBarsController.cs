using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DimraethMinimap
{
    internal sealed class VitalBarsController : IDisposable
    {
        private sealed class Bar
        {
            internal RectTransform Track, Fill;
            internal TextMeshProUGUI Text;
            internal string Label;
            internal int LastCurrent = -1, LastMaximum = -1;
            internal bool LastAvailable;
        }

        private readonly Bar[] _bars = new Bar[3];
        private GameObject _root;
        private RectTransform _panel;
        private CanvasGroup _group;
        private Component _player;
        private VitalSnapshot _snapshot;
        private bool _hasSnapshot;
        private float _nextRead, _nextFontTry;
        private float _scale = -1f;
        private bool _numbers;
        private bool _showStamina, _showConcentration;
        private TMP_FontAsset _font;

        public void Tick()
        {
            var net = Unity.Netcode.NetworkManager.Singleton;
            if (!VitalBarsConfig.Enabled.Value || net == null || !net.IsListening ||
                !GameAccess.TryGetLocal(out var position))
            {
                Hide();
                return;
            }

            var player = GameAccess.LocalComponent;
            var camera = Camera.main;
            if (player == null || !player.gameObject.activeInHierarchy || camera == null || !camera.isActiveAndEnabled)
            {
                Hide();
                return;
            }

            // ScreenSpaceOverlay canvas uses pixels. Position follows the camera after its normal Update.
            Vector3 point = camera.WorldToScreenPoint(position);
            Rect viewport = camera.pixelRect;
            if (point.z <= 0f || !viewport.Contains(new Vector2(point.x, point.y)))
            {
                Hide();
                return;
            }

            float now = Time.unscaledTime;
            if (player != _player)
            {
                _player = player;
                _hasSnapshot = false;
                _nextRead = 0f;
            }
            if (now >= _nextRead)
            {
                _nextRead = now + 0.1f;
                _hasSnapshot = VitalBarsAccess.TryRead(player, out _snapshot);
            }
            if (!_hasSnapshot) { Hide(); return; }

            EnsureUi();
            float resolutionScale = Mathf.Max(0.25f, Screen.height / 1080f);
            float scale = resolutionScale * VitalBarsConfig.Scale.Value;
            bool numbers = VitalBarsConfig.ShowNumbers.Value;
            if (!Mathf.Approximately(scale, _scale) || numbers != _numbers ||
                _showStamina != VitalBarsConfig.ShowStamina.Value ||
                _showConcentration != VitalBarsConfig.ShowConcentration.Value)
                Layout(scale, numbers);

            // Keep the complete cluster on screen near the edges; never project an offscreen player.
            float halfWidth = _panel.sizeDelta.x * 0.5f;
            float x = Mathf.Clamp(point.x, halfWidth, Mathf.Max(halfWidth, Screen.width - halfWidth));
            float y = Mathf.Clamp(point.y + VitalBarsConfig.OffsetY.Value * resolutionScale,
                0f, Mathf.Max(0f, Screen.height - _panel.sizeDelta.y));
            _panel.anchoredPosition = new Vector2(x, y);
            _group.alpha = VitalBarsConfig.Opacity.Value;
            UpdateBar(_bars[0], _snapshot.Health);
            UpdateBar(_bars[1], _snapshot.Stamina);
            UpdateBar(_bars[2], _snapshot.Concentration);

            if (numbers && _font == null && now >= _nextFontTry)
            {
                _nextFontTry = now + 1f;
                _font = GameMarkers.TryGetGameFont() ?? TMP_Settings.defaultFontAsset;
                if (_font != null)
                    foreach (var bar in _bars) bar.Text.font = _font;
            }
            if (!_root.activeSelf) _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            _hasSnapshot = false;
            _nextRead = 0f;
        }

        private void EnsureUi()
        {
            if (_root != null) return;
            _font = null;
            _nextFontTry = 0f;
            _root = new GameObject("DimraethCharacterBars");
            _root.layer = 5;
            _root.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.SetActive(false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2;
            _group = _root.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _panel = Rect("Bars", _root.transform);
            _panel.anchorMin = _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0.5f, 0f);
            Image(_panel, new Color(0.02f, 0.025f, 0.04f, 0.92f));
            _bars[0] = CreateBar("Health", "HP", new Color(0.92f, 0.24f, 0.28f));
            _bars[1] = CreateBar("Stamina", "ST", new Color(0.3f, 0.83f, 0.46f));
            _bars[2] = CreateBar("Concentration", "FO", new Color(0.31f, 0.64f, 1f));
            _scale = -1f;
        }

        private Bar CreateBar(string name, string label, Color color)
        {
            var track = Rect(name, _panel);
            track.anchorMin = track.anchorMax = new Vector2(0f, 1f);
            track.pivot = new Vector2(0f, 1f);
            Image(track, new Color(0.12f, 0.14f, 0.18f, 1f));
            var fill = Rect("Fill", track);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Image(fill, color);
            var textRect = Rect("Value", track);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            textRect.gameObject.SetActive(false);
            return new Bar { Track = track, Fill = fill, Text = text, Label = label };
        }

        private void Layout(float scale, bool numbers)
        {
            _scale = scale;
            _numbers = numbers;
            _showStamina = VitalBarsConfig.ShowStamina.Value;
            _showConcentration = VitalBarsConfig.ShowConcentration.Value;
            float width = (numbers ? 144f : 116f) * scale;
            float height = (numbers ? 16f : 7f) * scale;
            float padding = 3f * scale, gap = 3f * scale;
            int count = 1 + (_showStamina ? 1 : 0) + (_showConcentration ? 1 : 0);
            _panel.sizeDelta = new Vector2(width + padding * 2f, height * count + gap * (count - 1) + padding * 2f);
            int row = 0;
            for (int i = 0; i < _bars.Length; i++)
            {
                var bar = _bars[i];
                bool visible = i == 0 || (i == 1 ? _showStamina : _showConcentration);
                bar.Track.gameObject.SetActive(visible);
                if (!visible) continue;
                bar.Track.anchoredPosition = new Vector2(padding, -padding - row++ * (height + gap));
                bar.Track.sizeDelta = new Vector2(width, height);
                bar.Text.fontSize = 11f * scale;
                bar.Text.gameObject.SetActive(numbers);
            }
        }

        private void UpdateBar(Bar bar, VitalValue value)
        {
            bar.Fill.anchorMax = new Vector2(value.Ratio, 1f);
            if (!_numbers) return;
            int current = Mathf.CeilToInt(value.Current), maximum = Mathf.CeilToInt(value.Maximum);
            if (bar.LastCurrent == current && bar.LastMaximum == maximum && bar.LastAvailable == value.Available) return;
            bar.LastCurrent = current;
            bar.LastMaximum = maximum;
            bar.LastAvailable = value.Available;
            string label = value.Available
                ? bar.Label + " " + current + " / " + maximum
                : bar.Label + " —";
            if (bar.Text.text != label) bar.Text.text = label;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = 5;
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
            _player = null;
            _hasSnapshot = false;
        }
    }
}
