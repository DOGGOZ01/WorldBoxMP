using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public class PlayerInfo
    {
        public byte Slot;
        public string Name = "";
        public int PingMs;
        public readonly HashSet<long> Units = new HashSet<long>();
    }

    public static class Lobby
    {
        public const int MaxGuests = 3;

        public static readonly Color[] Colors =
        {
            new Color(1f, 0.66f, 0.2f),
            new Color(0.45f, 0.85f, 1f),
            new Color(0.5f, 0.95f, 0.45f),
            new Color(0.85f, 0.6f, 1f),
        };

        public static Color ColorOf(byte slot) => Colors[slot < Colors.Length ? slot : 0];

        public static readonly bool[] SlotOpen = { true, true, true };

        public static readonly Dictionary<string, string> Kicked = new Dictionary<string, string>();

        static readonly List<KeyValuePair<float, Peer>> _closing = new List<KeyValuePair<float, Peer>>();
        static float _sendTimer;
        static string _sent;

        public static int FreeSlot()
        {
            for (byte s = 1; s <= MaxGuests; s++)
                if (SlotOpen[s - 1] && Net.GuestIn(s) == null) return s;
            return 0;
        }

        public static int OpenSlots
        {
            get
            {
                int n = 0;
                foreach (bool o in SlotOpen) if (o) n++;
                return n;
            }
        }

        public static void OnConnect(Peer peer)
        {
            string key = peer.BanKey;
            if (key != null && Kicked.ContainsKey(key))
            {
                Refuse(peer, "lobby_kicked");
                SessionLog.Write("refused " + peer.Remote + ": removed earlier, not invited again");
                return;
            }
            int slot = FreeSlot();
            if (slot == 0)
            {
                Refuse(peer, "lobby_full");
                SessionLog.Write("refused " + peer.Remote + ": no free slot");
                return;
            }
            var g = new Guest { Slot = (byte)slot, Peer = peer, BanKey = key, Name = PeerName(peer) };
            Net.AddGuest(g);
            SessionLog.Write("player " + peer.Remote + " in slot " + slot);
            Changed();
        }

        static string PeerName(Peer p)
        {
            string r = p.Remote ?? "";
            int i = r.IndexOf(" (Steam)");
            return i > 0 ? r.Substring(0, i) : r;
        }

        static void Refuse(Peer peer, string key)
        {
            using (var ms = new MemoryStream())
            {
                new BinaryWriter(ms).Write(key);
                peer.Send(Msg.Refused, ms.ToArray());
            }
            _closing.Add(new KeyValuePair<float, Peer>(Time.unscaledTime + 1.5f, peer));
        }

        public static void Kick(byte slot)
        {
            Guest g = Net.GuestIn(slot);
            if (g == null) return;
            if (g.BanKey != null) Kicked[g.BanKey] = g.Label;
            g.Peer.Send(Msg.Kicked, null);
            SessionLog.Write("removed " + g.Label + " from slot " + slot);
            MPMod.Tip("tip_kicked_host", g.Label);
            Leave(g);
            _closing.Add(new KeyValuePair<float, Peer>(Time.unscaledTime + 1.5f, g.Peer));
        }

        public static void InviteAgain(string key)
        {
            if (!Kicked.TryGetValue(key, out string name)) return;
            Kicked.Remove(key);
            if (key.StartsWith("steam:") && ulong.TryParse(key.Substring(6), out ulong id)) SteamSession.InviteFriend(id);
            SessionLog.Write("invited again: " + name);
            Changed();
        }

        public static void ToggleSlot(int index)
        {
            if (index < 0 || index >= MaxGuests || Net.GuestIn((byte)(index + 1)) != null) return;
            SlotOpen[index] = !SlotOpen[index];
            SteamSession.UpdateLobbySize();
            SessionLog.Write("slot " + (index + 1) + (SlotOpen[index] ? " opened" : " closed"));
            Changed();
        }

        public static void Leave(Guest g)
        {
            Net.Current = g;
            try { RemotePossession.ForgetGuest(g); }
            finally { Net.Current = null; }
            Net.Guests.Remove(g);
            Changed();
        }

        public static void ResetHost()
        {
            Kicked.Clear();
            for (int i = 0; i < SlotOpen.Length; i++) SlotOpen[i] = true;
            _sent = null;
        }

        public static void Changed() => _sendTimer = 0f;

        public static void HostUpdate()
        {
            for (int i = _closing.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _closing[i].Key) { _closing[i].Value.Pump(); continue; }
                try { _closing[i].Value.Dispose(); } catch { }
                _closing.RemoveAt(i);
            }
            if (Net.Role != Role.Host) return;
            _sendTimer -= Time.unscaledDeltaTime;
            byte[] data = Write();
            string now = System.Convert.ToBase64String(data);
            if (now == _sent && _sendTimer > 0f) return;
            _sendTimer = 1f;
            _sent = now;
            foreach (Guest g in Net.Guests) g.Peer.Send(Msg.Lobby, data);
            Roster.FromHost();
        }

        static byte[] Write()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(PlayerNames.LocalName);
            WriteUnits(w, ControllableUnit.getCotrolledUnits());
            for (byte s = 1; s <= MaxGuests; s++)
            {
                Guest g = Net.GuestIn(s);
                w.Write((byte)(g != null ? 2 : SlotOpen[s - 1] ? 1 : 0));
                if (g == null) continue;
                w.Write(g.Label);
                w.Write(g.PingMs);
                w.Write(g.Units.Count);
                foreach (long id in g.Units) w.Write(id);
            }
            return ms.ToArray();
        }

        static void WriteUnits(BinaryWriter w, HashSet<Actor> units)
        {
            var ids = new List<long>();
            if (units != null) foreach (Actor a in units) if (a != null && a.isAlive()) ids.Add(a.getID());
            w.Write(ids.Count);
            foreach (long id in ids) w.Write(id);
        }

        public static readonly byte[] SlotState = new byte[MaxGuests];

        public static void ClientReceive(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var list = new List<PlayerInfo>();
            var host = new PlayerInfo { Slot = 0, Name = r.ReadString(), PingMs = NetStats.LastPingMs };
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) host.Units.Add(r.ReadInt64());
            list.Add(host);
            for (byte s = 1; s <= MaxGuests; s++)
            {
                byte state = r.ReadByte();
                SlotState[s - 1] = state;
                if (state != 2) continue;
                var p = new PlayerInfo { Slot = s, Name = r.ReadString(), PingMs = r.ReadInt32() };
                int u = r.ReadInt32();
                for (int i = 0; i < u; i++) p.Units.Add(r.ReadInt64());
                list.Add(p);
            }
            Roster.Set(list);
        }
    }

    public static class Roster
    {
        public static readonly List<PlayerInfo> Players = new List<PlayerInfo>();

        public static byte LocalSlot;

        public static void Clear()
        {
            Players.Clear();
            LocalSlot = 0;
        }

        public static void Set(List<PlayerInfo> list)
        {
            Players.Clear();
            Players.AddRange(list);
        }

        public static void FromHost()
        {
            var list = new List<PlayerInfo>();
            var host = new PlayerInfo { Slot = 0, Name = PlayerNames.LocalName };
            HashSet<Actor> mine = ControllableUnit.getCotrolledUnits();
            if (mine != null) foreach (Actor a in mine) if (a != null) host.Units.Add(a.getID());
            list.Add(host);
            foreach (Guest g in Net.Guests)
            {
                if (!g.Ready) continue;
                var p = new PlayerInfo { Slot = g.Slot, Name = g.Label, PingMs = g.PingMs };
                p.Units.UnionWith(g.Units);
                list.Add(p);
            }
            list.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            Set(list);
        }

        public static PlayerInfo Get(byte slot)
        {
            foreach (PlayerInfo p in Players) if (p.Slot == slot) return p;
            return null;
        }

        public static bool OtherControls(long id)
        {
            foreach (PlayerInfo p in Players)
                if (p.Slot != LocalSlot && p.Units.Contains(id)) return true;
            return false;
        }
    }
}
