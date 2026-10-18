using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class AnimTest
    {
        public static bool On;
        static int _wheel;
        static readonly AccessTools.FieldRef<MapBox, ZoneCalculator> ZoneCalc = AccessTools.FieldRefAccess<MapBox, ZoneCalculator>("zone_calculator");
        static readonly AccessTools.FieldRef<City, List<TileZone>> CityZones = AccessTools.FieldRefAccess<City, List<TileZone>>("zones");
        static int _highlights;
        static PowerButtonSelector Selector() => Object.FindObjectOfType<PowerButtonSelector>();
        public static WorldTile TestMouseTile;
        static readonly AccessTools.FieldRef<PowerButton, GodPower> ButtonPower = AccessTools.FieldRefAccess<PowerButton, GodPower>("godPower");
        static readonly AccessTools.FieldRef<MapLayer, Color32[]> LayerPixels = AccessTools.FieldRefAccess<MapLayer, Color32[]>("pixels");

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.getMouseTilePos))]
        static class TestMouseOnTheMap
        {
            static void Postfix(ref WorldTile __result) { if (TestMouseTile != null) __result = TestMouseTile; }
        }

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.isOverUI))]
        static class TestMouseNotOverUi
        {
            static bool Prefix(ref bool __result)
            {
                if (TestMouseTile == null) return true;
                __result = false;
                return false;
            }
        }

        static bool BreakZones;

        [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.draw))]
        static class TestBrokenZones
        {
            static void Prefix() { if (BreakZones) throw new System.NullReferenceException("test: the borders layer throws"); }
        }

        [HarmonyPatch(typeof(PlayerControl), "highlightCursor")]
        static class CountHighlights
        {
            static void Postfix() { if (TestMouseTile != null) _highlights++; }
        }
        static readonly AccessTools.FieldRef<MapBox, PixelFlashEffects> FlashLayer = AccessTools.FieldRefAccess<MapBox, PixelFlashEffects>("flash_effects");
        static readonly AccessTools.FieldRef<Actor, Vector3> TargetAngle = AccessTools.FieldRefAccess<Actor, Vector3>("target_angle");
        static readonly AccessTools.FieldRef<MapBox, bool> PausedF = AccessTools.FieldRefAccess<MapBox, bool>("_is_paused");
        static readonly AccessTools.FieldRef<Actor, float> AttackTimer = AccessTools.FieldRefAccess<Actor, float>("attack_timer");
        static readonly AccessTools.FieldRef<Actor, bool> HasTarget = AccessTools.FieldRefAccess<Actor, bool>("has_attack_target");
        static float _clock = -1f;
        static readonly Dictionary<long, Sampled> _samples = new Dictionary<long, Sampled>();
        static Actor _own;
        static Vector2 _start;
        static int _ownFrames, _ownMoving;
        static int _facedLeft, _facedRight, _facingChecks;
        static float _animTimeStart;
        static int _effectsBefore;
        static double _swingBefore;

        class Sampled
        {
            public int Frames, Moving, Changes;
            public Sprite Last;
            public readonly HashSet<Sprite> Sprites = new HashSet<Sprite>();
        }

        static void Check(string what, bool ok, string detail = "") =>
            Debug.Log("[WorldBoxMP] test: anim " + (ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? " (" + detail + ")" : ""));

        public static void OnWorldLoaded()
        {
            if (On && _clock < 0f) _clock = 0f;
        }

        public static void Update(float dt)
        {
            if (!On) return;
            if (Net.Role == Role.Host) { HostLog(dt); return; }
            if (_clock < 0f || !Net.IsClientConnected || SmoothLoader.isLoading()) return;
            float before = _clock;
            _clock += dt;
            bool At(float t) => before < t && _clock >= t;
            bool In(float a, float b) => _clock >= a && _clock < b;

            if (At(8f)) StartSampling();
            if (In(8f, 11f)) Sample();
            if (At(11f)) Report("as is");

            if (At(12f)) PossessSix();
            if (At(13f))
            {
                StartSampling();
                TestHooks.Movement = new Vector2(1f, 0f);
                _start = _own != null ? _own.current_position : Vector2.zero;
            }
            if (In(13f, 16f)) Sample();
            if (At(16f))
            {
                TestHooks.Movement = Vector2.zero;
                Report("while holding a unit");
                Check("the held unit walks", _own != null && (_own.current_position - _start).magnitude > 1f,
                    _own != null ? "moved " + (_own.current_position - _start).magnitude.ToString("0.0") : "no unit");
                Check("the held unit animates while walking", _ownMoving > 20 && _ownFrames >= 2, "frames " + _ownFrames + " over " + _ownMoving + " moving samples");
            }
            if (At(17f) && _own != null)
            {
                TestHooks.Mouse = _own.current_position + new Vector2(-4f, 0f);
                TestHooks.Attack = true;
                _effectsBefore = EffectSync.SlashesShown;
                _swingBefore = Access.LastAttack(_own);
            }
            if (At(19f) && _own != null) TestHooks.Mouse = _own.current_position + new Vector2(4f, 0f);
            if (In(17.3f, 19f) || In(19.3f, 21f)) Facing();
            if (At(21f))
            {
                TestHooks.Attack = false;
                TestHooks.Mouse = null;
                Check("the held unit faces the mouse", _facingChecks > 0 && _facedLeft > _facingChecks / 4 && _facedRight > _facingChecks / 4,
                    "left " + _facedLeft + ", right " + _facedRight + " of " + _facingChecks);
                int effects = EffectSync.SlashesShown - _effectsBefore;
                Check("the hits show (slashes)", effects > 0, effects + " slashes shown");
                Check("the held unit swings on the player's screen", Access.LastAttack(_own) != _swingBefore, _swingBefore.ToString("0.0") + " -> " + Access.LastAttack(_own).ToString("0.0"));
                ControllableUnit.clear(pCallKill: false);
            }
            if (At(22f))
            {
                Check("the held unit stands up after its swings", _own == null || Mathf.Abs(TargetAngle(_own).z) < 1f, _own != null ? TargetAngle(_own).z.ToString("0") : "-");
                int tilted = 0, all = 0;
                foreach (Actor a in World.world.units)
                {
                    if (!a.isAlive() || a.isInsideSomething() || a.is_unconscious) continue;
                    all++;
                    if (Mathf.Abs(TargetAngle(a).z) >= 30f) tilted++;
                }
                Check("nobody keeps leaning after a swing", tilted <= all / 500 + 2, tilted + " of " + all + " leaning");
                MPWindow.Open();
            }
            if (At(22.5f)) StartSampling();
            if (In(22.5f, 25.5f)) Sample();
            if (At(25.5f))
            {
                Report("with a window open");
                ScrollWindow.hideAllEvent(false);
            }
            if (At(27f)) _wheel = 8;
            if (_wheel > 0 && _clock >= 27f) { _wheel--; Config.nextWorldSpeed(); }
            if (At(31f))
            {
                Check("the wheel takes the player to x20 and it holds", Config.time_scale_asset?.id == "x20", Config.time_scale_asset?.id + ", sent " + PlayerEdits.SpeedChanges);
                Config.setWorldSpeed(AssetManager.time_scales.get("x1"));
            }
            if (At(35f))
            {
                City city = null;
                foreach (City c in World.world.cities) if (CityZones(c).Count > 0 && Access.CityKingdom(c) != null) { city = c; break; }
                string error = "no city";
                if (city != null)
                {
                    Kingdom k = Access.CityKingdom(city);
                    Access.CityKingdom(city) = null;
                    try { ZoneCalc(World.world).drawZoneAlliance(CityZones(city)[0], 0); error = null; }
                    catch (System.Exception e) { error = e.GetType().Name; }
                    finally { Access.CityKingdom(city) = k; }
                }
                Check("alliance borders over a city without its kingdom yet", error == null, error ?? "drawn");
            }
            if (At(34f)) RemoteMagnet.TestSendBall(BallTo + new Vector2(-12f, 0f), BallTo);
            if (At(32.5f))
            {
                BreakZones = true;
                ScrollWindow.hideAllEvent(false);
                Vector3 c = Camera.main.transform.position;
                TestMouseTile = World.world.GetTile((int)c.x, (int)c.y);
                _highlights = 0;
                foreach (PowerButton b in Resources.FindObjectsOfTypeAll<PowerButton>())
                    if (b.gameObject.scene.IsValid() && ButtonPower(b)?.id == "tile_mountains") { Selector().setPower(b); break; }
            }
            if (At(33.5f))
            {
                PixelFlashEffects layer = FlashLayer(World.world);
                SpriteRenderer r = layer.GetComponent<SpriteRenderer>();
                Color32[] px = LayerPixels(layer);
                byte alpha = TestMouseTile != null && px != null ? px[TestMouseTile.data.tile_id].a : (byte)0;
                Check("the brush outline shows under the cursor", r != null && r.enabled && alpha > 0 && _highlights > 0,
                    "power " + World.world.selected_power?.id + ", highlight calls " + _highlights + ", renderer " + (r != null && r.enabled) + ", alpha " + alpha);
                TestMouseTile = null;
                BreakZones = false;
                Selector().unselectAll();
            }
            if (At(36f)) Debug.Log("[WorldBoxMP] test: anim done");
        }

        static Vector2 BallTo => new Vector2(MapBox.width / 2f + 7.5f, MapBox.height / 2f + 3.5f);

        static void StartSampling()
        {
            _samples.Clear();
            _ownFrames = _ownMoving = 0;
            _ownSprites.Clear();
            _animTimeStart = AnimationHelper.getTime();
        }

        static readonly HashSet<Sprite> _ownSprites = new HashSet<Sprite>();

        static void Sample()
        {
            Camera cam = Camera.main;
            foreach (Actor a in World.world.units)
            {
                if (!a.isAlive() || a.isInsideSomething() || !a.asset.has_sprite_renderer) continue;
                if (a == _own)
                {
                    if (a.is_moving) { _ownMoving++; if (_ownSprites.Add(a.calculateMainSprite())) _ownFrames = _ownSprites.Count; }
                    continue;
                }
                if (!_samples.TryGetValue(a.getID(), out Sampled s)) _samples[a.getID()] = s = new Sampled();
                s.Frames++;
                if (a.is_moving)
                {
                    s.Moving++;
                    Sprite sprite = a.calculateMainSprite();
                    s.Sprites.Add(sprite);
                    if (s.Last != null && sprite != s.Last) s.Changes++;
                    s.Last = sprite;
                }
                else s.Last = null;
            }
        }

        static void Report(string when)
        {
            int walkers = 0, animated = 0, moving = 0, changes = 0;
            foreach (Sampled s in _samples.Values)
            {
                if (s.Frames == 0 || s.Moving < s.Frames / 2) continue;
                walkers++;
                moving += s.Moving;
                changes += s.Changes;
                if (s.Sprites.Count >= 2) animated++;
            }
            float perSecond = moving > 0 ? changes / (moving * Time.smoothDeltaTime) : 0f;
            float animTime = AnimationHelper.getTime() - _animTimeStart;
            Debug.Log("[WorldBoxMP] test: anim " + when + ": paused=" + PausedF(World.world) + " config_paused=" + Config.paused +
                      " window=" + ScrollWindow.isWindowActive() + "/" + (ScrollWindow.getCurrentWindow() != null ? ScrollWindow.getCurrentWindow().screen_id : "-") + " anim_time=" + animTime.ToString("0.00") + " speed=" + Config.time_scale_asset?.id);
            Check(when + ": walking units animate", walkers > 5 && animated >= walkers * 0.8f && perSecond >= 3f,
                animated + " of " + walkers + " walkers, " + perSecond.ToString("0.0") + " frame changes a second each");
        }

        static void PossessSix()
        {
            var pick = new List<Actor>();
            var c = new Vector2(MapBox.width / 2f, MapBox.height / 2f);
            foreach (Actor a in World.world.units)
                if (a.isAlive() && a.asset.allow_possession && a.canBePossessed() && !a.isInsideSomething() && !SpecialActors.IsSpecial(a)) pick.Add(a);
            pick.Sort((x, y) => (x.current_position - c).sqrMagnitude.CompareTo((y.current_position - c).sqrMagnitude));
            if (pick.Count > 6) pick.RemoveRange(6, pick.Count - 6);
            SelectedUnit.clear();
            foreach (Actor a in pick) SelectedUnit.select(a, false);
            if (pick.Count > 0) SelectedUnit.makeMainSelected(pick[0]);
            ButtonEvent button = Object.FindObjectOfType<ButtonEvent>();
            AccessTools.Method(typeof(ButtonEvent), "clickPossess").Invoke(button, null);
            _own = ControllableUnit.getControllableUnit();
            int held = ControllableUnit.getCotrolledUnits()?.Count ?? 0;
            Check("the possess button takes one unit of six selected", held == 1 && _own != null, held + " held of " + pick.Count);
            if (_own != null) Camera.main.transform.position = new Vector3(_own.current_position.x, _own.current_position.y, Camera.main.transform.position.z);
        }

        static void Facing()
        {
            if (_own == null || !TestHooks.Mouse.HasValue) return;
            _facingChecks++;
            bool facesLeft = !Access.Flip(_own);
            if (TestHooks.Mouse.Value.x < _own.current_position.x) { if (facesLeft) _facedLeft++; }
            else if (!facesLeft) _facedRight++;
        }

        static float _hostTimer;

        static float _hostClock = -1f;
        static string _oldCode;
        static int _ballsBefore = -1;
        static bool _hostSawX20;

        static void HostLog(float dt)
        {
            if (Net.IsHostWithPeer && !SmoothLoader.isLoading())
            {
                if (_hostClock < 0f) _hostClock = 0f;
                float before = _hostClock;
                _hostClock += dt;
                bool At(float t) => before < t && _hostClock >= t;
                if (At(3f)) Camera.main.transform.position = new Vector3(10f, 10f, Camera.main.transform.position.z);
                if (At(6f))
                {
                    _oldCode = SteamSession.InviteCode;
                    _ballsBefore = RemoteMagnet.Balls;
                    TestHooks.LoseLobby = true;
                }
                if (At(26f))
                    Check("host: a lost Steam lobby opens again", !SteamSession.Available ||
                        SteamSession.LobbiesReopened == 1 && SteamSession.LobbyOpen && SteamSession.InviteCode != _oldCode,
                        _oldCode + " -> " + SteamSession.InviteCode + ", reopened " + SteamSession.LobbiesReopened);
                if (At(30f)) Check("host: hits it does not see are sent", EffectSync.UnseenSlashes > 0, EffectSync.UnseenSlashes + " slashes");
                if (Config.time_scale_asset?.id == "x20") _hostSawX20 = true;
                if (At(45f)) Check("host: the player's x20 reached the host", _hostSawX20, Config.time_scale_asset?.id);
                if (At(50f))
                {
                    Check("host: the player's ball starts where they let it go", RemoteMagnet.Balls > _ballsBefore && (RemoteMagnet.LastBallStart - BallTo).magnitude < 0.01f,
                        "balls " + (RemoteMagnet.Balls - _ballsBefore) + ", start " + RemoteMagnet.LastBallStart + ", want " + BallTo);
                    Debug.Log("[WorldBoxMP] test: anim host done");
                }
            }
            _hostTimer -= dt;
            if (_hostTimer > 0f) return;
            _hostTimer = 1f;
            foreach (Actor a in World.world.units)
                if (RemotePossession.IsRemote(a))
                    Debug.Log("[WorldBoxMP] test: anim host: held " + a.getID() + " at " + a.current_position + " moving=" + a.is_moving +
                              " flip=" + Access.Flip(a) + " attack_timer=" + AttackTimer(a).ToString("0.00") + " target=" + HasTarget(a));
        }
    }
}
