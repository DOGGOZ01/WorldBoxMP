using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class MetaSync
    {
        interface IChannel
        {
            string Name { get; }
            void Snapshot();
            void Write(BinaryWriter w);
            void Read(BinaryReader r);
        }

        class Channel<TObj, TData> : IChannel
            where TObj : CoreSystemObject<TData>, new()
            where TData : BaseSystemData, new()
        {
            readonly Func<CoreSystemManager<TObj, TData>> _manager;
            readonly Dictionary<long, int> _sent = new Dictionary<long, int>();
            readonly HashSet<long> _seen = new HashSet<long>();

            public string Name { get; }
            public Action<TObj, TData> UpdateInPlace;
            public readonly List<TObj> Touched = new List<TObj>();

            public Channel(string name, Func<CoreSystemManager<TObj, TData>> manager)
            {
                Name = name;
                _manager = manager;
            }

            public bool HasUnsent()
            {
                foreach (TObj o in _manager())
                    if (!_sent.ContainsKey(o.getID())) return true;
                return false;
            }

            public void Snapshot()
            {
                _sent.Clear();
                foreach (TObj o in _manager())
                {
                    o.save();
                    _sent[o.getID()] = Json.Write(o.data).GetHashCode();
                }
            }

            public void Write(BinaryWriter w)
            {
                _seen.Clear();
                var changed = new List<string>();
                foreach (TObj o in _manager())
                {
                    long id = o.getID();
                    _seen.Add(id);
                    o.save();
                    string json = Json.Write(o.data);
                    int h = json.GetHashCode();
                    if (_sent.TryGetValue(id, out int old) && old == h) continue;
                    _sent[id] = h;
                    changed.Add(json);
                }
                var gone = new List<long>();
                foreach (long id in _sent.Keys)
                    if (!_seen.Contains(id)) gone.Add(id);
                foreach (long id in gone) _sent.Remove(id);

                w.Write(gone.Count);
                foreach (long id in gone) w.Write(id);
                w.Write(changed.Count);
                foreach (string json in changed) w.Write(json);
                Changes += changed.Count + gone.Count;
            }

            public void Read(BinaryReader r)
            {
                Touched.Clear();
                CoreSystemManager<TObj, TData> m = _manager();
                int gone = r.ReadInt32();
                for (int i = 0; i < gone; i++)
                {
                    long id = r.ReadInt64();
                    TObj o = m.get(id);
                    if (o == null) continue;
                    try { m.removeObject(o); }
                    catch (Exception e)
                    {
                        Failures++;
                        Debug.Log("[WorldBoxMP] meta remove " + Name + " " + id + ": " + e.Message);
                    }
                }
                int changed = r.ReadInt32();
                for (int i = 0; i < changed; i++)
                {
                    TData data = Json.Read<TData>(r.ReadString());
                    try
                    {
                        TObj existing = m.get(data.id);
                        if (existing != null)
                        {
                            if (UpdateInPlace != null) UpdateInPlace(existing, data);
                            else existing.loadData(data);
                            Touched.Add(existing);
                        }
                        else
                        {
                            TObj created = m.loadObject(data);
                            if (created == null) { Failures++; continue; }
                            Touched.Add(created);
                        }
                    }
                    catch (Exception e)
                    {
                        Failures++;
                        Debug.Log("[WorldBoxMP] meta " + Name + " " + data.id + ": " + e.Message);
                    }
                }
            }
        }

        static readonly AccessTools.FieldRef<MapBox, MapStats> MapStatsRef =
            AccessTools.FieldRefAccess<MapBox, MapStats>("map_stats");
        static readonly Action<MapStats> RecalcCounters =
            AccessTools.MethodDelegate<Action<MapStats>>(AccessTools.Method(typeof(MapStats), "recalcCounters"));
        static readonly Action<City, TileZone> CityRemoveZone =
            AccessTools.MethodDelegate<Action<City, TileZone>>(AccessTools.Method(typeof(City), "removeZone"));
        static readonly Action<Kingdom> KingdomLoad2 =
            AccessTools.MethodDelegate<Action<Kingdom>>(AccessTools.Method(typeof(Kingdom), "load2"));
        static readonly AccessTools.FieldRef<City, List<TileZone>> CityZones =
            AccessTools.FieldRefAccess<City, List<TileZone>>("zones");

        static readonly Channel<Kingdom, KingdomData> Kingdoms =
            new Channel<Kingdom, KingdomData>("kingdoms", () => World.world.kingdoms);
        static readonly Channel<City, CityData> Cities =
            new Channel<City, CityData>("cities", () => World.world.cities) { UpdateInPlace = UpdateCity };

        static readonly IChannel[] Channels =
        {
            new Channel<Subspecies, SubspeciesData>("subspecies", () => World.world.subspecies)
                { UpdateInPlace = (o, d) => o.setData(d) },
            new Channel<Family, FamilyData>("families", () => World.world.families),
            new Channel<Language, LanguageData>("languages", () => World.world.languages),
            new Channel<Religion, ReligionData>("religions", () => World.world.religions),
            new Channel<Item, ItemData>("items", () => World.world.items),
            new Channel<Book, BookData>("books", () => World.world.books),
            new Channel<Culture, CultureData>("cultures", () => World.world.cultures),
            new Channel<Clan, ClanData>("clans", () => World.world.clans),
            Kingdoms,
            Cities,
            new Channel<War, WarData>("wars", () => World.world.wars),
            new Channel<Army, ArmyData>("armies", () => World.world.armies),
            new Channel<Alliance, AllianceData>("alliances", () => World.world.alliances),
            new Channel<Plot, PlotData>("plots", () => World.world.plots),
            new Channel<DiplomacyRelation, DiplomacyRelationData>("diplomacy", () => World.world.diplomacy),
        };

        static int _lastStatsHash;
        public static int Changes;
        public static int Failures;

        static void UpdateCity(City city, CityData data)
        {
            var keep = new HashSet<long>();
            if (data.zones != null)
                foreach (ZoneData z in data.zones) keep.Add(((long)z.x << 32) | (uint)z.y);
            foreach (TileZone zone in new List<TileZone>(CityZones(city)))
                if (!keep.Contains(((long)zone.x << 32) | (uint)zone.y)) CityRemoveZone(city, zone);
            city.loadCity(data);
        }

        public static bool HasNewKingdomOrCity() => Kingdoms.HasUnsent() || Cities.HasUnsent();

        public static void ResetAfterSnapshot()
        {
            foreach (IChannel c in Channels) c.Snapshot();
            _lastStatsHash = Json.Write(MapStatsRef(World.world)).GetHashCode();
        }

        public static int Write(BinaryWriter w)
        {
            Changes = 0;
            foreach (IChannel c in Channels) c.Write(w);
            string stats = Json.Write(MapStatsRef(World.world));
            int h = stats.GetHashCode();
            bool statsChanged = h != _lastStatsHash;
            _lastStatsHash = h;
            w.Write(statsChanged);
            if (statsChanged) w.Write(stats);
            return Changes;
        }

        public static void Read(BinaryReader r)
        {
            foreach (IChannel c in Channels) c.Read(r);
            if (r.ReadBoolean())
            {
                MapStats stats = Json.Read<MapStats>(r.ReadString());
                MapStatsRef(World.world) = stats;
                stats.load();
                RecalcCounters(stats);
            }
        }

        public static void FinishApply()
        {
            foreach (Kingdom k in Kingdoms.Touched)
            {
                try { KingdomLoad2(k); }
                catch (Exception e) { Failures++; Debug.Log("[WorldBoxMP] kingdom link " + k.getID() + ": " + e.Message); }
            }
            foreach (City c in Cities.Touched)
            {
                try { c.loadLeader(); }
                catch (Exception e) { Failures++; Debug.Log("[WorldBoxMP] city leader " + c.getID() + ": " + e.Message); }
            }
            if (Kingdoms.Touched.Count > 0 || Cities.Touched.Count > 0) SetDrawnZonesDirty(Access.ZoneCalculator(World.world));
            Kingdoms.Touched.Clear();
            Cities.Touched.Clear();
        }

        static readonly Action<ZoneCalculator> SetDrawnZonesDirty =
            AccessTools.MethodDelegate<Action<ZoneCalculator>>(AccessTools.Method(typeof(ZoneCalculator), "setDrawnZonesDirty"));
    }
}
