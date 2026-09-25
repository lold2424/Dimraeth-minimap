using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace DimraethMinimap
{
    // Aliased: the game has its own global types with these names.
    using Keyboard = UnityEngine.InputSystem.Keyboard;
    using Key = UnityEngine.InputSystem.Key;

    /// <summary>
    /// Renders the world from above with a second camera into a texture and shows it in a screen corner.
    /// Purely local and visual: no game state is changed, nothing is sent over the network.
    /// </summary>
    internal sealed class MinimapController
    {
        private const int UiLayer = 5;
        private const float BorderFraction = 0.035f;
        private const float DotFraction = 0.055f;
        private const float ZoomStep = 1.25f;
        private const float RenderPadding = 0.6f; // extra world rendered around the visible area, as a fraction of it

        private static readonly Color FrameColor = new Color(0.07f, 0.06f, 0.09f, 1f);
        private static readonly Color LocalDotColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color TeammateDotColor = new Color(0.30f, 0.85f, 1f, 1f);

        private Camera _cam;
        private RenderTexture _rt;
        private float _nextRender;
        private bool _hasRender;
        private float _viewHalf;
        private Vector3 _focus;
        private RawImage _view;
        private bool _renderedThisFrame, _renderedLastFrame;
        private float _nextMaskSync;

        private GameObject _root;
        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _frame;
        private RectTransform _maskRect;
        private RectTransform _dotLayer;
        private RectTransform _localDot;
        private readonly List<RectTransform> _teamDots = new List<RectTransform>();
        private Sprite _circle;

        private readonly List<Vector3> _others = new List<Vector3>();
        private int _layoutW, _layoutH;
        private float _layoutSize, _layoutMargin, _layoutMarginY;
        private Corner _layoutCorner;
        private bool _layoutCircular;
        private float _sizePx;

        private bool _mapMode;
        private GameMap.Info _map;
        private RectTransform _mapHolder;
        private GameObject _mapInstance;
        private IntPtr _mapInstanceId;
        private Transform _fogParent;
        private float _nextFogSync;
        private float _nextRebuildTry;
        private readonly HashSet<string> _regions = new HashSet<string>();

        private const float MarkerRim = 0.8f; // markers are bigger than dots: keep them clear of the round edge
        private const float QuestHintRange = 1500f; // world units; beyond this a rim hint is just noise
        private const int MaxDashes = 160;
        private RectTransform _areaLayer, _routeLayer, _iconLayer, _pinLayer;
        private readonly List<Image> _dashPool = new List<Image>();
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<Image> _iconPool = new List<Image>();
        private readonly List<QuestPin> _questPins = new List<QuestPin>();

        private SettingsPanel _settings;

        private float _worldSince = -1f;
        private bool _autoDiagDone;

        public void Tick()
        {
            FrameStats.Record(Time.unscaledDeltaTime, _renderedLastFrame, _root != null && _root.activeSelf);
            _renderedLastFrame = _renderedThisFrame;
            _renderedThisFrame = false;

            HandleKeys();

            var main = Camera.main;
            bool hasLocal = GameAccess.TryGetLocal(out Vector3 localPos);
            bool show = Plugin.Enabled.Value && main != null && (hasLocal || GameAccess.Broken);

            // Map mode crops the game's own map image: no second render of the world, so no frame cost.
            // Camera mode is the fallback when the map classes are gone after a game update.
            _mapMode = Plugin.Mode.Value == MinimapMode.Map && !GameMap.Broken && !GameAccess.Broken;
            GameMap.Info map = default;
            if (show && _mapMode && !GameMap.TryGet(localPos, out map))
                show = GameMap.Broken; // no map for this place: hide. Broken: carry on in camera mode.
            _mapMode &= !GameMap.Broken;

            bool settingsOpen = _settings != null && _settings.IsOpen;
            if (!show)
            {
                // The settings panel must stay usable with the minimap switched off (that is one of its settings).
                if (settingsOpen && main != null && hasLocal && _root != null)
                {
                    ApplyLayout();
                    if (_frame.gameObject.activeSelf) _frame.gameObject.SetActive(false);
                    LayoutSettings();
                    if (!_root.activeSelf) _root.SetActive(true);
                    return;
                }
                if (settingsOpen) _settings.Toggle();
                Hide();
                _worldSince = -1f;
                return;
            }

            EnsureUi();
            if (!_frame.gameObject.activeSelf) _frame.gameObject.SetActive(true);
            ApplyLayout();
            LayoutSettings();
            _viewHalf = (main.orthographic ? main.orthographicSize : 8f) * Plugin.Zoom.Value;

            if (_mapMode)
            {
                if (_cam != null && _cam.enabled) _cam.enabled = false;
                UpdateMapView(map);
            }
            else
            {
                EnsureCamera(main);
                _focus = hasLocal ? localPos : main.transform.position;
                RenderIfDue(main, _focus);
                ScrollView(_focus);
            }
            if (_view.enabled == _mapMode) _view.enabled = !_mapMode;
            if (_mapHolder.gameObject.activeSelf != _mapMode) _mapHolder.gameObject.SetActive(_mapMode);
            UpdateMarkers();
            UpdateDots(hasLocal, localPos);

            if (!_root.activeSelf) _root.SetActive(true);

            if (_worldSince < 0f) _worldSince = Time.unscaledTime;
            if (Plugin.AutoDiagnostics.Value && !_autoDiagDone && Time.unscaledTime - _worldSince > 5f)
            {
                _autoDiagDone = true;
                Diagnostics.Dump(_cam, _mapHolder);
            }
        }

        public void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            if (_cam != null && _cam.enabled) _cam.enabled = false;
            _hasRender = false; // whatever is in the texture will be stale when we come back
        }

        // ---------------------------------------------------------------- input

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (Pressed(kb, Plugin.ToggleKey.Value)) Plugin.Enabled.Value = !Plugin.Enabled.Value;
            if (Pressed(kb, Plugin.SettingsKey.Value)) ToggleSettings();
            _settings?.HandleKeys(kb);
            _settings?.HandleMouse();
            if (Pressed(kb, Plugin.ZoomInKey.Value)) SetZoom(Plugin.Zoom.Value / ZoomStep);
            if (Pressed(kb, Plugin.ZoomOutKey.Value)) SetZoom(Plugin.Zoom.Value * ZoomStep);
            if (Pressed(kb, Plugin.DiagnosticsKey.Value)) Diagnostics.Dump(_cam, _mapHolder);
        }

        private static bool Pressed(Keyboard kb, Key key)
        {
            if (key == Key.None) return false;
            var control = kb[key];
            return control != null && control.wasPressedThisFrame;
        }

        private void ToggleSettings()
        {
            // Only in a world: the panel lives on the minimap's canvas and borrows the game's font.
            if (GameAccess.LocalComponent == null) return;
            EnsureUi();
            if (_settings == null)
            {
                var font = GameMarkers.TryGetGameFont();
                if (font == null) font = TMPro.TMP_Settings.defaultFontAsset;
                _settings = new SettingsPanel(_root.transform, font);
                Plugin.Logger.LogInfo("Settings panel built (font: " + (font != null ? font.name : "none") + ").");
            }
            _settings.Toggle();
        }

        private void LayoutSettings()
        {
            if (_settings == null || !_settings.IsOpen) return;
            float marginX = Mathf.Round(Screen.height * Plugin.MarginFraction.Value);
            float marginY = Mathf.Round(Screen.height * Plugin.MarginVertical.Value);
            _settings.Layout();
        }

        private void SetZoom(float zoom)
        {
            Plugin.Zoom.Value = Mathf.Clamp(zoom, Plugin.ZoomMin.Value, Plugin.ZoomMax.Value);
            _hasRender = false;
        }

        // --------------------------------------------------------------- camera

        private void EnsureCamera(Camera main)
        {
            // The texture covers more world than is displayed, so it can be scrolled between renders.
            int size = Mathf.RoundToInt(Plugin.TextureSize.Value * (1f + RenderPadding));
            if (_rt == null || _rt.width != size)
            {
                if (_rt != null) { if (_cam != null) _cam.targetTexture = null; _rt.Release(); UnityEngine.Object.Destroy(_rt); }
                _rt = new RenderTexture(size, size, 16);
                _rt.name = "DimraethMinimapRT";
                _rt.filterMode = FilterMode.Bilinear;
                _rt.hideFlags = HideFlags.HideAndDontSave;
                _rt.Create();
                if (_cam != null) _cam.targetTexture = _rt;
                _hasRender = false;
            }

            if (_cam != null) return;

            var go = new GameObject("DimraethMinimapCamera");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _cam = go.AddComponent<Camera>();
            _cam.CopyFrom(main);
            _cam.enabled = false;
            _cam.orthographic = true;
            _cam.rect = new Rect(0f, 0f, 1f, 1f);
            _cam.depth = main.depth - 10f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            _cam.targetTexture = _rt;
            _cam.ResetAspect();
            go.tag = "Untagged";
            SyncCullingMask(main);
            TryConfigureUrp(go);
            Plugin.Logger.LogInfo("Minimap camera created.");
        }

        // Effects that cost a lot to draw over a wide area and add nothing at minimap scale.
        private static readonly string[] SkippedLayers =
        {
            "UI", "TransparentFX", "Spells", "lightrays2d", "ModernShadows", "LightingSystem",
            "Reflections", "WaterPostProcessing", "Obstructors", "Boundary", "InvisibleForHost", "Camera",
        };

        private void SyncCullingMask(Camera main)
        {
            int mask = main.cullingMask & ~(1 << UiLayer);
            if (Plugin.SimpleRender.Value)
            {
                foreach (var name in SkippedLayers)
                {
                    int layer = LayerMask.NameToLayer(name);
                    if (layer >= 0) mask &= ~(1 << layer);
                }
            }
            if (Plugin.ShowGameIcons.Value)
            {
                int icons = LayerMask.NameToLayer("Minimap"); // the game's own (unused) minimap icon layer
                if (icons >= 0) mask |= 1 << icons;
            }
            _cam.cullingMask = mask;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TryConfigureUrp(GameObject go)
        {
            try
            {
                var data = go.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("URP camera settings not applied: " + e.Message);
            }
        }

        private void RenderIfDue(Camera main, Vector3 focus)
        {
            // Rendering the world a second time is the expensive part, so it happens rarely:
            // the camera is switched on for a single frame, and between renders the texture is only scrolled.
            float now = Time.unscaledTime;
            if (_cam.enabled) _cam.enabled = false;

            var mt = main.transform;
            float baseSize = main.orthographic ? main.orthographicSize : 8f;
            _viewHalf = baseSize * Plugin.Zoom.Value;
            float renderHalf = _viewHalf * (1f + RenderPadding);

            bool due = !_hasRender
                       || now >= _nextRender
                       || !Mathf.Approximately(renderHalf, _cam.orthographicSize)
                       || PlaneOffset(focus).magnitude > _viewHalf * RenderPadding * 0.8f;
            if (!due) return;

            _nextRender = now + Plugin.RefreshInterval.Value;
            _hasRender = true;

            if (now >= _nextMaskSync)
            {
                _nextMaskSync = now + 2f;
                SyncCullingMask(main);
                _cam.backgroundColor = main.backgroundColor;
            }

            // Slide the game camera's pose sideways (in its own view plane) until it is centred on the focus.
            Vector3 delta = focus - mt.position;
            Vector3 fwd = mt.forward;
            Vector3 inPlane = delta - fwd * Vector3.Dot(delta, fwd);
            _cam.transform.SetPositionAndRotation(mt.position + inPlane, mt.rotation);
            _cam.orthographicSize = renderHalf;
            _cam.enabled = true;
            _renderedThisFrame = true;
        }

        /// <summary>Offset of a world point from the last rendered centre, in minimap-camera right/up units.</summary>
        private Vector2 PlaneOffset(Vector3 world)
        {
            var ct = _cam.transform;
            Vector3 d = world - ct.position;
            return new Vector2(Vector3.Dot(d, ct.right), Vector3.Dot(d, ct.up));
        }

        private void ScrollView(Vector3 focus)
        {
            float renderHalf = Mathf.Max(0.01f, _cam.orthographicSize);
            float span = _viewHalf / renderHalf;               // displayed fraction of the texture
            Vector2 c = PlaneOffset(focus) / (2f * renderHalf); // focus, in uv units from the texture centre
            _view.uvRect = new Rect(0.5f + c.x - span * 0.5f, 0.5f + c.y - span * 0.5f, span, span);
        }
        // ------------------------------------------------------------- map mode

        private void UpdateMapView(GameMap.Info map)
        {
            _map = map;
            if (_mapInstanceId != map.MapId || (_mapInstance == null && Time.unscaledTime >= _nextRebuildTry))
            {
                _nextRebuildTry = Time.unscaledTime + 5f; // if building fails, do not retry every frame
                RebuildMap(map);
            }

            // Map units per world unit is map.Scale; fit _viewHalf world units into half the minimap.
            float s = (_sizePx * 0.5f) / Mathf.Max(0.01f, _viewHalf * Mathf.Abs(map.Scale.x));
            Vector2 mapPos = Vector2.Scale(map.WorldPosition, map.Scale) + map.Offset;
            _mapHolder.localScale = new Vector3(s, s, 1f);
            _mapHolder.anchoredPosition = -mapPos * s;

            if (Time.unscaledTime >= _nextFogSync)
            {
                _nextFogSync = Time.unscaledTime + 2f;
                SyncFog();
            }
        }

        private void RebuildMap(GameMap.Info map)
        {
            if (_mapInstance != null) UnityEngine.Object.Destroy(_mapInstance);
            _mapInstance = null;
            _fogParent = null;
            _mapInstanceId = map.MapId;

            // Instantiate under an inactive parent so none of the prefab's scripts wake up before we switch them off.
            _mapHolder.gameObject.SetActive(false);
            _mapInstance = UnityEngine.Object.Instantiate(map.Prefab, _mapHolder, false).TryCast<GameObject>();
            if (_mapInstance == null) return;
            _mapInstance.name = "DimraethMinimapMap";

            // The prefab holds every quest pin and quest-area circle of the region; the map screen's scripts show
            // only the active ones. We silence those scripts, so drop the whole container rather than show them all.
            var content = FindChild(_mapInstance.transform, "MapContent");
            var quests = FindChild(_mapInstance.transform, "Quests");
            if (quests != null) { quests.gameObject.SetActive(false); UnityEngine.Object.Destroy(quests.gameObject); }

            // Map coordinates are relative to MapContent. Some prefabs have an offset root, so line MapContent's
            // origin up with the holder's origin instead of trusting the root position.
            if (content != null)
            {
                Vector3 off = _mapHolder.InverseTransformPoint(content.position);
                _mapInstance.transform.localPosition -= new Vector3(off.x, off.y, 0f);
            }

            int disabled = 0, colliders = 0;
            foreach (var c in _mapInstance.GetComponentsInChildren(Il2CppInterop.Runtime.Il2CppType.Of<Component>(), true))
            {
                if (c == null) continue;
                var type = c.GetIl2CppType();

                // The fog pieces carry polygon colliders (for mouse hover on the map screen). We slide the map
                // every frame, and a moving collider makes the physics engine rebuild it every step: remove them.
                string typeName = type.Name;
                if (typeName.Contains("Collider") || typeName.Contains("Rigidbody"))
                {
                    UnityEngine.Object.Destroy(c);
                    colliders++;
                    continue;
                }

                // Keep Unity's own UI components (Image, Mask, TMP...); silence the game's map-screen logic.
                var mb = c.TryCast<MonoBehaviour>();
                if (mb != null && type.Assembly.FullName.StartsWith("Assembly-CSharp")) { mb.enabled = false; disabled++; }
            }

            _fogParent = FindFogParent(_mapInstance.transform);
            _nextFogSync = 0f;
            Plugin.Logger.LogInfo($"Minimap map built from '{map.Prefab.name}' ({disabled} game scripts silenced, {colliders} colliders removed, fog parent: {(_fogParent != null ? _fogParent.name : "none")}).");
        }

        private static Transform FindChild(Transform root, string name)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.name == name) return t;
                if (stack.Count > 64) continue; // only look near the top of the prefab
                for (int i = 0; i < t.childCount && i < 8; i++) stack.Push(t.GetChild(i));
            }
            return null;
        }

        private static Transform FindFogParent(Transform root)
        {
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.childCount > 0 && t.name.IndexOf("fog", StringComparison.OrdinalIgnoreCase) >= 0) return t;
                for (int i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
            }
            return null;
        }

        private void SyncFog()
        {
            // Respect exploration: only regions the player has discovered are uncovered, same as the map screen.
            if (_fogParent == null || !GameMap.TryGetDiscoveredRegions(_regions)) return;
            for (int i = 0; i < _fogParent.childCount; i++)
            {
                var piece = _fogParent.GetChild(i).gameObject;
                bool covered = !_regions.Contains(piece.name);
                if (piece.activeSelf != covered) piece.SetActive(covered);
            }
        }

        // ------------------------------------------------------------------- ui

        /// <summary>
        /// Throws the whole UI away so the next Tick builds a fresh one. The game destroys our objects when a
        /// multiplayer session starts; everything cached below the root (pools included) is then dead and must
        /// not be touched again - touching a destroyed Unity object throws.
        /// </summary>
        public void ResetUi()
        {
            try { if (_root != null) UnityEngine.Object.Destroy(_root); } catch { }
            _root = null;
            ForgetUi();
        }

        private void ForgetUi()
        {
            if (_settings != null && _settings.IsOpen) { Plugin.Settings.SaveOnConfigSet = true; }
            _settings = null;
            _teamDots.Clear();
            _iconPool.Clear();
            _questPins.Clear();
            _dashPool.Clear();
            _localDot = null;
            _mapInstance = null;
            _mapInstanceId = IntPtr.Zero;
            _fogParent = null;
            _nextRebuildTry = 0f;
            _layoutW = -1;
        }

        private void EnsureUi()
        {
            if (_root != null) return;
            ForgetUi(); // first build, or the game destroyed the previous UI

            _circle = MakeCircleSprite(128);

            _root = new GameObject("DimraethMinimapUI");
            _root.layer = UiLayer;
            UnityEngine.Object.DontDestroyOnLoad(_root);
            // Hidden from scene-wide searches, so game code that sweeps up stray objects leaves it alone.
            _root.hideFlags = HideFlags.HideAndDontSave;
            Plugin.Logger.LogInfo("Minimap UI built.");
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _group = _root.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false; // never eat clicks meant for the game
            // Only the settings panel is a raycast target (it overrides the group above). The raycaster is what
            // makes the game's "is the pointer over UI?" checks see the panel while the mouse is on it.
            _root.AddComponent<GraphicRaycaster>();

            _frame = NewRect("Frame", _root.transform);
            var frameImage = _frame.gameObject.AddComponent<Image>();
            frameImage.color = FrameColor;
            frameImage.raycastTarget = false;

            _maskRect = NewRect("Mask", _frame);
            var maskImage = _maskRect.gameObject.AddComponent<Image>();
            maskImage.raycastTarget = false;
            var mask = _maskRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var view = NewRect("View", _maskRect);
            Stretch(view);
            _view = view.gameObject.AddComponent<RawImage>();
            _view.texture = _rt;
            _view.raycastTarget = false;

            // Map mode: the game's map prefab lives in here, scaled and slid so the player sits in the middle.
            _mapHolder = NewRect("MapHolder", _maskRect);
            _mapHolder.anchorMin = _mapHolder.anchorMax = _mapHolder.pivot = new Vector2(0.5f, 0.5f);
            _mapHolder.sizeDelta = Vector2.zero;
            // Its own canvas: sliding the map every frame then moves one batch instead of rebuilding
            // the geometry of every map tile and fog piece.
            _mapHolder.gameObject.AddComponent<Canvas>();

            // Markers sit above the map but inside the mask: quest areas, then icons, then quest pins.
            _areaLayer = NewRect("QuestAreas", _maskRect); Stretch(_areaLayer);
            _routeLayer = NewRect("Routes", _maskRect); Stretch(_routeLayer);
            _iconLayer = NewRect("Icons", _maskRect); Stretch(_iconLayer);
            _pinLayer = NewRect("QuestPins", _maskRect); Stretch(_pinLayer);

            _dotLayer = NewRect("Dots", _frame);
            Stretch(_dotLayer);
            _localDot = NewDot("Me", LocalDotColor);

            _layoutW = -1; // force first layout
        }

        private void ApplyLayout()
        {
            _canvas.sortingOrder = Plugin.SortingOrder.Value;
            _group.alpha = Plugin.Opacity.Value;

            if (_rt != null && (_view.texture == null || _view.texture.Pointer != _rt.Pointer)) _view.texture = _rt;

            if (_layoutW == Screen.width && _layoutH == Screen.height &&
                _layoutSize == Plugin.SizeFraction.Value && _layoutMargin == Plugin.MarginFraction.Value && _layoutMarginY == Plugin.MarginVertical.Value &&
                _layoutCorner == Plugin.Position.Value && _layoutCircular == Plugin.Circular.Value)
                return;

            _layoutW = Screen.width; _layoutH = Screen.height;
            _layoutSize = Plugin.SizeFraction.Value; _layoutMargin = Plugin.MarginFraction.Value; _layoutMarginY = Plugin.MarginVertical.Value;
            _layoutCorner = Plugin.Position.Value; _layoutCircular = Plugin.Circular.Value;

            _sizePx = Mathf.Round(Screen.height * _layoutSize);
            float margin = Mathf.Round(Screen.height * _layoutMargin);
            float marginY = Mathf.Round(Screen.height * _layoutMarginY);
            bool right = _layoutCorner == Corner.TopRight || _layoutCorner == Corner.BottomRight;
            bool top = _layoutCorner == Corner.TopLeft || _layoutCorner == Corner.TopRight;
            var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);

            _frame.anchorMin = anchor; _frame.anchorMax = anchor; _frame.pivot = anchor;
            _frame.sizeDelta = new Vector2(_sizePx, _sizePx);
            _frame.anchoredPosition = new Vector2(right ? -margin : margin, top ? -marginY : marginY);

            float border = Mathf.Max(2f, Mathf.Round(_sizePx * BorderFraction));
            _maskRect.anchorMin = Vector2.zero; _maskRect.anchorMax = Vector2.one;
            _maskRect.offsetMin = new Vector2(border, border);
            _maskRect.offsetMax = new Vector2(-border, -border);

            var shape = _layoutCircular ? _circle : null;
            _frame.GetComponent<Image>().sprite = shape;
            _maskRect.GetComponent<Image>().sprite = shape;

            ResizeDot(_localDot);
            foreach (var d in _teamDots) ResizeDot(d);
        }

        private void UpdateDots(bool hasLocal, Vector3 localPos)
        {
            _localDot.gameObject.SetActive(hasLocal);
            if (hasLocal)
            {
                if (_mapMode) _localDot.anchoredPosition = Vector2.zero; // the map is centred on us
                else PlaceDot(_localDot, localPos);
            }

            _others.Clear();
            // Indoors the map is pinned to a fixed spot, so world offsets to teammates mean nothing there.
            if (Plugin.ShowTeammates.Value && !(_mapMode && _map.Indoors)) GameAccess.GetOthers(_others);

            while (_teamDots.Count < _others.Count) _teamDots.Add(NewDot("Teammate", TeammateDotColor));
            for (int i = 0; i < _teamDots.Count; i++)
            {
                bool used = i < _others.Count;
                var dot = _teamDots[i];
                if (dot.gameObject.activeSelf != used) dot.gameObject.SetActive(used);
                if (used) PlaceDot(dot, _others[i]);
            }
            _localDot.SetAsLastSibling();
        }

        private void PlaceDot(RectTransform dot, Vector3 world)
        {
            // Out-of-range teammates stick to the rim so you can still see which way they are.
            dot.anchoredPosition = ClampToRim(Normalize(world), out _) * (_sizePx * 0.5f);
        }

        /// <summary>World position to minimap coordinates: (0,0) is the centre, 1 is the edge.</summary>
        private Vector2 Normalize(Vector3 world)
        {
            if (_mapMode)
            {
                var d = new Vector2(world.x, world.y) - _map.WorldPosition;
                float unit = Mathf.Max(0.01f, _viewHalf * Mathf.Abs(_map.Scale.x));
                return Vector2.Scale(d, _map.Scale) / unit;
            }
            return (PlaneOffset(world) - PlaneOffset(_focus)) / Mathf.Max(0.01f, _viewHalf);
        }

        private Vector2 ClampToRim(Vector2 n, out bool clamped, float rim = 0.93f)
        {
            clamped = _layoutCircular ? n.magnitude > rim : Mathf.Max(Mathf.Abs(n.x), Mathf.Abs(n.y)) > rim;
            if (!clamped) return n;
            if (_layoutCircular) return n.normalized * rim;
            return new Vector2(Mathf.Clamp(n.x, -rim, rim), Mathf.Clamp(n.y, -rim, rim));
        }

        // -------------------------------------------------------------- markers

        private sealed class QuestPin
        {
            public RectTransform Root, Area;
            public Image AreaImage, Fill;
        }

        private void UpdateMarkers()
        {
            _markers.Clear();
            // Markers are placed with the map's coordinates; indoors the map is pinned, so they would be wrong.
            if (_mapMode && !_map.Indoors) GameMarkers.Collect(_markers);

            float half = _sizePx * 0.5f;
            float pxPerWorld = half / Mathf.Max(0.01f, _viewHalf);
            DrawRoutes(half);
            float iconPx = Mathf.Max(10f, _sizePx * Plugin.IconScale.Value);
            int icons = 0, pins = 0;

            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                Vector2 n = Normalize(m.World);

                if (m.Kind == MarkerKind.QuestTarget)
                {
                    // Too far to mean anything as a direction (other map / other end of the world).
                    if (n.magnitude * _viewHalf > QuestHintRange) continue;
                    Vector2 placed = ClampToRim(n, out bool clamped, MarkerRim);
                    if (pins == _questPins.Count) _questPins.Add(NewQuestPin());
                    var pin = _questPins[pins++];
                    pin.Root.gameObject.SetActive(true);
                    pin.Root.anchoredPosition = placed * half;
                    float d = iconPx * 0.31f;
                    pin.Root.sizeDelta = new Vector2(d, d);
                    pin.Fill.color = m.Color;

                    // The area is drawn where it really is (the mask clips it), independent of the pin: when the
                    // pin is pushed to the rim because the centre is out of view, part of the area can still be in view.
                    float reach = m.Radius / Mathf.Max(0.01f, _viewHalf);
                    bool showArea = m.Radius > 0.5f && n.magnitude - reach < 1.45f;
                    if (pin.Area.gameObject.activeSelf != showArea) pin.Area.gameObject.SetActive(showArea);
                    if (showArea)
                    {
                        float area = m.Radius * 2f * pxPerWorld;
                        pin.Area.sizeDelta = new Vector2(area, area);
                        pin.Area.anchoredPosition = n * half;
                        pin.AreaImage.color = new Color(m.Color.r, m.Color.g, m.Color.b, 0.22f);
                    }
                    continue;
                }

                if (m.Sprite == null) continue;
                if (m.Kind == MarkerKind.Beacon)
                {
                    // Beacons are "go here" marks: keep them on the rim when out of view, like quest targets.
                    if (n.magnitude * _viewHalf > QuestHintRange) continue;
                    n = ClampToRim(n, out _, MarkerRim);
                }
                // Other icons outside the view are simply not drawn.
                else if ((_layoutCircular ? n.magnitude : Mathf.Max(Mathf.Abs(n.x), Mathf.Abs(n.y))) > 1.05f) continue;
                if (icons == _iconPool.Count) _iconPool.Add(NewIcon());
                var image = _iconPool[icons++];
                image.gameObject.SetActive(true);
                if (image.sprite == null || image.sprite.Pointer != m.Sprite.Pointer) image.sprite = m.Sprite;
                image.color = m.Color.a > 0f && m.Kind == MarkerKind.Poi ? m.Color : Color.white;
                float s = m.Kind == MarkerKind.QuestNpc ? iconPx * 0.75f : iconPx;
                var fit = FitFor(m.Sprite);
                image.rectTransform.sizeDelta = new Vector2(s * fit.Grow, s * fit.Grow);
                image.rectTransform.anchoredPosition = n * half + fit.Shift * s;
            }

            for (int i = icons; i < _iconPool.Count; i++) if (_iconPool[i].gameObject.activeSelf) _iconPool[i].gameObject.SetActive(false);
            for (int i = pins; i < _questPins.Count; i++)
            {
                var unused = _questPins[i];
                if (unused.Root.gameObject.activeSelf) unused.Root.gameObject.SetActive(false);
                if (unused.Area.gameObject.activeSelf) unused.Area.gameObject.SetActive(false);
            }
        }

        private struct SpriteFit { public float Grow; public Vector2 Shift; }
        private readonly Dictionary<IntPtr, SpriteFit> _spriteFits = new Dictionary<IntPtr, SpriteFit>();

        /// <summary>
        /// Many of the game's map icons are a small picture inside a large transparent canvas (room for glow),
        /// so drawn at a fixed box size they come out tiny. This returns how much to enlarge the box so the
        /// visible picture fills it, and how far to shift it so the picture - not the canvas - is centred.
        /// </summary>
        private SpriteFit FitFor(Sprite sprite)
        {
            if (_spriteFits.TryGetValue(sprite.Pointer, out var fit)) return fit;
            fit = new SpriteFit { Grow = 1f, Shift = Vector2.zero };
            try
            {
                Rect r = sprite.rect;
                Vector4 pad = UnityEngine.Sprites.DataUtility.GetPadding(sprite); // left, bottom, right, top
                float visW = r.width - pad.x - pad.z, visH = r.height - pad.y - pad.w;
                float vis = Mathf.Max(visW, visH);
                if (vis > 1f)
                {
                    fit.Grow = Mathf.Clamp(Mathf.Max(r.width, r.height) / vis, 1f, 8f);
                    fit.Shift = -new Vector2((pad.x - pad.z) * 0.5f, (pad.y - pad.w) * 0.5f) / vis;
                }
                Plugin.Logger.LogInfo($"icon {sprite.name}: rect={r.size} padding={pad} grow={fit.Grow:F2}");
            }
            catch (Exception e)
            {
                Plugin.Logger.LogDebug("sprite padding unavailable: " + e.Message);
            }
            _spriteFits[sprite.Pointer] = fit;
            return fit;
        }

        // --------------------------------------------------------------- routes

        private void DrawRoutes(float half)
        {
            int used = 0;
            int count = _mapMode && !_map.Indoors ? GameMarkers.RouteCount : 0;
            float dash = Mathf.Max(5f, _sizePx * 0.03f), gap = dash * 0.8f, thickness = Mathf.Max(2.5f, _sizePx * 0.012f);

            for (int r = 0; r < count; r++)
            {
                var route = GameMarkers.Routes[r];
                var colour = new Color(route.Color.r, route.Color.g, route.Color.b, 0.9f);
                float phase = 0f; // distance into the current dash+gap cycle, carried across corners
                for (int i = 0; i + 1 < route.Points.Count; i++)
                {
                    Vector2 a = Normalize(route.Points[i]), b = Normalize(route.Points[i + 1]);
                    if (!ClipToView(ref a, ref b)) continue;
                    a *= half; b *= half;
                    float length = (b - a).magnitude;
                    if (length < 0.5f) continue;
                    Vector2 dir = (b - a) / length;
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                    float t = -phase;
                    while (t < length && used < MaxDashes)
                    {
                        float from = Mathf.Max(0f, t), to = Mathf.Min(length, t + dash);
                        if (to - from > 0.5f)
                        {
                            if (used == _dashPool.Count) _dashPool.Add(NewDash());
                            var image = _dashPool[used++];
                            if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
                            image.color = colour;
                            var rect = image.rectTransform;
                            rect.sizeDelta = new Vector2(to - from, thickness);
                            rect.anchoredPosition = a + dir * ((from + to) * 0.5f);
                            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                        }
                        t += dash + gap;
                    }
                    phase = (phase + length) % (dash + gap);
                }
            }
            for (int i = used; i < _dashPool.Count; i++) if (_dashPool[i].gameObject.activeSelf) _dashPool[i].gameObject.SetActive(false);
        }

        /// <summary>Clips a segment (minimap units, edge = 1) to a box a little larger than the view. False = fully outside.</summary>
        private static bool ClipToView(ref Vector2 a, ref Vector2 b)
        {
            const float limit = 1.1f;
            float t0 = 0f, t1 = 1f;
            Vector2 d = b - a;
            for (int axis = 0; axis < 2; axis++)
            {
                float p = axis == 0 ? d.x : d.y, q = axis == 0 ? a.x : a.y;
                if (Mathf.Abs(p) < 1e-6f) { if (q < -limit || q > limit) return false; continue; }
                float near = (-limit - q) / p, far = (limit - q) / p;
                if (near > far) { float swap = near; near = far; far = swap; }
                t0 = Mathf.Max(t0, near); t1 = Mathf.Min(t1, far);
                if (t0 > t1) return false;
            }
            Vector2 start = a;
            a = start + d * t0;
            b = start + d * t1;
            return true;
        }

        private Image NewDash()
        {
            var rect = NewRect("RouteDash", _routeLayer);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private Image NewIcon()
        {
            var rect = NewRect("Icon", _iconLayer);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            var image = rect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private QuestPin NewQuestPin()
        {
            var pin = new QuestPin();

            // Quest area first so it sits under every pin.
            pin.Area = NewRect("QuestArea", _areaLayer);
            pin.Area.anchorMin = pin.Area.anchorMax = pin.Area.pivot = new Vector2(0.5f, 0.5f);
            pin.AreaImage = pin.Area.gameObject.AddComponent<Image>();
            pin.AreaImage.sprite = _circle;
            pin.AreaImage.raycastTarget = false;

            // A diamond, like the game's own quest markers: two squares turned 45 degrees.
            pin.Root = NewRect("QuestPin", _pinLayer);
            pin.Root.anchorMin = pin.Root.anchorMax = pin.Root.pivot = new Vector2(0.5f, 0.5f);
            pin.Root.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var outline = pin.Root.gameObject.AddComponent<Image>();
            outline.color = new Color(0f, 0f, 0f, 0.9f);
            outline.raycastTarget = false;

            var fill = NewRect("Fill", pin.Root);
            fill.anchorMin = new Vector2(0.2f, 0.2f);
            fill.anchorMax = new Vector2(0.8f, 0.8f);
            fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
            pin.Fill = fill.gameObject.AddComponent<Image>();
            pin.Fill.raycastTarget = false;

            return pin;
        }

        private RectTransform NewDot(string name, Color color)
        {
            var rect = NewRect(name, _dotLayer);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

            var outline = rect.gameObject.AddComponent<Image>();
            outline.sprite = _circle;
            outline.color = new Color(0f, 0f, 0f, 0.9f);
            outline.raycastTarget = false;

            var fill = NewRect("Fill", rect);
            fill.anchorMin = new Vector2(0.18f, 0.18f);
            fill.anchorMax = new Vector2(0.82f, 0.82f);
            fill.offsetMin = Vector2.zero; fill.offsetMax = Vector2.zero;
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.sprite = _circle;
            fillImage.color = color;
            fillImage.raycastTarget = false;

            ResizeDot(rect);
            return rect;
        }

        private void ResizeDot(RectTransform dot)
        {
            float s = Mathf.Max(6f, Mathf.Round(_sizePx * DotFraction));
            dot.sizeDelta = new Vector2(s, s);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        private static Sprite MakeCircleSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "DimraethMinimapCircle";
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                byte a = (byte)(Mathf.Clamp01(r - dist) * 255f); // 1px soft edge
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
