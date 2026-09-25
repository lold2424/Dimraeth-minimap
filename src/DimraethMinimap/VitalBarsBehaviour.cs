using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DimraethMinimap
{
    using Keyboard = UnityEngine.InputSystem.Keyboard;
    using Key = UnityEngine.InputSystem.Key;
    // Independent from MinimapBehaviour: hiding or a failure of the minimap does not stop the bars.
    public class VitalBarsBehaviour : MonoBehaviour
    {
        private VitalBarsController _controller;
        private int _errors;
        public VitalBarsBehaviour(IntPtr ptr) : base(ptr) { }

        private void LateUpdate()
        {
            try
            {
                var keyboard = Keyboard.current;
                var key = VitalBarsConfig.ToggleKey.Value;
                if (keyboard != null && key != Key.None && keyboard[key] != null && keyboard[key].wasPressedThisFrame)
                    VitalBarsConfig.Enabled.Value = !VitalBarsConfig.Enabled.Value;

                _controller ??= new VitalBarsController();
                _controller.Tick();
                _errors = 0;
            }
            catch (Exception e)
            {
                try { _controller?.Hide(); } catch { }
                if (++_errors == 1) Plugin.Logger.LogWarning("Character bars could not update: " + e);
                if (_errors >= 30)
                {
                    Plugin.Logger.LogError("Character bars disabled after repeated errors. Check compatibility with the current game version.");
                    enabled = false;
                }
            }
        }

        private void OnDisable() { _controller?.Hide(); }
        private void OnDestroy() { _controller?.Dispose(); _controller = null; }
    }
}
