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
        public const string Version = "0.3.4";

        internal static ManualLogSource Logger;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Corner> Position;
        internal static ConfigEntry<float> SizeFraction;
        internal static ConfigEntry<float> MarginFraction;
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
        internal static ConfigEntry<Key> ToggleKey;
        internal static ConfigEntry<Key> ZoomInKey;
        internal static ConfigEntry<Key> ZoomOutKey;
        internal static ConfigEntry<Key> DiagnosticsKey;
        internal static ConfigEntry<bool> AutoDiagnostics;

        public override void Load()
        {
            Logger = Log;

            Enabled = Config.Bind("General", "Enabled", true, "Show the minimap. Toggled in-game with ToggleKey.");
            ShowTeammates = Config.Bind("General", "ShowTeammates", true, "Show other players as dots (clamped to the edge when out of range).");

            Mode = Config.Bind("General", "Mode", MinimapMode.Map, "Map = crop of the game's own world map (no FPS cost, respects fog of war). Camera = live top-down view of the world (shows everything around you, but causes a hitch on every redraw).");

            Position = Config.Bind("Layout", "Corner", Corner.TopLeft, "Screen corner the minimap sits in.");
            SizeFraction = Config.Bind("Layout", "Size", 0.24f, new ConfigDescription("Minimap size as a fraction of screen height.", new AcceptableValueRange<float>(0.08f, 0.6f)));
            MarginFraction = Config.Bind("Layout", "Margin", 0.02f, new ConfigDescription("Gap from the screen edge as a fraction of screen height.", new AcceptableValueRange<float>(0f, 0.3f)));
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

            ToggleKey = Config.Bind("Keys", "Toggle", Key.F7, "Show / hide the minimap.");
            ZoomInKey = Config.Bind("Keys", "ZoomIn", Key.PageUp, "Zoom in.");
            ZoomOutKey = Config.Bind("Keys", "ZoomOut", Key.PageDown, "Zoom out.");
            DiagnosticsKey = Config.Bind("Keys", "Diagnostics", Key.F9, "Write scene / camera details to BepInEx/LogOutput.log (for bug reports).");
            AutoDiagnostics = Config.Bind("Debug", "AutoDiagnostics", false, "Write diagnostics once automatically a few seconds after entering a world.");

            AddComponent<MinimapBehaviour>();
            Log.LogInfo($"Dimraeth Minimap {Version} loaded.");
        }
    }
}
