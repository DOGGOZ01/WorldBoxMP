using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            void Write(BinaryWriter w, bool full);
            void Read(BinaryReader r);
            long Aggregate();
            void WriteList(BinaryWriter w);
            void CheckList(BinaryReader r, List<long> repair);
            void ForceResend(long id);
            bool HasUnsent();
            void ForceResendAll();
            int Count { get; }
            void Each(Action<long, object, BaseSystemData> f);
            object Find(long id);
            BaseSystemData DataOf(object o);
        }

        class Channel<TObj, TData> : IChannel
            where TObj : CoreSystemObject<TData>, new()
            where TData : BaseSystemData, new()
        {
            readonly Func<CoreSystemManager<TObj, TData>> _manager;
            readonly Dictionary<long, Dictionary<string, int>> _sent = new Dictionary<long, Dictionary<string, int>>();
            readonly Dictionary<long, int> _hash = new Dictionary<long, int>();
            readonly HashSet<long> _seen = new HashSet<long>();

            public string Name { get; }
            public Action<TObj, TData> UpdateInPlace;
            public Func<TObj, long> Extra;
            public Func<TObj, string> Describe;
            public Action<TObj> AfterApply;
            public Action<TObj> BeforeRemove;
            public Action<TData> BeforeCreate;
            public readonly List<TObj> Touched = new List<TObj>();
            readonly Dictionary<long, int> _repairs = new Dictionary<long, int>();

            public int Count => _manager().Count;

            public void Each(Action<long, object, BaseSystemData> f)
            {
                foreach (TObj o in _manager()) f(o.getID(), o, o.data);
            }

            public object Find(long id) => _manager().get(id);
            public BaseSystemData DataOf(object o) => (o as TObj)?.data;

            long Fingerprint(TObj o)
            {
                unchecked
                {
                    long h = o.getID() * 1000003L;
                    h = h * 31 + (o.data.name ?? "").GetHashCode();
                    if (o.data is MetaObjectData md) h = h * 31 + md.color_id;
                    if (Extra != null) h = h * 31 + Extra(o);
                    return h;
                }
            }

            string Text(TObj o)
            {
                string s = "\"" + o.data.name + "\"";
                if (o.data is MetaObjectData md) s += " color=" + md.color_id;
                if (Describe != null) s += " " + Describe(o);
                return s;
            }

            public long Aggregate()
            {
                long sum = 0;
                unchecked
                {
                    foreach (TObj o in _manager()) sum += Fingerprint(o) * 2654435761L + 1;
                }
                return sum;
            }

            public void WriteList(BinaryWriter w)
            {
                var all = new List<TObj>(_manager());
                w.Write(all.Count);
                foreach (TObj o in all)
                {
                    w.Write(o.getID());
                    w.Write(Fingerprint(o));
                    w.Write(Text(o));
                }
            }

            public void CheckList(BinaryReader r, List<long> repair)
            {
                CoreSystemManager<TObj, TData> m = _manager();
                int n = r.ReadInt32();
                var host = new HashSet<long>();
                for (int i = 0; i < n; i++)
                {
                    long id = r.ReadInt64();
                    long fp = r.ReadInt64();
                    string text = r.ReadString();
                    host.Add(id);
                    TObj mine = m.get(id);
                    if (mine != null && Fingerprint(mine) == fp) { _repairs.Remove(id); continue; }
                    if (mine != null && PlayerEdits.IsPending(Name, id)) continue;
                    repair.Add(id);
                    _repairs.TryGetValue(id, out int times);
                    _repairs[id] = ++times;
                    string line = "repair " + Name + " " + id + ": host " + text + " | client " + (mine != null ? Text(mine) : "missing");
                    if (times == 3) line += "  STILL DIFFERS after 3 repairs";
                    if (times <= 3) SessionLog.Write(line);
                    Repairs++;
                }
                var extra = new List<TObj>();
                foreach (TObj o in m)
                    if (!host.Contains(o.getID())) extra.Add(o);
                foreach (TObj o in extra)
                {
                    SessionLog.Write("repair " + Name + " " + o.getID() + ": not on the host, removed here | client " + Text(o));
                    Remove(m, o);
                    Repairs++;
                }
            }

            public void ForceResend(long id) { _sent.Remove(id); _hash.Remove(id); }
            public void ForceResendAll() { _sent.Clear(); _hash.Clear(); }

            void Remove(CoreSystemManager<TObj, TData> m, TObj o)
            {
                try
                {
                    BeforeRemove?.Invoke(o);
                    m.removeObject(o);
                }
                catch (Exception e)
                {
                    Failures++;
                    Debug.Log("[WorldBoxMP] meta remove " + Name + " " + o.getID() + ": " + e.Message);
                }
            }

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
                _hash.Clear();
                foreach (TObj o in _manager())
                {
                    o.save();
                    _sent[o.getID()] = Json.Fields(o.data, out _);
                    _hash[o.getID()] = Json.Write(o.data).GetHashCode();
                }
            }

            public void Write(BinaryWriter w, bool full)
            {
                _seen.Clear();
                var whole = new List<string>();
                var parts = new List<KeyValuePair<long, string>>();
                foreach (TObj o in _manager())
                {
                    long id = o.getID();
                    _seen.Add(id);
                    if (!full && _sent.ContainsKey(id)) continue;
                    o.save();
                    string whole_json = Json.Write(o.data);
                    int h = whole_json.GetHashCode();
                    bool known = _sent.TryGetValue(id, out Dictionary<string, int> old);
                    if (known && _hash.TryGetValue(id, out int was) && was == h) continue;
                    _hash[id] = h;
                    Dictionary<string, int> fields = Json.Fields(o.data, out var json);
                    if (!known)
                    {
                        whole.Add(whole_json);
                    }
                    else
                    {
                        string part = Json.Changed(json, fields, old, null, null);
                        if (part == null) continue;
                        parts.Add(new KeyValuePair<long, string>(id, part));
                    }
                    _sent[id] = fields;
                }
                var gone = new List<long>();
                if (full)
                    foreach (long id in _sent.Keys)
                        if (!_seen.Contains(id)) gone.Add(id);
                foreach (long id in gone) { _sent.Remove(id); _hash.Remove(id); }

                w.Write(gone.Count);
                foreach (long id in gone) w.Write(id);
                w.Write(whole.Count);
                foreach (string json in whole) w.Write(json);
                w.Write(parts.Count);
                foreach (var kv in parts) { w.Write(kv.Key); w.Write(kv.Value); }
                Changes += whole.Count + parts.Count + gone.Count;
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
                    if (o != null) Remove(m, o);
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
                            int oldColor = existing.data is MetaObjectData oldMeta ? oldMeta.color_id : 0;
                            if (UpdateInPlace != null) UpdateInPlace(existing, data);
                            else existing.loadData(data);
                            if (data is MetaObjectData newMeta && newMeta.color_id != oldColor) RefreshColor(existing, newMeta.color_id);
                            Touched.Add(existing);
                            AfterApply?.Invoke(existing);
                            PlayerEdits.HostApplied(Name, existing.getID(), existing, existing.data);
                        }
                        else
                        {
                            BeforeCreate?.Invoke(data);
                            TObj created = m.loadObject(data);
                            if (created == null) { Failures++; continue; }
                            Touched.Add(created);
                            AfterApply?.Invoke(created);
                            PlayerEdits.HostApplied(Name, created.getID(), created, created.data);
                        }
                    }
                    catch (Exception e)
                    {
                        Failures++;
                        Debug.Log("[WorldBoxMP] meta " + Name + " " + data.id + ": " + e.Message);
                    }
                }
                int partial = r.ReadInt32();
                for (int i = 0; i < partial; i++)
                {
                    long id = r.ReadInt64();
                    string part = r.ReadString();
                    TObj existing = m.get(id);
                    if (existing == null) continue;
                    try
                    {
                        int oldColor = existing.data is MetaObjectData oldMeta ? oldMeta.color_id : 0;
                        Json.Populate(part, existing.data);
                        if (UpdateInPlace != null) UpdateInPlace(existing, existing.data);
                        else existing.loadData(existing.data);
                        if (existing.data is MetaObjectData newMeta && newMeta.color_id != oldColor) RefreshColor(existing, newMeta.color_id);
                        Touched.Add(existing);
                        AfterApply?.Invoke(existing);
                        PlayerEdits.HostAppliedPartial(Name, id, existing.data, part);
                    }
                    catch (Exception e)
                    {
                        Failures++;
                        Debug.Log("[WorldBoxMP] meta " + Name + " " + id + " fields: " + e.Message);
                    }
                }
            }
        }

        static readonly Channel<Kingdom, KingdomData> Kingdoms =
            new Channel<Kingdom, KingdomData>("kingdoms", () => World.world.kingdoms)
            {
                Extra = k => (k.king != null ? k.king.getID() : -1) * 7919L + (k.capital != null ? k.capital.getID() : -1) * 104729L +
                             k.data.banner_background_id * 31L + k.data.banner_icon_id,
                AfterApply = k => _hostKingdomLinks[k.getID()] = new KeyValuePair<long, long>(k.data.capitalID, k.data.kingID),
                Describe = k => "king=" + (k.king != null ? k.king.getID() + " " + k.king.getName() : "none") +
                                " capital=" + (k.capital != null ? k.capital.getID() + " " + k.capital.name : "none") +
                                " banner=" + k.data.banner_background_id + "/" + k.data.banner_icon_id,
            };
        static readonly Channel<City, CityData> Cities =
            new Channel<City, CityData>("cities", () => World.world.cities)
            {
                UpdateInPlace = UpdateCity,
                AfterApply = c => _hostLeaders[c.getID()] = c.data.leaderID,
                Extra = c =>
                {
                    Kingdom k = Access.CityKingdom(c);
                    long h = (k != null ? k.getID() : -1) * 7919L + (c.leader != null ? c.leader.getID() : -1) * 104729L;
                    unchecked
                    {
                        foreach (TileZone z in Access.MetaSyncCityZones(c)) h += ((long)z.x * 7919 + z.y) * 31 + 1;
                    }
                    return h;
                },
                Describe = c =>
                {
                    Kingdom k = Access.CityKingdom(c);
                    return "kingdom=" + (k != null ? k.getID() + " " + k.name : "none") + " zones=" + Access.MetaSyncCityZones(c).Count +
                           " leader=" + (c.leader != null ? c.leader.getID().ToString() : "none");
                },
            };

        static readonly IChannel[] Channels =
        {
            new Channel<Subspecies, SubspeciesData>("subspecies", () => World.world.subspecies)
                { UpdateInPlace = (o, d) => o.setData(d) },
            new Channel<Family, FamilyData>("families", () => World.world.families),
            new Channel<Language, LanguageData>("languages", () => World.world.languages),
            new Channel<Religion, ReligionData>("religions", () => World.world.religions),
            new Channel<Item, ItemData>("items", () => World.world.items) { BeforeRemove = TakeFromOwner },
            new Channel<Book, BookData>("books", () => World.world.books),
            new Channel<Culture, CultureData>("cultures", () => World.world.cultures),
            new Channel<Clan, ClanData>("clans", () => World.world.clans),
            Kingdoms,
            Cities,
            new Channel<War, WarData>("wars", () => World.world.wars),
            new Channel<Army, ArmyData>("armies", () => World.world.armies),
            new Channel<Alliance, AllianceData>("alliances", () => World.world.alliances),
            new Channel<Plot, PlotData>("plots", () => World.world.plots),
            new Channel<DiplomacyRelation, DiplomacyRelationData>("diplomacy", () => World.world.diplomacy)
            {
                BeforeCreate = d =>
                {
                    if (World.world.diplomacy.tryGet(d.kingdom1_id + "_" + d.kingdom2_id, out DiplomacyRelation local) && local.getID() != d.id)
                        World.world.diplomacy.removeObject(local);
                },
            },
        };

        static int _lastStatsHash;
        public static int Changes;
        public static int Failures;
        public static int Repairs;

        static readonly HashSet<int> _listRequests = new HashSet<int>();
        static readonly float[] _lastAsked = new float[32];

        public static int ChannelCount => Channels.Length;
        public static string ChannelName(int i) => Channels[i].Name;
        public static int ChannelSize(int i) => Channels[i].Count;

        public static void WriteAggregates(BinaryWriter w)
        {
            w.Write((byte)Channels.Length);
            foreach (IChannel c in Channels) w.Write(c.Aggregate());
        }

        public static void CompareAggregates(BinaryReader r, int seq)
        {
            int n = r.ReadByte();
            var ask = new List<byte>();
            for (int i = 0; i < n; i++)
            {
                long host = r.ReadInt64();
                if (i >= Channels.Length || host == Channels[i].Aggregate()) continue;
                if (Time.unscaledTime - _lastAsked[i] < 4f) continue;
                _lastAsked[i] = Time.unscaledTime;
                ask.Add((byte)i);
            }
            if (ask.Count == 0) return;
            PlayerEdits.ScanNow();
            var names = new List<string>();
            foreach (byte i in ask) names.Add(Channels[i].Name);
            SessionLog.Write("check " + seq + ": meta differs in " + string.Join(", ", names.ToArray()) + ", asking the host for the lists");
            Net.Peer?.Send(Msg.MetaCheck, ask.ToArray());
        }

        public static void RequestLists(byte[] data)
        {
            if (data == null) return;
            foreach (byte i in data)
                if (i < Channels.Length) _listRequests.Add(i);
        }

        public static bool HasListRequests => _listRequests.Count > 0;

        public static void WriteLists(BinaryWriter w)
        {
            w.Write((byte)_listRequests.Count);
            foreach (int i in _listRequests)
            {
                w.Write((byte)i);
                Channels[i].WriteList(w);
            }
            _listRequests.Clear();
        }

        public static void CheckLists(BinaryReader r)
        {
            int n = r.ReadByte();
            var w = new BinaryWriter(new MemoryStream());
            int asked = 0;
            for (int k = 0; k < n; k++)
            {
                int i = r.ReadByte();
                var repair = new List<long>();
                Channels[i].CheckList(r, repair);
                if (repair.Count == 0) continue;
                w.Write((byte)i);
                w.Write(repair.Count);
                foreach (long id in repair) w.Write(id);
                asked += repair.Count;
            }
            Access.SetDrawnZonesDirty(Access.ZoneCalculator(World.world));
            if (asked > 0) Net.Peer?.Send(Msg.MetaRepair, ((MemoryStream)w.BaseStream).ToArray());
        }

        public static void Repair(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var parts = new List<string>();
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                int i = r.ReadByte();
                int n = r.ReadInt32();
                for (int k = 0; k < n; k++)
                {
                    long id = r.ReadInt64();
                    if (i < Channels.Length) Channels[i].ForceResend(id);
                }
                parts.Add(n + " " + (i < Channels.Length ? Channels[i].Name : "?"));
            }
            SessionLog.Write("client asked to resend: " + string.Join(", ", parts.ToArray()));
        }

        public static void EachObject(Action<string, long, object, BaseSystemData> f)
        {
            foreach (IChannel c in Channels)
            {
                string name = c.Name;
                c.Each((id, o, d) => f(name, id, o, d));
            }
        }

        public static object Find(string channel, long id, out BaseSystemData data)
        {
            data = null;
            foreach (IChannel c in Channels)
            {
                if (c.Name != channel) continue;
                object o = c.Find(id);
                if (o != null) data = c.DataOf(o);
                return o;
            }
            return null;
        }

        public static void ForceResend(string channel, long id)
        {
            foreach (IChannel c in Channels)
                if (c.Name == channel) c.ForceResend(id);
        }

        public static void RefreshColor(object o, int colorId)
        {
            try
            {
                var lib = AccessTools.Method(o.GetType(), "getColorLibrary")?.Invoke(o, null) as ColorLibrary;
                if (lib == null || colorId < 0 || colorId >= lib.list.Count) return;
                AccessTools.Method(o.GetType(), "updateColor", new[] { typeof(ColorAsset) })?.Invoke(o, new object[] { lib.list[colorId] });
            }
            catch (Exception e)
            {
                Debug.Log("[WorldBoxMP] colour refresh failed: " + e.GetBaseException().Message);
            }
        }

        public static string DescribeKingdom(Kingdom k) => Kingdoms.Describe(k);
        public static string DescribeCity(City c) => Cities.Describe(c);

        static void UpdateCity(City city, CityData data)
        {
            var keep = new HashSet<long>();
            if (data.zones != null)
                foreach (ZoneData z in data.zones) keep.Add(((long)z.x << 32) | (uint)z.y);
            foreach (TileZone zone in new List<TileZone>(Access.MetaSyncCityZones(city)))
                if (!keep.Contains(((long)zone.x << 32) | (uint)zone.y)) Access.CityRemoveZone(city, zone);
            Kingdom k = Access.CityKingdom(city);
            bool wasCapital = k != null && k.capital == city;
            city.loadCity(data);
            if (wasCapital && Access.CityKingdom(city) == k && k.capital == null) k.setCapital(city);
        }

        public static bool HasUnsent()
        {
            foreach (IChannel c in Channels)
                if (c.HasUnsent()) return true;
            return false;
        }

        static readonly Dictionary<long, KeyValuePair<long, long>> _hostKingdomLinks = new Dictionary<long, KeyValuePair<long, long>>();
        static readonly Dictionary<long, long> _hostLeaders = new Dictionary<long, long>();
        public static int Relinks;
        static readonly Dictionary<string, int> _relinkWhy = new Dictionary<string, int>();
        static float _relinkLogTimer;

        static void Why(string what)
        {
            _relinkWhy.TryGetValue(what, out int n);
            _relinkWhy[what] = n + 1;
        }

        static void LogRelinks()
        {
            _relinkLogTimer -= Time.unscaledDeltaTime;
            if (_relinkLogTimer > 0f || _relinkWhy.Count == 0) return;
            _relinkLogTimer = 10f;
            var top = new List<KeyValuePair<string, int>>(_relinkWhy);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new System.Text.StringBuilder("relinks in 10 s:");
            for (int i = 0; i < top.Count && i < 6; i++) sb.Append(' ').Append(top[i].Key).Append('=').Append(top[i].Value);
            SessionLog.Write(sb.ToString());
            _relinkWhy.Clear();
        }

        public static void Relink()
        {
            MapBox w = World.world;
            LogRelinks();
            foreach (Kingdom k in w.kingdoms)
            {
                if (!_hostKingdomLinks.TryGetValue(k.getID(), out var l)) continue;
                if (l.Value < 0 && k.king != null) { k.clearKingData(); Relinks++; Why("k" + k.getID() + ":no_king"); }
                if (l.Key < 0 && k.capital != null) { k.clearCapital(); Relinks++; Why("k" + k.getID() + ":no_capital"); }
                bool capitalOk = l.Key < 0 || (k.capital != null && k.capital.getID() == l.Key) || w.cities.get(l.Key) == null;
                bool kingOk = l.Value < 0 || (k.king != null && k.king == w.units.get(l.Value)) || w.units.get(l.Value) == null;
                if (capitalOk && kingOk) continue;
                Why("k" + k.getID() + (capitalOk ? "" : ":capital(" + (k.capital != null ? k.capital.getID().ToString() : "none") + "->" + l.Key + ")") +
                    (kingOk ? "" : ":king(" + (k.king != null ? k.king.getID() + (k.king.isRekt() ? "rekt" : "") : "none") + "->" + l.Value + ")"));
                k.data.capitalID = l.Key;
                k.data.kingID = l.Value;
                try { Access.KingdomLoad2(k); Relinks++; }
                catch (Exception e) { Failures++; Debug.Log("[WorldBoxMP] kingdom relink " + k.getID() + ": " + e.Message); }
            }
            foreach (City c in w.cities)
            {
                if (!_hostLeaders.TryGetValue(c.getID(), out long leader)) continue;
                if (leader < 0)
                {
                    if (c.leader != null) { c.leader = null; Relinks++; Why("c" + c.getID() + ":no_leader"); }
                    continue;
                }
                if (c.leader != null && c.leader == w.units.get(leader)) continue;
                if (w.units.get(leader) == null) continue;
                Why("c" + c.getID() + ":leader(" + (c.leader != null ? c.leader.getID() + (c.leader.isRekt() ? "rekt" : "") : "none") + "->" + leader + ")");
                c.data.leaderID = leader;
                try { c.loadLeader(); Relinks++; }
                catch (Exception e) { Failures++; Debug.Log("[WorldBoxMP] city relink " + c.getID() + ": " + e.Message); }
            }
        }

        static void TakeFromOwner(Item item)
        {
            Actor owner = item.getActor();
            if (owner == null || owner.equipment == null) return;
            foreach (ActorEquipmentSlot slot in owner.equipment)
                if (slot.getItem() == item) slot.takeAwayItem();
        }

        public static void ResendKingdomsAndCities()
        {
            Kingdoms.ForceResendAll();
            Cities.ForceResendAll();
        }

        public static void ResetAfterSnapshot()
        {
            foreach (IChannel c in Channels) c.Snapshot();
            Kingdoms.ForceResendAll();
            Cities.ForceResendAll();
            _lastStatsHash = Json.Write(Access.MapStatsRef(World.world)).GetHashCode();
        }

        public static int Write(BinaryWriter w, bool full)
        {
            Changes = 0;
            foreach (IChannel c in Channels) c.Write(w, full);
            if (!full)
            {
                w.Write(false);
                return Changes;
            }
            string stats = Json.Write(Access.MapStatsRef(World.world));
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
                MapStats old = Access.MapStatsRef(World.world);
                if (old != null)
                {
                    stats.id_status = Math.Max(stats.id_status, old.id_status);
                    stats.id_projectile = Math.Max(stats.id_projectile, old.id_projectile);
                }
                Access.MapStatsRef(World.world) = stats;
                stats.load();
                Access.RecalcCounters(stats);
            }
        }

        public static void FinishApply()
        {
            foreach (Kingdom k in Kingdoms.Touched)
            {
                try { Access.KingdomLoad2(k); }
                catch (Exception e) { Failures++; Debug.Log("[WorldBoxMP] kingdom link " + k.getID() + ": " + e.Message); }
            }
            foreach (City c in Cities.Touched)
            {
                try { c.loadLeader(); }
                catch (Exception e) { Failures++; Debug.Log("[WorldBoxMP] city leader " + c.getID() + ": " + e.Message); }
            }
            Relink();
            if (Kingdoms.Touched.Count > 0 || Cities.Touched.Count > 0) Access.SetDrawnZonesDirty(Access.ZoneCalculator(World.world));
            Kingdoms.Touched.Clear();
            Cities.Touched.Clear();
        }
    }
}
