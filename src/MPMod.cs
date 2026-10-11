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
                Tip("Запрошен мир у хоста...");
            }
            else if (Net.IsHostWithPeer)
            {
                SendWorld();
                Tip("Мир отправлен второму игроку");
            }
            else Tip("Нет подключения");
        }

        public void StopSession()
        {
            if (Net.Role == Role.None)
            {
                Tip("Игра вдвоём не запущена");
                return;
            }
            _hasWorld = false;
            Net.Stop();
            Tip("Игра вдвоём завершена");
            SessionLog.Stop();
        }

        public static string StatusText()
        {
            switch (Net.Role)
            {
                case Role.Host when Net.IsHostWithPeer:
                    return "Вы хост. Играет: " + Net.Peer.Remote + "\nПоток: " + HostSync.LastKiBps + " КБ/с";
                case Role.Host:
                    return "Вы хост. Ждём второго игрока..." + (SteamSession.LobbyOpen ? "\nЛобби Steam открыто" : "");
                case Role.Client when Net.IsClientConnected:
                    return "Подключено к " + Net.Peer.Remote + "\nСверка мира: " + Checksum.Matches + " ок, расхождений " + Checksum.Mismatches;
                case Role.Client:
                    return "Подключение...";
                default:
                    return "Не подключено.\nF7 — создать игру, F8 — подключиться по IP";
            }
        }

        public void Host()
        {
            Cfg = MPConfig.Load(GetDeclaration().FolderPath);
            if (Net.Role != Role.Host) SessionLog.Start("host");
            try
            {
                Net.StartHost(Cfg.Port);
                Tip("Игра создана (порт " + Cfg.Port + "). Ждём второго игрока...");
            }
            catch (Exception e)
            {
                Net.BecomeHost();
                Tip("Порт для IP не открылся (" + e.GetBaseException().Message + "), но приглашение через Steam работает");
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
                Tip("Подключено к " + Cfg.HostIp + ". Загружаем мир хоста...");
                return true;
            }
            catch (Exception e)
            {
                Net.Stop();
                Tip("Не удалось подключиться к " + Cfg.HostIp + ":" + Cfg.Port + " (" + e.GetBaseException().Message + ")");
                return false;
            }
        }

        void HostUpdate()
        {
            bool loading = SmoothLoader.isLoading();
            if (_hostWasLoading && !loading && Net.IsHostWithPeer) SendWorld();
            _hostWasLoading = loading;
            HostSync.Update(Cfg.TickMs / 1000f);
            RemotePossession.HostUpdate();
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
            if (Net.Role == Role.Host && Net.TakeAccepted() is Peer joined)
            {
                Tip("Второй игрок подключился: " + joined.Remote);
                ViewSync.Reset();
                Flow.Reset();
                _resyncTimer = 0f;
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

            if (peer.Closed && peer.Inbox.IsEmpty)
            {
                if (Net.Role == Role.Client)
                {
                    _hasWorld = false;
                    Net.Stop();
                    SessionLog.Stop();
                    Tip("Связь с хостом потеряна. Это копия мира хоста, она стоит на паузе: загрузите своё сохранение или создайте новый мир, чтобы играть дальше.");
                    TestHarness.OnClientDropped();
                }
                else if (Net.Role == Role.Host)
                {
                    RemotePossession.HostReceiveUnpossess();
                    Net.DropPeer();
                    Tip("Второй игрок вышел. Игра по-прежнему открыта.");
                }
                return;
            }

            if (Net.IsHostWithPeer && Cfg.ResyncSeconds > 0f && !SmoothLoader.isLoading())
            {
                _resyncTimer += Time.unscaledDeltaTime;
                if (_resyncTimer >= Cfg.ResyncSeconds && !RemotePossession.HasRemote) SendWorld();
            }
        }

        void Handle(Packet pkt)
        {
            switch (pkt.Type)
            {
                case Msg.Hello when Net.Role == Role.Host:
                    int version = pkt.Data.Length >= 4 ? BitConverter.ToInt32(pkt.Data, 0) : -1;
                    if (version != Net.ProtocolVersion)
                        Tip("У второго игрока другая версия WorldBoxMP (" + version + ", у вас " + Net.ProtocolVersion + ")");
                    SendWorld();
                    break;
                case Msg.Resync when Net.Role == Role.Host:
                    SendWorld();
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

        void SendWorld()
        {
            _resyncTimer = 0f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (Actor a in World.world.units) a.getName();
            byte[] bytes = SaveManager.currentWorldToSavedMap().toZip();
            Net.Peer.Send(Msg.World, bytes);
            HostSync.ResetAfterSnapshot();
            Debug.Log("[WorldBoxMP] sent world: " + bytes.Length / 1024 + " KiB, built in " + sw.ElapsedMilliseconds + " ms");
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

        internal static void Tip(string text)
        {
            Debug.Log("[WorldBoxMP] " + text);
            WorldTip.showNow(text, false, "top", 4f);
        }
    }
}
