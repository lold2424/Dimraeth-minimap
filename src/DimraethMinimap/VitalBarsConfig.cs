using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace DimraethMinimap
{
    internal static class VitalBarsConfig
    {
        internal static ConfigEntry<bool> Enabled, ShowNumbers, ShowStamina, ShowConcentration;
        internal static ConfigEntry<float> Scale, OffsetY, Opacity;
        internal static ConfigEntry<Key> ToggleKey;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("VitalBars", "Enabled", true, "Show health, stamina and concentration above your own character. Independent of the minimap toggle.");
            ShowStamina = config.Bind("VitalBars", "ShowStamina", true, "Show the green stamina bar.");
            ShowConcentration = config.Bind("VitalBars", "ShowConcentration", true, "Show the blue concentration (mana) bar.");
            Scale = config.Bind("VitalBars", "Scale", 1f, new ConfigDescription("Size of the three character bars.", new AcceptableValueRange<float>(0.6f, 2f)));
            OffsetY = config.Bind("VitalBars", "OffsetY", 100f, new ConfigDescription("Distance from the character's origin to the bottom of the bars, in pixels at 1080p.", new AcceptableValueRange<float>(0f, 220f)));
            Opacity = config.Bind("VitalBars", "Opacity", 0.9f, new ConfigDescription("Opacity of character bars.", new AcceptableValueRange<float>(0.2f, 1f)));
            ShowNumbers = config.Bind("VitalBars", "ShowNumbers", false, "Show current / maximum values. Increases bar height for readability.");
            ToggleKey = config.Bind("Keys", "ToggleVitalBars", Key.F6, "Show / hide character health, stamina and concentration bars.");
        }
    }
}
