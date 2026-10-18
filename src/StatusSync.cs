using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class StatusSync
    {
        const string Possessed = "possessed";

        static readonly Dictionary<long, int> _actors = new Dictionary<long, int>();
        static readonly Dictionary<long, int> _buildings = new Dictionary<long, int>();
        static readonly HashSet<long> _seen = new HashSet<long>();
        static readonly List<long> _drop = new List<long>();
        static readonly List<BaseSimObject> _changed = new List<BaseSimObject>();

        public static int Sent, Applied, Failures;

        public static void Reset()
        {
            _actors.Clear();
            _buildings.Clear();
        }

        public static void ResendActor(long id) => _actors.Remove(id);
        public static void ResendBuilding(long id) => _buildings.Remove(id);

        static int SetHash(BaseSimObject o)
        {
            if (o.countStatusEffects() == 0) return 0;
            int h = 0;
            unchecked
            {
                foreach (string id in o.getStatusesIds())
                    if (id != Possessed) h += id.GetHashCode() * 16777619 + 7;
            }
            return h;
        }

        public static int Write(BinaryWriter w)
        {
            _changed.Clear();
            Collect(World.world.units, _actors);
            int actorCount = _changed.Count;
            Collect(World.world.buildings, _buildings);

            w.Write(_changed.Count);
            for (int i = 0; i < _changed.Count; i++)
            {
                BaseSimObject o = _changed[i];
                w.Write(i < actorCount);
                w.Write(o.getID());
                var list = new List<Status>();
                foreach (Status s in o.getStatuses())
                    if (!s.is_finished && s.asset.id != Possessed) list.Add(s);
                w.Write((byte)Mathf.Min(list.Count, 255));
                for (int j = 0; j < list.Count && j < 255; j++)
                {
                    w.Write(list[j].asset.id);
                    w.Write((float)Math.Max(0.0, list[j].getRemainingTime()));
                }
            }
            Sent += _changed.Count;
            return _changed.Count;
        }

        static void Collect<T>(IEnumerable<T> objects, Dictionary<long, int> sent) where T : BaseSimObject
        {
            _seen.Clear();
            foreach (T o in objects)
            {
                if (!o.isAlive()) continue;
                long id = o.getID();
                int h = SetHash(o);
                if (h == 0 && !sent.ContainsKey(id)) continue;
                _seen.Add(id);
                if (sent.TryGetValue(id, out int old) && old == h) continue;
                if (h == 0) sent.Remove(id); else sent[id] = h;
                _changed.Add(o);
            }
            _drop.Clear();
            foreach (long id in sent.Keys)
                if (!_seen.Contains(id)) _drop.Add(id);
            foreach (long id in _drop) sent.Remove(id);
        }

        public static void Read(BinaryReader r)
        {
            MapBox world = World.world;
            int n = r.ReadInt32();
            var wanted = new Dictionary<string, float>();
            for (int i = 0; i < n; i++)
            {
                bool actor = r.ReadBoolean();
                long id = r.ReadInt64();
                int count = r.ReadByte();
                wanted.Clear();
                for (int j = 0; j < count; j++)
                {
                    string sid = r.ReadString();
                    wanted[sid] = r.ReadSingle();
                }
                BaseSimObject o = actor ? (BaseSimObject)world.units.get(id) : world.buildings.get(id);
                if (o == null) continue;
                try
                {
                    Apply(o, wanted);
                    Applied++;
                }
                catch (Exception e)
                {
                    if (Failures++ < 20) SessionLog.Write("status apply " + (actor ? "unit " : "building ") + id + " failed: " + e.Message);
                }
            }
        }

        static void Apply(BaseSimObject o, Dictionary<string, float> wanted)
        {
            Dictionary<string, Status> active = Access.ActiveStatuses(o);
            var remove = new List<Status>();
            foreach (var kv in active)
                if (kv.Key != Possessed && !wanted.ContainsKey(kv.Key)) remove.Add(kv.Value);
            foreach (Status s in remove)
            {
                s.finish();
                World.world.statuses.removeObject(s);
            }
            foreach (var kv in wanted)
            {
                if (active.TryGetValue(kv.Key, out Status have))
                {
                    if (kv.Value > 0f) have.setDuration(kv.Value);
                    continue;
                }
                StatusAsset asset = AssetManager.status.get(kv.Key);
                if (asset == null) continue;
                Status s = World.world.statuses.newStatus(o, asset, kv.Value);
                active[kv.Key] = s;
            }
            Access.HasAnyStatus(o) = active.Count > 0;
            Access.UpdateCachedStatuses(o);
            o.setStatsDirty();
        }

        public static void UpdateClient()
        {
            float elapsed = Access.Elapsed(World.world);
            List<Status> list = World.world.statuses.list;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Status s = list[i];
                BaseSimObject o = s.sim_object;
                if (s.is_finished || o == null || !o.isAlive())
                {
                    s.finish();
                    World.world.statuses.removeObject(s);
                    continue;
                }
                Access.UpdateAnimationFrame(s, elapsed);
            }
        }
    }
}
