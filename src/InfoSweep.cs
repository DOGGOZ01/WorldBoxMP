using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldBoxMP
{
    public static class InfoSweep
    {
        class Print
        {
            public Dictionary<string, int> Fields;
            public int Hash;
            public double Visited;
        }

        static readonly HashSet<string> SkipActor = new HashSet<string> { "x", "y" };
        static readonly string[] Gear = { "saved_traits", "saved_items" };

        static readonly Dictionary<long, Print> _actors = new Dictionary<long, Print>();
        static readonly Dictionary<long, Print> _buildings = new Dictionary<long, Print>();
        static readonly List<Actor> _actorList = new List<Actor>();
        static readonly List<Building> _buildingList = new List<Building>();
        static int _actorCursor, _buildingCursor;
        static readonly List<KeyValuePair<long, string>> _softActors = new List<KeyValuePair<long, string>>();
        static readonly List<KeyValuePair<long, string>> _softBuildings = new List<KeyValuePair<long, string>>();
        static readonly List<Actor> _pushed = new List<Actor>();

        public static int SoftActors, SoftBuildings, GearReloads;

        static float FarPeriod => Mathf.Max(1f, MPMod.Cfg != null ? MPMod.Cfg.InfoSeconds : 20f);

        static float Period(int priority, bool building)
        {
            float p = priority == 0 ? 2f : priority == 1 ? 5f : FarPeriod;
            if (building) p *= 2.5f;
            if (Flow.Level > 0 && priority > 0) p /= Flow.Scale;
            return p;
        }

        public static void Reset()
        {
            _actors.Clear();
            _buildings.Clear();
            _softActors.Clear();
            _softBuildings.Clear();
            _pushed.Clear();
            double now = Clock.Now;
            foreach (Actor a in World.world.units)
                if (HostSync.Syncable(a)) _actors[a.getID()] = new Print { Fields = ActorFields(a, out _), Hash = ActorHash(a), Visited = now };
            foreach (Building b in World.world.buildings)
                if (HostSync.Syncable(b)) _buildings[b.getID()] = new Print { Fields = null, Visited = now - 1000 };
        }

        public static void SentFull(Actor a) => _actors[a.getID()] = new Print { Fields = ActorFields(a, out _), Hash = ActorHash(a), Visited = Clock.Now };
        public static void SentFull(Building b) => _buildings[b.getID()] = new Print { Fields = BuildingFields(b, out _), Visited = Clock.Now };

        public static void Forget(long actorId) => _actors.Remove(actorId);
        public static void ForgetBuilding(long buildingId) => _buildings.Remove(buildingId);

        public static void Push(Actor a) => _pushed.Add(a);

        static Dictionary<string, int> ActorFields(Actor a, out JObject json)
        {
            a.getName();
            a.prepareForSave();
            return Json.Fields(Access.ActorData(a), out json);
        }

        static int ActorHash(Actor a)
        {
            ActorData d = Access.ActorData(a);
            int x = d.x, y = d.y;
            d.x = 0;
            d.y = 0;
            int h = Json.Write(d).GetHashCode();
            d.x = x;
            d.y = y;
            return h;
        }

        static Dictionary<string, int> BuildingFields(Building b, out JObject json)
        {
            b.prepareForSave();
            return Json.Fields(Access.BuildingData(b), out json);
        }

        public static void Sweep(float tickSeconds, System.Action<long> reload)
        {
            _softActors.Clear();
            _softBuildings.Clear();
            double now = Clock.Now;
            var names = new List<string>();

            foreach (Actor a in _pushed)
            {
                if (a == null || !HostSync.Syncable(a)) continue;
                VisitActor(a, now, reload, names, force: true);
            }
            _pushed.Clear();

            int budget = Mathf.Max(8, Mathf.RoundToInt(120 * Flow.Scale * tickSeconds / 0.05f));
            if (_actorCursor >= _actorList.Count)
            {
                _actorList.Clear();
                foreach (Actor a in World.world.units) _actorList.Add(a);
                _actorCursor = 0;
            }
            int looked = 0;
            while (budget > 0 && _actorCursor < _actorList.Count && looked < 600)
            {
                Actor a = _actorList[_actorCursor++];
                looked++;
                if (a == null || !HostSync.Syncable(a) || SpecialActors.IsSpecial(a)) continue;
                if (!_actors.TryGetValue(a.getID(), out Print p)) continue;
                if (now - p.Visited < Period(ViewSync.Priority(a.current_position), false)) continue;
                VisitActor(a, now, reload, names, force: false);
                budget--;
            }

            int bBudget = Mathf.Max(4, budget);
            if (_buildingCursor >= _buildingList.Count)
            {
                _buildingList.Clear();
                foreach (Building b in World.world.buildings) _buildingList.Add(b);
                _buildingCursor = 0;
            }
            looked = 0;
            while (bBudget > 0 && _buildingCursor < _buildingList.Count && looked < 1500)
            {
                Building b = _buildingList[_buildingCursor++];
                looked++;
                if (b == null || !HostSync.Syncable(b)) continue;
                long id = b.getID();
                if (!_buildings.TryGetValue(id, out Print p)) continue;
                if (now - p.Visited < Period(ViewSync.Priority(b.current_tile != null ? (Vector2)b.current_tile.posV3 : Vector2.zero), true)) continue;
                b.prepareForSave();
                int bHash = Json.Write(Access.BuildingData(b)).GetHashCode();
                if (p.Fields != null && p.Hash == bHash) { p.Visited = now; bBudget--; continue; }
                Dictionary<string, int> now2 = Json.Fields(Access.BuildingData(b), out JObject json);
                string part = p.Fields == null ? null : Json.Changed(json, now2, p.Fields, null, null);
                _buildings[id] = new Print { Fields = now2, Hash = bHash, Visited = now };
                if (part != null) _softBuildings.Add(new KeyValuePair<long, string>(id, part));
                bBudget--;
            }
        }

        static void VisitActor(Actor a, double now, System.Action<long> reload, List<string> names, bool force)
        {
            long id = a.getID();
            _actors.TryGetValue(id, out Print old);
            a.getName();
            a.prepareForSave();
            int hash = ActorHash(a);
            if (old != null && old.Hash == hash && !force)
            {
                old.Visited = now;
                return;
            }
            Dictionary<string, int> fields = Json.Fields(Access.ActorData(a), out JObject json);
            names.Clear();
            string part = Json.Changed(json, fields, old?.Fields, SkipActor, names);
            _actors[id] = new Print { Fields = fields, Hash = hash, Visited = now };
            if (part == null) return;
            foreach (string g in Gear)
            {
                if (!names.Contains(g)) continue;
                GearReloads++;
                reload(id);
                return;
            }
            _softActors.Add(new KeyValuePair<long, string>(id, part));
        }

        public static int PendingCount => _softActors.Count + _softBuildings.Count;

        public static void Write(BinaryWriter w, HashSet<long> sentWholeActors, HashSet<long> sentWholeBuildings)
        {
            int count = 0;
            foreach (var kv in _softActors) if (!sentWholeActors.Contains(kv.Key)) count++;
            w.Write(count);
            foreach (var kv in _softActors)
            {
                if (sentWholeActors.Contains(kv.Key)) continue;
                w.Write(kv.Key);
                w.Write(kv.Value);
                SoftActors++;
            }
            count = 0;
            foreach (var kv in _softBuildings) if (!sentWholeBuildings.Contains(kv.Key)) count++;
            w.Write(count);
            foreach (var kv in _softBuildings)
            {
                if (sentWholeBuildings.Contains(kv.Key)) continue;
                w.Write(kv.Key);
                w.Write(kv.Value);
                SoftBuildings++;
            }
            _softActors.Clear();
            _softBuildings.Clear();
        }

        public static void Read(BinaryReader r)
        {
            MapBox world = World.world;
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                string part = r.ReadString();
                Actor a = world.units.get(id);
                ActorData d = a != null ? Access.ActorData(a) : null;
                if (d == null) continue;
                try
                {
                    Json.Populate(part, d);
                    a.setStatsDirty();
                    PlayerEdits.HostAppliedUnitPartial(a, part);
                    SoftActors++;
                }
                catch (System.Exception e)
                {
                    SessionLog.Write("unit data " + id + " failed: " + e.GetBaseException().Message);
                }
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                string part = r.ReadString();
                Building b = world.buildings.get(id);
                BuildingData d = b != null ? Access.BuildingData(b) : null;
                if (d == null) continue;
                try
                {
                    Json.Populate(part, d);
                    if (b.animData == null && !b.isUnderConstruction()) b.setAnimData(d.frameID);
                    b.setStatsDirty();
                    SoftBuildings++;
                }
                catch (System.Exception e)
                {
                    SessionLog.Write("building data " + id + " failed: " + e.GetBaseException().Message);
                }
            }
        }
    }
}
