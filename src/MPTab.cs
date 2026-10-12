using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class MPTab
    {
        static bool _created;
        public static PowersTab Tab;
        static LocalizedText _state, _detail;
        static float _statusTimer;

        public static void TryCreate()
        {
            if (_created) return;
            _created = true;
            try
            {
                Lang.Register();
                Rules.BuildPowerMap();

                PowersTab tab = TabManager.CreateTab(Lang.Id("tab"), Lang.Id("tab"), Lang.Id("tab_description"), Icons.Get("tab"));
                tab.SetLayout(new List<string> { "session", "status" });
                Tab = tab;
                Add(tab, "menu", "tab", MPWindow.Open);
                Add(tab, "lobby", "player", LobbyWindow.Open);
                Add(tab, "invite", "invite", SteamSession.Invite);
                Add(tab, "disconnect", "leave", () => MPMod.Instance.StopSession());

                RectTransform status = CreateStatus(tab);
                tab.PutElement("status", status, new Vector2(0f, 0f));
                tab.UpdateLayout();
                Debug.Log("[WorldBoxMP] multiplayer tab created");
            }
            catch (System.Exception e)
            {
                Debug.LogError("[WorldBoxMP] could not create the multiplayer tab: " + e);
            }
        }

        static void Add(PowersTab tab, string key, string icon, UnityEngine.Events.UnityAction action)
        {
            PowerButton button = PowerButtonCreator.CreateSimpleButton(Lang.Id(key), action, Icons.Get(icon));
            tab.AddPowerButton("session", button);
        }

        public static void OpenLogs()
        {
            string folder = MPMod.Instance.LogFolder;
            System.IO.Directory.CreateDirectory(folder);
            Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }

        static RectTransform CreateStatus(PowersTab tab)
        {
            var go = new GameObject("wbmp_status", typeof(RectTransform));
            go.transform.SetParent(tab.transform, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(170f, 40f);
            _state = Ui.Label(rect, null, 9, Ui.Gold, new Vector2(170f, 18f), TextAnchor.MiddleLeft);
            ((RectTransform)_state.transform).anchoredPosition = new Vector2(0f, 8f);
            _detail = Ui.Label(rect, null, 8, Ui.Pale, new Vector2(170f, 14f), TextAnchor.MiddleLeft);
            ((RectTransform)_detail.transform).anchoredPosition = new Vector2(0f, -9f);
            return rect;
        }

        public static void Update()
        {
            if (_state == null) return;
            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer > 0f) return;
            _statusTimer = 0.5f;
            Lang.Live(_state, "tab_state", SessionStatus.State());
            Lang.Live(_detail, "tab_detail", SessionStatus.Detail());
        }
    }
}
