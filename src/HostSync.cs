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
        static float _tickTimer;
        static long _bytesSent;
        static float _statsTimer;
        static float _checkTimer;
        static float _metaTimer;

        public static long LastKiBps;

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
            _ready = true;
        }

        public static void Stop() => _ready = false;

        public static void Update(float tickSeconds)
        {
            if (!Tracking || SmoothLoader.isLoading()) return;
            _tickTimer += Time.unscaledDeltaTime;
            _statsTimer += Time.unscaledDeltaTime;
            _checkTimer += Time.unscaledDeltaTime;
            _metaTimer += Time.unscaledDeltaTime;
            if (_statsTimer >= 10f)
            {
                LastKiBps = _bytesSent / 1024 / 10;
                Debug.Log("[WorldBoxMP] stream " + LastKiBps + " KiB/s, " + _actors.Count + " actors, " + _buildings.Count + " buildings");
                _bytesSent = 0;
                _statsTimer = 0f;
            }
            if (_tickTimer < tickSeconds) return;
            _tickTimer = 0f;

            byte[] packet = BuildDelta();
            if (packet == null) return;
            Net.Peer.Send(Msg.Delta, packet);
            _bytesSent += packet.Length;
        }

        static byte[] BuildDelta()
        {
            var raw = new MemoryStream();
            var w = new BinaryWriter(raw);
            int changes = 0;
            w.Write(Clock.Now);

            bool meta = _metaTimer >= 1f || _checkTimer >= 2f || MetaSync.HasNewKingdomOrCity();
            w.Write(meta);
            if (meta)
            {
                _metaTimer = 0f;
                changes += MetaSync.Write(w);
            }

            w.Write(DirtyTiles.Count);
            foreach (WorldTile t in DirtyTiles)
            {
                w.Write(t.data.tile_id);
                w.Write(t.main_type.id);
                w.Write(t.top_type != null ? t.top_type.id : "");
                w.Write(t.isOnFire());
            }
            changes += DirtyTiles.Count;
            DirtyTiles.Clear();

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
            foreach (long id in _gone) { w.Write(id); _buildings.Remove(id); }
            changes += _gone.Count;
            w.Write(newBuildings.Count);
            foreach (Building b in newBuildings)
            {
                b.prepareForSave();
                w.Write(Json.Write(Access.BuildingData(b)));
            }
            changes += newBuildings.Count;

            _seen.Clear();
            var newActors = new List<Actor>();
            var moved = new List<Actor>();
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
                else if (ControllableUnit.isControllingUnit(a) || (old.Pos - now.Pos).sqrMagnitude > 0.0004f || Mathf.Abs(old.Height - now.Height) > 0.02f || old.Flip != now.Flip || old.Moving != now.Moving)
                {
                    moved.Add(a);
                    _actors[id] = now;
                }
            }
            CollectGone(_actors.Keys);
            w.Write(_gone.Count);
            foreach (long id in _gone) { w.Write(id); _actors.Remove(id); ActionSync.Forget(id); }
            changes += _gone.Count;
            w.Write(newActors.Count);
            foreach (Actor a in newActors)
            {
                a.prepareForSave();
                w.Write(Json.Write(Access.ActorData(a)));
            }
            changes += newActors.Count;
            w.Write(moved.Count);
            foreach (Actor a in moved)
            {
                w.Write(a.getID());
                w.Write(a.current_position.x);
                w.Write(a.current_position.y);
                w.Write(a.position_height);
                w.Write((byte)((Access.Flip(a) ? 1 : 0) | (a.is_moving ? 2 : 0) | (Access.UnderForces(a) ? 4 : 0)));
            }
            changes += moved.Count;

            changes += ActionSync.PendingCount;
            ActionSync.Write(w);

            changes += EffectSync.PendingCount;
            EffectSync.Write(w);

            bool check = _checkTimer >= 2f;
            w.Write(check);
            if (check)
            {
                _checkTimer = 0f;
                Checksum.Write(w);
            }

            if (changes == 0 && !check) return null;
            w.Flush();
            return Deflate(raw.ToArray());
        }

        static void CollectGone(IEnumerable<long> known)
        {
            _gone.Clear();
            foreach (long id in known)
                if (!_seen.Contains(id)) _gone.Add(id);
        }

        static bool Syncable(Actor a) => a.isAlive() && !a.asset.skip_save;
        static bool Syncable(Building b) => Access.BuildingData(b).state != BuildingState.Removed;

        static SentActor Capture(Actor a) => new SentActor
        {
            Sig = Sig(a),
            Pos = a.current_position,
            Height = a.position_height,
            Flip = Access.Flip(a),
            Moving = a.is_moving,
        };

        static int Sig(Actor a)
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

        static int Sig(Building b)
        {
            unchecked
            {
                int h = Access.BuildingAsset(b).id.GetHashCode();
                h = h * 31 + (int)Access.BuildingData(b).state;
                h = h * 31 + b.animData_index;
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
}
