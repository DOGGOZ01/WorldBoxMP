using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace WorldBoxMP
{
    public enum Msg : byte
    {
        Hello = 1,
        World = 2,
        Power = 3,
        Resync = 4,
        Delta = 5,
        Possess = 6,
        Unpossess = 7,
        PossessInput = 8,
        Names = 9,
        Look = 10,
        MetaCheck = 11,
        MetaRepair = 12,
        Ping = 13,
        Pong = 14,
        Edit = 15,
        Cursor = 16,
        Pos = 17,
        View = 18,
        BucketCheck = 19,
        BucketList = 20,
        ObjRepair = 21,
        Order = 22,
        Rules = 23,
        Denied = 24,
        Laws = 25,
        Unlock = 26,
        Welcome = 27,
        Lobby = 28,
        Kicked = 29,
        Refused = 30,
        Magnet = 31,
        Boulder = 32,
    }

    public struct Packet
    {
        public Msg Type;
        public byte[] Data;
    }

    public enum Role { None, Host, Client }

    public abstract class Peer : IDisposable
    {
        public readonly ConcurrentQueue<Packet> Inbox = new ConcurrentQueue<Packet>();
        public abstract bool Closed { get; }
        public abstract string Remote { get; }
        public abstract void Send(Msg type, byte[] data);
        public virtual void SendUnreliable(Msg type, byte[] data) => Send(type, data);
        public virtual long PendingBytes => 0;
        public virtual int SendRate => 0;
        public virtual void Pump() { }
        public virtual string BanKey => null;
        public abstract void Dispose();
    }

    public class TcpPeer : Peer
    {
        const int MaxFrame = 256 * 1024 * 1024;

        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly object _sendLock = new object();
        readonly ConcurrentQueue<byte[]> _throttled = new ConcurrentQueue<byte[]>();
        long _throttledBytes;
        Thread _throttle;
        readonly Thread _reader;
        volatile bool _closed;
        readonly string _remote;

        public override bool Closed => _closed;
        public override string Remote => _remote;
        public override string BanKey
        {
            get
            {
                try { return "ip:" + ((IPEndPoint)_tcp.Client.RemoteEndPoint).Address; }
                catch { return null; }
            }
        }

        public TcpPeer(TcpClient tcp)
        {
            _tcp = tcp;
            _tcp.NoDelay = true;
            _stream = tcp.GetStream();
            _remote = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "WorldBoxMP reader" };
            _reader.Start();
        }

        void ReadLoop()
        {
            try
            {
                var br = new BinaryReader(_stream);
                while (!_closed)
                {
                    int len = br.ReadInt32();
                    if (len < 1 || len > MaxFrame) throw new InvalidDataException("bad frame length " + len);
                    var type = (Msg)br.ReadByte();
                    byte[] data = ReadExactly(br, len - 1);
                    Inbox.Enqueue(new Packet { Type = type, Data = data });
                }
            }
            catch (Exception e)
            {
                if (!_closed) UnityEngine.Debug.Log("[WorldBoxMP] connection closed: " + e.Message);
            }
            _closed = true;
        }

        static byte[] ReadExactly(BinaryReader br, int count)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = br.Read(buf, read, count - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
            return buf;
        }

        public override long PendingBytes => Interlocked.Read(ref _throttledBytes);
        public override int SendRate => TestHarness.RateKiBps > 0 ? TestHarness.RateKiBps * 1024 : 0;

        public override void SendUnreliable(Msg type, byte[] data)
        {
            if (TestHarness.RateKiBps > 0 && PendingBytes > TestHarness.RateKiBps * 1024 / 10) return;
            Send(type, data);
        }

        public override void Send(Msg type, byte[] data)
        {
            NetStats.CountOut(data);
            if (_closed) return;
            data = data ?? new byte[0];
            if (TestHarness.RateKiBps > 0)
            {
                var frame = new byte[data.Length + 5];
                BitConverter.GetBytes(data.Length + 1).CopyTo(frame, 0);
                frame[4] = (byte)type;
                Buffer.BlockCopy(data, 0, frame, 5, data.Length);
                Interlocked.Add(ref _throttledBytes, frame.Length);
                _throttled.Enqueue(frame);
                if (_throttle == null)
                {
                    _throttle = new Thread(ThrottleLoop) { IsBackground = true, Name = "WorldBoxMP rate limit" };
                    _throttle.Start();
                }
                return;
            }
            try
            {
                lock (_sendLock)
                {
                    var header = new byte[5];
                    BitConverter.GetBytes(data.Length + 1).CopyTo(header, 0);
                    header[4] = (byte)type;
                    _stream.Write(header, 0, 5);
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[WorldBoxMP] send failed: " + e.Message);
                _closed = true;
            }
        }

        void ThrottleLoop()
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double sent = 0;
                while (!_closed)
                {
                    if (!_throttled.TryDequeue(out byte[] frame)) { Thread.Sleep(2); continue; }
                    lock (_sendLock) _stream.Write(frame, 0, frame.Length);
                    Interlocked.Add(ref _throttledBytes, -frame.Length);
                    sent += frame.Length;
                    double rate = TestHarness.RateKiBps * 1024.0;
                    double ahead = sent / rate - clock.Elapsed.TotalSeconds;
                    if (ahead > 0) Thread.Sleep((int)(ahead * 1000));
                    if (clock.Elapsed.TotalSeconds > 5) { clock.Restart(); sent = 0; }
                }
            }
            catch (Exception e)
            {
                if (!_closed) UnityEngine.Debug.Log("[WorldBoxMP] send failed: " + e.Message);
                _closed = true;
            }
        }

        public override void Dispose()
        {
            _closed = true;
            try { _tcp.Close(); } catch { }
        }
    }

    public class Guest
    {
        public byte Slot;
        public Peer Peer;
        public bool Ready;
        public string Name = "";
        public readonly HashSet<long> Units = new HashSet<long>();
        public int PingMs;
        public string BanKey;
        public bool ViewKnown;
        public float MinX, MinY, MaxX, MaxY;
        public bool OwnRules;
        public Perm Perms = Perm.All;
        public readonly HashSet<string> Denied = new HashSet<string>();

        public string Label => string.IsNullOrEmpty(Name) ? Peer?.Remote ?? "?" : Name;
    }

    class BroadcastPeer : Peer
    {
        public override bool Closed => !Net.HasReadyGuest;
        public override string Remote
        {
            get
            {
                var names = new List<string>();
                foreach (Guest g in Net.Guests) if (g.Ready) names.Add(g.Label);
                return names.Count == 0 ? "none" : string.Join(",", names.ToArray());
            }
        }

        public override void Send(Msg type, byte[] data)
        {
            foreach (Guest g in Net.Guests)
                if (g.Ready && !g.Peer.Closed) g.Peer.Send(type, data);
        }

        public override void SendUnreliable(Msg type, byte[] data)
        {
            foreach (Guest g in Net.Guests)
                if (g.Ready && !g.Peer.Closed) g.Peer.SendUnreliable(type, data);
        }

        public override long PendingBytes
        {
            get
            {
                long most = 0;
                foreach (Guest g in Net.Guests) if (g.Ready && !g.Peer.Closed) most = Math.Max(most, g.Peer.PendingBytes);
                return most;
            }
        }

        public override int SendRate
        {
            get
            {
                int least = 0;
                foreach (Guest g in Net.Guests)
                {
                    if (!g.Ready || g.Peer.Closed) continue;
                    int r = g.Peer.SendRate;
                    if (r > 0 && (least == 0 || r < least)) least = r;
                }
                return least;
            }
        }

        public override void Pump()
        {
            foreach (Guest g in Net.Guests) g.Peer.Pump();
        }

        public override void Dispose() { }
    }

    public static class Net
    {
        public const int ProtocolVersion = 20;

        public static Role Role { get; private set; } = Role.None;
        static Peer _peer;
        static readonly BroadcastPeer _broadcast = new BroadcastPeer();

        public static Peer Peer => Role == Role.Host ? (HasReadyGuest ? _broadcast : null) : _peer;

        public static readonly List<Guest> Guests = new List<Guest>();

        public static Guest Current;

        public static bool HasReadyGuest
        {
            get
            {
                foreach (Guest g in Guests) if (g.Ready && !g.Peer.Closed) return true;
                return false;
            }
        }

        public static void Reply(Msg type, byte[] data)
        {
            if (Role == Role.Host) Current?.Peer.Send(type, data);
            else _peer?.Send(type, data);
        }

        public static Guest GuestIn(byte slot)
        {
            foreach (Guest g in Guests) if (g.Slot == slot) return g;
            return null;
        }

        public static void AddGuest(Guest g) => Guests.Add(g);

        public static void RemoveGuest(Guest g)
        {
            Guests.Remove(g);
            try { g.Peer.Dispose(); } catch { }
        }

        static TcpListener _listener;
        static readonly ConcurrentQueue<Peer> _accepted = new ConcurrentQueue<Peer>();

        public static bool IsClientConnected => Role == Role.Client && _peer != null && !_peer.Closed;

        public static bool MirrorWorld;
        public static bool IsHostWithPeer => Role == Role.Host && HasReadyGuest;

        public static void StartHost(int port)
        {
            Stop();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Role = Role.Host;
            _listener.BeginAcceptTcpClient(OnAccept, _listener);
        }

        static void OnAccept(IAsyncResult ar)
        {
            var listener = (TcpListener)ar.AsyncState;
            try
            {
                _accepted.Enqueue(new TcpPeer(listener.EndAcceptTcpClient(ar)));
                listener.BeginAcceptTcpClient(OnAccept, listener);
            }
            catch (ObjectDisposedException) { }
            catch (Exception e) { UnityEngine.Debug.Log("[WorldBoxMP] accept failed: " + e.Message); }
        }

        public static void Offer(Peer peer) => _accepted.Enqueue(peer);

        public static Peer TakeAccepted() => _accepted.TryDequeue(out var peer) ? peer : null;

        public static void Connect(string host, int port)
        {
            Stop();
            var tcp = new TcpClient();
            if (!tcp.ConnectAsync(host, port).Wait(5000))
            {
                tcp.Close();
                throw new TimeoutException("no answer from " + host + ":" + port);
            }
            Role = Role.Client;
            _peer = new TcpPeer(tcp);
        }

        public static void ConnectWith(Peer peer)
        {
            _peer?.Dispose();
            Role = Role.Client;
            _peer = peer;
        }

        public static void BecomeHost()
        {
            if (Role != Role.Host) Stop();
            Role = Role.Host;
        }

        public static void Stop()
        {
            if (Role == Role.Client) ReleaseClientUnits();
            _peer?.Dispose();
            _peer = null;
            foreach (Guest g in Guests) { try { g.Peer.Dispose(); } catch { } }
            Guests.Clear();
            Current = null;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            while (_accepted.TryDequeue(out var p)) p.Dispose();
            RemotePossession.HostClear();
            SteamSession.Stop();
            Role = Role.None;
        }

        static void ReleaseClientUnits()
        {
            try
            {
                PlayerLook.Clear();
                OwnUnit.Reset();
                if (ControllableUnit.isControllingUnit())
                {
                    ControllableUnit.clear(pCallKill: false);
                    UnityEngine.Debug.Log("[WorldBoxMP] left the possessed unit after the session ended");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.Log("[WorldBoxMP] could not leave the possessed unit: " + e.Message);
            }
        }
    }
}
