using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public class MPWindow : SingleAutoLayoutWindow<MPWindow>
    {
        const float Width = 190f;
        enum View { Offline, Connecting, Online }

        LocalizedText _state, _detail;
        Image _stateIcon;
        GameObject _offline, _connecting, _online;
        View _shown = (View)(-1);
        float _timer;

        public static void Open()
        {
            if (Instance == null) CreateWindow("wbmp_window", Lang.Id("btn_mp"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_window");
        }

        protected override void Init()
        {
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 6, 10);

            var art = Ui.Image(transform, SpriteTextureLoader.getSprite("ui/illustrations/art_alliances"), new Vector2(Width, Width * 88f / 300f));
            art.preserveAspect = true;

            RectTransform status = Ui.Panel(transform, new Vector2(Width, 36f));
            _stateIcon = Ui.Image(status, Icons.Get("tab"), new Vector2(22f, 22f));
            ((RectTransform)_stateIcon.transform).anchoredPosition = new Vector2(-Width / 2f + 18f, 0f);
            _state = Ui.Label(status, null, 10, Ui.Gold, new Vector2(Width - 66f, 16f), TextAnchor.MiddleLeft);
            _detail = Ui.Label(status, null, 8, Ui.Pale, new Vector2(Width - 66f, 12f), TextAnchor.MiddleLeft);
            ((RectTransform)_detail.transform).anchoredPosition = new Vector2(4f, -8f);
            Button logs = Ui.IconButton(status, "logs", "logs", MPTab.OpenLogs, 22f);
            ((RectTransform)logs.transform).anchoredPosition = new Vector2(Width / 2f - 17f, 0f);

            _offline = Ui.Group(transform, Width);
            Ui.Button(_offline.transform, "host", "create_lobby", true, CreateLobby, Width);
            Ui.Hint(_offline.transform, "hint_create", Width);
            Ui.Separator(_offline.transform, Width);
            Ui.Button(_offline.transform, "join", "find_lobbies", true, BrowserWindow.Open, Width);
            Ui.Hint(_offline.transform, "hint_find", Width);

            _connecting = Ui.Group(transform, Width);
            Ui.Button(_connecting.transform, "leave", "btn_exit", false, () => MPMod.Instance.ExitLobby(), Width);

            _online = Ui.Group(transform, Width);
            Ui.Button(_online.transform, "tab", "btn_settings", true, LobbyWindow.Open, Width);
            Ui.Button(_online.transform, "leave", "btn_exit", false, () => MPMod.Instance.ExitLobby(), Width);
            Ui.Hint(_online.transform, "hint_keys", Width);

            Refresh(true);
        }

        public static void CreateLobby() => LobbyWindow.OpenSetup();

        public override void OnNormalEnable() => Refresh(true);

        void Update()
        {
            if (!IsOpened) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            Refresh(false);
        }

        void Refresh(bool force)
        {
            _timer = 0.25f;
            View view = Net.Role == Role.Host || Net.IsClientConnected && !SmoothLoader.isLoading() ? View.Online
                : Net.Role == Role.Client ? View.Connecting
                : View.Offline;
            if (view != _shown || force)
            {
                _shown = view;
                _offline.SetActive(view == View.Offline);
                _connecting.SetActive(view == View.Connecting);
                _online.SetActive(view == View.Online);
                _stateIcon.sprite = Icons.Get(Net.Role == Role.Host ? "host" : view == View.Offline ? "tab" : "join");
            }
            Lang.Live(_state, "window_state", SessionStatus.State());
            string detail = SessionStatus.Detail();
            Lang.Live(_detail, "window_detail", detail);
            ((RectTransform)_state.transform).anchoredPosition = new Vector2(4f, detail.Length > 0 ? 6f : 0f);
        }
    }

    public static class SessionStatus
    {
        public static string State()
        {
            string other = string.IsNullOrEmpty(PlayerNames.RemoteName) ? Net.Peer?.Remote : PlayerNames.RemoteName;
            if (Net.IsHostWithPeer) return Lang.T("state_hosting", other);
            if (Net.Role == Role.Host) return Lang.T("state_waiting");
            if (Net.IsClientConnected) return Lang.T("state_client", other);
            if (Net.Role == Role.Client) return Lang.T("state_connecting");
            return Lang.T("state_offline");
        }

        public static string Detail()
        {
            if (Net.IsHostWithPeer || Net.IsClientConnected)
                return NetStats.LastPingMs > 0 ? Lang.T("ping", NetStats.LastPingMs) : "";
            if (Net.Role == Role.Host) return Lang.T("ip_port", MPMod.Cfg.Port);
            return "";
        }
    }
}
