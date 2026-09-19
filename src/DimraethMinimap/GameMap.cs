using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace DimraethMinimap
{
    /// <summary>
    /// Read-only access to the game's own world map (the one on the map screen): which map the local
    /// player is on, how world coordinates convert to map coordinates, and which regions are discovered.
    /// Like <see cref="GameAccess"/>, this soft-fails if a game update changes the classes involved.
    /// </summary>
    internal static class GameMap
    {
        internal struct Info
        {
            public IntPtr MapId;          // identity of the map asset, to notice map changes
            public GameObject Prefab;     // the map UI prefab the game itself instantiates on the map screen
            public Vector2 Scale, Offset; // map = world * Scale + Offset
            public Vector2 WorldPosition; // where to centre (indoors: the fixed spot the game uses)
            public bool Indoors;
        }

        private static bool _broken;
        public static bool Broken => _broken;

        private const float LookupInterval = 0.25f;
        private static float _nextLookup;
        private static bool _cachedOk;
        private static Info _cached;

        /// <param name="localPosition">The local player's position this frame (the only per-frame input).</param>
        public static bool TryGet(Vector3 localPosition, out Info info)
        {
            info = default;
            if (_broken) return false;
            try
            {
                // Which map we are on changes rarely; asking the game every frame costs interop calls and garbage.
                if (Time.unscaledTime >= _nextLookup)
                {
                    _nextLookup = Time.unscaledTime + LookupInterval;
                    _cached = default;
                    _cachedOk = Read(ref _cached);
                }
                if (!_cachedOk) return false;
                info = _cached;
                if (!info.Indoors) info.WorldPosition = new Vector2(localPosition.x, localPosition.y);
                return true;
            }
            catch (Exception e)
            {
                _broken = true;
                Plugin.Logger.LogWarning("Could not read the game's map data (game update?). Map mode disabled. " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Read(ref Info info)
        {
            var player = GameAccess.LocalComponent?.TryCast<Player>();
            if (player == null) return false;
            var handler = player.SceneHandler;
            if (handler == null) return false;
            var map = handler.Map;
            if (map == null || map.Map == null) return false;

            info.MapId = map.Pointer;
            info.Prefab = map.Map;
            info.Scale = map.Scale;
            info.Offset = map.Offset;
            Vector3 p = player.transform.position;
            info.Indoors = handler.MapIndoors;
            info.WorldPosition = handler.MapIndoors ? handler.IndoorMapMiddleCoordinates : new Vector2(p.x, p.y);
            return true;
        }

        /// <summary>Names of the regions whose fog the local player has lifted.</summary>
        public static bool TryGetDiscoveredRegions(HashSet<string> into)
        {
            into.Clear();
            if (_broken) return false;
            try { ReadRegions(into); return true; }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("Could not read discovered regions; fog left as authored. " + e.Message);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadRegions(HashSet<string> into)
        {
            var player = GameAccess.LocalComponent?.TryCast<Player>();
            var regions = player?.DiscoveredRegions;
            if (regions == null) return;
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (!string.IsNullOrEmpty(r)) into.Add(r);
            }
        }

        // ------------------------------------------------------------ diagnostics

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Describe(StringBuilder sb)
        {
            var player = GameAccess.LocalComponent?.TryCast<Player>();
            if (player == null) { sb.AppendLine("  no local player"); return; }
            var handler = player.SceneHandler;
            if (handler == null) { sb.AppendLine("  player has no SceneHandler"); return; }
            sb.AppendLine($"  handler scene={handler.Scene} indoors={handler.MapIndoors} indoorMiddle={handler.IndoorMapMiddleCoordinates}");
            var map = handler.Map;
            if (map == null) { sb.AppendLine("  handler has no map"); return; }
            sb.AppendLine($"  map asset={map.name} scale={map.Scale} offset={map.Offset} ppu={map.PixelsPerWorldUnit} bl={map.MapBottomLeft} tr={map.MapTopRight} zoom(min/def/max)={map.MinZoomLevel}/{map.DefaultZoomLevel}/{map.MaxZoomLevel} markerScale={map.MarkerScale}");

            var regions = player.DiscoveredRegions;
            var names = new List<string>();
            if (regions != null) for (int i = 0; i < regions.Count; i++) names.Add(regions[i]);
            sb.AppendLine($"  discovered regions ({names.Count}): {string.Join(", ", names)}");

            if (map.Map != null)
            {
                sb.AppendLine("  map prefab hierarchy:");
                DescribeTree(sb, map.Map.transform, 2, 3, 40);
            }

            var views = UnityEngine.Object.FindObjectsByType(Il2CppType.Of<MapContentView>(), FindObjectsInactive.Include, FindObjectsSortMode.None);
            sb.AppendLine($"  MapContentView instances: {views.Length}");
            foreach (var o in views)
            {
                var v = o.TryCast<MapContentView>();
                if (v == null) continue;
                sb.AppendLine($"    view active={v.gameObject.activeInHierarchy} loadedMap={(v._loadedMapData != null ? v._loadedMapData.name : "null")} zoom={v._zoomLevel}");
                if (v._mapContent != null) sb.AppendLine($"    content: size={v._mapContent.rect.size} scale={v._mapContent.localScale} pivot={v._mapContent.pivot}");
                if (v._mapInstance != null)
                {
                    var t = v._mapInstance.transform;
                    sb.AppendLine($"    instance: parent={(t.parent != null ? t.parent.name : "-")} localPos={t.localPosition} localScale={t.localScale}");
                }
                if (v._fogOfWarParent != null)
                {
                    sb.AppendLine($"    fog parent={v._fogOfWarParent.name} children={v._fogOfWarParent.childCount}");
                    for (int i = 0; i < v._fogOfWarParent.childCount && i < 40; i++)
                    {
                        var c = v._fogOfWarParent.GetChild(i);
                        sb.AppendLine($"      fog '{c.name}' active={c.gameObject.activeSelf}");
                    }
                }
                if (v._loadedMapData != null)
                {
                    Vector3 p = player.transform.position;
                    sb.AppendLine($"    game WorldToMapPosition(player)={v.WorldToMapPosition(new Vector2(p.x, p.y))}  ours={new Vector2(p.x, p.y) * map.Scale + map.Offset}");
                }
            }
        }

        private static void DescribeTree(StringBuilder sb, Transform t, int indent, int depth, int maxChildren)
        {
            var rect = t.TryCast<RectTransform>();
            var comps = new List<string>();
            foreach (var c in t.gameObject.GetComponents<Component>())
            {
                if (c == null) continue;
                string n = c.GetIl2CppType().Name;
                if (n != "RectTransform" && n != "Transform" && n != "CanvasRenderer") comps.Add(n);
            }
            string size = rect != null ? $" size={rect.rect.size} anchored={rect.anchoredPosition} pivot={rect.pivot}" : "";
            sb.AppendLine($"{new string(' ', indent * 2)}{t.name} active={t.gameObject.activeSelf} scale={t.localScale}{size} [{string.Join(",", comps)}] children={t.childCount}");
            if (depth <= 0) return;
            for (int i = 0; i < t.childCount && i < maxChildren; i++)
                DescribeTree(sb, t.GetChild(i), indent + 1, depth - 1, maxChildren);
        }
    }
}
