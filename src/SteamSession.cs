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
    public static class SteamSession
    {
        static readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        static bool _initDone;
        static Lobby? _lobby;
        static HostSocket _socket;
        static ClientConnection _connection;
        static ulong _pendingLobby;

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
                Debug.Log("[WorldBoxMP] Steam is not available; only direct IP (F7/F8) works");
                return;
            }
            try
            {
                SteamNetworkingUtils.SendBufferSize = 64 * 1024 * 1024;
                SteamNetworkingUtils.InitRelayNetworkAccess();
            }
            catch (Exception e)
            {
                Debug.Log("[WorldBoxMP] Steam networking setup: " + e.Message);
            }
            SteamFriends.OnGameLobbyJoinRequested += (lobby, friend) =>
                _mainThread.Enqueue(() => JoinLobby(lobby.Id));

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong id))
                    _pendingLobby = id;
            Debug.Log("[WorldBoxMP] Steam ready as " + SteamClient.Name + (_pendingLobby != 0 ? ", invited to lobby " + _pendingLobby : ""));
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
                JoinLobby(id);
            }
            try
            {
                _socket?.Receive(64);
                _connection?.Receive(64);
            }
            catch (Exception e) { Debug.Log("[WorldBoxMP] steam receive: " + e.Message); }
        }

        public static async void Host()
        {
            if (!Available) return;
            try
            {
                if (_socket == null) _socket = SteamNetworkingSockets.CreateRelaySocket<HostSocket>(0);
                if (_lobby == null)
                {
                    Lobby? created = await SteamMatchmaking.CreateLobbyAsync(2);
                    if (!created.HasValue)
                    {
                        MPMod.Tip("Не удалось создать лобби Steam; друг может подключиться по IP");
                        return;
                    }
                    Lobby lobby = created.Value;
                    lobby.SetFriendsOnly();
                    lobby.SetJoinable(true);
                    lobby.SetData("wbmp_protocol", Net.ProtocolVersion.ToString());
                    _lobby = lobby;
                    Debug.Log("[WorldBoxMP] Steam lobby " + lobby.Id.Value + " created by " + SteamClient.SteamId.Value);
                }
                SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
                MPMod.Tip("Лобби Steam открыто: пригласите друга в окне Steam");
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldBoxMP] steam host: " + e);
                MPMod.Tip("Ошибка Steam при создании игры: " + e.Message);
            }
        }

        static async void JoinLobby(ulong lobbyId)
        {
            if (!Available) return;
            try
            {
                Net.Stop();
                SessionLog.Start("client");
                var lobby = new Lobby(lobbyId);
                RoomEnter entered = await lobby.Join();
                if (entered != RoomEnter.Success)
                {
                    MPMod.Tip("Не удалось войти в лобби Steam (" + entered + ")");
                    return;
                }
                _lobby = lobby;
                string theirs = lobby.GetData("wbmp_protocol");
                if (theirs != Net.ProtocolVersion.ToString())
                    MPMod.Tip("У хоста другая версия WorldBoxMP (" + theirs + ", у вас " + Net.ProtocolVersion + ")");

                Friend owner = lobby.Owner;
                if (owner.Id == SteamClient.SteamId)
                {
                    MPMod.Tip("Это ваше собственное лобби: две игры на одном аккаунте Steam не соединяются. Для проверки на одном ПК используйте IP");
                    return;
                }
                _connection = SteamNetworkingSockets.ConnectRelay<ClientConnection>(owner.Id, 0);
                var peer = new SteamPeer(_connection.Connection, owner.Name);
                _connection.Peer = peer;
                Net.ConnectWith(peer);
                peer.Send(Msg.Hello, BitConverter.GetBytes(Net.ProtocolVersion));
                MPMod.Tip("Подключаемся к " + owner.Name + " через Steam...");
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldBoxMP] steam join: " + e);
                MPMod.Tip("Ошибка Steam при подключении: " + e.Message);
            }
        }

        public static bool LobbyOpen => _lobby.HasValue;

        public static void Invite()
        {
            if (!Available)
            {
                MPMod.Tip("Steam недоступен — используйте подключение по IP");
                return;
            }
            if (Net.Role != Role.Host || !_lobby.HasValue)
            {
                MPMod.Instance.Host();
                return;
            }
            SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
        }

        public static void Stop()
        {
            try { _connection?.Close(); } catch { }
            try { _socket?.Close(); } catch { }
            try { _lobby?.Leave(); } catch { }
            _connection = null;
            _socket = null;
            _lobby = null;
        }

        class HostSocket : SocketManager
        {
            readonly Dictionary<uint, SteamPeer> _peers = new Dictionary<uint, SteamPeer>();

            public override void OnConnecting(Connection connection, ConnectionInfo info)
            {
                if (Net.IsHostWithPeer) connection.Close();
                else base.OnConnecting(connection, info);
            }

            public override void OnConnected(Connection connection, ConnectionInfo info)
            {
                base.OnConnected(connection, info);
                string name = new Friend(info.Identity.SteamId).Name;
                var peer = new SteamPeer(connection, name);
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

        public void MarkConnected() => _connected = true;

        public void MarkClosed(string reason)
        {
            if (!_closed) Debug.Log("[WorldBoxMP] Steam connection closed: " + reason);
            _closed = true;
        }

        public override void Send(Msg type, byte[] data)
        {
            if (_closed) return;
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
