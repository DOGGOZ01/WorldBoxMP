using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class PositionSync
    {
        const int PerMessage = 110;
        static readonly float[] Interval = { 0.05f, 0.25f, 2f };
        static readonly float[] Refresh = { 1f, 3f, 10f };

        struct Sent
        {
            public Vector2 Pos;
            public float Height;
            public byte Flags;
            public double At;
        }

        static readonly Dictionary<long, Sent> _sent = new Dictionary<long, Sent>();
        static readonly HashSet<long> _seen = new HashSet<long>();
        static readonly List<long> _drop = new List<long>();
        static readonly List<KeyValuePair<long, Sent>> _due = new List<KeyValuePair<long, Sent>>();
        static uint _seq;

        public static long Records, Messages, Bytes;

        public static void ResetHost() => _sent.Clear();
        public static void Forget(long id) => _sent.Remove(id);

        static byte FlagsOf(Actor a) =>
            (byte)((Access.Flip(a) ? 1 : 0) | (a.is_moving ? 2 : 0) | (Access.UnderForces(a) ? 4 : 0));

        public static void Tick()
        {
            double now = Clock.Now;
            float slow = Flow.Level == 0 ? 1f : Flow.Level == 1 ? 2f : 4f;
            _seen.Clear();
            _due.Clear();
            foreach (Actor a in World.world.units)
            {
                if (!HostSync.Syncable(a) || SpecialActors.IsSpecial(a)) continue;
                long id = a.getID();
                _seen.Add(id);
                var cur = new Sent { Pos = a.current_position, Height = a.position_height, Flags = FlagsOf(a), At = now };
                bool known = _sent.TryGetValue(id, out Sent old);
                bool controlled = ControllableUnit.isControllingUnit(a);
                int p = controlled ? 0 : ViewSync.Priority(cur.Pos);
                float interval = controlled ? Interval[0] : Interval[p] * (p == 0 && Flow.Level < 2 ? 1f : slow);
                bool changed = !known || (old.Pos - cur.Pos).sqrMagnitude > 0.0004f || Mathf.Abs(old.Height - cur.Height) > 0.02f || old.Flags != cur.Flags;
                double since = known ? now - old.At : double.MaxValue;
                if ((changed && since >= interval - 0.001) || since >= Refresh[p] * slow)
                {
                    _sent[id] = cur;
                    _due.Add(new KeyValuePair<long, Sent>(id, cur));
                }
            }
            _drop.Clear();
            foreach (long id in _sent.Keys) if (!_seen.Contains(id)) _drop.Add(id);
            foreach (long id in _drop) _sent.Remove(id);
            if (_due.Count == 0) return;

            _due.Sort((x, y) => x.Key.CompareTo(y.Key));
            int dim = Mathf.Max(MapBox.width, MapBox.height, 1);
            float q = 65535f / dim;
            for (int start = 0; start < _due.Count; start += PerMessage)
            {
                int count = Mathf.Min(PerMessage, _due.Count - start);
                var ms = new MemoryStream(16 + count * 8);
                var w = new BinaryWriter(ms);
                w.Write(++_seq);
                w.Write(now);
                w.Write((ushort)dim);
                w.Write((ushort)count);
                long prev = 0;
                for (int i = start; i < start + count; i++)
                {
                    var kv = _due[i];
                    WriteVarint(w, (ulong)(kv.Key - prev));
                    prev = kv.Key;
                    w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(kv.Value.Pos.x * q), 0, 65535));
                    w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(kv.Value.Pos.y * q), 0, 65535));
                    w.Write((byte)Mathf.Clamp(Mathf.RoundToInt(kv.Value.Height * 16f), 0, 255));
                    w.Write(kv.Value.Flags);
                }
                byte[] data = ms.ToArray();
                Net.Peer.SendUnreliable(Msg.Pos, data);
                Messages++;
                Bytes += data.Length;
                HostSync.CountSection("moved", data.Length);
            }
            Records += _due.Count;
        }

        static void WriteVarint(BinaryWriter w, ulong v)
        {
            while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
            w.Write((byte)v);
        }

        static ulong ReadVarint(BinaryReader r)
        {
            ulong v = 0;
            int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                v |= (ulong)(b & 0x7f) << shift;
                if (b < 0x80) return v;
                shift += 7;
            }
        }

        static uint _lastSeq;
        public static long Applied, Late;

        public static void ResetClient() => _lastSeq = 0;

        public static void Receive(byte[] data)
        {
            if (SmoothLoader.isLoading()) return;
            var r = new BinaryReader(new MemoryStream(data));
            uint seq = r.ReadUInt32();
            double hostTime = r.ReadDouble();
            int dim = r.ReadUInt16();
            int count = r.ReadUInt16();
            if (seq < _lastSeq) Late++;
            else _lastSeq = seq;
            float q = dim / 65535f;
            ClientSync.OnPositionsTime(hostTime);
            long id = 0;
            for (int i = 0; i < count; i++)
            {
                id += (long)ReadVarint(r);
                var pos = new Vector2(r.ReadUInt16() * q, r.ReadUInt16() * q);
                float height = r.ReadByte() / 16f;
                byte flags = r.ReadByte();
                Actor a = World.world.units.get(id);
                if (a == null) continue;
                ClientSync.AddSample(a, id, pos, height, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, hostTime);
                Applied++;
            }
        }
    }
}
