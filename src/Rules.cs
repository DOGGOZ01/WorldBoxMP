using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    [Flags]
    public enum Perm
    {
        None = 0,
        Terrain = 1,
        Creatures = 2,
        Nature = 4,
        Destruction = 8,
        Other = 16,
        Diplomacy = 32,
        Edit = 64,
        Orders = 128,
        Possess = 256,
        Speed = 512,
        All = 1023,
    }

    public static class Rules
    {
        public static readonly Perm[] List =
        {
            Perm.Terrain, Perm.Creatures, Perm.Nature, Perm.Destruction, Perm.Other, Perm.Diplomacy,
            Perm.Edit, Perm.Orders, Perm.Possess, Perm.Speed,
        };

        static readonly Dictionary<string, Perm> TabPerms = new Dictionary<string, Perm>
        {
            { "creation", Perm.Terrain }, { "units", Perm.Creatures }, { "nature", Perm.Nature },
            { "destruction", Perm.Destruction }, { "other", Perm.Other },
        };

        static readonly HashSet<string> DiplomacyPowers = new HashSet<string>
        {
            "spite", "friendship", "inspiration", "whisper_of_war", "discord", "unity",
        };

        public static Perm Host = Perm.All;

        public static Perm Client = Perm.All;

        public static readonly HashSet<string> HostDenied = new HashSet<string>();
        public static readonly HashSet<string> ClientDenied = new HashSet<string>();

        public static bool ClientMayPower(GodPower p) => !Net.IsClientConnected || p == null || (ClientMay(Of(p)) && !ClientDenied.Contains(p.id));

        public static bool HostAllowsPower(GodPower p) => p == null || (HostAllows(Of(p)) && !HostDenied.Contains(p.id));

        public static List<GodPower> PowersOf(Perm p)
        {
            if (_byPerm == null) BuildPowerMap();
            return _byPerm.TryGetValue(p, out List<GodPower> l) ? l : new List<GodPower>();
        }

        public static bool HasPowerList(Perm p) => PowersOf(p).Count > 0;

        public static void TogglePower(string id)
        {
            if (!HostDenied.Remove(id)) HostDenied.Add(id);
            SessionLog.Write("rules: power " + id + " " + (HostDenied.Contains(id) ? "off" : "on"));
            Send();
        }

        public static void SetAll(Perm p, bool on)
        {
            foreach (GodPower gp in PowersOf(p))
                if (on) HostDenied.Remove(gp.id); else HostDenied.Add(gp.id);
            if (on && !HostAllows(p)) Host |= p;
            SessionLog.Write("rules: all " + p + " powers " + (on ? "on" : "off"));
            Send();
        }

        public static string Key(Perm p) => "perm_" + p.ToString().ToLowerInvariant();
        public static string Name(Perm p) => Lang.T(Key(p));

        public static bool ClientMay(Perm p) => !Net.IsClientConnected || p == Perm.None || (Client & p) != 0;

        public static bool HostAllows(Perm p) => p == Perm.None || (Host & p) != 0;

        static Dictionary<string, Perm> _powers;
        static Dictionary<Perm, List<GodPower>> _byPerm;
        static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        public static Sprite IconOf(GodPower p) => p != null && _icons.TryGetValue(p.id, out Sprite s) && s != null ? s : p?.getIconSprite();

        public static void BuildPowerMap()
        {
            var map = new Dictionary<string, Perm>();
            var byPerm = new Dictionary<Perm, List<GodPower>>();
            var free = new HashSet<string>();
            foreach (PowersTab tab in Resources.FindObjectsOfTypeAll<PowersTab>())
            {
                if (!tab.gameObject.scene.IsValid()) continue;
                PowerTabAsset asset = TabAsset(tab);
                if (asset == null) continue;
                foreach (PowerButton b in tab.GetComponentsInChildren<PowerButton>(true))
                {
                    GodPower gp = ButtonPower(b);
                    if (gp == null) continue;
                    if (asset.id == "main") { free.Add(gp.id); continue; }
                    Perm p;
                    if (TabPerms.TryGetValue(asset.id, out p)) map[gp.id] = p;
                    else if (asset.id == "noosphere" && DiplomacyPowers.Contains(gp.id)) map[gp.id] = p = Perm.Diplomacy;
                    else continue;
                    if (!byPerm.TryGetValue(p, out List<GodPower> l)) byPerm[p] = l = new List<GodPower>();
                    if (!l.Contains(gp)) l.Add(gp);
                    if (b.icon != null && b.icon.sprite != null) _icons[gp.id] = b.icon.sprite;
                }
            }
            foreach (string id in free) map.Remove(id);
            foreach (var l in byPerm.Values) l.RemoveAll(gp => free.Contains(gp.id) || IconOf(gp) == null);
            _powers = map;
            _byPerm = byPerm;
            Debug.Log("[WorldBoxMP] rules: " + map.Count + " powers sorted by tab");
        }

        public static Perm Of(GodPower power)
        {
            if (power == null) return Perm.None;
            if (_powers == null) BuildPowerMap();
            if (_powers.TryGetValue(power.id, out Perm p)) return p;
            switch (power.type)
            {
                case PowerActionType.PowerSpawnActor:
                case PowerActionType.PowerSpawnBuilding: return Perm.Creatures;
                case PowerActionType.PowerDrawTile: return Perm.Terrain;
                case PowerActionType.PowerSpawnDrops:
                case PowerActionType.PowerSpawnSeeds: return Perm.Other;
                default: return Perm.None;
            }
        }

        static readonly AccessTools.FieldRef<PowersTab, PowerTabAsset> TabAssetRef = AccessTools.FieldRefAccess<PowersTab, PowerTabAsset>("_asset");
        static readonly AccessTools.FieldRef<PowerButton, GodPower> ButtonPowerRef = AccessTools.FieldRefAccess<PowerButton, GodPower>("godPower");
        static readonly AccessTools.FieldRef<PowerButtonSelector> SelectorInstance =
            AccessTools.StaticFieldRefAccess<PowerButtonSelector>(AccessTools.Field(typeof(PowerButtonSelector), "instance"));
        static PowerTabAsset TabAsset(PowersTab t) => TabAssetRef(t);
        static GodPower ButtonPower(PowerButton b) => ButtonPowerRef(b);

        public static void ResetHost() => Host = Perm.All;

        public static void Toggle(Perm p)
        {
            Host ^= p;
            SessionLog.Write("rules: " + p + " " + (HostAllows(p) ? "on" : "off") + " (" + Host + ")");
            Send();
            if (!HostAllows(Perm.Possess)) RemotePossession.HostReceiveUnpossess();
        }

        public static void Send()
        {
            if (!Net.IsHostWithPeer) return;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((int)Host);
            w.Write(HostDenied.Count);
            foreach (string id in HostDenied) w.Write(id);
            Net.Peer.Send(Msg.Rules, ms.ToArray());
        }

        public static void Deny(Perm p, string tipKey, ICollection<long> release = null)
        {
            if (Net.Peer == null) return;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((int)p);
            w.Write(tipKey);
            w.Write(release?.Count ?? 0);
            if (release != null) foreach (long id in release) w.Write(id);
            Net.Peer.Send(Msg.Denied, ms.ToArray());
            SessionLog.Write("refused the second player: " + p + " (" + tipKey + ")" + (release != null && release.Count > 0 ? ", released " + release.Count : ""));
        }

        static bool _received;

        public static void ResetClient()
        {
            Client = Perm.All;
            ClientDenied.Clear();
            _received = false;
            ApplyUi();
        }

        public static void Receive(byte[] data)
        {
            Perm was = Client;
            int wasDenied = ClientDenied.Count;
            var r = new BinaryReader(new MemoryStream(data));
            Client = (Perm)r.ReadInt32();
            var denied = new HashSet<string>();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) denied.Add(r.ReadString());
            bool listChanged = !denied.SetEquals(ClientDenied);
            ClientDenied.Clear();
            ClientDenied.UnionWith(denied);
            SessionLog.Write("rules from the host: " + Client + ", " + ClientDenied.Count + " powers off");
            if (_received && (was != Client || listChanged)) MPMod.Tip("tip_rules_changed");
            _received = true;
            ApplyUi();
            Enforce();
        }

        public static void ReceiveDenied(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var p = (Perm)r.ReadInt32();
            string tip = r.ReadString();
            int n = r.ReadInt32();
            var release = new List<long>();
            for (int i = 0; i < n; i++) release.Add(r.ReadInt64());
            if (tip == "tip_denied") Refuse(p);
            else Say(tip);
            foreach (long id in release)
            {
                Actor a = World.world.units.get(id);
                if (a != null && ControllableUnit.isControllingUnit(a)) ControllableUnit.remove(a);
            }
        }

        static readonly Dictionary<string, float> _lastTip = new Dictionary<string, float>();

        static void Say(string key, params object[] args)
        {
            string id = key + (args.Length > 0 ? args[0] : "");
            if (_lastTip.TryGetValue(id, out float t) && Time.unscaledTime - t < 1.5f) return;
            _lastTip[id] = Time.unscaledTime;
            MPMod.Tip(key, args);
        }

        public static void Refuse(Perm p) => Say("tip_denied", Name(p));

        public static void Busy() => Say("tip_unit_busy");

        static void Enforce()
        {
            if (!Net.IsClientConnected) return;
            PowersTab active = PowersTab.getActiveTab();
            if (active != null && TabAsset(active) != null && TabPerms.TryGetValue(TabAsset(active).id, out Perm tp) && !ClientMay(tp))
                PowersTab.unselect();
            if (World.world.selected_power != null && !ClientMayPower(World.world.selected_power))
                SelectorInstance()?.unselectAll();
            if (!ClientMay(Perm.Possess) && ControllableUnit.isControllingUnit())
            {
                foreach (Actor a in new List<Actor>(ControllableUnit.getCotrolledUnits()))
                    if (!SpecialActors.IsSpecial(a)) ControllableUnit.remove(a);
            }
        }

        static readonly List<KeyValuePair<Button, Perm>> _buttons = new List<KeyValuePair<Button, Perm>>();
        static readonly List<KeyValuePair<Button, GodPower>> _powerButtons = new List<KeyValuePair<Button, GodPower>>();
        static float _uiTimer;

        static void FindButtons()
        {
            _buttons.Clear();
            _powerButtons.Clear();
            foreach (PowersTab tab in Resources.FindObjectsOfTypeAll<PowersTab>())
            {
                if (!tab.gameObject.scene.IsValid() || tab == MPTab.Tab) continue;
                PowerTabAsset asset = TabAsset(tab);
                if (asset == null) continue;
                if (TabPerms.TryGetValue(asset.id, out Perm p) && tab.powerButton != null)
                    _buttons.Add(new KeyValuePair<Button, Perm>(tab.powerButton, p));
                if (asset.tab_type_main && asset.id != "main")
                    foreach (PowerButton b in tab.GetComponentsInChildren<PowerButton>(true))
                        if (ButtonPower(b) != null && b.GetComponent<Button>() != null && Of(ButtonPower(b)) != Perm.None)
                            _powerButtons.Add(new KeyValuePair<Button, GodPower>(b.GetComponent<Button>(), ButtonPower(b)));
                if (asset.tab_type_main) continue;
                foreach (Button b in tab.GetComponentsInChildren<Button>(true))
                {
                    string n = b.gameObject.name;
                    if (n == "unit_possession") _buttons.Add(new KeyValuePair<Button, Perm>(b, Perm.Possess));
                    else if (n.EndsWith("_trait_editor") || n.EndsWith("_traits_editor") || n.EndsWith("_equipment_editor"))
                        _buttons.Add(new KeyValuePair<Button, Perm>(b, Perm.Edit));
                }
            }
        }

        public static void ApplyUi()
        {
            if (_buttons.Count == 0) FindButtons();
            foreach (var kv in _powerButtons)
            {
                if (kv.Key == null) continue;
                bool on = ClientMayPower(kv.Value);
                if (kv.Key.interactable != on) kv.Key.interactable = on;
                CanvasGroup g = kv.Key.GetComponent<CanvasGroup>();
                if (!on && g == null) g = kv.Key.gameObject.AddComponent<CanvasGroup>();
                if (g != null) g.alpha = on ? 1f : 0.35f;
            }
            foreach (var kv in _buttons)
            {
                if (kv.Key == null) continue;
                bool on = ClientMay(kv.Value);
                if (kv.Key.interactable != on) kv.Key.interactable = on;
                CanvasGroup g = kv.Key.GetComponent<CanvasGroup>();
                if (!on && g == null) g = kv.Key.gameObject.AddComponent<CanvasGroup>();
                if (g != null) g.alpha = on ? 1f : 0.35f;
            }
        }

        public static void Update()
        {
            if (!Net.IsClientConnected || (Client == Perm.All && ClientDenied.Count == 0)) return;
            _uiTimer -= Time.unscaledDeltaTime;
            if (_uiTimer > 0f) return;
            _uiTimer = 0.5f;
            ApplyUi();
            Enforce();
        }

        public static string DescribeUi()
        {
            var parts = new List<string>();
            foreach (var kv in _buttons)
            {
                if (kv.Key == null) { parts.Add("(gone)"); continue; }
                CanvasGroup g = kv.Key.GetComponent<CanvasGroup>();
                parts.Add(kv.Key.name + "@" + kv.Key.transform.parent?.name + ":" + kv.Value + (kv.Key.interactable ? " on" : " off") + " a" + (g != null ? g.alpha.ToString("0.00") : "-") + " groups" + kv.Key.GetComponentsInParent<CanvasGroup>(true).Length);
            }
            return string.Join(" | ", parts.ToArray());
        }

        public static string OffList()
        {
            var off = new List<string>();
            foreach (Perm p in List)
            {
                if (!ClientMay(p)) off.Add(Name(p));
                else
                {
                    int n = 0;
                    foreach (GodPower gp in PowersOf(p)) if (ClientDenied.Contains(gp.id)) n++;
                    if (n > 0) off.Add(Name(p) + " (" + n + ")");
                }
            }
            return off.Count == 0 ? "" : Lang.T("client_denied_list", string.Join(", ", off.ToArray()));
        }

        [HarmonyPatch(typeof(PowersTab), nameof(PowersTab.showTab))]
        static class TabsThatAreOff
        {
            static bool Prefix(PowersTab __instance)
            {
                if (!Net.IsClientConnected) return true;
                PowerTabAsset asset = TabAsset(__instance);
                if (asset == null || !TabPerms.TryGetValue(asset.id, out Perm p) || ClientMay(p)) return true;
                Refuse(p);
                return false;
            }
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.setControllableCreature))]
        static class OneControllerPerUnit
        {
            static bool Prefix(Actor pActor)
            {
                if (pActor == null) return true;
                if (Net.IsClientConnected)
                {
                    if (SpecialActors.IsSpecial(pActor)) return true;
                    if (!ClientMay(Perm.Possess)) { Refuse(Perm.Possess); return false; }
                    if (PlayerNames.OtherControls(pActor.getID())) { Busy(); return false; }
                }
                else if (Net.Role == Role.Host && RemotePossession.IsRemote(pActor))
                {
                    Busy();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerControl), "tryToMoveSelectedUnit")]
        static class HostOrdersSkipFriendUnits
        {
            static bool Prefix(Actor pActor, ref bool __result)
            {
                if (Net.Role != Role.Host || Orders.Replaying || !RemotePossession.IsRemote(pActor)) return true;
                Busy();
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(EquipmentEditor), "addAugmentation")]
        static class ClientAddsItemsThroughHost
        {
            static bool Prefix(EquipmentButton pButton, ref bool __result)
            {
                if (!Net.IsClientConnected) return true;
                __result = false;
                if (!ClientMay(Perm.Edit)) { Refuse(Perm.Edit); return false; }
                Actor a = SelectedUnit.unit;
                EquipmentAsset asset = pButton.getElementAsset();
                if (a != null && asset != null) PlayerEdits.SendUnitField(a.getID(), "item+", asset.id);
                return false;
            }
        }

        [HarmonyPatch(typeof(EquipmentEditor), "removeAugmentation")]
        static class ClientRemovesItemsThroughHost
        {
            static bool Prefix(EquipmentButton pButton, ref bool __result)
            {
                if (!Net.IsClientConnected) return true;
                __result = false;
                if (!ClientMay(Perm.Edit)) { Refuse(Perm.Edit); return false; }
                Actor a = SelectedUnit.unit;
                EquipmentAsset asset = pButton.getElementAsset();
                if (a != null && asset != null) PlayerEdits.SendUnitField(a.getID(), "item-", asset.equipment_type.ToString());
                return false;
            }
        }
    }
}
