using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class SoundSync
    {
        struct Rec
        {
            public string Path;
            public float X, Y;
            public bool VisibleOnly;
        }

        const int MaxPerTick = 40;
        static readonly List<Rec> _pending = new List<Rec>();
        static int _inTileOverload;
        static int _external;
        static bool _replaying;
        static bool _worldfallPatched;

        public static int MuteDepth;

        public static int Sent, Played;

        static bool Recording => HostSync.Tracking && _external == 0;

        public static void Reset() => _pending.Clear();
        public static int PendingCount => _pending.Count;

        static void Record(string path, float x, float y, bool visibleOnly)
        {
            if (!Recording || string.IsNullOrEmpty(path) || _pending.Count >= MaxPerTick / (Flow.Level == 0 ? 1 : Flow.Level == 1 ? 2 : 5)) return;
            foreach (Rec r in _pending)
                if (r.Path == path && Mathf.Abs(r.X - x) < 2f && Mathf.Abs(r.Y - y) < 2f) return;
            _pending.Add(new Rec { Path = path, X = x, Y = y, VisibleOnly = visibleOnly });
        }

        public static void Write(BinaryWriter w)
        {
            w.Write(_pending.Count);
            foreach (Rec r in _pending)
            {
                w.Write(r.Path);
                w.Write(r.X);
                w.Write(r.Y);
                w.Write(r.VisibleOnly);
            }
            Sent += _pending.Count;
            _pending.Clear();
        }

        public static void Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            _replaying = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    string path = r.ReadString();
                    float x = r.ReadSingle(), y = r.ReadSingle();
                    bool visibleOnly = r.ReadBoolean();
                    if (visibleOnly)
                    {
                        WorldTile t = World.world.GetTile((int)x, (int)y);
                        if (t == null || !t.zone.visible) continue;
                    }
                    try
                    {
                        MusicBox.playSound(path, x, y, false, false);
                        Played++;
                    }
                    catch { }
                }
            }
            finally { _replaying = false; }
        }

        public static void PatchWorldfall(Harmony harmony)
        {
            if (_worldfallPatched) return;
            _worldfallPatched = true;
            try
            {
                var m = AccessTools.Method(AccessTools.TypeByName("FirstPerson.Sounds"), "PlayAt");
                if (m == null) return;
                harmony.Patch(m, new HarmonyMethod(typeof(SoundSync), nameof(ExternalPrefix)),
                    finalizer: new HarmonyMethod(typeof(SoundSync), nameof(ExternalFinalizer)));
            }
            catch (System.Exception e)
            {
                Debug.Log("[WorldBoxMP] could not separate Worldfall sounds: " + e.GetBaseException().Message);
            }
        }

        static void ExternalPrefix() => _external++;
        static void ExternalFinalizer() => _external--;

        static bool ClientMutes => Net.IsClientConnected && MuteDepth > 0 && !_replaying;

        [HarmonyPatch(typeof(MusicBox), nameof(MusicBox.playSound), typeof(string), typeof(WorldTile), typeof(bool), typeof(bool))]
        static class CaptureTileSound
        {
            static bool Prefix(string pSoundPath, WorldTile pTile, bool pVisibleOnly)
            {
                _inTileOverload++;
                if (ClientMutes) return false;
                if (pTile != null) Record(pSoundPath, pTile.pos.x, pTile.pos.y, pVisibleOnly);
                return true;
            }
            static void Finalizer() => _inTileOverload--;
        }

        [HarmonyPatch(typeof(MusicBox), nameof(MusicBox.playSound), typeof(string), typeof(float), typeof(float), typeof(bool), typeof(bool))]
        static class CaptureSound
        {
            static bool Prefix(string pSoundPath, float pX, float pY, bool pVisibleOnly)
            {
                bool positional = pX != -1f && pY != -1f;
                if (positional && ClientMutes) return false;
                if (positional && _inTileOverload == 0) Record(pSoundPath, pX, pY, pVisibleOnly);
                return true;
            }
        }
    }
}
