using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace WorldBoxMP
{
    public class LobbyEntry
    {
        public ulong Id;
        public string Host = "";
        public int Players, Slots, Year = -1;
        public string Size = "";
        public bool Locked, Mods, SameVersion, Friend;
    }

    public static class SteamSession
    {
        static readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        static bool _initDone;
        static Steamworks.Data.Lobby? _lobby;
        static bool _creating;
        static HostSocket _socket;
        static ClientConnection _connection;
        static ulong _pendingLobby;
        static float _infoTimer;
        static readonly Dictionary<string, string> _published = new Dictionary<string, string>();

        public static ulong LastLobby;

        public static bool Available
        {
            get
            {
                try { return SteamClient.IsValid; }
                catch { return false; }
            }
        }

        public static void Init()
        {
            if (_initDone) return;
            _initDone = true;
            if (!Available)
            {
                Debug.Log("[WorldBoxMP] Steam is not available; only direct IP works");
                return;
            }
            try
            {
                SteamNetworkingUtils.SendBufferSize = 16 * 1024 * 1024;
                SteamPeer.SetRateLimits(256 * 1024, 4 * 1024 * 1024);
                SteamNetworkingUtils.InitRelayNetworkAccess();
            }
            catch (Exception e)
            {
                Debug.Log("[WorldBoxMP] Steam networking setup: " + e.Message);
            }
            try
            {
                string left = SteamFriends.GetRichPresence("connect");
                if (!string.IsNullOrEmpty(left)) Debug.Log("[WorldBoxMP] Steam: an old connect string was set (" + left + "), cleared");
                SteamFriends.SetRichPresence("connect", "");
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] Steam rich presence: " + e.Message); }
            SteamFriends.OnGameLobbyJoinRequested += (lobby, friend) =>
                _mainThread.Enqueue(() => Join(lobby.Id, ""));
            SteamFriends.OnGameRichPresenceJoinRequested += (friend, connect) =>
                _mainThread.Enqueue(() => { if (ParseConnect(connect, out ulong id)) Join(id, ""); });

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
                    _pendingLobby = id;
            Debug.Log("[WorldBoxMP] Steam ready as " + SteamClient.Name + (_pendingLobby != 0 ? ", invited to lobby " + _pendingLobby : ""));
        }

        static bool ParseConnect(string connect, out ulong id)
        {
            id = 0;
            string[] parts = (connect ?? "").Split(' ');
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out id)) return true;
            return false;
        }

        public static void Update()
        {
            while (_mainThread.TryDequeue(out Action a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogError("[WorldBoxMP] steam: " + e); }
            }
            if (_pendingLobby != 0 && Config.game_loaded && !SmoothLoader.isLoading())
            {
                ulong id = _pendingLobby;
                _pendingLobby = 0;
                Join(id, "");
            }
            try
            {
                _socket?.Receive(64);
                _connection?.Receive(64);
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] steam receive: " + e.Message); }
            _infoTimer -= Time.unscaledDeltaTime;
            if (_infoTimer <= 0f && Net.Role == Role.Host && _lobby.HasValue)
            {
                _infoTimer = 5f;
                if (CheckLobby()) PublishInfo();
            }
        }

        public static async void Host()
        {
            if (!Available || _lobby.HasValue || _creating) return;
            _creating = true;
            try
            {
                if (_socket == null) _socket = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(0);
                Steamworks.Data.Lobby? created = await SteamMatchmaking.CreateLobbyAsync(1 + Lobby.OpenSlots);
                if (!created.HasValue)
                {
                    MPMod.Tip("tip_lobby_failed");
                    return;
                }
                if (Net.Role != Role.Host)
                {
                    created.Value.Leave();
                    return;
                }
                Steamworks.Data.Lobby lobby = created.Value;
                lobby.SetData("wbmp", "1");
                lobby.SetData("wbmp_protocol", Net.ProtocolVersion.ToString());
                _lobby = lobby;
                _published.Clear();
                ApplyVisibility();
                PublishInfo();
                Debug.Log("[WorldBoxMP] Steam lobby " + lobby.Id.Value + " created by " + SteamClient.SteamId.Value + ", code " + InviteCode);
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldBoxMP] steam host: " + e);
                MPMod.Tip("tip_steam_error", e.Message);
            }
            finally { _creating = false; }
        }

        public static bool LobbyOpen => _lobby.HasValue;

        static int _lobbyMissing;
        public static int LobbiesReopened;

        static bool CheckLobby()
        {
            bool ours;
            try { ours = _lobby.Value.IsOwnedBy(SteamClient.SteamId) && _lobby.Value.MemberCount > 0; }
            catch { ours = false; }
            if (ours || TestLoseLobby) { _lobbyMissing = TestLoseLobby ? _lobbyMissing + 1 : 0; if (!TestLoseLobby) return true; }
            else _lobbyMissing++;
            if (_lobbyMissing < 2) return true;
            _lobbyMissing = 0;
            TestLoseLobby = false;
            ulong old = _lobby.Value.Id.Value;
            try { _lobby.Value.Leave(); } catch { }
            _lobby = null;
            _published.Clear();
            LobbiesReopened++;
            Debug.Log("[WorldBoxMP] Steam lobby " + old + " is no longer ours; opening a new one");
            SessionLog.Write("Steam lobby " + old + " lost (the host is no longer in it), opening a new one");
            Host();
            return false;
        }

        public static bool TestLoseLobby;

        public static void ApplyVisibility()
        {
            if (!_lobby.HasValue || Net.Role != Role.Host) return;
            try
            {
                Steamworks.Data.Lobby l = _lobby.Value;
                if (Lobby.Visibility == 0) l.SetPublic();
                else if (Lobby.Visibility == 1) l.SetFriendsOnly();
                else l.SetPrivate();
                l.MaxMembers = 1 + Lobby.OpenSlots;
                l.SetJoinable(Lobby.FreeSlot() != 0);
                SteamFriends.SetRichPresence("connect", Lobby.Visibility < 2 ? "+connect_lobby " + l.Id.Value : "");
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] Steam lobby settings: " + e.Message); }
        }

        public static void UpdateLobbySize()
        {
            ApplyVisibility();
            PublishInfo();
        }

        public static void PublishInfo()
        {
            if (!_lobby.HasValue || Net.Role != Role.Host) return;
            try
            {
                int players = 1;
                foreach (Guest g in Net.Guests) if (!g.Peer.Closed) players++;
                Set("wbmp_host", PlayerNames.LocalName);
                Set("wbmp_players", players.ToString());
                Set("wbmp_slots", (1 + Lobby.OpenSlots).ToString());
                Set("wbmp_lock", Lobby.Password.Length > 0 ? "1" : "0");
                Set("wbmp_mods", NetStats.OtherModCount() > 0 ? "1" : "0");
                Set("wbmp_size", MapSize());
                Set("wbmp_year", World.world != null && !SmoothLoader.isLoading() ? Date.getCurrentYear().ToString() : "");
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] Steam lobby data: " + e.Message); }
        }

        static void Set(string key, string value)
        {
            if (_published.TryGetValue(key, out string was) && was == value) return;
            _published[key] = value;
            _lobby.Value.SetData(key, value);
        }

        public static string MapSize()
        {
            try { return MapSizeLibrary.getPresetAsset(Config.ZONE_AMOUNT_X)?.id ?? ""; }
            catch { return ""; }
        }

        const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        const ulong LobbyHigh = 0x01860000UL << 32;

        public static string InviteCode => _lobby.HasValue && Net.Role == Role.Host ? CodeOf(_lobby.Value.Id.Value) : "";

        public static string CodeOf(ulong id)
        {
            bool shortForm = (id & 0xFFFFFFFF00000000UL) == LobbyHigh;
            ulong v = shortForm ? id & 0xFFFFFFFFUL : id;
            int len = shortForm ? 7 : 13;
            var c = new char[len];
            for (int i = len - 1; i >= 0; i--) { c[i] = Alphabet[(int)(v & 31)]; v >>= 5; }
            string s = new string(c);
            return shortForm ? s.Substring(0, 3) + "-" + s.Substring(3) : s;
        }

        public static bool ParseCode(string text, out ulong id)
        {
            id = 0;
            var sb = new System.Text.StringBuilder();
            foreach (char ch in (text ?? "").ToUpperInvariant())
            {
                char c = ch == 'O' ? '0' : ch == 'I' || ch == 'L' ? '1' : ch;
                if (Alphabet.IndexOf(c) >= 0) sb.Append(c);
            }
            string s = sb.ToString();
            if (s.Length != 7 && s.Length != 13) return false;
            ulong v = 0;
            foreach (char c in s) v = (v << 5) | (uint)Alphabet.IndexOf(c);
            id = s.Length == 7 ? LobbyHigh | (v & 0xFFFFFFFFUL) : v;
            return true;
        }

        public static void JoinByCode(string code, string password)
        {
            if (!Available) { MPMod.Tip("tip_no_steam"); return; }
            if (!ParseCode(code, out ulong id)) { MPMod.Tip("tip_bad_code"); return; }
            Join(id, password);
        }

        public static async void Search(Action<List<LobbyEntry>> done)
        {
            var list = new List<LobbyEntry>();
            if (!Available) { done(list); return; }
            var seen = new HashSet<ulong>();
            ulong own = _lobby.HasValue ? _lobby.Value.Id.Value : 0;
            try
            {
                Steamworks.Data.Lobby[] found = await SteamMatchmaking.LobbyList.WithKeyValue("wbmp", "1")
                    .FilterDistanceWorldwide().WithMaxResults(50).RequestAsync();
                if (found != null)
                    foreach (Steamworks.Data.Lobby l in found)
                        if (l.Id.Value != own && seen.Add(l.Id.Value)) list.Add(Entry(l, false));
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] Steam lobby search: " + e.Message); }
            var friends = new List<Steamworks.Data.Lobby>();
            try
            {
                foreach (Friend f in SteamFriends.GetFriends())
                {
                    if (!f.IsPlayingThisGame) continue;
                    Friend.FriendGameInfo? info = f.GameInfo;
                    if (!info.HasValue || !info.Value.Lobby.HasValue) continue;
                    Steamworks.Data.Lobby l = info.Value.Lobby.Value;
                    if (l.Id.Value == own) continue;
                    if (!seen.Add(l.Id.Value))
                    {
                        foreach (LobbyEntry e in list) if (e.Id == l.Id.Value) e.Friend = true;
                        continue;
                    }
                    l.Refresh();
                    friends.Add(l);
                }
                if (friends.Count > 0) await System.Threading.Tasks.Task.Delay(1500);
                foreach (Steamworks.Data.Lobby l in friends)
                    if (l.GetData("wbmp") == "1") list.Add(Entry(l, true));
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] Steam friends' lobbies: " + e.Message); }
            list.Sort((a, b) => a.Friend != b.Friend ? (a.Friend ? -1 : 1) : b.Players.CompareTo(a.Players));
            _mainThread.Enqueue(() => done(list));
        }

        static LobbyEntry Entry(Steamworks.Data.Lobby l, bool friend)
        {
            var e = new LobbyEntry { Id = l.Id.Value, Friend = friend };
            e.Host = l.GetData("wbmp_host");
            if (string.IsNullOrEmpty(e.Host)) { try { e.Host = l.Owner.Name; } catch { e.Host = "?"; } }
            e.Players = int.TryParse(l.GetData("wbmp_players"), out int p) ? p : l.MemberCount;
            e.Slots = int.TryParse(l.GetData("wbmp_slots"), out int s) ? s : l.MaxMembers;
            e.Locked = l.GetData("wbmp_lock") == "1";
            e.Mods = l.GetData("wbmp_mods") == "1";
            e.Size = l.GetData("wbmp_size") ?? "";
            e.Year = int.TryParse(l.GetData("wbmp_year"), out int y) ? y : -1;
            e.SameVersion = l.GetData("wbmp_protocol") == Net.ProtocolVersion.ToString();
            return e;
        }

        public static void OpenInviteOverlay()
        {
            if (!Available) { MPMod.Tip("tip_no_steam"); return; }
            if (!_lobby.HasValue || Net.Role != Role.Host) return;
            SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
            SessionLog.Write("Steam invite window opened");
        }

        public static void InviteFriend(ulong steamId, string name = null)
        {
            if (!Available || !_lobby.HasValue) return;
            try
            {
                Lobby.Invited.Add(steamId);
                bool ok = _lobby.Value.InviteFriend(steamId);
                SessionLog.Write("Steam invite to " + (name ?? steamId.ToString()) + (ok ? " sent" : " failed"));
                if (ok && name != null) MPMod.Tip("tip_invited", name);
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] Steam invite: " + e.Message); }
        }

        public static void Join(ulong lobbyId, string password)
        {
            if (lobbyId == 0) return;
            if (Net.Role == Role.Host && _lobby.HasValue && _lobby.Value.Id.Value == lobbyId) return;
            LastLobby = lobbyId;
            Net.JoinPassword = password ?? "";
            JoinLobby(lobbyId);
        }

        static async void JoinLobby(ulong lobbyId)
        {
            if (!Available) return;
            try
            {
                MPMod.Instance.EndSessionQuietly();
                SessionLog.Start("client");
                var lobby = new Steamworks.Data.Lobby(lobbyId);
                RoomEnter entered = await lobby.Join();
                if (entered != RoomEnter.Success)
                {
                    Debug.Log("[WorldBoxMP] steam lobby: " + entered);
                    MPMod.Tip(entered == RoomEnter.DoesntExist ? "tip_bad_code" : "tip_lobby_join_failed");
                    return;
                }
                _lobby = lobby;
                string theirs = lobby.GetData("wbmp_protocol");
                if (theirs != Net.ProtocolVersion.ToString())
                    MPMod.Tip("tip_version", theirs, Net.ProtocolVersion);

                Friend owner = lobby.Owner;
                if (owner.Id == SteamClient.SteamId)
                {
                    MPMod.Tip("tip_own_lobby");
                    return;
                }
                _connection = SteamNetworkingSockets.ConnectRelay<ClientConnection>(owner.Id, 0);
                var peer = new SteamPeer(_connection.Connection, owner.Name);
                _connection.Peer = peer;
                Net.ConnectWith(peer);
                peer.Send(Msg.Hello, Net.Hello());
                MPMod.Tip("tip_joining_steam", owner.Name);
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldBoxMP] steam join: " + e);
                MPMod.Tip("tip_steam_error", e.Message);
            }
        }

        public static void Stop()
        {
            try { _connection?.Close(); } catch { }
            try { _socket?.Close(); } catch { }
            try { _lobby?.Leave(); } catch { }
            try { if (Available) SteamFriends.SetRichPresence("connect", ""); } catch { }
            _connection = null;
            _socket = null;
            _lobby = null;
            _published.Clear();
        }

        class HostSocket : SocketManager
        {
            readonly Dictionary<uint, SteamPeer> _peers = new Dictionary<uint, SteamPeer>();

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                base.OnConnecting(connection, info);
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                base.OnConnected(connection, info);
                string name = new Friend(info.Identity.SteamId).Name;
                var peer = new SteamPeer(connection, name) { SteamId = info.Identity.SteamId };
                peer.MarkConnected();
                _peers[connection.Id] = peer;
                Net.Offer(peer);
            }

            public override void OnDisconnected(Connection connection, ConnectionInfo info)
            {
                base.OnDisconnected(connection, info);
                if (_peers.TryGetValue(connection.Id, out SteamPeer peer)) peer.MarkClosed(info.EndReason.ToString());
                _peers.Remove(connection.Id);
            }

            public override void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                if (_peers.TryGetValue(connection.Id, out SteamPeer peer)) peer.OnRaw(data, size);
            }
        }

        class ClientConnection : ConnectionManager
        {
            public SteamPeer Peer;

            public override void OnConnected(ConnectionInfo info)
            {
                base.OnConnected(info);
                Peer?.MarkConnected();
            }

            public override void OnDisconnected(ConnectionInfo info)
            {
                base.OnDisconnected(info);
                Peer?.MarkClosed(info.EndReason.ToString());
            }

            public override void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
            {
                Peer?.OnRaw(data, size);
            }
        }
    }

    public class SteamPeer : Peer
    {
        const int ChunkSize = 256 * 1024;

        readonly Connection _connection;
        readonly string _name;
        readonly Queue<byte[]> _outgoing = new Queue<byte[]>();
        readonly MemoryStream _assembling = new MemoryStream();
        bool _connected;
        bool _closed;

        public SteamPeer(Connection connection, string name)
        {
            _connection = connection;
            _name = name;
        }

        public override bool Closed => _closed;
        public override string Remote => _name + " (Steam)";
        public ulong SteamId;
        public override string BanKey => SteamId != 0 ? "steam:" + SteamId : null;

        public void MarkConnected() => _connected = true;

        public void MarkClosed(string reason)
        {
            if (!_closed) Debug.Log("[WorldBoxMP] Steam connection closed: " + reason);
            _closed = true;
        }

        public int QueuedChunks => _outgoing.Count;

        static object _sockets;
        static System.Reflection.MethodInfo _quickStatus;
        static Type _statusType;
        static System.Reflection.FieldInfo _pendingReliable, _pendingUnreliable, _unacked, _sendRate;
        static bool _statusBroken;
        long _pending;
        int _rate;
        float _statusAt;

        public static void SetRateLimits(int min, int max)
        {
            try
            {
                var set = HarmonyLib.AccessTools.Method(typeof(SteamNetworkingUtils), "SetConfigInt");
                Type config = set.GetParameters()[0].ParameterType;
                set.Invoke(null, new object[] { Enum.ToObject(config, 10), min });
                set.Invoke(null, new object[] { Enum.ToObject(config, 11), max });
                Debug.Log("[WorldBoxMP] Steam send rate " + min / 1024 + "-" + max / 1024 + " KiB/s");
            }
            catch (Exception e)
            {
                Debug.Log("[WorldBoxMP] could not set Steam send rate: " + e.GetBaseException().Message);
            }
        }

        void ReadStatus()
        {
            if (_statusBroken || Time.unscaledTime < _statusAt) return;
            _statusAt = Time.unscaledTime + 0.2f;
            try
            {
                if (_quickStatus == null)
                {
                    _sockets = HarmonyLib.AccessTools.Property(typeof(SteamNetworkingSockets), "Internal").GetValue(null, null);
                    _quickStatus = HarmonyLib.AccessTools.Method(_sockets.GetType(), "GetQuickConnectionStatus");
                    _statusType = _quickStatus.GetParameters()[1].ParameterType.GetElementType();
                    _pendingReliable = HarmonyLib.AccessTools.Field(_statusType, "CbPendingReliable");
                    _pendingUnreliable = HarmonyLib.AccessTools.Field(_statusType, "CbPendingUnreliable");
                    _unacked = HarmonyLib.AccessTools.Field(_statusType, "CbSentUnackedReliable");
                    _sendRate = HarmonyLib.AccessTools.Field(_statusType, "SendRateBytesPerSecond");
                }
                object status = Activator.CreateInstance(_statusType);
                HarmonyLib.AccessTools.Field(_statusType, "Reserved")?.SetValue(status, new uint[16]);
                var args = new object[] { _connection, status };
                _quickStatus.Invoke(_sockets, args);
                status = args[1];
                _pending = (int)_pendingReliable.GetValue(status) + (int)_pendingUnreliable.GetValue(status);
                _rate = (int)_sendRate.GetValue(status);
            }
            catch (Exception e)
            {
                _statusBroken = true;
                Debug.Log("[WorldBoxMP] Steam quick status not available: " + e.GetBaseException().Message);
            }
        }

        public override long PendingBytes
        {
            get
            {
                ReadStatus();
                long queued = 0;
                foreach (byte[] chunk in _outgoing) queued += chunk.Length;
                return _pending + queued;
            }
        }

        public override int SendRate
        {
            get { ReadStatus(); return _rate; }
        }

        public override void SendUnreliable(Msg type, byte[] data)
        {
            if (_closed || !_connected) return;
            data = data ?? new byte[0];
            NetStats.CountOut(data);
            var frame = new byte[data.Length + 2];
            frame[0] = 2;
            frame[1] = (byte)type;
            Buffer.BlockCopy(data, 0, frame, 2, data.Length);
            _connection.SendMessage(frame, SendType.Unreliable | SendType.NoNagle);
        }
        public string DetailedStatus()
        {
            try { return _connected && !_closed ? _connection.DetailedStatus() : null; }
            catch { return null; }
        }

        public override void Send(Msg type, byte[] data)
        {
            if (_closed) return;
            NetStats.CountOut(data);
            data = data ?? new byte[0];
            int total = data.Length + 1;
            int offset = 0;
            do
            {
                int len = Math.Min(ChunkSize, total - offset);
                var chunk = new byte[len + 1];
                chunk[0] = (byte)(offset + len == total ? 1 : 0);
                for (int i = 0; i < len; i++)
                {
                    int f = offset + i;
                    chunk[1 + i] = f == 0 ? (byte)type : data[f - 1];
                }
                _outgoing.Enqueue(chunk);
                offset += len;
            } while (offset < total);
            Pump();
        }

        public override void Pump()
        {
            if (!_connected || _closed) return;
            while (_outgoing.Count > 0)
            {
                Result r = _connection.SendMessage(_outgoing.Peek(), SendType.Reliable | SendType.NoNagle);
                if (r == Result.OK) { _outgoing.Dequeue(); continue; }
                if (r == Result.LimitExceeded) return;
                MarkClosed("send " + r);
                return;
            }
        }

        public void OnRaw(IntPtr data, int size)
        {
            if (size < 1) return;
            var buf = new byte[size];
            Marshal.Copy(data, buf, 0, size);
            if (buf[0] == 2)
            {
                if (size < 2) return;
                var single = new byte[size - 2];
                Buffer.BlockCopy(buf, 2, single, 0, single.Length);
                Inbox.Enqueue(new Packet { Type = (Msg)buf[1], Data = single });
                return;
            }
            _assembling.Write(buf, 1, size - 1);
            if (buf[0] != 1) return;
            byte[] frame = _assembling.ToArray();
            _assembling.SetLength(0);
            if (frame.Length < 1) return;
            var payload = new byte[frame.Length - 1];
            Buffer.BlockCopy(frame, 1, payload, 0, payload.Length);
            Inbox.Enqueue(new Packet { Type = (Msg)frame[0], Data = payload });
        }

        public override void Dispose()
        {
            _closed = true;
            try { _connection.Close(); } catch { }
        }
    }
}
