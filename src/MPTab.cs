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
        static LocalizedText _state, _detail, _pingHint, _chatHint;
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
                Add(tab, "btn_mp", "tab", MPWindow.Open);
                Add(tab, "btn_settings", "player", OpenSettings);
                Add(tab, "btn_exit", "leave", () => MPMod.Instance.ExitLobby());

                RectTransform status = CreateStatus(tab);
                tab.PutElement("status", status, new Vector2(0f, 0f));
                tab.PutElement("status", CreateHelp(tab), new Vector2(180f, 0f));
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

        static void OpenSettings()
        {
            if (Net.Role == Role.None) { MPMod.Tip("tip_not_connected"); return; }
            LobbyWindow.Open();
        }

        static RectTransform CreateHelp(PowersTab tab)
        {
            var go = new GameObject("wbmp_help", typeof(RectTransform));
            go.transform.SetParent(tab.transform, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(130f, 40f);
            Image ping = Ui.Image(rect, Icons.Get("ping"), new Vector2(14f, 14f));
            ((RectTransform)ping.transform).anchoredPosition = new Vector2(-56f, 9f);
            Image chat = Ui.Image(rect, Icons.Get("chat"), new Vector2(14f, 14f));
            ((RectTransform)chat.transform).anchoredPosition = new Vector2(-56f, -8f);
            _pingHint = Ui.Label(rect, null, 8, Ui.Pale, new Vector2(108f, 16f), TextAnchor.MiddleLeft);
            ((RectTransform)_pingHint.transform).anchoredPosition = new Vector2(9f, 9f);
            _chatHint = Ui.Label(rect, null, 8, Ui.Pale, new Vector2(108f, 16f), TextAnchor.MiddleLeft);
            ((RectTransform)_chatHint.transform).anchoredPosition = new Vector2(9f, -8f);
            return rect;
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
            string[] keys = Lang.T("hint_keys").Split('·');
            Lang.Live(_pingHint, "tab_ping_hint", keys[0].Trim());
            Lang.Live(_chatHint, "tab_chat_hint", keys.Length > 1 ? keys[1].Trim() : "");
        }
    }
}
