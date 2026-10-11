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
        static bool _camera;
        static string _speed;
        static int _slot;
        static bool _openTab;
        static bool _possessTest;
        static bool _fightTest;
        public static int LagMs;
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
                    case "-wbmplag": LagMs = int.Parse(next); break;
                }
            }
            if (_autoRole != null || _shotPath != null || _powerTest)
                Debug.Log("[WorldBoxMP] test harness: role=" + _autoRole + " shot=" + _shotPath + " powertest=" + _powerTest);
        }

        public static void OnClientWorldLoaded()
        {
            if (_powerTest && _testStep == 0) _testTimer = 4f;
            _cameraDone = false;
        }

        public static void Update(MPMod mod)
        {
            float dt = Time.unscaledDeltaTime;

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
                    Debug.Log("[WorldBoxMP] test: done");
                    if (_possessTest) _possessClock = 0f;
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
