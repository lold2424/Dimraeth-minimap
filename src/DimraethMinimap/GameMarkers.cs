using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace DimraethMinimap
{
    internal enum MarkerKind { Poi, QuestNpc, QuestTarget, Beacon }

    /// <summary>A walking route the game has already worked out (quest guidance, beacon guidance).</summary>
    internal sealed class Route
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public Color Color;
    }

    internal struct Marker
    {
        public MarkerKind Kind;
        public Vector2 World;
        public Sprite Sprite;  // Poi / QuestNpc: the same icon the map screen uses
        public Color Color;    // QuestTarget: the game's colour for that quest category. Icons: tint (unset = none)
        public float Radius;   // QuestTarget: size of the quest area in world units (0 = a point)
    }

    /// <summary>
    /// Read-only list of what the game's own map screen and HUD already show the player:
    /// discovered points of interest, quest-giver icons and the targets of tracked quests.
    /// Nothing undiscovered is exposed. No scene searches (see GameAccess) - only lists the game keeps.
    ///
    /// The game's MapState snapshot arrays (PlayerMap.Build*) are deliberately NOT used: they are arrays of
    /// non-blittable structs, which Il2CppInterop cannot index safely. Reading them through raw memory was
    /// tried (v0.4.3) and rolled back: a wrong layout would crash the game instead of failing softly.
    /// Known gap as a result: quest targets come from the HUD indicator, which drops a quest while the
    /// player stands inside its area.
    /// </summary>
    internal static class GameMarkers
    {
        private const float FastInterval = 0.25f; // quest targets, quest NPCs (they move / change)
        private const float SlowInterval = 1.5f;  // points of interest (static)

        private static readonly List<Marker> _pois = new List<Marker>();
        private static readonly List<Marker> _live = new List<Marker>();
        private static readonly HashSet<string> _discovered = new HashSet<string>();
        private static float _nextFast, _nextSlow;
        private static bool _poiBroken, _npcBroken, _questBroken, _beaconBroken;
        private static Sprite _beaconSprite, _fragmentSprite;
        private static int _fragmentCount;
        private static bool _routeBroken;
        private static readonly List<Route> _routes = new List<Route>();
        private static int _routeCount;
        private static string _routeReport = "";

        /// <summary>Routes read on the last Collect. Only the first <see cref="RouteCount"/> entries are live.</summary>
        public static List<Route> Routes => _routes;
        public static int RouteCount => _routeBroken ? 0 : _routeCount;
        private static readonly List<string> _poiReport = new List<string>();
        private static IntPtr _playerId;
        private static Component _playerMap;

        public static void Collect(List<Marker> into)
        {
            into.Clear();
            float now = Time.unscaledTime;
            var player = GameAccess.LocalComponent;
            if (player == null) { _pois.Clear(); _live.Clear(); return; }

            if (player.Pointer != _playerId) { _playerId = player.Pointer; _playerMap = null; _nextFast = _nextSlow = 0f; }

            if (now >= _nextSlow)
            {
                _nextSlow = now + SlowInterval;
                _pois.Clear();
                if (Plugin.ShowPois.Value) Guard(ref _poiBroken, "points of interest", ReadPois);
            }
            if (now >= _nextFast)
            {
                _nextFast = now + FastInterval;
                _live.Clear();
                if (Plugin.ShowQuestNpcs.Value) Guard(ref _npcBroken, "quest NPC icons", ReadQuestNpcs);
                if (Plugin.ShowQuests.Value) Guard(ref _questBroken, "quest targets", ReadQuestTargets);
                if (Plugin.ShowBeacons.Value) Guard(ref _beaconBroken, "map beacons", ReadBeacons);
                _routeCount = 0;
                if (Plugin.ShowRoutes.Value) Guard(ref _routeBroken, "routes", ReadRoutes);
            }

            into.AddRange(_pois);
            into.AddRange(_live);
        }

        private static void Guard(ref bool broken, string what, Action read)
        {
            if (broken) return;
            try { read(); }
            catch (Exception e)
            {
                broken = true;
                Plugin.Logger.LogWarning($"Could not read {what} (game update?); they are left off the minimap. {e.GetType().Name}: {e.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static PlayerMap Map()
        {
            if (_playerMap == null)
            {
                var player = GameAccess.LocalComponent;
                _playerMap = player.GetComponent<PlayerMap>() ?? player.GetComponentInChildren<PlayerMap>();
            }
            return _playerMap?.TryCast<PlayerMap>();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadPois()
        {
            var map = Map();
            var player = GameAccess.LocalComponent.TryCast<Player>();
            var markers = map?._POIMapMarkers;
            if (markers == null || player == null) return;

            // Same rule as the map screen: a point of interest appears once the player has discovered it.
            _discovered.Clear();
            var names = player.DiscoveredPOIs;
            if (names != null) for (int i = 0; i < names.Count; i++) _discovered.Add(names[i]);

            _poiReport.Clear();
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                bool known = _discovered.Contains(m.MarkerName);
                bool usable = m.Sprite != null && m.Marker != null;
                if (_poiReport.Count < 80)
                    _poiReport.Add($"{m.MarkerName} cat={m.Category} waypoint={m.IsWaypoint} discovered={known} sprite={(m.Sprite != null ? m.Sprite.name : "-")} pos={(m.Marker != null ? m.Marker.transform.position.ToString() : "no object")}");
                if (!known || !usable) continue;
                Vector3 p = m.Marker.transform.position;
                _pois.Add(new Marker { Kind = MarkerKind.Poi, World = new Vector2(p.x, p.y), Sprite = m.Sprite });
            }

            if (Plugin.ShowMapFragments.Value) ReadFragments(map);

            // Reward spots (spellbooks, pets...) the game has already put on the player's map.
            var rewards = map._rewardMarkers;
            if (rewards == null) return;
            for (int i = 0; i < rewards.Count; i++)
            {
                var r = rewards[i];
                if (r == null || r.Marker == null) continue;
                bool found = r.Component != null && r.Component._discovered;
                if (_poiReport.Count < 80) _poiReport.Add($"[reward] {r.MarkerName} kind={r.Kind} discovered={found}");
                if (!found || r.UnobtainedSprite == null) continue;
                Vector3 p = r.Marker.transform.position;
                _pois.Add(new Marker { Kind = MarkerKind.Poi, World = new Vector2(p.x, p.y), Sprite = r.UnobtainedSprite });
            }
        }

        /// <summary>
        /// Map fragments. The map screen shows every fragment of the region, even inside fog (they are the
        /// way to lift it), and greys out the collected ones - so the minimap does exactly the same.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadFragments(PlayerMap map)
        {
            var fragments = map._mapFragmentMarkers;
            var manager = MapManager.Singleton;
            _fragmentCount = 0;
            if (fragments == null || manager == null) return;

            if (_fragmentSprite == null)
            {
                var prefab = manager.GetMapFragmentMarker();
                var image = prefab != null ? prefab.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
                _fragmentSprite = image != null ? image.sprite : null;
                if (_fragmentSprite == null) return;
            }

            for (int i = 0; i < fragments.Count; i++)
            {
                var f = fragments[i];
                if (f == null || f.Marker == null) continue;
                var fragment = f.Marker.GetComponent<MapFragment>();
                bool collected = fragment != null && fragment.IsCollected();
                Vector3 p = f.Marker.transform.position;
                _pois.Add(new Marker
                {
                    Kind = MarkerKind.Poi,
                    World = new Vector2(p.x, p.y),
                    Sprite = _fragmentSprite,
                    Color = collected ? new Color(0.55f, 0.55f, 0.55f, 0.75f) : Color.white,
                });
                _fragmentCount++;
            }
        }

        /// <summary>
        /// The dashed guidance lines of the map screen. The game's HUD indicators keep these paths up to date
        /// for their own use (screen-edge arrows); we only copy the corner points. Nothing is path-found by us.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadRoutes()
        {
            var map = Map();
            if (map == null) return;
            var report = new StringBuilder();

            var quests = map._questIndicator;
            var caches = quests != null ? quests._pathCaches : null;
            if (caches != null)
            {
                int active = Mathf.Min(quests._activePathCount, caches.Count);
                report.Append($"quest paths active={active}/{caches.Count}");
                for (int i = 0; i < active; i++)
                {
                    var c = caches[i];
                    if (c == null || !c.Valid) continue;
                    var corners = c.TrimmedCorners;
                    var ahead = c.AheadRoutePoints;
                    report.Append($" [corners={(corners != null ? corners.Count : -1)} ahead={(ahead != null ? ahead.Count : -1)} hasAhead={c.HasAhead}]");
                    var points = corners != null && corners.Count > 1 ? corners : ahead;
                    AddRoute(points, quests.ColourFor(c.Category));
                }
            }

            var beacons = map._markerIndicator;
            var followers = beacons != null ? beacons._followers : null;
            if (followers != null)
            {
                report.Append($" | beacon followers={followers.Count}");
                for (int i = 0; i < followers.Count; i++)
                {
                    var f = followers[i];
                    if (f == null) continue;
                    var route = f._route;
                    report.Append($" [route={(route != null ? route.Count : -1)}]");
                    AddRoute(route, beacons.BannerColour);
                }
            }
            _routeReport = report.ToString();
        }

        private static void AddRoute(Il2CppSystem.Collections.Generic.List<Vector3> points, Color colour)
        {
            if (points == null) return;
            int n = points.Count;
            if (n < 2 || n > 512) return;
            if (_routeCount == _routes.Count) _routes.Add(new Route());
            var route = _routes[_routeCount++];
            route.Points.Clear();
            for (int i = 0; i < n; i++) { Vector3 p = points[i]; route.Points.Add(new Vector2(p.x, p.y)); }
            route.Color = colour.a > 0f ? colour : new Color(1f, 0.3f, 0.3f, 1f);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadBeacons()
        {
            // Beacons the player or their party dropped on the map screen ("place marker").
            var map = Map();
            var manager = MapManager.Singleton;
            var list = manager != null ? manager.CustomMarkers : null;
            if (map == null || list == null) return;

            if (_beaconSprite == null)
            {
                var prefab = manager.GetCustomMapMarker();
                var image = prefab != null ? prefab.GetComponentInChildren<UnityEngine.UI.Image>(true) : null;
                _beaconSprite = image != null ? image.sprite : null;
            }

            int count = list.Count;
            for (int i = 0; i < count; i++)
            {
                CustomMarkerData data = list[i];
                if (!map.IsMarkerFromMeOrMyParty(data.ownerId)) continue;
                _live.Add(new Marker
                {
                    Kind = MarkerKind.Beacon,
                    World = new Vector2(data.worldPosition.x, data.worldPosition.y),
                    Sprite = _beaconSprite,
                    Color = new Color(1f, 0.85f, 0.25f, 1f),
                });
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadQuestNpcs()
        {
            var npcs = Map()?._activeQuestNPCs;
            var icons = MapManager.Singleton;
            if (npcs == null || icons == null) return;

            for (int i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                if (npc == null || npc._hiddenForInteraction) continue;
                Sprite sprite = npc.MapIconType switch
                {
                    NpcMapIconType.Quest => icons.QuestNPCSprite,
                    NpcMapIconType.SideQuest => icons.SideQuestNPCSprite,
                    NpcMapIconType.Conversation => icons.ConversationNPCSprite,
                    _ => null,
                };
                if (sprite == null) continue;
                Vector3 p = npc.transform.position;
                _live.Add(new Marker { Kind = MarkerKind.QuestNpc, World = new Vector2(p.x, p.y), Sprite = sprite });
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReadQuestTargets()
        {
            // The HUD's quest direction indicator keeps the world targets of the tracked quests up to date.
            var indicator = Map()?._questIndicator;
            var positions = indicator?._targetPositions;
            if (positions == null) return;
            var radii = indicator._targetRadii;
            var categories = indicator._targetCategories;

            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i];
                var marker = new Marker { Kind = MarkerKind.QuestTarget, World = new Vector2(p.x, p.y), Color = new Color(1f, 0.82f, 0.2f, 1f) };
                if (radii != null && i < radii.Count) marker.Radius = radii[i];
                if (categories != null && i < categories.Count) marker.Color = indicator.ColourFor(categories[i]);
                _live.Add(marker);
            }
        }

        public static void Describe(StringBuilder sb)
        {
            sb.AppendLine($"  points of interest shown={_pois.Count} (discovered names={_discovered.Count}) broken={_poiBroken}");
            int npcs = 0, quests = 0, beacons = 0;
            foreach (var m in _live) { if (m.Kind == MarkerKind.QuestNpc) npcs++; else if (m.Kind == MarkerKind.QuestTarget) quests++; else if (m.Kind == MarkerKind.Beacon) beacons++; }
            sb.AppendLine($"  quest NPC icons={npcs} broken={_npcBroken} | quest targets={quests} broken={_questBroken} | beacons={beacons} broken={_beaconBroken} sprite={(_beaconSprite != null ? _beaconSprite.name : "-")}");
            sb.AppendLine($"  routes={RouteCount} broken={_routeBroken} :: {_routeReport}");
            sb.AppendLine($"  map fragments={_fragmentCount} sprite={(_fragmentSprite != null ? _fragmentSprite.name : "-")}");
            foreach (var line in _poiReport) sb.AppendLine("    poi " + line);
            foreach (var m in _live)
                if (m.Kind == MarkerKind.QuestTarget) sb.AppendLine($"    quest target world={m.World} radius={m.Radius} colour={m.Color}");
        }
    }
}
