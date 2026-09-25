using System.Runtime.CompilerServices;
using UnityEngine;

namespace DimraethMinimap
{
    internal struct VitalValue
    {
        public float Current, Maximum;
        public bool Available => Maximum > 0f;
        public float Ratio => Available ? Mathf.Clamp01(Current / Maximum) : 0f;

        public static bool TryCreate(float current, float maximum, out VitalValue result)
        {
            result = default;
            if (float.IsNaN(current) || float.IsInfinity(current) ||
                float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum < 0f) return false;
            result.Maximum = maximum;
            result.Current = Mathf.Clamp(current, 0f, maximum);
            return true;
        }
    }

    internal struct VitalSnapshot
    {
        public VitalValue Health, Stamina, Concentration;
    }

    // Only reads the same network values used by the game HUD. No RPCs, patches, or setters.
    internal static class VitalBarsAccess
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool TryRead(Component component, out VitalSnapshot snapshot)
        {
            snapshot = default;
            var player = component == null ? null : component.TryCast<Player>();
            if (player == null || !player.IsSpawned || !player.LoadingDone) return false;
            if (player.Health == null || player.MaxHealth == null ||
                player.Stamina == null || player.MaxStamina == null ||
                player.Concentration == null || player.MaxConcentration == null) return false;

            return VitalValue.TryCreate(player.Health.Value, player.MaxHealth.Value, out snapshot.Health)
                && VitalValue.TryCreate(player.Stamina.Value, player.MaxStamina.Value, out snapshot.Stamina)
                && VitalValue.TryCreate(player.Concentration.Value, player.MaxConcentration.Value, out snapshot.Concentration)
                && snapshot.Health.Available;
        }
    }
}
