using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class RulesTest
    {
        public static bool On;
        static float _clock = -1f;
        static int _humansAtOff = -1;
        static string _tileAtOff;
        static string _nameAtOff;
        static string _weapon;
        static bool _uChecked;
        static float _offPossessAt = -1f;
        static bool _possessOff;
        static readonly Vector2Int Off = new Vector2Int(20, -25);

        static readonly System.Action<PlayerControl, Vector2Int, GodPower, bool> ClickedFinal =
            AccessTools.MethodDelegate<System.Action<PlayerControl, Vector2Int, GodPower, bool>>(AccessTools.Method(typeof(PlayerControl), "clickedFinal"));
        static readonly System.Func<PlayerControl, Actor, WorldTile, bool, bool> MoveOne =
            AccessTools.MethodDelegate<System.Func<PlayerControl, Actor, WorldTile, bool, bool>>(AccessTools.Method(typeof(PlayerControl), "tryToMoveSelectedUnit"));

        static void Check(string what, bool ok, string detail = "")
        {
            Debug.Log("[WorldBoxMP] test: rules " + (ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? " (" + detail + ")" : ""));
        }

        static Vector2Int Center => new Vector2Int(MapBox.width / 2, MapBox.height / 2);

        static Actor Pick(int index)
        {
            var list = new List<Actor>();
            foreach (Actor a in World.world.units)
                if (a.isAlive() && a.asset.allow_possession && a.canBePossessed() && !SpecialActors.IsSpecial(a) && !a.isInsideSomething()) list.Add(a);
            list.Sort((x, y) => x.getID().CompareTo(y.getID()));
            return index < list.Count ? list[index] : null;
        }

        static int Count(string asset)
        {
            int n = 0;
            foreach (Actor a in World.world.units) if (a.isAlive() && a.asset.id == asset) n++;
            return n;
        }

        static string Weapon()
        {
            if (_weapon != null) return _weapon;
            Actor v = Human();
            foreach (EquipmentAsset e in AssetManager.items.list)
                if (e.equipment_type == EquipmentType.Weapon && e.show_in_meta_editor && e.id.StartsWith("sword") && (v == null || v.asset.canEditItem(e))) { _weapon = e.id; break; }
            return _weapon;
        }

        static Actor Human()
        {
            Actor best = null;
            foreach (Actor a in World.world.units)
                if (a.isAlive() && a.asset.id == "human" && (best == null || a.getID() < best.getID())) best = a;
            return best;
        }

        static string ItemIn(Actor a)
        {
            if (a == null || a.equipment == null) return "-";
            ActorEquipmentSlot s = a.equipment.getSlot(EquipmentType.Weapon);
            return s.isEmpty() ? "-" : s.getItem().getAsset().id;
        }

        static void RawPossess(long id)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(1);
            w.Write(id);
            Net.Peer.Send(Msg.Possess, ms.ToArray());
        }

        public static void Update(float dt)
        {
            if (!On || World.world == null || SmoothLoader.isLoading()) return;
            if (!(Net.IsHostWithPeer || Net.IsClientConnected)) return;
            if (_clock < 0f) _clock = 0f;
            float before = _clock;
            _clock += dt;
            bool At(float t) => before < t && _clock >= t;
            PlayerControl pc = World.world.player_control;
            if (Net.Role == Role.Host) Host(At, pc);
            else Client(At, pc);
        }

        delegate bool AtFn(float t);

        static void Host(AtFn At, PlayerControl pc)
        {
            if (At(5f))
            {
                for (int i = 0; i < 8; i++) ClickedFinal(pc, Center + new Vector2Int(i * 2 - 8, 6), AssetManager.powers.get("human"), false);
                Debug.Log("[WorldBoxMP] test: rules host spawned people");
            }
            if (At(20f))
            {
                foreach (Perm p in Rules.List) if (Rules.HostAllows(p)) Rules.Toggle(p);
                _humansAtOff = Count("human");
                WorldTile t = World.world.GetTile(Center.x + Off.x, Center.y + Off.y);
                _tileAtOff = t?.main_type.id;
                _nameAtOff = Pick(0)?.getName();
                Debug.Log("[WorldBoxMP] test: rules host turned everything off: " + Rules.Host + ", humans " + _humansAtOff + ", tile " + _tileAtOff + ", T " + Pick(0)?.getID());
            }
            if (At(50f))
            {
                Actor T = Pick(0);
                WorldTile t = World.world.GetTile(Center.x + Off.x, Center.y + Off.y);
                Check("host: no creatures spawned while off", Count("human") <= _humansAtOff, Count("human") + " vs " + _humansAtOff);
                Check("host: terrain unchanged while off", t?.main_type.id == _tileAtOff, t?.main_type.id + " vs " + _tileAtOff);
                Check("host: unit not renamed while off", T != null && T.getName() == _nameAtOff, T?.getName());
                Check("host: no item given while off", ItemIn(T) != Weapon() || ItemIn(T) == "-", ItemIn(T));
                Check("host: no possession while off", !RemotePossession.IsRemote(T));
                Check("host: world speed kept", Config.time_scale_asset?.id == "x1", Config.time_scale_asset?.id);
                foreach (Perm p in Rules.List) if (!Rules.HostAllows(p)) Rules.Toggle(p);
                Debug.Log("[WorldBoxMP] test: rules host turned everything on: " + Rules.Host);
            }
            if (At(60f))
            {
                Actor T = Pick(0);
                ControllableUnit.setControllableCreature(T);
                Check("host: host controls T", RemotePossession.HostControls(T), T?.getID().ToString());
            }
            if (At(76f))
            {
                Actor T = Pick(0), V = Human();
                Check("host: T still the host's, not the second player's", RemotePossession.HostControls(T) && !RemotePossession.IsRemote(T));
                Check("host: item given through the host", ItemIn(V) == Weapon(), V?.getID() + " " + ItemIn(V));
                ControllableUnit.clear();
                Debug.Log("[WorldBoxMP] test: rules host released T");
            }
            if (_clock > 62f && !_uChecked && Pick(1) != null && RemotePossession.IsRemote(Pick(1)))
            {
                _uChecked = true;
                Actor U = Pick(1);
                Check("host: second player holds U", RemotePossession.IsRemote(U), U?.getID().ToString());
                ControllableUnit.setControllableCreature(U);
                Check("host: host cannot take U", !RemotePossession.HostControls(U));
                WorldTile goal = World.world.GetTile(Center.x, Center.y);
                bool moved = MoveOne(pc, U, goal, true);
                Check("host: host order skips U", !moved);
                if (RemotePossession.HostControls(U)) ControllableUnit.remove(U);
                _offPossessAt = _clock + 5f;
            }
            if (_offPossessAt > 0f && _clock >= _offPossessAt && !_possessOff)
            {
                _possessOff = true;
                Rules.Toggle(Perm.Possess);
                Debug.Log("[WorldBoxMP] test: rules host turned possession off while U is held");
            }
            if (_possessOff && _clock >= _offPossessAt + 6f && _offPossessAt > 0f)
            {
                _offPossessAt = -1f;
                Check("host: U let go after possession was turned off", !RemotePossession.IsRemote(Pick(1)));
                Rules.Toggle(Perm.Possess);
            }
            if (At(170f)) { if (!_uChecked) Check("host: second player holds U", false, "never"); Debug.Log("[WorldBoxMP] test: rules host done"); }
        }

        static void Client(AtFn At, PlayerControl pc)
        {
            if (At(32f))
            {
                Check("client: rules received (all off)", Rules.Client == Perm.None, Rules.Client.ToString());
                Actor T = Pick(0);
                PowersTab creation = null;
                foreach (PowersTab tab in Resources.FindObjectsOfTypeAll<PowersTab>())
                    if (tab.gameObject.scene.IsValid() && tab.name == "creation") creation = tab;
                if (creation != null)
                {
                    creation.showTab(null);
                    Check("client: terrain tab does not open", PowersTab.getActiveTab() != creation);
                    Check("client: terrain tab button greyed", creation.powerButton != null && !creation.powerButton.interactable);
                }
                int sentBefore = Powers.SentCount;
                ClickedFinal(pc, Center + new Vector2Int(-6, 10), AssetManager.powers.get("human"), false);
                Check("client: power not sent when off", Powers.SentCount == sentBefore, (Powers.SentCount - sentBefore).ToString());
                ControllableUnit.setControllableCreature(T);
                Check("client: possession refused when off", !ControllableUnit.isControllingUnit(T));
                PlayerEdits.ForceInteractive = true;
                Config.setWorldSpeed(AssetManager.time_scales.get("x5"));
                for (int i = 0; i < 4; i++) Powers.SendClick(Center + new Vector2Int(i * 2 - 4, 12), AssetManager.powers.get("human"));
                Powers.SendClick(Center + Off, AssetManager.powers.get("tile_mountains"));
                PlayerEdits.SendUnitField(T.getID(), "name", "HACKED");
                PlayerEdits.SendUnitField(T.getID(), "item+", Weapon());
                Orders.Send(World.world.GetTile(Center.x, Center.y), false, null, new List<Actor> { T });
                RawPossess(T.getID());
                Debug.Log("[WorldBoxMP] test: rules client sent everything around its interface, T " + T.getID());
            }
            if (At(46f)) ScrollWindow.hideAllEvent(false);
            if (At(48f))
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(TestHarness.UiFolder, "rules_tabs.png"));
                Debug.Log("[WorldBoxMP] test: rules ui " + Rules.DescribeUi());
            }
            if (At(44f))
            {
                PlayerEdits.ForceInteractive = false;
                Check("client: speed back to the host's", Config.time_scale_asset?.id == "x1", Config.time_scale_asset?.id);
                Check("client: T keeps its name", Pick(0)?.getName() != "HACKED", Pick(0)?.getName());
                Check("client: holds nothing", !ControllableUnit.isControllingUnit());
            }
            if (At(58f)) Check("client: rules received (all on)", Rules.Client == Perm.All, Rules.Client.ToString());
            if (At(62f))
            {
                Actor V = Human();
                Check("client: weapon " + Weapon() + " for V", V != null && Weapon() != null, V?.getID().ToString());
                PlayerEdits.SendUnitField(V.getID(), "item+", Weapon());
            }
            if (At(68f))
            {
                Actor T = Pick(0);
                Check("client: knows the host holds T", PlayerNames.OtherControls(T.getID()));
                ControllableUnit.setControllableCreature(T);
                Check("client: cannot take the host's T", !ControllableUnit.isControllingUnit(T));
                RawPossess(T.getID());
                Orders.Send(World.world.GetTile(Center.x + 10, Center.y), false, null, new List<Actor> { T });
            }
            if (At(74f))
            {
                Check("client: still holds nothing", !ControllableUnit.isControllingUnit());
                Check("client: V got the weapon from the host", ItemIn(Human()) == Weapon(), ItemIn(Human()));
            }
            if (At(84f))
            {
                Actor U = Pick(1);
                ControllableUnit.setControllableCreature(U);
                Check("client: takes U", ControllableUnit.isControllingUnit(U), U?.getID().ToString());
            }
            if (At(150f))
            {
                Check("client: let go of U when possession was turned off", !ControllableUnit.isControllingUnit(Pick(1)));
                ControllableUnit.clear();
                Debug.Log("[WorldBoxMP] test: rules client done");
            }
        }
    }
}
