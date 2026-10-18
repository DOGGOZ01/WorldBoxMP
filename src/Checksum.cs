using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class Checksum
    {
        public static int Matches { get; private set; }
        public static int Mismatches { get; private set; }
        public static int Consecutive { get; private set; }
        static int _seq;
        public const int DumpEvery = 5;

        public static int CityZoneCount(City c) => Access.CityZones(c).Count;

        struct State
        {
            public int Units;
            public int Buildings;
            public long Terrain;
            public int Kingdoms;
            public int Cities;
            public long Borders;
            public long Names;
        }

        static State Take()
        {
            MapBox w = World.world;
            var s = new State();
            foreach (Actor a in w.units)
                if (a.isAlive() && !a.asset.skip_save) s.Units++;
            foreach (Building b in w.buildings)
                if (Access.BuildingData(b).state != BuildingState.Removed) s.Buildings++;
            long h = 17;
            foreach (WorldTile t in Access.TilesList(w))
                h = h * 31 + t.main_type.id.GetHashCode() * 7 + (t.top_type != null ? t.top_type.id.GetHashCode() : 0) + (t.data.frozen ? 1 : 0);
            s.Terrain = h;
            s.Kingdoms = w.kingdoms.Count;
            s.Cities = w.cities.Count;
            long borders = 17;
            foreach (City c in w.cities)
                foreach (TileZone z in Access.CityZones(c))
                    borders += (c.getID() * 1000003L) ^ ((long)z.x * 7919 + z.y);
            s.Borders = borders;
            long names = 17;
            foreach (Kingdom k in w.kingdoms)
                names += k.getID() * 7919L ^ (k.name ?? "").GetHashCode();
            foreach (City c in w.cities)
            {
                Kingdom ck = Access.CityKingdom(c);
                names += c.getID() * 104729L ^ (c.name ?? "").GetHashCode() ^ (ck != null ? ck.getID() * 31L : 0L);
            }
            s.Names = names;
            return s;
        }

        public static void Write(BinaryWriter w)
        {
            int seq = ++_seq;
            w.Write(seq);
            State s = Take();
            w.Write(s.Units);
            w.Write(s.Buildings);
            w.Write(s.Terrain);
            w.Write(s.Kingdoms);
            w.Write(s.Cities);
            w.Write(s.Borders);
            w.Write(s.Names);
            MetaSync.WriteAggregates(w);
            if (seq % DumpEvery == 0) SessionLog.DumpState(seq);
        }

        public static void Compare(BinaryReader r)
        {
            int seq = r.ReadInt32();
            var host = new State
            {
                Units = r.ReadInt32(), Buildings = r.ReadInt32(), Terrain = r.ReadInt64(),
                Kingdoms = r.ReadInt32(), Cities = r.ReadInt32(), Borders = r.ReadInt64(), Names = r.ReadInt64(),
            };
            State mine = Take();
            MetaSync.CompareAggregates(r, seq);
            if (seq % DumpEvery == 0) SessionLog.DumpState(seq);
            if (host.Units == mine.Units && host.Buildings == mine.Buildings && host.Terrain == mine.Terrain &&
                host.Kingdoms == mine.Kingdoms && host.Cities == mine.Cities && host.Borders == mine.Borders &&
                host.Names == mine.Names)
            {
                Matches++;
                Consecutive = 0;
                return;
            }
            Mismatches++;
            Consecutive++;
            if (host.Units != mine.Units) ObjectRepair.Ask(ObjectRepair.Units, seq);
            if (host.Buildings != mine.Buildings) ObjectRepair.Ask(ObjectRepair.Buildings, seq);
            if (host.Terrain != mine.Terrain) ObjectRepair.Ask(ObjectRepair.Tiles, seq);
            Debug.Log("[WorldBoxMP] desync at check " + seq + ": host units=" + host.Units + " buildings=" + host.Buildings +
                      " terrain=" + host.Terrain.ToString("x") + " kingdoms=" + host.Kingdoms + " cities=" + host.Cities +
                      " borders=" + host.Borders.ToString("x") + " | client units=" + mine.Units +
                      " buildings=" + mine.Buildings + " terrain=" + mine.Terrain.ToString("x") + " kingdoms=" + mine.Kingdoms +
                      " cities=" + mine.Cities + " borders=" + mine.Borders.ToString("x") +
                      (host.Names != mine.Names ? " NAMES DIFFER" : ""));
            if (Consecutive == 1 && seq % DumpEvery != 0) SessionLog.DumpState(seq);
        }

        public static void Reset()
        {
            Matches = 0;
            Mismatches = 0;
            Consecutive = 0;
        }
    }
}
