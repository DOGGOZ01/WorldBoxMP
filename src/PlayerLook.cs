using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class PlayerLook
    {
        private const float SendInterval = 0.05f;

        private static float _sendTimer;

        class Look
        {
            public long Id = -1L;
            public float Yaw;
            public double At;
            public Actor Shown;
        }

        private static readonly Dictionary<byte, Look> _looks = new Dictionary<byte, Look>();

        public static void Clear()
        {
            foreach (Look l in _looks.Values)
                if (l.Shown != null) WorldfallBridge.ClearLook(l.Shown);
            _looks.Clear();
        }

        public static void Receive(byte[] data)
        {
            byte slot;
            byte[] payload = data;
            if (Net.Role == Role.Host)
            {
                if (Net.Current == null) return;
                slot = Net.Current.Slot;
                byte[] marked = WithSlot(slot, data);
                foreach (Guest g in Net.Guests)
                    if (g.Ready && g.Slot != slot && !g.Peer.Closed) g.Peer.Send(Msg.Look, marked);
            }
            else
            {
                if (data.Length < 1) return;
                slot = data[0];
                if (slot == Roster.LocalSlot) return;
                payload = new byte[data.Length - 1];
                System.Buffer.BlockCopy(data, 1, payload, 0, payload.Length);
            }
            BinaryReader binaryReader = new BinaryReader(new MemoryStream(payload));
            if (!_looks.TryGetValue(slot, out Look l)) _looks[slot] = l = new Look();
            l.Id = binaryReader.ReadInt64();
            l.Yaw = binaryReader.ReadSingle();
            l.At = Clock.Now;
        }

        static byte[] WithSlot(byte slot, byte[] payload)
        {
            var b = new byte[payload.Length + 1];
            b[0] = slot;
            System.Buffer.BlockCopy(payload, 0, b, 1, payload.Length);
            return b;
        }

        public static void Update()
        {
            if (!PlayerNames.InSession || !WorldfallBridge.Present)
            {
                if (_looks.Count > 0) Clear();
                return;
            }
            _sendTimer -= Time.unscaledDeltaTime;
            Actor controllableUnit = ControllableUnit.getControllableUnit();
            if (_sendTimer <= 0f && controllableUnit != null && WorldfallBridge.InFirstPerson && Net.Peer != null)
            {
                _sendTimer = SendInterval;
                MemoryStream memoryStream = new MemoryStream(12);
                BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
                binaryWriter.Write(controllableUnit.getID());
                binaryWriter.Write(WorldfallBridge.Yaw);
                byte[] payload = memoryStream.ToArray();
                Net.Peer.Send(Msg.Look, Net.Role == Role.Host ? WithSlot(0, payload) : payload);
            }
            foreach (Look l in _looks.Values)
            {
                Actor actor = l.Id >= 0 && Clock.Now - l.At < 0.5 ? World.world.units.get(l.Id) : null;
                if (actor != l.Shown && l.Shown != null) WorldfallBridge.ClearLook(l.Shown);
                l.Shown = actor;
                if (actor != null && actor.isAlive())
                {
                    Vector2 vector = new Vector2(Mathf.Cos(l.Yaw), Mathf.Sin(l.Yaw));
                    WorldfallBridge.SetLook(actor, actor.current_position + vector * 3f);
                }
            }
        }
    }
}
