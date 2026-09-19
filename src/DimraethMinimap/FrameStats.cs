using System.Diagnostics;

namespace DimraethMinimap
{
    /// <summary>
    /// Frame pacing evidence for bug reports: hitches counted separately while the minimap is shown and while it
    /// is hidden (F7), plus how long the plugin's own per-frame work takes. Averages hide hitches; counts do not.
    /// </summary>
    internal static class FrameStats
    {
        private const float HitchMs = 25f, BigHitchMs = 50f;

        private struct Bucket
        {
            public double Sum; public int Count, Hitches, BigHitches; public float Max;
            public void Add(float ms) { Sum += ms; Count++; if (ms > Max) Max = ms; if (ms > HitchMs) Hitches++; if (ms > BigHitchMs) BigHitches++; }
            public string Text(string label)
            {
                double avg = Count > 0 ? Sum / Count : 0, seconds = Sum / 1000.0;
                double perMin = seconds > 0 ? Hitches / seconds * 60.0 : 0;
                return $"{label}: {seconds:F0}s, avg {avg:F2} ms, max {Max:F0} ms, hitches>{HitchMs:F0}ms {Hitches} ({perMin:F1}/min), >{BigHitchMs:F0}ms {BigHitches}";
            }
        }

        private static Bucket _shown, _hidden, _redraw;
        private static double _ownSumMs; private static int _ownCount; private static double _ownMaxMs;

        private static int _skip;

        /// <summary>Leave the next frames out: the diagnostics dump itself stalls the game and would pollute the numbers.</summary>
        public static void SkipNext(int frames) { _skip = frames; }

        public static void Record(float deltaTime, bool cameraRedrawn, bool minimapShown)
        {
            if (_skip > 0) { _skip--; return; }
            if (deltaTime <= 0f || deltaTime > 1f) return; // loading screens, alt-tab
            float ms = deltaTime * 1000f;
            if (cameraRedrawn) _redraw.Add(ms);
            if (minimapShown) _shown.Add(ms); else _hidden.Add(ms);
        }

        public static void RecordOwnCost(long stopwatchTicks)
        {
            if (_skip > 0) return;
            double ms = stopwatchTicks * 1000.0 / Stopwatch.Frequency;
            _ownSumMs += ms; _ownCount++;
            if (ms > _ownMaxMs) _ownMaxMs = ms;
        }

        public static string Summary()
        {
            double own = _ownCount > 0 ? _ownSumMs / _ownCount : 0;
            string s = "frames | " + _shown.Text("minimap shown") + "\n       | " + _hidden.Text("minimap hidden")
                     + (_redraw.Count > 0 ? "\n       | " + _redraw.Text("camera redraw frames") : "")
                     + $"\n       | plugin's own work per frame: avg {own:F3} ms, max {_ownMaxMs:F2} ms";
            _shown = default; _hidden = default; _redraw = default;
            _ownSumMs = 0; _ownCount = 0; _ownMaxMs = 0;
            return s;
        }
    }
}
