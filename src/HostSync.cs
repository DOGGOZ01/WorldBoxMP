using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class HostSync
    {
        struct SentActor
        {
            public int Sig;
            public Vector2 Pos;
            public float Height;
            public bool Flip;
            public bool Moving;
        }

        public static readonly HashSet<WorldTile> DirtyTiles = new HashSet<WorldTile>();
        static readonly Dictionary<long, SentActor> _actors = new Dictionary<long, SentActor>();
        static readonly Dictionary<long, int> _buildings = new Dictionary<long, int>();
        static readonly HashSet<long> _seen = new HashSet<long>();
        static readonly List<long> _gone = new List<long>();

        static bool _ready;
        static float _tickTimer, _deltaTimer;
        static long _bytesSent;
        static float _statsTimer;
        static float _checkTimer;
        static float _metaTimer;

        public static long LastKiBps;

        static readonly string[] SectionNames = { "meta", "tiles", "buildings", "actors", "moved", "info", "actions", "effects", "drops", "sounds", "statuses", "specials", "avatars", "live", "check" };

        static readonly long[] _sectionBytes = new long[SectionNames.Length];
        static readonly long[] _sectionTicks = new long[SectionNames.Length + 2];
        static readonly System.Diagnostics.Stopwatch _clock = new System.Diagnostics.Stopwatch();
        static string _sectionReport = "", _timeReport = "";
        public static string SectionReport() => _sectionReport;

        public static void CountSection(string name, long bytes)
        {
            int i = System.Array.IndexOf(SectionNames, name);
            if (i >= 0) _sectionBytes[i] += bytes;
            _bytesSent += bytes;
        }
        public static string TimeReport() => _timeReport;

        public static bool Tracking => _ready && Net.IsHostWithPeer;

        public static void ResetAfterSnapshot()
        {
            DirtyTiles.Clear();
            _actors.Clear();
            _buildings.Clear();
            foreach (Actor a in World.world.units)
                if (Syncable(a)) _actors[a.getID()] = Capture(a);
            foreach (Building b in World.world.buildings)
                if (Syncable(b)) _buildings[b.getID()] = Sig(b);
            MetaSync.ResetAfterSnapshot();
            EffectSync.Reset();
            ActionSync.ResetHost();
            InfoSweep.Reset();
            StatusSync.Reset();
            DropSync.Reset();
            SoundSync.Reset();
            SpecialActors.ResetHost();
            AvatarSync.Reset();
            PositionSync.ResetHost();
            LiveState.ResetHost();
            _ready = true;
        }

        public static void Stop() => _ready = false;

        public static void Reload(long actorId) => _actors.Remove(actorId);
        public static void ReloadBuilding(long buildingId) => _buildings.Remove(buildingId);

        public static void Update(float tickSeconds)
        {
            if (!Tracking || SmoothLoader.isLoading()) return;
            Flow.UpdateHost();
            _tickTimer += Time.unscaledDeltaTime;
            _statsTimer += Time.unscaledDeltaTime;
            _checkTimer += Time.unscaledDeltaTime;
            _metaTimer += Time.unscaledDeltaTime;
            if (_statsTimer >= 10f)
            {
                LastKiBps = _bytesSent / 1024 / 10;
                var parts = new List<string>();
                for (int i = 0; i < SectionNames.Length; i++)
                {
                    parts.Add(SectionNames[i] + ":" + (_sectionBytes[i] / 1024f / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    _sectionBytes[i] = 0;
                }
                _sectionReport = string.Join(",", parts.ToArray());
                parts.Clear();
                for (int i = 0; i < _sectionTicks.Length; i++)
                {
                    string name = i < SectionNames.Length ? SectionNames[i] : i == SectionNames.Length ? "sweep" : "deflate";
                    double msPerSecond = _sectionTicks[i] * 1000.0 / System.Diagnostics.Stopwatch.Frequency / 10.0;
                    parts.Add(name + ":" + msPerSecond.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                    _sectionTicks[i] = 0;
                }
                _timeReport = string.Join(",", parts.ToArray());
                Debug.Log("[WorldBoxMP] stream " + LastKiBps + " KiB/s, " + _actors.Count + " actors, " + _buildings.Count + " buildings");
                _bytesSent = 0;
                _statsTimer = 0f;
            }
            if (_tickTimer < tickSeconds) return;
            _tickTimer = 0f;

            PositionSync.Tick();
            _deltaTimer += tickSeconds;
            float deltaEvery = tickSeconds * (Flow.Level == 0 ? 1f : Flow.Level == 1 ? 2f : 4f);
            if (_deltaTimer + 0.0001f < deltaEvery) return;
            float elapsed = _deltaTimer;
            _deltaTimer = 0f;
            byte[] packet = BuildDelta(elapsed);
            if (packet == null) return;
            Net.Peer.Send(Msg.Delta, packet);
            _bytesSent += packet.Length;
        }

        static byte[] BuildDelta(float tickSeconds)
        {
            var raw = new MemoryStream();
            var w = new BinaryWriter(raw);
            int changes = 0;
            int section = 0;
            long mark = 0;
            _clock.Restart();
            long t0 = 0;
            void Mark()
            {
                w.Flush();
                _sectionTicks[section] += _clock.ElapsedTicks - t0;
                t0 = _clock.ElapsedTicks;
                _sectionBytes[section++] += raw.Position - mark;
                mark = raw.Position;
            }
            var wholeActors = new HashSet<long>();
            var wholeBuildings = new HashSet<long>();
            w.Write(Clock.Now);
            w.Write(Config.time_scale_asset != null ? Config.time_scale_asset.id : "x1");
            w.Write(Config.paused);
            w.Write(EraSync.HostAge());

            float slow = 1f / Flow.Scale;
            bool fullMeta = _metaTimer >= 1f * slow || _checkTimer >= 2f * slow || MetaSync.HasListRequests;
            bool meta = fullMeta || MetaSync.HasUnsent();
            w.Write(meta);
            if (meta)
            {
                if (fullMeta) _metaTimer = 0f;
                changes += MetaSync.Write(w, fullMeta);
            }
            Mark();

            InfoSweep.Sweep(tickSeconds, id => _actors.Remove(id));
            _sectionTicks[SectionNames.Length] += _clock.ElapsedTicks - t0;
            t0 = _clock.ElapsedTicks;

            w.Write(DirtyTiles.Count);
            foreach (WorldTile t in DirtyTiles)
            {
                w.Write(t.data.tile_id);
                w.Write(t.main_type.id);
                w.Write(t.top_type != null ? t.top_type.id : "");
                w.Write((byte)((t.isOnFire() ? 1 : 0) | (t.data.frozen ? 2 : 0)));
                w.Write((byte)Mathf.Clamp(t.burned_stages, 0, 255));
            }
            changes += DirtyTiles.Count;
            DirtyTiles.Clear();
            Mark();

            _seen.Clear();
            var newBuildings = new List<Building>();
            foreach (Building b in World.world.buildings)
            {
                if (!Syncable(b)) continue;
                long id = b.getID();
                _seen.Add(id);
                int sig = Sig(b);
                if (!_buildings.TryGetValue(id, out int old) || old != sig)
                {
                    _buildings[id] = sig;
                    newBuildings.Add(b);
                }
            }
            CollectGone(_buildings.Keys);
            w.Write(_gone.Count);
            foreach (long id in _gone) { w.Write(id); _buildings.Remove(id); InfoSweep.ForgetBuilding(id); }
            changes += _gone.Count;
            w.Write(newBuildings.Count);
            foreach (Building b in newBuildings)
            {
                b.prepareForSave();
                w.Write(Json.Write(Access.BuildingData(b)));
                wholeBuildings.Add(b.getID());
                InfoSweep.SentFull(b);
                StatusSync.ResendBuilding(b.getID());
            }
            changes += newBuildings.Count;
            Mark();

            _seen.Clear();
            var newActors = new List<Actor>();
            foreach (Actor a in World.world.units)
            {
                if (!Syncable(a)) continue;
                long id = a.getID();
                _seen.Add(id);
                SentActor now = Capture(a);
                ActionSync.Watch(a, id);
                if (!_actors.TryGetValue(id, out SentActor old) || old.Sig != now.Sig)
                {
                    newActors.Add(a);
                    _actors[id] = now;
                }
            }
            CollectGone(_actors.Keys);
            w.Write(_gone.Count);
            foreach (long id in _gone) { w.Write(id); _actors.Remove(id); ActionSync.Forget(id); InfoSweep.Forget(id); PositionSync.Forget(id); }
            changes += _gone.Count;
            w.Write(newActors.Count);
            foreach (Actor a in newActors)
            {
                a.getName();
                a.prepareForSave();
                w.Write(Json.Write(Access.ActorData(a)));
                wholeActors.Add(a.getID());
                InfoSweep.SentFull(a);
                StatusSync.ResendActor(a.getID());
                AvatarSync.Resend(a.getID());
                LiveState.ResendActor(a.getID());
            }
            changes += newActors.Count;
            Mark();
            Mark();

            changes += InfoSweep.PendingCount;
            InfoSweep.Write(w, wholeActors, wholeBuildings);
            Mark();

            changes += ActionSync.PendingCount;
            ActionSync.Write(w);
            Mark();

            changes += EffectSync.PendingCount;
            EffectSync.Write(w);
            Mark();

            changes += DropSync.PendingCount;
            DropSync.Write(w);
            Mark();

            changes += SoundSync.PendingCount;
            SoundSync.Write(w);
            Mark();

            changes += StatusSync.Write(w);
            Mark();

            SpecialActors.Write(w);
            changes += SpecialActors.Count;
            Mark();

            changes += AvatarSync.Write(w);
            Mark();

            int liveBefore = LiveState.Sent;
            LiveState.Write(w, tickSeconds);
            changes += LiveState.Sent - liveBefore;
            Mark();

            bool check = _checkTimer >= 2f * slow;
            w.Write(check);
            if (check)
            {
                _checkTimer = 0f;
                Checksum.Write(w);
            }

            bool lists = fullMeta && MetaSync.HasListRequests;
            w.Write(lists);
            if (lists) MetaSync.WriteLists(w);
            bool repair = ObjectRepair.HasWork;
            w.Write(repair);
            if (repair) ObjectRepair.Write(w);
            Mark();

            if (changes == 0 && !check && !lists && !repair) return null;
            w.Flush();
            long before = _clock.ElapsedTicks;
            byte[] packed = Deflate(raw.ToArray());
            _sectionTicks[SectionNames.Length + 1] += _clock.ElapsedTicks - before;
            return packed;
        }

        static void CollectGone(IEnumerable<long> known)
        {
            _gone.Clear();
            foreach (long id in known)
                if (!_seen.Contains(id)) _gone.Add(id);
        }

        public static bool Syncable(Actor a) => a.isAlive() && a.asset != null && !a.asset.skip_save;
        public static bool Syncable(Building b) => Access.BuildingData(b) != null && Access.BuildingData(b).state != BuildingState.Removed;

        static SentActor Capture(Actor a) => new SentActor
        {
            Sig = Sig(a),
            Pos = a.current_position,
            Height = a.position_height,
            Flip = Access.Flip(a),
            Moving = a.is_moving,
        };

        public static int Sig(Actor a)
        {
            unchecked
            {
                int h = a.asset.id.GetHashCode();
                h = h * 31 + (a.kingdom != null ? a.kingdom.getID().GetHashCode() : 0);
                h = h * 31 + (a.city != null ? a.city.getID().GetHashCode() : 0);
                h = h * 31 + (int)Access.Profession(a);
                h = h * 31 + (a.isBaby() ? 1 : 0);
                return h;
            }
        }

        public static int Sig(Building b)
        {
            unchecked
            {
                int h = Access.BuildingAsset(b).id.GetHashCode();
                h = h * 31 + (int)Access.BuildingData(b).state;
                h = h * 31 + b.animData_index;
                h = h * 31 + (b.isUnderConstruction() ? 1 : 0);
                h = h * 31 + (b.city != null ? b.city.getID().GetHashCode() : 0);
                return h;
            }
        }

        public static byte[] Deflate(byte[] data)
        {
            var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                ds.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        public static byte[] Inflate(byte[] data)
        {
            var outMs = new MemoryStream();
            using (var ds = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
                ds.CopyTo(outMs);
            return outMs.ToArray();
        }
    }

    [HarmonyPatch(typeof(MapBox), nameof(MapBox.setTileDirty))]
    static class TrackDirtyTiles
    {
        static void Postfix(WorldTile pTile)
        {
            if (HostSync.Tracking) HostSync.DirtyTiles.Add(pTile);
        }
    }

    [HarmonyPatch(typeof(WorldTile), nameof(WorldTile.setBurnedStage))]
    static class TrackBurnedTiles
    {
        static void Postfix(WorldTile __instance)
        {
            if (HostSync.Tracking) HostSync.DirtyTiles.Add(__instance);
        }
    }
}
