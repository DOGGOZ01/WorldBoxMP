using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class TestHarness
    {
        static string _autoRole;
        static string _shotPath;
        static bool _powerTest;
        static bool _camera;
        static string _speed;
        static int _slot;
        static bool _openTab;
        static bool _possessTest;
        static bool _fightTest;
        static bool _fxTest;
        static int _afterDropSlot;
        static string _eraTest;
        static bool _boxTest;
        static bool _specialTest;
        static float _dayTest = -1f, _dayClock = -1f;
        static bool _windowTest;
        static bool _dumpSprites;
        static string _uiTest, _uiFolder;
        public static string UiFolder => _uiFolder ?? ".";
        static float _uiClock = -1f;
        static int _uiIndex;
        static string _uiOriginal;
        static float _windowClock = -1f;
        static long _windowKing = -1;
        static Kingdom _windowKingdom;
        public static int WindowRefreshes;
        static float _specialClock = -1f;
        static float _boxClock = -1f;
        static float _eraClock = -1f;
        static float _afterDropClock = -1f;
        static double _afterDropWorldTime;
        public static bool CursorTest;
        public static int LagMs;
        public static int RateKiBps;
        static float _possessClock = -1f;
        static float _tabDelay = 3f;
        static bool _slotStarted;
        static float _slotWait;
        static bool _cameraDone;

        static float _autoTimer;
        static int _autoTries;
        static float _shotTimer;
        static float _statsTimer;
        static float[] _meteorShots;
        static float _meteorClock;
        static int _meteorShot;
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
                    case "-wbmp": _autoRole = next; break;
                    case "-wbmpshot": _shotPath = next; break;
                    case "-wbmptest": _powerTest = true; break;
                    case "-wbmpcam": _camera = true; break;
                    case "-wbmpspeed": _speed = next; break;
                    case "-wbmpslot": _slot = int.Parse(next); break;
                    case "-wbmptab": _openTab = true; break;
                    case "-wbmppossess": _possessTest = true; break;
                    case "-wbmpfight": _fightTest = true; break;
                    case "-wbmpfx": _fxTest = true; break;
                    case "-wbmpafterdrop": _afterDropSlot = int.Parse(next); break;
                    case "-wbmpera": _eraTest = next; break;
                    case "-wbmpbox": _boxTest = true; break;
                    case "-wbmpspecial": _specialTest = true; break;
                    case "-wbmprules": RulesTest.On = true; break;
                    case "-wbmprules2": RulesTest2.On = true; break;
                    case "-wbmpday": _dayTest = float.Parse(next, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "-wbmpwindow": _windowTest = true; break;
                    case "-wbmpsprites": _dumpSprites = true; break;
                    case "-wbmpui": _uiTest = next; break;
                    case "-wbmpuidir": _uiFolder = next; break;
                    case "-wbmpnofix": Replacements.Disabled = true; break;
                    case "-wbmpcursor": CursorTest = true; break;
                    case "-wbmplag": LagMs = int.Parse(next); break;
                    case "-wbmprate": RateKiBps = int.Parse(next); break;
                }
            }
            if (_autoRole != null || _shotPath != null || _powerTest)
                Debug.Log("[WorldBoxMP] test harness: role=" + _autoRole + " shot=" + _shotPath + " powertest=" + _powerTest);
        }

        public static void OnClientDropped()
        {
            if (_afterDropSlot > 0) _afterDropClock = 0f;
            Debug.Log("[WorldBoxMP] test: host dropped, role " + Net.Role + ", paused " + Config.paused + ", speed " + Config.time_scale_asset?.id + ", controlling " + ControllableUnit.isControllingUnit());
        }

        static void AfterDrop(float dt)
        {
            if (_afterDropClock < 0f) return;
            float before = _afterDropClock;
            _afterDropClock += dt;
            if (before < 3f && _afterDropClock >= 3f)
            {
                Debug.Log("[WorldBoxMP] test: loading save slot " + _afterDropSlot + " after the host dropped");
                SaveManager.setCurrentSlot(_afterDropSlot);
                AccessTools.FieldRefAccess<MapBox, SaveManager>("save_manager")(World.world).startLoadSlot();
            }
            if (before < 15f && _afterDropClock >= 15f) _afterDropWorldTime = World.world.getCurWorldTime();
            if (before < 25f && _afterDropClock >= 25f)
            {
                double now = World.world.getCurWorldTime();
                Debug.Log("[WorldBoxMP] test: after reload units=" + World.world.units.Count + " kingdoms=" + World.world.kingdoms.Count +
                          " world time advanced " + (now - _afterDropWorldTime).ToString("0.0") + " in 10 s, paused " + Config.paused +
                          ", loading " + SmoothLoader.isLoading() + ", role " + Net.Role + ", rules " + Rules.Client +
                          ", greyed buttons " + (Rules.DescribeUi().Split('|').Length - (Rules.DescribeUi().Contains(" off ") ? 0 : 0)) + " off " + (Rules.DescribeUi().Length - Rules.DescribeUi().Replace(" off ", "").Length) / 5);
                _afterDropClock = -1f;
            }
        }

        static void BoxTest(float dt)
        {
            if (_boxClock < 0f || !Net.IsClientConnected) return;
            float before = _boxClock;
            _boxClock += dt;
            if (before >= 15f || _boxClock < 15f) return;
            _boxClock = -1f;
            Actor best = null;
            int bestCount = 0;
            foreach (Actor a in World.world.units)
            {
                if (!a.isAlive() || !a.asset.can_be_inspected) continue;
                int near = 0;
                foreach (Actor b in World.world.units)
                    if (b.isAlive() && Mathf.Abs(b.current_position.x - a.current_position.x) <= 5f && Mathf.Abs(b.current_position.y - a.current_position.y) <= 5f) near++;
                if (near > bestCount) { best = a; bestCount = near; }
            }
            if (best == null) { Debug.Log("[WorldBoxMP] test: box selection: no units"); return; }
            PlayerControl pc = World.world.player_control;
            Vector2 p = best.current_position;
            pc.square_selection_position_current = new Vector2(p.x - 5f, p.y - 5f);
            Orders.TestBoxEnd = new Vector2(p.x + 5f, p.y + 5f);
            SelectedUnit.clear();
            AccessTools.Method(typeof(PlayerControl), "checkSelectedUnits").Invoke(pc, null);
            Orders.TestBoxEnd = null;
            pc.square_selection_position_current = new Vector2(-1f, -1f);
            var units = new System.Collections.Generic.List<Actor>(SelectedUnit.getAllSelected());
            int inside = 0, hidden = 0, dead = 0;
            foreach (Actor b in World.world.units)
            {
                if (Mathf.Abs(b.current_position.x - p.x) > 5f || Mathf.Abs(b.current_position.y - p.y) > 5f) continue;
                if (!b.isAlive()) dead++; else if (b.isInsideSomething()) inside++; else if (!b.asset.can_be_inspected) hidden++;
            }
            Debug.Log("[WorldBoxMP] test: box skipped inside=" + inside + " not_inspectable=" + hidden + " dead=" + dead);
            Debug.Log("[WorldBoxMP] test: box selection around " + (int)p.x + ":" + (int)p.y + " selected " + units.Count + " of " + bestCount + " units in the box");
            SessionLog.Write("test: box selected " + units.Count + " of " + bestCount);
            if (units.Count == 0) return;
            WorldTile tile = World.world.GetTile(Mathf.Clamp((int)p.x + 10, 0, MapBox.width - 1), Mathf.Clamp((int)p.y, 0, MapBox.height - 1));
            if (tile != null) Orders.Send(tile, false, null, units);
        }

        static void SpecialTest(float dt)
        {
            if (!_specialTest || !(Net.IsClientConnected || Net.IsHostWithPeer) || World.world == null || SmoothLoader.isLoading()) return;
            if (_specialClock < 0f) _specialClock = 0f;
            float before = _specialClock;
            _specialClock += dt;
            if (Net.IsClientConnected && before < 15f && _specialClock >= 15f)
            {
                var c = new Vector2Int(MapBox.width / 2, MapBox.height / 2);
                PlayerControl pc = World.world.player_control;
                int i = 0;
                foreach (string power in new[] { "god_finger", "UFO", "dragon" })
                {
                    GodPower p = AssetManager.powers.get(power);
                    if (p == null) { Debug.Log("[WorldBoxMP] test: no power " + power); continue; }
                    ClickedFinal(pc, c + new Vector2Int(-10 + i * 10, 5), p, false);
                    i++;
                }
                Debug.Log("[WorldBoxMP] test: sent god_finger, UFO, dragon");
            }
            if (_specialClock > 20f)
                foreach (Actor a in World.world.units)
                    if (a.asset.id == "god_finger" && a.isAlive())
                    {
                        Vector3 cam = UnityEngine.Camera.main.transform.position;
                        UnityEngine.Camera.main.transform.position = new Vector3(a.current_position.x, a.current_position.y, cam.z);
                        break;
                    }
            foreach (float at in new[] { 25f, 40f })
            {
                if (before >= at || _specialClock < at) continue;
                var units = new System.Collections.Generic.Dictionary<string, int>();
                foreach (Actor a in World.world.units)
                {
                    if (a.asset.id != "god_finger" && a.asset.id != "UFO" && a.asset.id != "dragon") continue;
                    units.TryGetValue(a.asset.id, out int n);
                    units[a.asset.id] = n + 1;
                }
                string u = "";
                foreach (var kv in units) u += kv.Key + "=" + kv.Value + " ";
                Debug.Log("[WorldBoxMP] test: special " + Net.Role + " at " + at + "s units: " + u +
                          "| drawn GodFinger=" + UnityEngine.Object.FindObjectsOfType<GodFinger>().Length +
                          " UFO=" + UnityEngine.Object.FindObjectsOfType<UFO>().Length +
                          " Dragon=" + UnityEngine.Object.FindObjectsOfType<Dragon>().Length);
                foreach (SpriteRenderer sr in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
                {
                    if (sr.sprite == null || !sr.enabled) continue;
                    string nm = sr.sprite.name.ToLowerInvariant();
                    if (!nm.Contains("finger") && !sr.gameObject.name.StartsWith("GF") && (sr.transform.parent == null || !sr.transform.parent.name.StartsWith("GF"))) continue;
                    Debug.Log("[WorldBoxMP] test: finger sprite " + sr.sprite.name + " on " + sr.gameObject.name + " parent " + (sr.transform.parent != null ? sr.transform.parent.name : "-") +
                              " at " + sr.transform.position + " active " + sr.gameObject.activeInHierarchy);
                }
                foreach (Actor a in World.world.units)
                    if (a.asset.id == "god_finger")
                        Debug.Log("[WorldBoxMP] test: finger unit " + a.getID() + " pos " + a.current_position + " height " + a.position_height + " avatar " + (a.avatar != null ? a.avatar.name + " at " + a.avatar.transform.position : "-"));
            }
        }

        static void WindowTest(float dt)
        {
            if (!_windowTest || !Net.IsClientConnected || SmoothLoader.isLoading()) return;
            if (_windowClock < 0f) _windowClock = 0f;
            float before = _windowClock;
            _windowClock += dt;
            bool At(float t) => before < t && _windowClock >= t;
            if (At(20f) || At(50f))
            {
                _windowKingdom = null;
                foreach (Kingdom k in World.world.kingdoms)
                    if (k.king != null && k.king.isAlive() && k.isCiv()) { _windowKingdom = k; break; }
                if (_windowKingdom == null) { Debug.Log("[WorldBoxMP] test: window: no kingdom with a king"); _windowTest = false; return; }
                _windowKing = _windowKingdom.king.getID();
                WindowRefreshes = 0;
                if (_windowClock < 40f) ActionLibrary.openUnitWindow(_windowKingdom.king);
                else { SelectedMetas.selected_kingdom = _windowKingdom; ScrollWindow.showWindow("kingdom"); }
                Debug.Log("[WorldBoxMP] test: window: opened " + (_windowClock < 40f ? "unit" : "kingdom") + " window, king " + _windowKing);
            }
            foreach (float t in new[] { 22f, 24f, 26f, 28f, 30f, 52f, 54f, 56f, 58f, 60f })
            {
                if (!At(t) || _windowKing < 0) continue;
                var ms = new System.IO.MemoryStream();
                var w = new System.IO.BinaryWriter(ms);
                w.Write(ObjectRepair.Units);
                w.Write(1);
                w.Write(_windowKing);
                Net.Peer?.Send(Msg.ObjRepair, ms.ToArray());
            }
            if (At(33f) || At(63f))
            {
                Actor now = World.world.units.get(_windowKing);
                Debug.Log("[WorldBoxMP] test: window: after 5 reloads refreshes=" + WindowRefreshes + " window_active=" + ScrollWindow.isWindowActive() +
                          " animations=" + Replacements.WindowAnimationCount + " selected_current=" + (SelectedUnit.unit != null && SelectedUnit.unit == now) +
                          " king_current=" + (_windowKingdom != null && _windowKingdom.king == now) + " fixed=" + Replacements.Fixed);
                WindowRefreshes = 0;
                if (_windowClock < 40f && now != null)
                {
                    string trait = now.hasTrait("strong") ? "weak" : "strong";
                    now.addTrait(trait);
                    Debug.Log("[WorldBoxMP] test: window: edited unit, added trait " + trait);
                }
                else if (_windowKingdom != null)
                {
                    ColorAsset c = AssetManager.kingdom_colors_library.list[(_windowKingdom.data.color_id + 1) % AssetManager.kingdom_colors_library.list.Count];
                    _windowKingdom.data.color_id = AssetManager.kingdom_colors_library.list.IndexOf(c);
                    Debug.Log("[WorldBoxMP] test: window: edited kingdom colour to " + _windowKingdom.data.color_id);
                }
            }
            if (At(45f) || At(65f))
            {
                Debug.Log("[WorldBoxMP] test: window: 12 s after the edit refreshes=" + WindowRefreshes + " window_active=" + ScrollWindow.isWindowActive() +
                          " animations=" + Replacements.WindowAnimationCount + " edits_sent=" + PlayerEdits.Sent);
                WindowRefreshes = 0;
                ScrollWindow.hideAllEvent(true);
            }
            if (At(48f) || At(68f))
                Debug.Log("[WorldBoxMP] test: window: closed, refreshes since=" + WindowRefreshes + " window_active=" + ScrollWindow.isWindowActive() +
                          " animation_active=" + ScrollWindow.isAnimationActive() + " stuck_dropped=" + Replacements.StuckAnimations);
        }

        [HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.showSameWindow))]
        static class CountWindowRefreshes { static void Prefix() => WindowRefreshes++; }

        static void UiTest(float dt)
        {
            if (_uiTest == null || MPTab.Tab == null || SmoothLoader.isLoading() || World.world == null) return;
            if (_uiClock < 0f) _uiClock = 0f;
            float before = _uiClock;
            _uiClock += dt;
            var console = HarmonyLib.AccessTools.FieldRefAccess<MapBox, WorldBoxConsole.Console>("console")(World.world);
            if (console != null && console.isActive()) console.Hide();
            if (before < 6f && _uiClock >= 6f)
            {
                ScrollWindow.hideAllEvent(false);
                if (_uiTest != "all") LocalizedTextManager.instance.setLanguage(_uiTest);
                MPTab.Tab.tryToShowTab();
                try { MPWindow.Open(); }
                catch (System.Exception e) { Debug.LogError("[WorldBoxMP] test: window failed: " + e); }
                Debug.Log("[WorldBoxMP] test: ui opened in " + LocalizedTextManager.current_language?.id + ", window active " + ScrollWindow.isWindowActive() +
                          ", current " + (ScrollWindow.isWindowActive() ? ScrollWindow.getCurrentWindow().screen_id : "-"));
            }
            if (before < 8f && _uiClock >= 8f)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_uiFolder ?? ".", "ui_open.png"));
            if (before < 20f && _uiClock >= 20f)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_uiFolder ?? ".", "ui_open2.png"));
            if (before < 30f && _uiClock >= 30f && Net.Role == Role.Host && (RulesTest.On || RulesTest2.On)) RulesWindow.Open();
            if (before < 42f && _uiClock >= 42f && Net.Role == Role.Host && RulesTest2.On) PowerRulesWindow.Open(Perm.Creatures);
            if (before < 48f && _uiClock >= 48f)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_uiFolder ?? ".", "ui_open4.png"));
            if (before < 38f && _uiClock >= 38f)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_uiFolder ?? ".", "ui_open3.png"));
            if (_uiTest != "all" || _uiClock < 22f) return;
            const float step = 2.5f;
            float t = _uiClock - 22f, tb = before - 22f;
            var langs = new System.Collections.Generic.List<string>();
            foreach (GameLanguageAsset l in AssetManager.game_language_library.list) if (l.export) langs.Add(l.id);
            bool switchNow = tb < 0f || Mathf.Floor(t / step) != Mathf.Floor(tb / step);
            bool shotNow = tb >= 0f && (tb % step) < 2f && (t % step) >= 2f;
            if (shotNow && _uiIndex > 0)
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_uiFolder ?? ".", "ui_" + langs[_uiIndex - 1] + ".png"));
            if (!switchNow) return;
            if (_uiIndex == 0) _uiOriginal = LocalizedTextManager.current_language?.id;
            if (_uiIndex >= langs.Count)
            {
                if (_uiOriginal != null) LocalizedTextManager.instance.setLanguage(_uiOriginal);
                Debug.Log("[WorldBoxMP] test: ui languages done, back to " + _uiOriginal);
                _uiTest = "done";
                return;
            }
            LocalizedTextManager.instance.setLanguage(langs[_uiIndex]);
            _uiIndex++;
        }

        static void DayTest(float dt)
        {
            if (_dayTest < 0f || !(Net.IsHostWithPeer || Net.IsClientConnected) || SmoothLoader.isLoading()) return;
            if (_dayClock < 0f) _dayClock = 0f;
            float before = _dayClock;
            _dayClock += dt;
            if (Net.Role == Role.Host && before < 10f && _dayClock >= 10f)
            {
                WorldfallBridge.SetDayClock(7 + _dayTest);
                Debug.Log("[WorldBoxMP] test: host Worldfall clock set to " + WorldfallBridge.DayClock.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (before < 30f && _dayClock >= 30f)
                Debug.Log("[WorldBoxMP] test: " + Net.Role + " Worldfall clock " + WorldfallBridge.DayClock.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
        }

        public static void OnClientWorldLoaded()
        {
            if (_powerTest && _testStep == 0) _testTimer = 4f;
            if (_boxTest) _boxClock = 0f;
            _cameraDone = false;
        }

        public static void Update(MPMod mod)
        {
            float dt = Time.unscaledDeltaTime;
            if (_dumpSprites && World.world != null && !SmoothLoader.isLoading() && MPTab.Tab != null)
            {
                foreach (PowersTab tab in Resources.FindObjectsOfTypeAll<PowersTab>())
                {
                    if (!tab.gameObject.scene.IsValid()) continue;
                    PowerTabAsset asset = HarmonyLib.AccessTools.FieldRefAccess<PowersTab, PowerTabAsset>("_asset")(tab);
                    var ids = new System.Collections.Generic.List<string>();
                    foreach (PowerButton b in tab.GetComponentsInChildren<PowerButton>(true))
                    {
                        GodPower gp = HarmonyLib.AccessTools.FieldRefAccess<PowerButton, GodPower>("godPower")(b);
                        ids.Add(gp != null ? gp.id + "/" + gp.type : "(" + b.name + ")");
                    }
                    Debug.Log("[WorldBoxMP] tab " + tab.name + " asset " + asset?.id + " main " + asset?.tab_type_main + " powerButton " + (tab.powerButton != null ? tab.powerButton.name + "@" + tab.powerButton.transform.parent?.name : "-") + ": " + string.Join(" ", ids.ToArray()));
                }
                foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
                {
                    if (!b.gameObject.scene.IsValid()) continue;
                    for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                        if (b.onClick.GetPersistentTarget(i) is PowersTab pt)
                            Debug.Log("[WorldBoxMP] tab button " + b.name + "@" + b.transform.parent?.name + " -> " + pt.name + "." + b.onClick.GetPersistentMethodName(i));
                }
            }
            if (_dumpSprites && World.world != null && !SmoothLoader.isLoading() && MPTab.Tab != null)
            {
                _dumpSprites = false;
                foreach (string folder in new[] { "ui/special", "ui/buttons", "ui", "ui/Icons" })
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (Sprite sp in Resources.LoadAll<Sprite>(folder)) names.Add(sp.name + " " + (int)sp.rect.width + "x" + (int)sp.rect.height + (sp.border != Vector4.zero ? " b" + sp.border : ""));
                    names.Sort();
                    Debug.Log("[WorldBoxMP] sprites " + folder + " (" + names.Count + "): " + string.Join(" | ", names.ToArray()));
                }
            }
            AfterDrop(dt);
            UiTest(dt);
            BoxTest(dt);
            SpecialTest(dt);
            RulesTest.Update(dt);
            RulesTest2.Update(dt);
            DayTest(dt);
            WindowTest(dt);
            if (_eraTest != null && Net.IsHostWithPeer)
            {
                if (_eraClock < 0f) _eraClock = 0f;
                float before = _eraClock;
                _eraClock += dt;
                if (before < 20f && _eraClock >= 20f)
                {
                    WorldAgeAsset age = AssetManager.era_library.get(_eraTest);
                    var m = AccessTools.FieldRefAccess<MapBox, WorldAgeManager>("era_manager")(World.world);
                    if (age != null && m != null)
                    {
                        m.setAgeToSlot(age, m.getCurrentSlotIndex());
                        Debug.Log("[WorldBoxMP] test: host switched the world age to " + _eraTest);
                    }
                }
            }

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

            if (_camera && !_cameraDone && (Net.IsHostWithPeer || Net.IsClientConnected) && !SmoothLoader.isLoading())
            {
                _cameraDone = true;
                ScrollWindow.hideAllEvent(false);
                if (_speed != null && Net.Role == Role.Host) Config.setWorldSpeed(_speed);
                Vector3 target = new Vector3(MapBox.width / 2f, MapBox.height / 2f - 5f, -10f);
                City best = null;
                foreach (City c in World.world.cities)
                    if (best == null || c.countUnits() > best.countUnits()) best = c;
                if (best != null && best.getTile() != null)
                    target = new Vector3(best.getTile().x, best.getTile().y, -10f);
                UnityEngine.Camera.main.transform.position = target;
                Access.Camera.forceZoom(25f);
            }

            if (_openTab && !SmoothLoader.isLoading())
            {
                _tabDelay -= dt;
                if (_tabDelay <= 0f)
                {
                    _openTab = false;
                    ScrollWindow.hideAllEvent(false);
                    var entry = GameObject.Find("Button_wbmp_tab");
                    if (entry != null) entry.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    else Debug.Log("[WorldBoxMP] test: tab button not found");
                }
            }

            if (_meteorShots != null && _shotPath != null)
            {
                _meteorClock += dt;
                if (_meteorShot < _meteorShots.Length && _meteorClock >= _meteorShots[_meteorShot])
                    ScreenCapture.CaptureScreenshot(_shotPath.Replace(".png", "_meteor" + _meteorShot++ + ".png"));
                if (_meteorShot >= _meteorShots.Length) _meteorShots = null;
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

            if (_possessClock >= 0f && Net.IsClientConnected && !SmoothLoader.isLoading())
            {
                float before = _possessClock;
                _possessClock += dt;
                if (before == 0f)
                {
                    Actor pick = null;
                    var c = new Vector2(MapBox.width / 2f, MapBox.height / 2f);
                    foreach (Actor a in World.world.units)
                        if (a.isAlive() && a.canBePossessed() && (pick == null || (a.current_position - c).sqrMagnitude < (pick.current_position - c).sqrMagnitude))
                            pick = a;
                    if (pick != null)
                    {
                        ControllableUnit.setControllableCreature(pick);
                        Debug.Log("[WorldBoxMP] test: possessed " + pick.getID() + " " + pick.asset.id + " at " + pick.current_position);
                    }
                    RemotePossession.TestMovement = new Vector2(1f, 0f);
                }
                else if (before < 4f && _possessClock >= 4f) RemotePossession.TestMovement = new Vector2(0f, 1f);
                else if (before < 8f && _possessClock >= 8f)
                {
                    RemotePossession.TestMovement = null;
                    Actor u = ControllableUnit.getControllableUnit();
                    Debug.Log("[WorldBoxMP] test: releasing, unit now at " + (u != null ? u.current_position.ToString() : "?"));
                    ControllableUnit.clear();
                    _possessClock = -1f;
                }
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
                    _meteorShots = new float[] { 0.6f, 1.2f, 2.0f };
                    _meteorClock = 0f;
                    _testTimer = 5f;
                    break;
                case 3:
                    if (_fightTest)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            ClickedFinal(pc, center + new Vector2Int(i * 2, 8), AssetManager.powers.get("orc"), false);
                            ClickedFinal(pc, center + new Vector2Int(i * 2, -6), AssetManager.powers.get("elf"), false);
                        }
                        Debug.Log("[WorldBoxMP] test: sent orcs and elves");
                    }
                    if (_fxTest)
                    {
                        foreach (string power in new[] { "rain", "curse", "blessing", "fire", "crabzilla", "dragon", "UFO" })
                        {
                            GodPower p = AssetManager.powers.get(power);
                            if (p == null) { Debug.Log("[WorldBoxMP] test: no power " + power); continue; }
                            bool single = power == "crabzilla" || power == "dragon" || power == "UFO";
                            int clicks = single ? 1 : 6;
                            int dy = power == "fire" ? 3 : power == "crabzilla" ? 12 : power == "dragon" ? -10 : power == "UFO" ? 6 : 0;
                            int dx = power == "dragon" ? -12 : power == "UFO" ? 10 : 0;
                            for (int i = 0; i < clicks; i++)
                                ClickedFinal(pc, center + new Vector2Int(i * 3 + dx + (power == "fire" ? 0 : 1), dy), p, false);
                        }
                        Debug.Log("[WorldBoxMP] test: sent rain, curse, blessing, fire");
                        _testTimer = 10f;
                        break;
                    }
                    _testStep = 6;
                    goto case 6;
                case 4:
                    City victim = null;
                    foreach (City c in World.world.cities)
                        if (Checksum.CityZoneCount(c) > 1) { victim = c; break; }
                    if (victim == null)
                    {
                        if (_corruptTries++ < 12) { _testStep = 4; _testTimer = 5f; Debug.Log("[WorldBoxMP] test: no city yet to corrupt"); break; }
                    }
                    else
                    {
                        Kingdom k = Access.CityKingdom(victim);
                        if (k != null) k.data.name = "CORRUPTED_KINGDOM";
                        victim.data.name = "CORRUPTED_CITY";
                        TileZone zone = CityZonesRef(victim)[0];
                        CityRemoveZone(victim, zone);
                        Debug.Log("[WorldBoxMP] test: corrupted client meta: kingdom " + (k != null ? k.getID() : -1) + ", city " + victim.getID() + " renamed, zone " + zone.x + ":" + zone.y + " removed");
                        Actor lost = null;
                        foreach (Actor a in World.world.units) if (HostSync.Syncable(a) && !ControllableUnit.isControllingUnit(a)) { lost = a; break; }
                        Building gone = null;
                        foreach (Building b in World.world.buildings) if (HostSync.Syncable(b)) { gone = b; break; }
                        WorldTile tile = World.world.GetTile(MapBox.width / 2 + 30, MapBox.height / 2 - 30);
                        if (lost != null) DestroyUnit(World.world.units, lost);
                        if (gone != null) RemoveBuilding(gone);
                        if (tile != null) { tile.setTileTypes(AssetManager.tiles.get("deep_ocean"), null); World.world.setTileDirty(tile); }
                        Debug.Log("[WorldBoxMP] test: corrupted client objects: unit " + (lost != null ? lost.getID() : -1) + " and building " + (gone != null ? gone.getID() : -1) + " removed, a tile turned to deep ocean");
                    }
                    _testTimer = 12f;
                    break;
                case 5:
                    if (_fxTest)
                    {
                        PlayerEdits.ForceInteractive = true;
                        City city = null;
                        foreach (City c in World.world.cities) { city = c; break; }
                        if (city != null)
                        {
                            city.data.custom_name = true;
                            city.setName("EDITED_CITY");
                            Kingdom k = Access.CityKingdom(city);
                            if (k != null)
                            {
                                k.setName("EDITED_KINGDOM");
                                MetaSync.RefreshColor(k, (k.data.color_id + 3) % 20);
                            }
                        }
                        Actor unit = null;
                        foreach (Actor a in World.world.units) if (a.isAlive() && a.canBePossessed()) { unit = a; break; }
                        if (unit != null)
                        {
                            SelectedUnit.makeMainSelected(unit);
                            unit.setName("EDITED_UNIT");
                        }
                        Debug.Log("[WorldBoxMP] test: edited city " + (city != null ? city.getID() : -1) + ", its kingdom and unit " + (unit != null ? unit.getID() : -1) + " on the client");
                        var squad = new System.Collections.Generic.List<Actor>();
                        var mid = new Vector2(MapBox.width / 2f, MapBox.height / 2f);
                        foreach (Actor a in World.world.units)
                            if (a.isAlive() && HostSync.Syncable(a) && a.asset.allow_strange_urge_movement && !SpecialActors.IsSpecial(a)) squad.Add(a);
                        squad.Sort((p, q) => (p.current_position - mid).sqrMagnitude.CompareTo((q.current_position - mid).sqrMagnitude));
                        if (squad.Count > 5) squad.RemoveRange(5, squad.Count - 5);
                        WorldTile goal = World.world.GetTile(MapBox.width / 2 + 15, MapBox.height / 2);
                        if (squad.Count > 0 && goal != null)
                        {
                            _orderSquad = squad;
                            _orderGoal = goal;
                            Orders.Send(goal, false, null, squad);
                            Debug.Log("[WorldBoxMP] test: ordered " + squad.Count + " units to " + goal.x + ":" + goal.y + ", first at " + squad[0].current_position);
                        }
                        Actor steered = ControllableUnit.getControllableUnit();
                        if (steered != null && SpecialActors.IsSpecial(steered))
                        {
                            RemotePossession.TestMovement = new Vector2(1f, 0f);
                            Debug.Log("[WorldBoxMP] test: steering own " + steered.asset.id + " " + steered.getID() + " from " + steered.current_position);
                        }
                        _testTimer = 8f;
                        break;
                    }
                    goto case 6;
                case 6:
                    PlayerEdits.ForceInteractive = false;
                    if (_orderSquad != null)
                    {
                        float sum = 0f;
                        int alive = 0;
                        foreach (Actor a in _orderSquad)
                            if (a != null && a.isAlive()) { sum += Vector2.Distance(a.current_position, new Vector2(_orderGoal.x, _orderGoal.y)); alive++; }
                        Debug.Log("[WorldBoxMP] test: ordered units now " + (alive > 0 ? (sum / alive).ToString("0.0") : "?") + " tiles from the goal on average");
                        _orderSquad = null;
                    }
                    if (RemotePossession.TestMovement.HasValue)
                    {
                        RemotePossession.TestMovement = null;
                        Actor steered = ControllableUnit.getControllableUnit();
                        Debug.Log("[WorldBoxMP] test: stopped steering, now at " + (steered != null ? steered.current_position.ToString() : "?"));
                    }
                    Debug.Log("[WorldBoxMP] test: done");
                    if (_possessTest) _possessClock = 0f;
                    break;
            }
        }

        static int _corruptTries;
        static System.Collections.Generic.List<Actor> _orderSquad;
        static WorldTile _orderGoal;
        static readonly Action<ActorManager, Actor> DestroyUnit =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));
        static readonly Action<Building> RemoveBuilding =
            AccessTools.MethodDelegate<Action<Building>>(AccessTools.Method(typeof(Building), "removeBuildingFinal"));
        static readonly AccessTools.FieldRef<City, System.Collections.Generic.List<TileZone>> CityZonesRef =
            AccessTools.FieldRefAccess<City, System.Collections.Generic.List<TileZone>>("zones");
        static readonly Action<City, TileZone> CityRemoveZone =
            AccessTools.MethodDelegate<Action<City, TileZone>>(AccessTools.Method(typeof(City), "removeZone"));

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
            int inLists = 0, visibleFlag = 0, checkedB = 0;
            var cam = UnityEngine.Camera.main.transform.position;
            foreach (Building b in w.buildings)
            {
                if (Access.BuildingData(b).state == BuildingState.Removed) continue;
                if (Mathf.Abs(b.current_tile.x - cam.x) > 20 || Mathf.Abs(b.current_tile.y - cam.y) > 12) continue;
                checkedB++;
                if (Access.BuildingVisible(b)) visibleFlag++;
                if (b.current_tile.zone.buildings_render_list.Contains(b)) inLists++;
                if (checkedB == 1)
                    Debug.Log("[WorldBoxMP] building sample " + Access.BuildingAsset(b).id + " scale=" + b.current_scale +
                              " zone_visible=" + b.current_tile.zone.visible + " zone_dirty=" + b.current_tile.zone.isDirty());
            }
            var sb = new System.Text.StringBuilder();
            foreach (Kingdom k in w.kingdoms) sb.Append(" " + k.name + ":" + System.Linq.Enumerable.Count(k.getCities()));
            Debug.Log("[WorldBoxMP] borders " + Net.Role + ": show=" + Zones.showMapBorders() + " mode=" +
                      (w.getCachedMapMetaAsset()?.id ?? "none") + " kingdoms[cities]=" + sb);
            Debug.Log("[WorldBoxMP] render " + Net.Role + ": near_camera=" + checkedB + " visible_flag=" + visibleFlag +
                      " in_render_lists=" + inLists + " drawn=" + w.buildings.countVisibleBuildings());
            Debug.Log("[WorldBoxMP] stats " + Net.Role + ": units=" + w.units.Count + " buildings=" + buildings +
                      " terrain=" + terrain.ToString("x") + " kingdoms=" + w.kingdoms.Count + " cities=" + w.cities.Count + " meta_errors=" + MetaSync.Failures +
                      " stream_errors=" + ClientSync.Failures + " checks_ok=" + Checksum.Matches + " desyncs=" + Checksum.Mismatches);
        }
    }
}
