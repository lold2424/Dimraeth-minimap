using System;
using UnityEngine;

namespace DimraethMinimap
{
    /// <summary>Thin Unity hook. All logic lives in <see cref="MinimapController"/>.</summary>
    public class MinimapBehaviour : MonoBehaviour
    {
        private const int MaxConsecutiveErrors = 30;

        private MinimapController _controller;
        private int _errors;

        public MinimapBehaviour(IntPtr ptr) : base(ptr) { }

        private void Update()
        {
            try
            {
                _controller ??= new MinimapController();
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                _controller.Tick();
                FrameStats.RecordOwnCost(System.Diagnostics.Stopwatch.GetTimestamp() - started);
                _errors = 0;
            }
            catch (Exception e)
            {
                // Never let the minimap take the game down: rebuild the UI once, and after repeated failures switch off.
                if (++_errors == 1)
                {
                    Plugin.Logger.LogError("Minimap update failed (rebuilding the UI): " + e);
                    try { _controller?.ResetUi(); } catch { }
                }
                if (_errors >= MaxConsecutiveErrors)
                {
                    Plugin.Logger.LogError("Minimap disabled after repeated errors (game update?). The game itself is unaffected.");
                    try { _controller?.Hide(); } catch { }
                    enabled = false;
                }
            }
        }
    }
}
