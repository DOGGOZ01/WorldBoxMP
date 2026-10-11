using System.Diagnostics;
using UnityEngine;

namespace WorldBoxMP
{
    public static class Perf
    {
        private static readonly Stopwatch _watch = new Stopwatch();

        private static double _modMs;

        private static double _worstModMs;

        private static float _worstFrame;

        private static float _time;

        private static int _frames;

        public static void Begin()
        {
            _watch.Reset();
            _watch.Start();
        }

        public static void End()
        {
            _watch.Stop();
            double totalMilliseconds = _watch.Elapsed.TotalMilliseconds;
            _modMs += totalMilliseconds;
            if (totalMilliseconds > _worstModMs)
            {
                _worstModMs = totalMilliseconds;
            }
        }

        public static void Frame()
        {
            if (!PlayerNames.InSession)
            {
                _frames = 0;
                _time = (_worstFrame = 0f);
                _modMs = (_worstModMs = 0.0);
                return;
            }
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            _time += unscaledDeltaTime;
            _frames++;
            if (unscaledDeltaTime > _worstFrame)
            {
                _worstFrame = unscaledDeltaTime;
            }
            if (_time >= 10f)
            {
                SessionLog.Write("perf: " + ((float)_frames / _time).ToString("0") + " fps, slowest frame " + (_worstFrame * 1000f).ToString("0") + " ms, WorldBoxMP " + (_modMs / (double)_frames).ToString("0.00") + " ms/frame (worst " + _worstModMs.ToString("0.0") + " ms), first person " + (WorldfallBridge.InFirstPerson ? "on" : "off") + ", units " + World.world.units.Count + (Net.IsClientConnected ? (", shown attacks " + ActionSync.ShownAttacks + " shots " + ActionSync.ShownShots) : ""));
                _frames = 0;
                _time = (_worstFrame = 0f);
                _modMs = (_worstModMs = 0.0);
            }
        }
    }
}
