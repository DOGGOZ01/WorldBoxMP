using System;
using System.Collections.Concurrent;
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
        public virtual void Pump() { }
        public abstract void Dispose();
    }

    public class TcpPeer : Peer
    {
        const int MaxFrame = 256 * 1024 * 1024;

        readonly TcpClient _tcp;
        readonly NetworkStream _stream;
        readonly object _sendLock = new object();
        readonly Thread _reader;
        volatile bool _closed;
        readonly string _remote;

        public override bool Closed => _closed;
        public override string Remote => _remote;

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

        public override void Send(Msg type, byte[] data)
        {
            if (_closed) return;
            data = data ?? new byte[0];
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

        public override void Dispose()
        {
            _closed = true;
            try { _tcp.Close(); } catch { }
        }
    }

    public static class Net
    {
        public const int ProtocolVersion = 8;

        public static Role Role { get; private set; } = Role.None;
        public static Peer Peer { get; private set; }

        static TcpListener _listener;
        static readonly ConcurrentQueue<Peer> _accepted = new ConcurrentQueue<Peer>();

        public static bool IsClientConnected => Role == Role.Client && Peer != null && !Peer.Closed;
        public static bool IsHostWithPeer => Role == Role.Host && Peer != null && !Peer.Closed;

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

        public static Peer TakeAccepted()
        {
            if (!_accepted.TryDequeue(out var peer)) return null;
            if (IsHostWithPeer)
            {
                peer.Dispose();
                return null;
            }
            Peer?.Dispose();
            Peer = peer;
            return Peer;
        }

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
            Peer = new TcpPeer(tcp);
        }

        public static void ConnectWith(Peer peer)
        {
            Peer?.Dispose();
            Role = Role.Client;
            Peer = peer;
        }

        public static void BecomeHost()
        {
            if (Role != Role.Host) Stop();
            Role = Role.Host;
        }

        public static void DropPeer()
        {
            Peer?.Dispose();
            Peer = null;
        }

        public static void Stop()
        {
            if (Role == Role.Client) ReleaseClientUnits();
            Peer?.Dispose();
            Peer = null;
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
