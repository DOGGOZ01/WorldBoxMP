using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class ObjectRepair
    {
        public const byte Units = 0, Buildings = 1, Tiles = 2;
        static readonly string[] Names = { "units", "buildings", "tiles" };
        const int BucketCount = 256;

        static readonly Action<ActorManager, Actor> ScheduleActorDestroy =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));
        static readonly Action<Building> RemoveBuildingFinal =
            AccessTools.MethodDelegate<Action<Building>>(AccessTools.Method(typeof(Building), "removeBuildingFinal"));

        static readonly HashSet<byte> _bucketRequests = new HashSet<byte>();
        static readonly Dictionary<byte, HashSet<int>> _itemRequests = new Dictionary<byte, HashSet<int>>();
        static readonly float[] _askedAt = { -100f, -100f, -100f };
        public static int Repaired, Asked;

        static int Bucket(long id) => (int)(((id % BucketCount) + BucketCount) % BucketCount);

        static long Hashes(byte cat, long[] buckets)
        {
            Array.Clear(buckets, 0, buckets.Length);
            unchecked
            {
                switch (cat)
                {
                    case Units:
                        foreach (Actor a in World.world.units)
                            if (HostSync.Syncable(a)) buckets[Bucket(a.getID())] += a.getID() * 1000003L ^ HostSync.Sig(a);
                        break;
                    case Buildings:
                        foreach (Building b in World.world.buildings)
                            if (HostSync.Syncable(b)) buckets[Bucket(b.getID())] += b.getID() * 1000003L ^ HostSync.Sig(b);
                        break;
                    case Tiles:
                        foreach (WorldTile t in Access.TilesList(World.world))
                            buckets[Bucket(t.data.tile_id)] += t.data.tile_id * 31L + t.main_type.id.GetHashCode() * 7 + (t.top_type != null ? t.top_type.id.GetHashCode() : 0) + (t.data.frozen ? 13 : 0);
                        break;
                }
            }
            return 0;
        }

        public static void Ask(byte cat, int seq)
        {
            if (Time.unscaledTime - _askedAt[cat] < 5f) return;
            _askedAt[cat] = Time.unscaledTime;
            Asked++;
            SessionLog.Write("check " + seq + ": " + Names[cat] + " differ, comparing buckets with the host");
            Net.Peer?.Send(Msg.BucketCheck, new[] { cat });
        }

        public static void HostRequestBuckets(byte[] data)
        {
            foreach (byte cat in data) if (cat <= Tiles) _bucketRequests.Add(cat);
        }

        public static void HostRequestItems(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            byte cat = r.ReadByte();
            int n = r.ReadUInt16();
            var set = new HashSet<int>();
            for (int i = 0; i < n; i++) set.Add(r.ReadByte());
            if (cat == Tiles)
            {
                int marked = 0;
                foreach (WorldTile t in Access.TilesList(World.world))
                    if (set.Contains(Bucket(t.data.tile_id))) { HostSync.DirtyTiles.Add(t); marked++; }
                SessionLog.Write("repair: re-sending " + marked + " tiles in " + n + " buckets");
                return;
            }
            _itemRequests[cat] = set;
        }

        public static void HostResend(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            byte cat = r.ReadByte();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                if (cat == Units) HostSync.Reload(id); else HostSync.ReloadBuilding(id);
            }
            SessionLog.Write("repair: re-sending " + n + " " + Names[cat]);
        }

        public static bool HasWork => _bucketRequests.Count > 0 || _itemRequests.Count > 0;

        public static void Write(BinaryWriter w)
        {
            w.Write((byte)_bucketRequests.Count);
            var buckets = new long[BucketCount];
            foreach (byte cat in _bucketRequests)
            {
                Hashes(cat, buckets);
                w.Write(cat);
                foreach (long h in buckets) w.Write(h);
            }
            _bucketRequests.Clear();

            w.Write((byte)_itemRequests.Count);
            foreach (var kv in _itemRequests)
            {
                var items = new List<KeyValuePair<long, int>>();
                if (kv.Key == Units)
                {
                    foreach (Actor a in World.world.units)
                        if (HostSync.Syncable(a) && kv.Value.Contains(Bucket(a.getID()))) items.Add(new KeyValuePair<long, int>(a.getID(), HostSync.Sig(a)));
                }
                else
                {
                    foreach (Building b in World.world.buildings)
                        if (HostSync.Syncable(b) && kv.Value.Contains(Bucket(b.getID()))) items.Add(new KeyValuePair<long, int>(b.getID(), HostSync.Sig(b)));
                }
                w.Write(kv.Key);
                w.Write((ushort)kv.Value.Count);
                foreach (int bucket in kv.Value) w.Write((byte)bucket);
                w.Write(items.Count);
                foreach (var it in items) { w.Write(it.Key); w.Write(it.Value); }
            }
            _itemRequests.Clear();
        }

        public static void Read(BinaryReader r)
        {
            int n = r.ReadByte();
            var mine = new long[BucketCount];
            for (int k = 0; k < n; k++)
            {
                byte cat = r.ReadByte();
                var host = new long[BucketCount];
                for (int i = 0; i < BucketCount; i++) host[i] = r.ReadInt64();
                Hashes(cat, mine);
                var differ = new List<byte>();
                for (int i = 0; i < BucketCount; i++) if (host[i] != mine[i]) differ.Add((byte)i);
                SessionLog.Write("repair " + Names[cat] + ": " + differ.Count + " of " + BucketCount + " buckets differ");
                if (differ.Count == 0) continue;
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write(cat);
                w.Write((ushort)differ.Count);
                foreach (byte b in differ) w.Write(b);
                Net.Peer?.Send(Msg.BucketList, ms.ToArray());
            }

            n = r.ReadByte();
            for (int k = 0; k < n; k++)
            {
                byte cat = r.ReadByte();
                int count = r.ReadUInt16();
                var set = new HashSet<int>();
                for (int i = 0; i < count; i++) set.Add(r.ReadByte());
                int items = r.ReadInt32();
                var host = new Dictionary<long, int>(items);
                for (int i = 0; i < items; i++) host[r.ReadInt64()] = r.ReadInt32();
                CompareItems(cat, set, host);
            }
        }

        static void CompareItems(byte cat, HashSet<int> buckets, Dictionary<long, int> host)
        {
            MapBox world = World.world;
            var mineIds = new HashSet<long>();
            var resend = new List<long>();
            int removed = 0;
            if (cat == Units)
            {
                var extra = new List<Actor>();
                foreach (Actor a in world.units)
                {
                    if (!HostSync.Syncable(a) || !buckets.Contains(Bucket(a.getID()))) continue;
                    mineIds.Add(a.getID());
                    if (!host.TryGetValue(a.getID(), out int sig)) extra.Add(a);
                    else if (sig != HostSync.Sig(a)) resend.Add(a.getID());
                }
                foreach (Actor a in extra) { RemotePossession.OnClientActorRemoved(a); ScheduleActorDestroy(world.units, a); removed++; }
            }
            else
            {
                var extra = new List<Building>();
                foreach (Building b in world.buildings)
                {
                    if (!HostSync.Syncable(b) || !buckets.Contains(Bucket(b.getID()))) continue;
                    mineIds.Add(b.getID());
                    if (!host.TryGetValue(b.getID(), out int sig)) extra.Add(b);
                    else if (sig != HostSync.Sig(b)) resend.Add(b.getID());
                }
                foreach (Building b in extra) { RemoveBuildingFinal(b); removed++; }
            }
            foreach (long id in host.Keys) if (!mineIds.Contains(id)) resend.Add(id);
            Repaired += removed + resend.Count;
            SessionLog.Write("repair " + Names[cat] + ": removed " + removed + " the host does not have, asking for " + resend.Count);
            if (resend.Count == 0) return;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(cat);
            w.Write(resend.Count);
            foreach (long id in resend) w.Write(id);
            Net.Peer?.Send(Msg.ObjRepair, ms.ToArray());
        }
    }
}
