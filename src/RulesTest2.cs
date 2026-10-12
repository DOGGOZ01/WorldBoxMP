using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class RulesTest2
    {
        public static bool On;
        static float _clock = -1f;
        static int _humans = -1, _orcs = -1;
        static string _sandTile;
        static string _law;
        static bool _lawWas;

        static readonly System.Action<PlayerControl, Vector2Int, GodPower, bool> ClickedFinal =
            AccessTools.MethodDelegate<System.Action<PlayerControl, Vector2Int, GodPower, bool>>(AccessTools.Method(typeof(PlayerControl), "clickedFinal"));

        static void Check(string what, bool ok, string detail = "") =>
            Debug.Log("[WorldBoxMP] test: rules2 " + (ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? " (" + detail + ")" : ""));

        static Vector2Int Center => new Vector2Int(MapBox.width / 2, MapBox.height / 2);
        static readonly Vector2Int Sand = new Vector2Int(-20, -25);

        static int Count(string asset)
        {
            int n = 0;
            foreach (Actor a in World.world.units) if (a.isAlive() && a.asset.id == asset) n++;
            return n;
        }

        static Button PowerButtonOf(string id)
        {
            foreach (PowerButton b in Resources.FindObjectsOfTypeAll<PowerButton>())
                if (b.gameObject.scene.IsValid() && b.gameObject.name == id) return b.GetComponent<Button>();
            return null;
        }

        static string FirstBoolLaw()
        {
            WorldLaws laws = HostOnly.LawsOf(World.world);
            foreach (PlayerOptionData o in laws.list) if (o.name.StartsWith("world_law_") && o.name != "world_law_cursed_world") return o.name;
            return null;
        }

        static int Locked()
        {
            int n = 0;
            foreach (ActorTrait t in AssetManager.traits.list) if (!t.isAvailable()) n++;
            foreach (EquipmentAsset e in AssetManager.items.list) if (!e.isAvailable()) n++;
            return n;
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
            if (Net.Role == Role.Host)
            {
                if (At(15f))
                {
                    Rules.HostDenied.Add("human");
                    Rules.HostDenied.Add("tile_sand");
                    Rules.Send();
                    _law = FirstBoolLaw();
                    PlayerOptionData o = HostOnly.LawsOf(World.world).dict[_law];
                    o.boolVal = !o.boolVal;
                    Debug.Log("[WorldBoxMP] test: rules2 host: human and sand off, law " + _law + " now " + o.boolVal + ", locked before unlock " + Locked());
                    HostOnly.UnlockEverything();
                    _humans = Count("human");
                    _orcs = Count("orc");
                    _sandTile = World.world.GetTile(Center.x + Sand.x, Center.y + Sand.y)?.main_type.id;
                }
                if (At(45f))
                {
                    Check("host: no people from the second player", Count("human") <= _humans, Count("human") + " vs " + _humans);
                    Check("host: orcs from the second player", Count("orc") > _orcs, Count("orc") + " vs " + _orcs);
                    string now = World.world.GetTile(Center.x + Sand.x, Center.y + Sand.y)?.main_type.id;
                    Check("host: no sand drawn", now == _sandTile, now + " vs " + _sandTile);
                    Check("host: everything unlocked here", HostOnly.Unlocked && Locked() == 0, Locked().ToString());
                    Rules.HostDenied.Remove("human");
                    Rules.Send();
                }
                if (At(65f)) Debug.Log("[WorldBoxMP] test: rules2 host done");
                return;
            }
            if (At(5f))
            {
                _law = FirstBoolLaw();
                _lawWas = HostOnly.LawsOf(World.world).dict[_law].boolVal;
            }
            if (At(30f))
            {
                Check("client: human and sand off", Rules.ClientDenied.Contains("human") && Rules.ClientDenied.Contains("tile_sand"), Rules.ClientDenied.Count.ToString());
                Rules.ApplyUi();
                Button human = PowerButtonOf("human"), orc = PowerButtonOf("orc");
                Check("client: people button greyed, orcs not", human != null && !human.interactable && orc != null && orc.interactable);
                int sent = Powers.SentCount;
                ClickedFinal(pc, Center + new Vector2Int(4, 10), AssetManager.powers.get("human"), false);
                Check("client: people not sent", Powers.SentCount == sent);
                ClickedFinal(pc, Center + new Vector2Int(6, 10), AssetManager.powers.get("orc"), false);
                Check("client: orcs sent", Powers.SentCount == sent + 1);
                Powers.SendClick(Center + new Vector2Int(8, 10), AssetManager.powers.get("human"));
                Powers.SendClick(Center + Sand, AssetManager.powers.get("tile_sand"));
                Check("client: law from the host", HostOnly.LawsOf(World.world).dict[_law].boolVal != _lawWas, _law);
                Check("client: everything unlocked here", HostOnly.Unlocked && Locked() == 0, Locked().ToString());
                ScrollWindow.showWindow("world_laws");
                Check("client: world laws window stays shut", !ScrollWindow.isWindowActive() || ScrollWindow.getCurrentWindow().screen_id != "world_laws");
                ScrollWindow.showWindow("world_ages");
                Check("client: world ages window stays shut", !ScrollWindow.isWindowActive() || ScrollWindow.getCurrentWindow().screen_id != "world_ages");
                HostOnly.ApplyUi();
                Button laws = PowerButtonOf("world_laws"), ages = PowerButtonOf("ages");
                Check("client: laws and ages buttons greyed", laws != null && !laws.interactable && ages != null && !ages.interactable);
                string tip = laws != null ? HostOnly.TooltipOf(laws.GetComponent<PowerButton>()) : "-";
                Check("client: laws tooltip says only the host", tip == Lang.Id("host_only"), tip);
            }
            if (At(60f))
            {
                Rules.ApplyUi();
                Button human = PowerButtonOf("human");
                Check("client: people allowed again", !Rules.ClientDenied.Contains("human") && human != null && human.interactable);
                Debug.Log("[WorldBoxMP] test: rules2 client done");
            }
        }
    }
}
