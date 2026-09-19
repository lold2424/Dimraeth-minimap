using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace DimraethMinimap
{
    // Aliased: the game has its own global types with these names.
    using Key = UnityEngine.InputSystem.Key;

    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    public enum MinimapMode { Map, Camera }

    [BepInPlugin(Guid, "Dimraeth Minimap", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.yhj.dimraeth.minimap";
        public const string Version = "0.6.0";

        internal static ManualLogSource Logger;
        internal static ConfigFile Settings;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Corner> Position;
        internal static ConfigEntry<float> SizeFraction;
        internal static ConfigEntry<float> MarginFraction;
        internal static ConfigEntry<float> MarginVertical;
        internal static ConfigEntry<bool> Circular;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<int> SortingOrder;
        internal static ConfigEntry<float> Zoom;
        internal static ConfigEntry<float> ZoomMin;
        internal static ConfigEntry<float> ZoomMax;
        internal static ConfigEntry<int> TextureSize;
        internal static ConfigEntry<float> RefreshInterval;
        internal static ConfigEntry<bool> SimpleRender;
        internal static ConfigEntry<bool> ShowGameIcons;
        internal static ConfigEntry<bool> ShowTeammates;
        internal static ConfigEntry<MinimapMode> Mode;
        internal static ConfigEntry<bool> ShowQuests;
        internal static ConfigEntry<bool> ShowPois;
        internal static ConfigEntry<bool> ShowQuestNpcs;
        internal static ConfigEntry<bool> ShowBeacons;
        internal static ConfigEntry<bool> ShowMapFragments;
        internal static ConfigEntry<bool> ShowRoutes;
        internal static ConfigEntry<float> IconScale;
        internal static ConfigEntry<Key> ToggleKey;
        internal static ConfigEntry<Key> SettingsKey;
        internal static ConfigEntry<Key> ZoomInKey;
        internal static ConfigEntry<Key> ZoomOutKey;
        internal static ConfigEntry<Key> DiagnosticsKey;
        internal static ConfigEntry<bool> AutoDiagnostics;

        public override void Load()
        {
            Logger = Log;
            Settings = Config;

            Enabled = Config.Bind("General", "Enabled", true, "Show the minimap. Toggled in-game with ToggleKey.");
            ShowTeammates = Config.Bind("General", "ShowTeammates", true, "Show other players as dots (clamped to the edge when out of range).");

            Mode = Config.Bind("General", "Mode", MinimapMode.Map, "Map = crop of the game's own world map (no FPS cost, respects fog of war). Camera = live top-down view of the world (shows everything around you, but causes a hitch on every redraw).");

            ShowQuests = Config.Bind("Markers", "ShowQuests", true, "Show the targets of your tracked quests (diamond + quest area). Far targets stick to the rim as a direction hint.");
            ShowPois = Config.Bind("Markers", "ShowPointsOfInterest", true, "Show points of interest you have discovered (waypoints, landmarks), with the map screen's icons.");
            ShowQuestNpcs = Config.Bind("Markers", "ShowQuestNpcs", true, "Show quest / conversation NPC icons, as on the map screen.");
            ShowRoutes = Config.Bind("Markers", "ShowRoutes", true, "Show the game's dashed guidance routes to tracked quests and beacons, as on the map screen.");
            ShowMapFragments = Config.Bind("Markers", "ShowMapFragments", true, "Show map fragments the way the map screen does: all of them, collected ones greyed out.");
            ShowBeacons = Config.Bind("Markers", "ShowBeacons", true, "Show the map beacons you or your party placed on the map screen. Far beacons stick to the rim.");
            IconScale = Config.Bind("Markers", "IconSize", 0.2f, new ConfigDescription("Icon size as a fraction of the minimap size.", new AcceptableValueRange<float>(0.04f, 0.3f)));

            // Key renamed from "Corner" so the new default reaches people who never changed it
            // (top-left is where the party frames appear in multiplayer).
            Position = Config.Bind("Layout", "Position", Corner.BottomRight, "Screen corner the minimap sits in. The default sits above the skill bar.");
            SizeFraction = Config.Bind("Layout", "Size", 0.24f, new ConfigDescription("Minimap size as a fraction of screen height.", new AcceptableValueRange<float>(0.08f, 0.6f)));
            MarginFraction = Config.Bind("Layout", "MarginSide", 0.02f, new ConfigDescription("Gap from the left / right screen edge as a fraction of screen height.", new AcceptableValueRange<float>(0f, 0.5f)));
            MarginVertical = Config.Bind("Layout", "MarginVertical", 0.125f, new ConfigDescription("Gap from the top / bottom screen edge as a fraction of screen height. The default clears the skill bar at the bottom.", new AcceptableValueRange<float>(0f, 0.5f)));
            Circular = Config.Bind("Layout", "Circular", true, "Round minimap. Set false for a square one.");
            Opacity = Config.Bind("Layout", "Opacity", 0.95f, new ConfigDescription("Minimap opacity.", new AcceptableValueRange<float>(0.1f, 1f)));
            SortingOrder = Config.Bind("Layout", "SortingOrder", -1, "UI canvas sorting order. Raise it if game UI covers the minimap.");

            Zoom = Config.Bind("View", "Zoom", 3.5f, "How much more of the world the minimap shows compared with the game camera.");
            ZoomMin = Config.Bind("View", "ZoomMin", 1.5f, "Closest zoom.");
            ZoomMax = Config.Bind("View", "ZoomMax", 10f, "Farthest zoom.");
            TextureSize = Config.Bind("View", "TextureSize", 384, new ConfigDescription("Minimap render resolution in pixels. Lower it if you lose FPS.", new AcceptableValueRange<int>(128, 1024)));
            RefreshInterval = Config.Bind("View", "RefreshInterval", 1.0f, new ConfigDescription("Seconds between minimap redraws. The map scrolls smoothly in between; only moving things (icons, doors) wait for the redraw. Raise it if you feel hitches.", new AcceptableValueRange<float>(0.1f, 10f)));
            SimpleRender = Config.Bind("View", "SimpleRender", true, "Skip spell effects, light rays, reflections and similar layers on the minimap. Much cheaper to draw.");
            ShowGameIcons = Config.Bind("View", "ShowGameIcons", true, "Draw the game's own minimap icon layer (monster heads etc.) on the minimap.");

            // Both keys renamed: F7 used to be the show / hide key and is now the settings panel.
            SettingsKey = Config.Bind("Keys", "OpenSettings", Key.F7, "Open / close the in-game settings panel (arrow keys to select and adjust).");
            ToggleKey = Config.Bind("Keys", "ToggleMinimap", Key.F8, "Show / hide the minimap.");
            ZoomInKey = Config.Bind("Keys", "ZoomIn", Key.PageUp, "Zoom in.");
            ZoomOutKey = Config.Bind("Keys", "ZoomOut", Key.PageDown, "Zoom out.");
            DiagnosticsKey = Config.Bind("Keys", "Diagnostics", Key.F9, "Write scene / camera details to BepInEx/LogOutput.log (for bug reports).");
            AutoDiagnostics = Config.Bind("Debug", "AutoDiagnostics", false, "Write diagnostics once automatically a few seconds after entering a world.");

            AddComponent<MinimapBehaviour>();
            Log.LogInfo($"Dimraeth Minimap {Version} loaded.");
        }
    }
}
