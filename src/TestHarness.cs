using System;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class TestHarness
    {
        static string _autoRole;
        static string _shotPath;
        static bool _powerTest;
        static int _slot;
        static string _uiFolder;
        public static string UiFolder => _uiFolder ?? ".";
        static float _uiClock = -1f;
        static bool _slotStarted;
        static float _slotWait;

        static float _autoTimer;
        static int _autoTries;
        static float _shotTimer;
        static float _statsTimer;
        static float _testTimer = -1f;
        static int _testStep;

        static readonly Action<PlayerControl, Vector2Int, GodPower, bool> ClickedFinal =
            AccessTools.MethodDelegate<Action<PlayerControl, Vector2Int, GodPower, bool>>(
                AccessTools.Method(typeof(PlayerControl), "clickedFinal"));

        public static void ParseArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (args[i])
                {
                    case "-wbmp": _autoRole = _roleArg = next; break;
                    case "-wbmpshot": _shotPath = next; break;
                    case "-wbmptest": _powerTest = true; break;
                    case "-wbmpslot": _slot = int.Parse(next); break;
                    case "-wbmpname": TestHooks.Name = next; break;
                    case "-wbmppass": Lobby.Password = next ?? ""; Net.JoinPassword = next ?? ""; break;
                    case "-wbmprules": RulesTest.On = true; break;
                    case "-wbmplobby": LobbyTest.On = true; TestHooks.NoSaveWindow = true; break;
                    case "-wbmpanim": AnimTest.On = true; break;
                    case "-wbmpui": _uiFolder = next; break;
                    case "-wbmpfake": BrowserWindow.FakeLobbies = int.Parse(next); break;
                    case "-wbmpnofix": Replacements.Disabled = true; break;
                    case "-wbmplag": TestHooks.LagMs = int.Parse(next); break;
                    case "-wbmprate": TestHooks.RateKiBps = int.Parse(next); break;
                    case "-wbmpproto": TestHooks.Protocol = int.Parse(next); break;
                }
            }
            TestHooks.Update = Update;
            TestHooks.ClientDropped = OnClientDropped;
            TestHooks.WorldLoaded = OnClientWorldLoaded;
            if (_autoRole != null || _shotPath != null || _powerTest)
                Debug.Log("[WorldBoxMP] test harness: role=" + _autoRole + " name=" + TestHooks.Name + " shot=" + _shotPath + " powertest=" + _powerTest);
        }

        public static void OnClientDropped()
        {
            Debug.Log("[WorldBoxMP] test: host dropped, role " + Net.Role + ", paused " + Config.paused + ", controlling " + ControllableUnit.isControllingUnit());
        }

        public static void OnClientWorldLoaded()
        {
            if (_powerTest && _testStep == 0) _testTimer = 4f;
            AnimTest.OnWorldLoaded();
        }

        static void Shot(string name)
        {
            var console = AccessTools.FieldRefAccess<MapBox, WorldBoxConsole.Console>("console")(World.world);
            if (console != null && console.isActive()) console.Hide();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(UiFolder, name));
        }

        static void UiCheck(string what, bool ok, string detail = "") =>
            Debug.Log("[WorldBoxMP] test: ui " + (ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? " (" + detail + ")" : ""));

        static float _top0;

        static void UiTest(float dt)
        {
            if (_uiFolder == null || MPTab.Tab == null || SmoothLoader.isLoading() || World.world == null) return;
            if (_uiClock < 0f) _uiClock = 0f;
            float before = _uiClock;
            _uiClock += dt;
            bool At(float t) => before < t && _uiClock >= t;
            if (At(6f))
            {
                ScrollWindow.hideAllEvent(false);
                MPTab.Tab.tryToShowTab();
                MPWindow.Open();
            }
            if (At(8f)) Shot(Net.Role == Role.Host ? "ui_menu_host.png" : Net.Role == Role.Client ? "ui_menu_player.png" : "ui_menu.png");
            if (_roleArg == null) { UiAlone(At); return; }
            if (At(9f)) ScrollWindow.hideAllEvent(false);
            if (At(10f))
            {
                Chat.Send("chat test from " + PlayerNames.LocalName);
                Vector3 c = Camera.main.transform.position;
                MapPings.Place(new Vector2(c.x + (Net.Role == Role.Host ? -4f : 4f), c.y));
            }
            if (At(11.5f)) Shot(Net.Role == Role.Host ? "ui_chat_host.png" : "ui_chat_player.png");
            if (At(16f)) LobbyWindow.Open();
            if (At(19f)) Shot(Net.Role == Role.Host ? "ui_lobby_host.png" : "ui_lobby_player.png");
            if (At(21f)) ScrollWindow.hideAllEvent(false);
        }

        static string _roleArg;

        static void UiAlone(Func<float, bool> At)
        {
            if (At(10f)) BrowserWindow.Open();
            if (At(12f))
            {
                Shot("ui_browser_top.png");
                bool ok = BrowserWindow.TestLayout(out float fields, out float list, out int rows);
                UiCheck("browser: the fields are above the list", ok && (rows == 0 || list <= fields + 0.5f), "fields bottom " + fields.ToString("0") + ", list top " + list.ToString("0"));
                if (BrowserWindow.FakeLobbies > 0)
                    UiCheck("browser: every lobby has a row", rows == Mathf.Min(30, BrowserWindow.FakeLobbies), rows.ToString());
            }
            if (At(13f)) BrowserWindow.TestScroll(0f);
            if (At(14.5f)) Shot("ui_browser_bottom.png");
            if (At(16f))
            {
                ScrollWindow.hideAllEvent(false);
                MPWindow.CreateLobby();
            }
            if (At(18f))
            {
                UiCheck("create: settings first, the lobby is not open yet", Net.Role == Role.None);
                _top0 = LobbyWindow.TestTop();
            }
            float[] clicks = { 18.3f, 18.6f, 18.9f, 19.2f, 19.5f, 19.8f };
            int[] which = { 0, 0, 2, 2, 1, 1 };
            for (int i = 0; i < clicks.Length; i++) if (At(clicks[i])) LobbyWindow.TestClickVisibility(which[i]);
            if (At(20.5f))
            {
                float top = LobbyWindow.TestTop();
                UiCheck("setup: the window does not move when visibility is clicked", Mathf.Abs(top - _top0) < 1f, _top0.ToString("0.0") + " -> " + top.ToString("0.0"));
                Shot("ui_setup.png");
                LobbyWindow.TestClickVisibility(2);
            }
            if (At(22f)) LobbyWindow.TestOpen();
            if (At(28f))
            {
                UiCheck("open: the lobby is open, invite-only", Net.Role == Role.Host && Lobby.Visibility == 2 && (!SteamSession.Available || SteamSession.LobbyOpen),
                    Net.Role + ", visibility " + Lobby.Visibility + ", code " + SteamSession.InviteCode);
                _top0 = LobbyWindow.TestTop();
                LobbyWindow.TestClickVisibility(1);
            }
            if (At(28.4f)) LobbyWindow.TestClickVisibility(1);
            if (At(28.8f)) LobbyWindow.TestClickVisibility(0);
            if (At(29.5f))
            {
                float top = LobbyWindow.TestTop();
                UiCheck("hosting: the window does not move when visibility is clicked", Mathf.Abs(top - _top0) < 1f, _top0.ToString("0.0") + " -> " + top.ToString("0.0"));
                Shot("ui_lobby_open.png");
            }
            if (At(31f))
            {
                MPMod.Instance.ExitLobby();
                UiCheck("exit: the lobby is closed", Net.Role == Role.None);
                Debug.Log("[WorldBoxMP] test: ui done");
            }
        }

        public static void Update(MPMod mod)
        {
            float dt = Time.unscaledDeltaTime;
            UiTest(dt);
            RulesTest.Update(dt);
            LobbyTest.Update(dt);
            AnimTest.Update(dt);

            if (_slot > 0 && _autoRole == "host")
            {
                if (!_slotStarted)
                {
                    if (SmoothLoader.isLoading()) return;
                    _slotStarted = true;
                    _slotWait = 5f;
                    Debug.Log("[WorldBoxMP] test: loading save slot " + _slot);
                    SaveManager.setCurrentSlot(_slot);
                    AccessTools.FieldRefAccess<MapBox, SaveManager>("save_manager")(World.world).startLoadSlot();
                    return;
                }
                if (_slotWait > 0f) { _slotWait -= dt; return; }
                if (SmoothLoader.isLoading()) return;
            }

            if (_autoRole != null && Net.Role == Role.None && !SmoothLoader.isLoading())
            {
                _autoTimer -= dt;
                if (_autoTimer <= 0f && _autoTries < 40)
                {
                    _autoTimer = 3f;
                    _autoTries++;
                    if (_autoRole == "host") { mod.Host(); _autoRole = null; }
                    else if (_autoRole == "client" && mod.Join()) _autoRole = null;
                }
            }

            if (_shotPath != null)
            {
                _shotTimer -= dt;
                if (_shotTimer <= 0f)
                {
                    _shotTimer = 3f;
                    var console = AccessTools.FieldRefAccess<MapBox, WorldBoxConsole.Console>("console")(World.world);
                    if (console != null && console.isActive()) console.Hide();
                    ScreenCapture.CaptureScreenshot(_shotPath);
                }
            }

            if (Net.Role != Role.None && !SmoothLoader.isLoading())
            {
                _statsTimer -= dt;
                if (_statsTimer <= 0f) { _statsTimer = 5f; LogStats(); }
            }

            if (_testTimer > 0f && Net.IsClientConnected && !SmoothLoader.isLoading())
            {
                _testTimer -= dt;
                if (_testTimer <= 0f) RunPowerTestStep();
            }
        }

        static void RunPowerTestStep()
        {
            var center = new Vector2Int(MapBox.width / 2, MapBox.height / 2);
            PlayerControl pc = World.world.player_control;
            switch (_testStep++)
            {
                case 0:
                    for (int i = 0; i < 12; i++)
                        ClickedFinal(pc, center + new Vector2Int(i % 6 * 3, i / 6 * 3), AssetManager.powers.get("human"), false);
                    Debug.Log("[WorldBoxMP] test: sent 12x human at " + center);
                    _testTimer = 3f;
                    break;
                case 1:
                    for (int i = 0; i < 12; i++)
                        ClickedFinal(pc, center + new Vector2Int(-20 + i, -15), AssetManager.powers.get("tile_mountains"), false);
                    Debug.Log("[WorldBoxMP] test: painted mountains at " + (center + new Vector2Int(-20, -15)));
                    _testTimer = 3f;
                    break;
                case 2:
                    ClickedFinal(pc, center + new Vector2Int(0, 25), AssetManager.powers.get("meteorite"), false);
                    Debug.Log("[WorldBoxMP] test: sent meteorite at " + (center + new Vector2Int(0, 25)));
                    Debug.Log("[WorldBoxMP] test: done");
                    break;
            }
        }

        public static void LogStats()
        {
            if (_autoRole == null && _shotPath == null && !_powerTest && Net.Role == Role.None) return;
            MapBox w = World.world;
            int buildings = 0;
            foreach (Building b in w.buildings)
                if (Access.BuildingData(b).state != BuildingState.Removed) buildings++;
            long terrain = 0;
            foreach (WorldTile t in Access.TilesList(w))
                terrain = terrain * 31 + t.main_type.id.GetHashCode() + (t.top_type != null ? t.top_type.id.GetHashCode() : 0);
            Debug.Log("[WorldBoxMP] stats " + Net.Role + ": units=" + w.units.Count + " buildings=" + buildings +
                      " terrain=" + terrain.ToString("x") + " kingdoms=" + w.kingdoms.Count + " cities=" + w.cities.Count + " meta_errors=" + MetaSync.Failures +
                      " stream_errors=" + ClientSync.Failures + " checks_ok=" + Checksum.Matches + " desyncs=" + Checksum.Mismatches);
        }
    }
}
