using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public class LobbyWindow : SingleAutoLayoutWindow<LobbyWindow>
    {
        const float Width = 190f;
        RectTransform _list;
        GameObject _hostBox, _setupBox, _codeRow, _invite;
        LocalizedText _code;
        TextInput _password;
        readonly Image[] _visButtons = new Image[3];
        string _shown, _hostShown;
        float _timer;
        static bool _setup;

        public static void Open()
        {
            _setup = false;
            Show();
        }

        public static void OpenSetup()
        {
            _setup = Net.Role == Role.None;
            Show();
        }

        static void Show()
        {
            if (Instance == null) CreateWindow("wbmp_lobby", Lang.Id("btn_settings"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_lobby");
            Instance._shown = null;
            Instance._hostShown = null;
            Instance.Rebuild();
        }

        static bool InSetup => _setup && Net.Role == Role.None;

        protected override void Init()
        {
            layout.spacing = 5f;
            layout.padding = new RectOffset(0, 0, 8, 10);

            _setupBox = Ui.Group(transform, Width);
            Ui.Hint(_setupBox.transform, "setup_hint", Width);
            Ui.Button(_setupBox.transform, "host", "open_lobby", true, OpenLobby, Width);

            _hostBox = Ui.Group(transform, Width);
            _codeRow = Ui.Row(_hostBox.transform, Width, 20f).gameObject;
            _code = Ui.Label(_codeRow.transform, null, 9, Ui.Gold, new Vector2(Width - 66f, 18f), TextAnchor.MiddleLeft);
            Ui.TextButton(_codeRow.transform, "copy", CopyCode, 58f, 18f);
            _invite = Ui.Button(_hostBox.transform, "invite", "invite", true, SteamSession.OpenInviteOverlay, Width).gameObject;
            Ui.Label(_hostBox.transform, Lang.Id("visibility"), 8, Ui.Gold, new Vector2(Width, 12f), TextAnchor.MiddleCenter);
            RectTransform vis = Ui.Row(_hostBox.transform, Width, 22f);
            string[] keys = { "vis_public", "vis_friends", "vis_invite" };
            for (int i = 0; i < 3; i++)
            {
                int v = i;
                Button b = Ui.TextButton(vis, keys[i], () => SetVisibility(v), Width / 3f - 3f, 22f);
                _visButtons[i] = b.GetComponent<Image>();
            }
            Ui.Label(_hostBox.transform, Lang.Id("lobby_password"), 8, Ui.Gold, new Vector2(Width, 12f), TextAnchor.MiddleCenter);
            _password = Ui.Input(_hostBox.transform, Lobby.Password, Width - 10f, SetPassword);
            Ui.Button(_hostBox.transform, "on", "players_can", false, () => RulesWindow.Open(null), Width);
            Ui.Button(_hostBox.transform, "key", "unlock_all", false, HostOnly.UnlockEverything, Width);
            Ui.Separator(_hostBox.transform, Width);

            _list = Ui.Group(transform, Width).GetComponent<RectTransform>();
            _list.GetComponent<VerticalLayoutGroup>().spacing = 4f;
        }

        public override void OnNormalEnable() { _shown = null; _hostShown = null; Rebuild(); }

        void Update()
        {
            if (!IsOpened) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            Rebuild();
        }

        void OpenLobby()
        {
            _setup = false;
            MPMod.Instance.Host();
            _shown = null;
            _hostShown = null;
            Rebuild();
        }

        static void SetVisibility(int v)
        {
            if (Lobby.Visibility == v) return;
            Lobby.Visibility = v;
            SteamSession.ApplyVisibility();
            SessionLog.Write("lobby visibility " + v);
            Instance?.RefreshHostBox();
        }

        static void SetPassword(string text)
        {
            string pw = (text ?? "").Trim();
            if (pw == Lobby.Password) return;
            Lobby.Password = pw;
            SteamSession.PublishInfo();
            SessionLog.Write(pw.Length > 0 ? "lobby password set" : "lobby password removed");
        }

        static void CopyCode()
        {
            string code = SteamSession.InviteCode;
            if (code.Length == 0) return;
            GUIUtility.systemCopyBuffer = code;
            MPMod.Tip("tip_copied", code);
        }

        void RefreshHostBox()
        {
            string code = SteamSession.InviteCode;
            string sig = Lobby.Visibility + "|" + code + "|" + Net.Role + "|" + SteamSession.LobbyOpen;
            if (sig == _hostShown) return;
            _hostShown = sig;
            bool hosting = Net.Role == Role.Host;
            if (_codeRow.activeSelf != hosting) _codeRow.SetActive(hosting);
            bool invite = hosting && SteamSession.LobbyOpen;
            if (_invite.activeSelf != invite) _invite.SetActive(invite);
            if (hosting) Lang.Live(_code, "lobby_code", code.Length > 0 ? Lang.T("code", code) : Lang.T("ip_port", MPMod.Cfg.Port));
            for (int i = 0; i < 3; i++)
                _visButtons[i].sprite = SpriteTextureLoader.getSprite(i == Lobby.Visibility ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray");
            Ui.Relayout(null, transform);
        }

        static string Signature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Net.Role).Append('|').Append(InSetup).Append('|').Append(Roster.LocalSlot).Append('|');
            foreach (PlayerInfo p in Roster.Players) sb.Append(p.Slot).Append(p.Name).Append(p.PingMs / 10).Append(';');
            for (int i = 0; i < Lobby.MaxGuests; i++) sb.Append(SlotState(i));
            foreach (var kv in Lobby.Kicked) sb.Append(kv.Key);
            if (Net.Role == Role.Host) foreach (Guest g in Net.Guests) sb.Append(g.Slot).Append(g.OwnRules);
            return sb.ToString();
        }

        static byte SlotState(int i)
        {
            if (Net.Role != Role.Host && !InSetup) return Lobby.SlotState[i];
            return (byte)(Net.GuestIn((byte)(i + 1)) != null ? 2 : Lobby.SlotOpen[i] ? 1 : 0);
        }

        void Rebuild()
        {
            if (_list == null) return;
            bool setup = InSetup;
            bool host = Net.Role == Role.Host || setup;
            bool changed = false;
            if (_setupBox.activeSelf != setup) { _setupBox.SetActive(setup); changed = true; }
            if (_hostBox.activeSelf != host) { _hostBox.SetActive(host); changed = true; }
            if (host) RefreshHostBox();

            string sig = Signature();
            if (sig == _shown)
            {
                if (changed) Ui.Relayout(_list, transform);
                return;
            }
            _shown = sig;
            Ui.Clear(_list);
            if (Roster.Players.Count == 0 && Net.Role == Role.Host) Roster.FromHost();

            PlayerRow(Roster.Get(0) ?? new PlayerInfo { Slot = 0, Name = PlayerNames.LocalName }, host);
            for (int i = 0; i < Lobby.MaxGuests; i++)
            {
                byte slot = (byte)(i + 1);
                byte state = SlotState(i);
                PlayerInfo p = Roster.Get(slot);
                if (state == 2 && p != null) PlayerRow(p, host);
                else if (state == 2 && Net.Role == Role.Host && Net.GuestIn(slot) != null) PlayerRow(new PlayerInfo { Slot = slot, Name = Net.GuestIn(slot).Label }, true);
                else EmptyRow(i, state, host);
            }

            if (Net.Role == Role.Host && Lobby.Kicked.Count > 0)
            {
                Ui.Separator(_list, Width);
                Ui.Label(_list, Lang.Id("removed_title"), 9, Ui.Gold, new Vector2(Width, 14f), TextAnchor.MiddleCenter);
                foreach (var kv in new List<KeyValuePair<string, string>>(Lobby.Kicked))
                {
                    string key = kv.Key;
                    RectTransform row = Ui.Panel(_list, new Vector2(Width, 24f));
                    LocalizedText name = Ui.Label(row, null, 8, Ui.Pale, new Vector2(Width - 90f, 18f), TextAnchor.MiddleLeft);
                    ((RectTransform)name.transform).anchoredPosition = new Vector2(-38f, 0f);
                    Lang.Live(name, "kicked_" + key, Ui.Fit(kv.Value, 20));
                    Button again = Ui.TextButton(row, "invite_again", () => { Lobby.InviteAgain(key); _shown = null; Rebuild(); }, 80f, 18f);
                    ((RectTransform)again.transform).anchoredPosition = new Vector2(Width / 2f - 44f, 0f);
                }
            }
            Ui.Relayout(_list, transform);
        }

        void PlayerRow(PlayerInfo p, bool host)
        {
            RectTransform row = Ui.Panel(_list, new Vector2(Width, 32f));
            Image fig = Ui.Image(row, Icons.Get("player"), new Vector2(20f, 20f));
            fig.color = Lobby.ColorOf(p.Slot);
            ((RectTransform)fig.transform).anchoredPosition = new Vector2(-Width / 2f + 16f, 0f);
            bool me = p.Slot == Roster.LocalSlot;
            Guest g = Net.Role == Role.Host ? Net.GuestIn(p.Slot) : null;
            float textW = Width - (g != null ? 86f : 40f);
            LocalizedText name = Ui.Label(row, null, 9, Lobby.ColorOf(p.Slot), new Vector2(textW, 15f), TextAnchor.MiddleLeft);
            ((RectTransform)name.transform).anchoredPosition = new Vector2(-Width / 2f + 30f + textW / 2f, 6f);
            Lang.Live(name, "lobby_name_" + p.Slot, Ui.Fit(p.Name, g != null ? 14 : 20) + (me ? " " + Lang.T("you") : ""));
            LocalizedText detail = Ui.Label(row, null, 7, Ui.Pale, new Vector2(textW, 12f), TextAnchor.MiddleLeft);
            ((RectTransform)detail.transform).anchoredPosition = new Vector2(-Width / 2f + 30f + textW / 2f, -7f);
            string info = p.Slot == 0 ? Lang.T("role_host") : p.PingMs > 0 ? Lang.T("ping", p.PingMs) : "";
            if (g != null && g.OwnRules) info += (info.Length > 0 ? " · " : "") + Lang.T("rules_own");
            Lang.Live(detail, "lobby_detail_" + p.Slot, info);
            if (g == null) return;
            byte slot = p.Slot;
            Button kick = Ui.IconButton(row, "cross", "kick", () => { Lobby.Kick(slot); _shown = null; Rebuild(); }, 20f);
            ((RectTransform)kick.transform).anchoredPosition = new Vector2(Width / 2f - 16f, 0f);
            Button rules = Ui.IconButton(row, "gear", "players_can", () => RulesWindow.Open(Net.GuestIn(slot)), 20f);
            ((RectTransform)rules.transform).anchoredPosition = new Vector2(Width / 2f - 40f, 0f);
        }

        void EmptyRow(int index, byte state, bool host)
        {
            RectTransform row = Ui.Panel(_list, new Vector2(Width, 26f));
            Image fig = Ui.Image(row, Icons.Get("player"), new Vector2(18f, 18f));
            fig.color = new Color(1f, 1f, 1f, 0.15f);
            ((RectTransform)fig.transform).anchoredPosition = new Vector2(-Width / 2f + 16f, 0f);
            LocalizedText text = Ui.Label(row, Lang.Id(state == 0 ? "slot_closed" : "slot_open"), 8, state == 0 ? new Color(0.6f, 0.55f, 0.5f) : Ui.Pale,
                new Vector2(Width - 90f, 18f), TextAnchor.MiddleLeft);
            ((RectTransform)text.transform).anchoredPosition = new Vector2(-12f, 0f);
            if (!host) return;
            int i = index;
            Button toggle = Ui.TextButton(row, state == 0 ? "open_slot" : "close_slot", () => { Lobby.ToggleSlot(i); _shown = null; Rebuild(); }, 62f, 18f);
            ((RectTransform)toggle.transform).anchoredPosition = new Vector2(Width / 2f - 36f, 0f);
        }

#if WBMP_TESTS
        public static float TestTop() => Instance != null ? Ui.WorldRect((RectTransform)Instance._hostBox.transform).yMax : 0f;

        public static void TestClickVisibility(int v)
        {
            if (Instance != null) Instance._visButtons[v].GetComponent<Button>().onClick.Invoke();
        }

        public static void TestOpen() { if (Instance != null && InSetup) Instance.OpenLobby(); }
#endif
    }
}
