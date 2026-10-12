using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace WorldBoxMP
{
    public class MPMod : BasicMod<MPMod>
    {
        public static Harmony Harmony;
        public static MPConfig Cfg;
        float _resyncTimer;
        bool _hostWasLoading;
        bool _askedResync;
        static bool _hasWorld;
        static readonly Queue<KeyValuePair<double, Packet>> _lagged = new Queue<KeyValuePair<double, Packet>>();
        static bool _restoreCamera;
        static Vector3 _cameraPos;
        static float _cameraZoom;

        protected override void OnModLoad()
        {
            Cfg = MPConfig.Load(GetDeclaration().FolderPath);
            SessionLog.Init(GetDeclaration().FolderPath);
            TestHarness.ParseArgs();
            Harmony = new Harmony("doggoz01.worldboxmp");
            Harmony.PatchAll(typeof(MPMod).Assembly);
            Debug.Log("[WorldBoxMP] loaded, protocol " + Net.ProtocolVersion);
        }

        void Update()
        {
            if (!Config.game_loaded) return;
            Perf.Begin();
            try
            {
                SteamSession.Init();
                SteamSession.Update();
                MPTab.TryCreate();
                MPTab.Update();
                Rules.Update();
                HostOnly.Update();
                SessionLog.Update();
                HandleHotkeys();
                TestHarness.Update(this);
                PumpNetwork();
                if (Net.Role == Role.Host) HostUpdate();
                if (Net.IsClientConnected) ClientUpdate();
                NetStats.Update();
                PlayerNames.Update();
                RemoteCursor.Update();
                PlayerLook.Update();
                if (WorldfallBridge.Present)
                {
                    WorldfallBridge.PatchSteering(Harmony);
                    SoundSync.PatchWorldfall(Harmony);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldBoxMP] " + e);
            }
            Perf.End();
            Perf.Frame();
        }

        void OnGUI()
        {
            GUI.depth = -1001;
            try
            {
                PlayerNames.Draw();
                RemoteCursor.Draw();
            }
            catch (Exception e)
            {
                Debug.LogError("[WorldBoxMP] names: " + e);
            }
        }

        void OnApplicationQuit()
        {
            Net.Stop();
            SessionLog.Stop();
        }

        public string LogFolder => System.IO.Path.Combine(GetDeclaration().FolderPath, "logs");

        void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                Host();
            }
            else if (Input.GetKeyDown(KeyCode.F8))
            {
                Join();
            }
            else if (Input.GetKeyDown(KeyCode.F9))
            {
                Resync();
            }
            else if (Input.GetKeyDown(KeyCode.F6))
            {
                StopSession();
            }
        }

        public void Resync()
        {
            if (Net.IsClientConnected)
            {
                Net.Peer.Send(Msg.Resync, null);
                Tip("tip_world_requested");
            }
            else if (Net.IsHostWithPeer)
            {
                SendWorldToAll();
                Tip("tip_world_sent");
            }
            else Tip("tip_not_connected");
        }

        public void StopSession()
        {
            if (Net.Role == Role.None)
            {
                Tip("tip_not_connected");
                return;
            }
            _hasWorld = false;
            Net.Stop();
            Rules.ResetClient();
            Tip("tip_ended");
            SessionLog.Stop();
        }

        public void Host()
        {
            Cfg = MPConfig.Load(GetDeclaration().FolderPath);
            if (Net.Role != Role.Host) SessionLog.Start("host");
            try
            {
                Net.StartHost(Cfg.Port);
                Tip("tip_hosting");
            }
            catch (Exception e)
            {
                Net.BecomeHost();
                Debug.Log("[WorldBoxMP] port " + Cfg.Port + ": " + e.GetBaseException().Message);
                Tip("tip_port_busy", Cfg.Port);
            }
            SteamSession.Host();
        }

        public bool Join()
        {
            Cfg = MPConfig.Load(GetDeclaration().FolderPath);
            try
            {
                SessionLog.Start("client");
                Net.Connect(Cfg.HostIp, Cfg.Port);
                Net.Peer.Send(Msg.Hello, BitConverter.GetBytes(Net.ProtocolVersion));
                Tip("tip_connecting", Cfg.HostIp);
                return true;
            }
            catch (Exception e)
            {
                Net.Stop();
                Debug.Log("[WorldBoxMP] connect " + Cfg.HostIp + ":" + Cfg.Port + ": " + e.GetBaseException().Message);
                Tip("tip_connect_failed", Cfg.HostIp + ":" + Cfg.Port);
                return false;
            }
        }

        void HostUpdate()
        {
            bool loading = SmoothLoader.isLoading();
            if (_hostWasLoading && !loading && Net.IsHostWithPeer) SendWorldToAll();
            _hostWasLoading = loading;
            HostSync.Update(Cfg.TickMs / 1000f);
            RemotePossession.HostUpdate();
            RemoteMagnet.HostUpdate();
        }

        void ClientUpdate()
        {
            if (_restoreCamera && !SmoothLoader.isLoading())
            {
                _restoreCamera = false;
                UnityEngine.Camera.main.transform.position = _cameraPos;
                Access.Camera.forceZoom(_cameraZoom);
            }
            ClientSync.Update();
            RemotePossession.ClientUpdate();
            RemoteMagnet.ClientUpdate();
            RemoteMagnet.ClientBallUpdate();
            PlayerEdits.UpdateClient();
            ViewSync.UpdateClient();
            if (Checksum.Consecutive >= 15 && !_askedResync)
            {
                _askedResync = true;
                Debug.Log("[WorldBoxMP] world differs from the host 15 checks in a row, asking for the whole world");
                Net.Peer.Send(Msg.Resync, null);
            }
            if (ClientSync.Failures > 20 && !_askedResync)
            {
                _askedResync = true;
                Debug.Log("[WorldBoxMP] too many stream errors, asking the host for the whole world");
                Net.Peer.Send(Msg.Resync, null);
            }
        }

        void PumpNetwork()
        {
            if (Net.Role == Role.Host)
            {
                while (Net.TakeAccepted() is Peer joined) Lobby.OnConnect(joined);
                Lobby.HostUpdate();
                foreach (Guest g in new List<Guest>(Net.Guests))
                {
                    g.Peer.Pump();
                    Net.Current = g;
                    try
                    {
                        for (int i = 0; i < 64 && g.Peer.Inbox.TryDequeue(out var pkt); i++)
                        {
                            NetStats.CountIn(pkt);
                            Handle(pkt);
                        }
                    }
                    finally { Net.Current = null; }
                    if (g.Peer.Closed && g.Peer.Inbox.IsEmpty && Net.Guests.Contains(g))
                    {
                        Lobby.Leave(g);
                        try { g.Peer.Dispose(); } catch { }
                        Tip("tip_player_left_named", g.Label);
                    }
                }
                if (Net.IsHostWithPeer && Cfg.ResyncSeconds > 0f && !SmoothLoader.isLoading())
                {
                    _resyncTimer += Time.unscaledDeltaTime;
                    if (_resyncTimer >= Cfg.ResyncSeconds && !RemotePossession.HasRemote) SendWorldToAll();
                }
                return;
            }

            Peer peer = Net.Peer;
            if (peer == null) return;
            peer.Pump();
            for (int i = 0; i < 64 && peer.Inbox.TryDequeue(out var pkt); i++)
            {
                NetStats.CountIn(pkt);
                if (TestHarness.LagMs > 0) _lagged.Enqueue(new KeyValuePair<double, Packet>(Clock.Now + TestHarness.LagMs / 1000.0, pkt));
                else Handle(pkt);
            }
            while (_lagged.Count > 0 && _lagged.Peek().Key <= Clock.Now)
                Handle(_lagged.Dequeue().Value);

            if (peer.Closed && peer.Inbox.IsEmpty && Net.Role == Role.Client) LeaveHost("tip_host_left");
        }

        void LeaveHost(string tipKey)
        {
            LobbyTest.LastLeave = tipKey;
            _hasWorld = false;
            Net.Stop();
            Rules.ResetClient();
            Roster.Clear();
            SessionLog.Stop();
            Tip(tipKey);
            TestHarness.OnClientDropped();
        }

        void Handle(Packet pkt)
        {
            switch (pkt.Type)
            {
                case Msg.Hello when Net.Role == Role.Host:
                    int version = pkt.Data.Length >= 4 ? BitConverter.ToInt32(pkt.Data, 0) : -1;
                    if (version != Net.ProtocolVersion)
                        Tip("tip_version", version, Net.ProtocolVersion);
                    Guest joined = Net.Current;
                    joined.Peer.Send(Msg.Welcome, new[] { joined.Slot });
                    Rules.SendTo(joined);
                    SendWorldTo(joined);
                    HostOnly.ResendLaws();
                    Tip("tip_joined_named", joined.Label);
                    break;
                case Msg.Welcome when Net.Role == Role.Client:
                    Roster.LocalSlot = pkt.Data.Length > 0 ? pkt.Data[0] : (byte)1;
                    SessionLog.Write("this player is in slot " + Roster.LocalSlot);
                    break;
                case Msg.Lobby when Net.Role == Role.Client:
                    Lobby.ClientReceive(pkt.Data);
                    break;
                case Msg.Kicked when Net.Role == Role.Client:
                    LeaveHost("tip_kicked");
                    break;
                case Msg.Refused when Net.Role == Role.Client:
                    string why = "lobby_full";
                    try { why = new System.IO.BinaryReader(new System.IO.MemoryStream(pkt.Data)).ReadString(); } catch { }
                    LeaveHost(why);
                    break;
                case Msg.Rules when Net.Role == Role.Client:
                    Rules.Receive(pkt.Data);
                    break;
                case Msg.Denied when Net.Role == Role.Client:
                    Rules.ReceiveDenied(pkt.Data);
                    break;
                case Msg.Laws when Net.Role == Role.Client:
                    HostOnly.ClientReceiveLaws(pkt.Data);
                    break;
                case Msg.Unlock when Net.Role == Role.Client:
                    HostOnly.ClientReceiveUnlock();
                    break;
                case Msg.Resync when Net.Role == Role.Host:
                    SendWorldTo(Net.Current);
                    break;
                case Msg.Power when Net.Role == Role.Host:
                    Powers.ApplyRemoteClick(pkt.Data);
                    break;
                case Msg.World when Net.Role == Role.Client:
                    LoadWorld(pkt.Data);
                    Checksum.Reset();
                    _askedResync = false;
                    break;
                case Msg.Delta when Net.Role == Role.Client:
                    ClientSync.Enqueue(pkt.Data);
                    break;
                case Msg.Possess when Net.Role == Role.Host:
                    RemotePossession.HostReceivePossess(pkt.Data);
                    break;
                case Msg.Unpossess when Net.Role == Role.Host:
                    RemotePossession.HostReceiveUnpossess();
                    break;
                case Msg.PossessInput when Net.Role == Role.Host:
                    RemotePossession.HostReceiveInput(pkt.Data);
                    break;
                case Msg.Names:
                    PlayerNames.Receive(pkt.Data);
                    break;
                case Msg.Look:
                    PlayerLook.Receive(pkt.Data);
                    break;
                case Msg.Pos when Net.Role == Role.Client:
                    PositionSync.Receive(pkt.Data);
                    break;
                case Msg.View when Net.Role == Role.Host:
                    ViewSync.Receive(pkt.Data);
                    break;
                case Msg.BucketCheck when Net.Role == Role.Host:
                    ObjectRepair.HostRequestBuckets(pkt.Data);
                    break;
                case Msg.BucketList when Net.Role == Role.Host:
                    ObjectRepair.HostRequestItems(pkt.Data);
                    break;
                case Msg.ObjRepair when Net.Role == Role.Host:
                    ObjectRepair.HostResend(pkt.Data);
                    break;
                case Msg.Magnet when Net.Role == Role.Host:
                    RemoteMagnet.HostReceive(pkt.Data);
                    break;
                case Msg.Boulder when Net.Role == Role.Host:
                    RemoteMagnet.HostBall(pkt.Data);
                    break;
                case Msg.Order when Net.Role == Role.Host:
                    Orders.HostReceive(pkt.Data);
                    break;
                case Msg.Cursor:
                    RemoteCursor.Receive(pkt.Data);
                    break;
                case Msg.Edit when Net.Role == Role.Host:
                    PlayerEdits.HostReceive(pkt.Data);
                    break;
                case Msg.Ping:
                    NetStats.OnPing(pkt.Data);
                    break;
                case Msg.Pong:
                    NetStats.OnPong(pkt.Data);
                    break;
                case Msg.MetaCheck when Net.Role == Role.Host:
                    MetaSync.RequestLists(pkt.Data);
                    break;
                case Msg.MetaRepair when Net.Role == Role.Host:
                    MetaSync.Repair(pkt.Data);
                    break;
                default:
                    Debug.Log("[WorldBoxMP] ignored " + pkt.Type + " as " + Net.Role);
                    break;
            }
        }

        static byte[] BuildWorld(out long ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (Actor a in World.world.units) a.getName();
            byte[] bytes = SaveManager.currentWorldToSavedMap().toZip();
            ms = sw.ElapsedMilliseconds;
            return bytes;
        }

        void SendWorldTo(Guest g)
        {
            if (g == null) return;
            _resyncTimer = 0f;
            bool others = false;
            foreach (Guest o in Net.Guests) if (o != g && o.Ready && !o.Peer.Closed) others = true;
            byte[] bytes = BuildWorld(out long ms);
            g.Peer.Send(Msg.World, bytes);
            g.Ready = true;
            if (others) HostSync.ResendOncePerObject();
            else HostSync.ResetAfterSnapshot();
            Lobby.Changed();
            Debug.Log("[WorldBoxMP] sent world to " + g.Label + ": " + bytes.Length / 1024 + " KiB, built in " + ms + " ms" + (others ? " (shared stream kept)" : ""));
        }

        void SendWorldToAll()
        {
            _resyncTimer = 0f;
            byte[] bytes = BuildWorld(out long ms);
            foreach (Guest g in Net.Guests)
            {
                if (g.Peer.Closed) continue;
                g.Peer.Send(Msg.World, bytes);
                g.Ready = true;
            }
            HostSync.ResetAfterSnapshot();
            Lobby.Changed();
            Debug.Log("[WorldBoxMP] sent world to everyone: " + bytes.Length / 1024 + " KiB, built in " + ms + " ms");
        }

        static void LoadWorld(byte[] bytes)
        {
            if (_hasWorld)
            {
                _cameraPos = UnityEngine.Camera.main.transform.position;
                _cameraZoom = Access.Camera.getTargetZoom();
                _restoreCamera = true;
            }
            _hasWorld = true;
            Net.MirrorWorld = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ClientSync.Reset();
            SaveManager.loadMapFromBytes(bytes);
            TestHarness.OnClientWorldLoaded();
            Debug.Log("[WorldBoxMP] loaded host world: " + bytes.Length / 1024 + " KiB in " + sw.ElapsedMilliseconds + " ms");
        }

        internal static void Tip(string key, params object[] args)
        {
            Debug.Log("[WorldBoxMP] " + Lang.En(key, args));
            WorldTip.showNow(Lang.T(key, args), false, "top", 4f);
        }

        public static void SaveAddress(string ip, int port)
        {
            Cfg = MPConfig.Load(Instance.GetDeclaration().FolderPath);
            if (Cfg.HostIp == ip && Cfg.Port == port) return;
            Cfg.HostIp = ip;
            Cfg.Port = port;
            Cfg.Save(System.IO.Path.Combine(Instance.GetDeclaration().FolderPath, "WorldBoxMP.cfg"));
        }
    }
}
