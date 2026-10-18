using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class HostOnly
    {
        static readonly HashSet<string> Windows = new HashSet<string> { "world_laws", "world_ages" };
        static readonly HashSet<string> ButtonNames = new HashSet<string> { "world_laws", "ages" };

        static readonly List<KeyValuePair<Button, string>> _buttons = new List<KeyValuePair<Button, string>>();

        static void FindButtons()
        {
            _buttons.Clear();
            foreach (PowerButton b in Resources.FindObjectsOfTypeAll<PowerButton>())
            {
                if (!b.gameObject.scene.IsValid() || !ButtonNames.Contains(b.gameObject.name)) continue;
                Button button = b.GetComponent<Button>();
                TipButton tip = b.GetComponent<TipButton>();
                if (button != null) _buttons.Add(new KeyValuePair<Button, string>(button, tip != null ? tip.textOnClickDescription : null));
            }
        }

        public static void ApplyUi()
        {
            if (_buttons.Count == 0) FindButtons();
            bool locked = Net.IsClientConnected;
            foreach (var kv in _buttons)
            {
                Button b = kv.Key;
                if (b == null) continue;
                if (b.interactable == locked) b.interactable = !locked;
                CanvasGroup g = b.GetComponent<CanvasGroup>();
                if (locked && g == null) g = b.gameObject.AddComponent<CanvasGroup>();
                if (g != null) g.alpha = locked ? 0.35f : 1f;
                TipButton tip = b.GetComponent<TipButton>();
                if (tip != null) tip.textOnClickDescription = locked ? Lang.Id("host_only") : kv.Value;
            }
        }

        static float _uiTimer;
        static bool _wasClient;

        public static void Update()
        {
            bool client = Net.IsClientConnected;
            _uiTimer -= Time.unscaledDeltaTime;
            if (client != _wasClient || (client && _uiTimer <= 0f))
            {
                _uiTimer = 0.5f;
                _wasClient = client;
                ApplyUi();
            }
            if (Net.IsHostWithPeer) HostSendLaws();
        }

        [HarmonyPatch(typeof(PowerButton), "getDescription")]
        static class HostOnlyTooltip
        {
            static bool Prefix(PowerButton __instance, ref string __result)
            {
                if (!Net.IsClientConnected || !ButtonNames.Contains(__instance.gameObject.name)) return true;
                __result = Lang.Id("host_only");
                return false;
            }
        }

        public static string TooltipOf(PowerButton b) => (string)AccessTools.Method(typeof(PowerButton), "getDescription").Invoke(b, null);

        [HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.showWindow), typeof(string), typeof(bool), typeof(bool))]
        static class HostWindows
        {
            static bool Prefix(string pWindowID)
            {
                if (!Net.IsClientConnected || pWindowID == null || !Windows.Contains(pWindowID)) return true;
                Say("host_only");
                return false;
            }
        }

        static float _lastSay = -10f;
        static void Say(string key)
        {
            if (Time.unscaledTime - _lastSay < 1.5f) return;
            _lastSay = Time.unscaledTime;
            MPMod.Tip(key);
        }

        static float _lawsTimer;
        static string _lawsSent;

        public static void ResendLaws() => _lawsSent = null;

        public static WorldLaws LawsOf(MapBox w) => w != null ? Access.Laws(w) : null;

        static void HostSendLaws()
        {
            _lawsTimer -= Time.unscaledDeltaTime;
            if (_lawsTimer > 0f) return;
            _lawsTimer = 1f;
            WorldLaws laws = World.world != null ? Access.Laws(World.world) : null;
            if (laws?.list == null) return;
            var sb = new StringBuilder();
            foreach (PlayerOptionData o in laws.list) sb.Append(o.name).Append('=').Append(o.boolVal).Append(',').Append(o.intVal).Append(',').Append(o.stringVal).Append(';');
            string now = sb.ToString();
            if (now == _lawsSent) return;
            bool first = _lawsSent == null;
            _lawsSent = now;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(laws.list.Count);
            foreach (PlayerOptionData o in laws.list)
            {
                w.Write(o.name ?? "");
                w.Write(o.boolVal);
                w.Write(o.intVal);
                w.Write(o.stringVal ?? "");
            }
            Net.Peer.Send(Msg.Laws, ms.ToArray());
            if (!first) SessionLog.Write("world laws sent: " + laws.list.Count);
        }

        public static void ClientReceiveLaws(byte[] data)
        {
            WorldLaws laws = World.world != null ? Access.Laws(World.world) : null;
            if (laws?.dict == null) return;
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            int changed = 0;
            for (int i = 0; i < n; i++)
            {
                string id = r.ReadString();
                bool b = r.ReadBoolean();
                int iv = r.ReadInt32();
                string s = r.ReadString();
                if (!laws.dict.TryGetValue(id, out PlayerOptionData o)) continue;
                if (o.boolVal != b || o.intVal != iv || o.stringVal != s) changed++;
                o.boolVal = b;
                o.intVal = iv;
                o.stringVal = s;
            }
            if (changed > 0) SessionLog.Write("world laws from the host: " + changed + " changed");
        }

        public const string UnlockFlag = "wbmp_unlocked_all";

        public static bool Unlocked
        {
            get
            {
                if (World.world == null) return false;
                MapStats stats = Access.MapStats(World.world);
                return stats?.custom_data != null && stats.custom_data.hasFlag(UnlockFlag);
            }
        }

        public static void UnlockEverything()
        {
            if (Net.IsClientConnected) { Say("host_only"); return; }
            Mark();
            if (Net.IsHostWithPeer) Net.Peer.Send(Msg.Unlock, null);
            MPMod.Tip("tip_unlocked");
            SessionLog.Write("unlocked everything in this world");
        }

        public static void ClientReceiveUnlock()
        {
            Mark();
            MPMod.Tip("tip_unlocked");
        }

        static void Mark()
        {
            MapStats stats = Access.MapStats(World.world);
            if (stats == null) return;
            if (stats.custom_data == null) stats.custom_data = new SaveCustomData();
            stats.custom_data.addFlag(UnlockFlag);
        }

        [HarmonyPatch(typeof(BaseUnlockableAsset), nameof(BaseUnlockableAsset.isUnlocked))]
        static class UnlockedInThisWorld
        {
            static void Postfix(ref bool __result)
            {
                if (!__result && Unlocked) __result = true;
            }
        }

        [HarmonyPatch(typeof(BaseUnlockableAsset), nameof(BaseUnlockableAsset.isAvailable))]
        static class AvailableInThisWorld
        {
            static void Postfix(ref bool __result)
            {
                if (!__result && Unlocked) __result = true;
            }
        }
    }
}
