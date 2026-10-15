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

    public class BrowserWindow : SingleAutoLayoutWindow<BrowserWindow>
    {
        const float Width = 190f;
        RectTransform _list;
        LocalizedText _status;
        TextInput _code, _ip;
        bool _searching;
        float _autoRefresh;
        public static int FakeLobbies;

        public static void Open()
        {
            if (Instance == null) CreateWindow("wbmp_browser", Lang.Id("browser"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_browser");
            Instance.Search();
        }

        protected override void Init()
        {
            layout.spacing = 4f;
            layout.padding = new RectOffset(0, 0, 8, 10);

            Ui.Label(transform, Lang.Id("join_code"), 8, Ui.Gold, new Vector2(Width, 12f), TextAnchor.MiddleCenter);
            RectTransform codeRow = Ui.Row(transform, Width, 20f);
            _code = Ui.Input(codeRow, "", Width - 66f, null);
            Ui.TextButton(codeRow, "join", () => SteamSession.JoinByCode(_code.input.text, ""), 58f, 18f);

            Ui.Label(transform, Lang.Id("join_ip"), 8, Ui.Gold, new Vector2(Width, 12f), TextAnchor.MiddleCenter);
            RectTransform ipRow = Ui.Row(transform, Width, 20f);
            _ip = Ui.Input(ipRow, MPMod.Cfg.HostIp + ":" + MPMod.Cfg.Port, Width - 66f, null);
            Ui.TextButton(ipRow, "join", JoinIp, 58f, 18f);

            Ui.Separator(transform, Width);
            RectTransform top = Ui.Row(transform, Width, 20f);
            Ui.TextButton(top, "refresh", Search, 70f, 18f);
            _status = Ui.Label(top, null, 8, Ui.Pale, new Vector2(Width - 76f, 18f), TextAnchor.MiddleLeft);

            _list = Ui.Group(transform, Width).GetComponent<RectTransform>();
            _list.GetComponent<VerticalLayoutGroup>().spacing = 3f;
        }

        public override void OnNormalEnable()
        {
            if (_ip != null) _ip.input.text = MPMod.Cfg.HostIp + ":" + MPMod.Cfg.Port;
        }

        void Update()
        {
            if (!IsOpened || _searching) return;
            _autoRefresh -= Time.unscaledDeltaTime;
            if (_autoRefresh <= 0f) Search();
        }

        void Search()
        {
            _autoRefresh = 30f;
            if (FakeLobbies > 0) { Show(Fake(FakeLobbies)); return; }
            if (!SteamSession.Available)
            {
                Lang.Live(_status, "browser_status", Lang.T("tip_no_steam"));
                return;
            }
            if (_searching) return;
            _searching = true;
            Lang.Live(_status, "browser_status", Lang.T("searching"));
            SteamSession.Search(Show);
        }

        static List<LobbyEntry> Fake(int n)
        {
            var list = new List<LobbyEntry>();
            string[] sizes = { "standard", "large", "huge", "titanic", "small" };
            for (int i = 0; i < n; i++)
                list.Add(new LobbyEntry
                {
                    Id = (ulong)(i + 1), Host = i % 4 == 0 ? "AVeryLongSteamNicknameNumber" + i : "Player " + i, Players = 1 + i % 4, Slots = 4,
                    Locked = i % 3 == 1, Mods = i % 5 == 2, SameVersion = i % 7 != 6, Friend = i < 2, Size = sizes[i % sizes.Length], Year = 10 + i * 37,
                });
            return list;
        }

        void Show(List<LobbyEntry> found)
        {
            _searching = false;
            if (_list == null) return;
            Ui.Clear(_list);
            Lang.Live(_status, "browser_status", found.Count == 0 ? Lang.T("no_lobbies") : found.Count.ToString());
            for (int i = 0; i < found.Count && i < 30; i++) Row(found[i], i);
            Ui.Relayout(_list, transform);
        }

        void Row(LobbyEntry e, int index)
        {
            bool full = e.Players >= e.Slots;
            bool canJoin = e.SameVersion && !full;
            RectTransform row = Ui.Panel(_list, new Vector2(Width, 34f));
            const float textW = Width - 70f;
            float left = -Width / 2f + 6f;
            int marks = (e.Locked ? 1 : 0) + (e.Mods ? 1 : 0);
            float nameW = textW - marks * 14f;
            LocalizedText name = Ui.Label(row, null, 9, e.Friend ? Ui.Gold : Color.white, new Vector2(nameW, 15f), TextAnchor.MiddleLeft);
            ((RectTransform)name.transform).anchoredPosition = new Vector2(left + nameW / 2f, 7f);
            Lang.Live(name, "browser_name_" + index, Ui.Fit(e.Host, 18));
            float x = left + nameW + 7f;
            if (e.Locked) { Ui.Mark(row, "lock", "has_password", new Vector2(x, 7f)); x += 14f; }
            if (e.Mods) Ui.Mark(row, "mods", "other_mods", new Vector2(x, 7f));
            LocalizedText detail = Ui.Label(row, null, 7, e.SameVersion ? Ui.Pale : new Color(1f, 0.5f, 0.45f), new Vector2(textW, 12f), TextAnchor.MiddleLeft);
            ((RectTransform)detail.transform).anchoredPosition = new Vector2(left + textW / 2f, -8f);
            Lang.Live(detail, "browser_detail_" + index, e.SameVersion
                ? Lang.T("lobby_line", e.Players, e.Slots, SizeName(e.Size), e.Year >= 0 ? e.Year.ToString() : "?")
                : Lang.T("other_version"));
            ulong id = e.Id;
            if (canJoin)
            {
                Button join = Ui.TextButton(row, "join", () => SteamSession.Join(id, ""), 58f, 22f);
                ((RectTransform)join.transform).anchoredPosition = new Vector2(Width / 2f - 33f, 0f);
            }
            else if (full && e.SameVersion)
            {
                LocalizedText none = Ui.Label(row, Lang.Id("lobby_full"), 7, new Color(1f, 0.6f, 0.45f), new Vector2(58f, 22f), TextAnchor.MiddleCenter);
                ((RectTransform)none.transform).anchoredPosition = new Vector2(Width / 2f - 33f, 0f);
            }
        }

        static string SizeName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            string key = "map_size_" + id;
            return LocalizedTextManager.stringExists(key) ? LocalizedTextManager.getText(key) : id;
        }

        void JoinIp()
        {
            string ip = (_ip.input.text ?? "").Trim();
            int port = MPMod.Cfg.Port;
            int colon = ip.LastIndexOf(':');
            if (colon > 0 && ip.IndexOf(':') == colon && int.TryParse(ip.Substring(colon + 1), out int p) && p > 0 && p < 65536)
            {
                port = p;
                ip = ip.Substring(0, colon);
            }
            if (ip.Length == 0) return;
            MPMod.SaveAddress(ip, port);
            Net.JoinPassword = "";
            MPMod.Instance.Join();
        }

        public static bool TestLayout(out float fieldsBottom, out float listTop, out int rows)
        {
            fieldsBottom = listTop = 0f;
            rows = 0;
            if (Instance == null || Instance._list == null) return false;
            fieldsBottom = Ui.WorldRect((RectTransform)Instance._ip.transform).yMin;
            rows = Instance._list.childCount;
            if (rows > 0) listTop = Ui.WorldRect((RectTransform)Instance._list.GetChild(0)).yMax;
            return true;
        }

        public static void TestScroll(float normalized)
        {
            ScrollRect sr = Instance != null ? Instance.GetComponentInParent<ScrollRect>() : null;
            if (sr != null) sr.verticalNormalizedPosition = normalized;
        }
    }

    public class RulesWindow : SingleAutoLayoutWindow<RulesWindow>
    {
        const float Width = 190f;
        readonly List<KeyValuePair<Perm, Image>> _toggles = new List<KeyValuePair<Perm, Image>>();
        LocalizedText _who;
        GameObject _shared;

        public static void Open(Guest g)
        {
            Rules.Editing = g;
            if (Instance == null) CreateWindow("wbmp_rules", Lang.Id("players_can"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_rules");
            Instance.Refresh();
        }

        protected override void Init()
        {
            layout.spacing = 4f;
            layout.padding = new RectOffset(0, 0, 8, 10);
            _who = Ui.Label(transform, null, 9, Ui.Gold, new Vector2(Width, 14f), TextAnchor.MiddleCenter);
            _shared = Ui.Button(transform, "tab", "rules_shared", false, () => { Rules.UseShared(Rules.Editing); Refresh(); }, Width).gameObject;
            foreach (Perm p in Rules.List)
            {
                Perm perm = p;
                Image box = Ui.Toggle(transform, Rules.Key(perm), () => { Rules.Toggle(perm); Refresh(); }, Width);
                _toggles.Add(new KeyValuePair<Perm, Image>(perm, box));
                if (Rules.HasPowerList(perm))
                {
                    Button gear = Ui.IconButton(box.transform.parent, "gear", "perm_settings", () => PowerRulesWindow.Open(perm), 18f);
                    ((RectTransform)gear.transform).anchoredPosition = new Vector2((Width - 10f) / 2f - 12f, 0f);
                }
            }
            Refresh();
        }

        public override void OnNormalEnable() => Refresh();

        public void Refresh()
        {
            Guest g = Rules.Editing;
            Lang.Live(_who, "rules_who", g == null ? Lang.T("players_can") : Lang.T("rules_for", g.Label));
            _shared.SetActive(g != null && g.OwnRules);
            foreach (var t in _toggles) t.Value.sprite = Icons.Get(Rules.TargetAllows(t.Key) ? "on" : "off");
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }
    }

    public class PowerRulesWindow : SingleAutoLayoutWindow<PowerRulesWindow>
    {
        const float Width = 190f;
        const float Cell = 28f;
        enum Filter { All, Allowed, Denied }

        static Perm _perm = Perm.Creatures;
        Filter _filter = Filter.All;
        LocalizedText _count;
        RectTransform _grid;
        readonly Dictionary<Filter, Image> _filterButtons = new Dictionary<Filter, Image>();

        public static void Open(Perm perm)
        {
            _perm = perm;
            if (Instance == null) CreateWindow("wbmp_power_rules", Lang.Id(Rules.Key(perm)));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_power_rules");
            Instance.Rebuild();
        }

        protected override void Init()
        {
            layout.spacing = 5f;
            layout.padding = new RectOffset(0, 0, 8, 10);
            _count = Ui.Label(transform, null, 9, Ui.Gold, new Vector2(Width, 14f), TextAnchor.MiddleCenter);

            RectTransform row = Ui.Row(transform, Width, 24f);
            foreach (Button half in new[]
            {
                Ui.Button(row, "on", "allow_all", false, () => { Rules.SetAll(_perm, true); Rebuild(); }, Width / 2f - 2f),
                Ui.Button(row, "cross", "deny_all", false, () => { Rules.SetAll(_perm, false); Rebuild(); }, Width / 2f - 2f),
            })
            {
                Text label = half.GetComponentInChildren<Text>();
                label.fontSize = 7;
                label.resizeTextMaxSize = 7;
            }

            RectTransform filters = Ui.Row(transform, Width, 18f);
            foreach (Filter f in new[] { Filter.All, Filter.Allowed, Filter.Denied })
            {
                Filter filter = f;
                string key = f == Filter.All ? "filter_all" : f == Filter.Allowed ? "filter_allowed" : "filter_denied";
                Button b = Ui.TextButton(filters, key, () => { _filter = filter; Rebuild(); }, Width / 3f - 3f, 18f);
                _filterButtons[f] = b.GetComponent<Image>();
            }

            var go = new GameObject("grid", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _grid = (RectTransform)go.transform;
            _grid.sizeDelta = new Vector2(Width, Cell);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Cell, Cell);
            grid.spacing = new Vector2(3f, 3f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;
            grid.childAlignment = TextAnchor.UpperCenter;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public override void OnNormalEnable() => Rebuild();

        void Rebuild()
        {
            if (_grid == null) return;
            ScrollWindowComponent.titleText.GetComponent<LocalizedText>()?.setKeyAndUpdate(Lang.Id(Rules.Key(_perm)));
            foreach (var kv in _filterButtons)
                kv.Value.sprite = SpriteTextureLoader.getSprite(kv.Key == _filter ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray");
            Ui.Clear(_grid);
            List<GodPower> powers = Rules.PowersOf(_perm);
            int allowed = 0;
            foreach (GodPower gp in powers)
            {
                bool on = !Rules.TargetDenies(gp.id);
                if (on) allowed++;
                if (_filter == Filter.Allowed && !on || _filter == Filter.Denied && on) continue;
                GodPower power = gp;
                Ui.PowerCell(_grid, power, on, () => { Rules.TogglePower(power.id); Rebuild(); }, Cell);
            }
            Lang.Live(_count, "power_count", Lang.T("allowed_count", allowed, powers.Count) + (Rules.TargetAllows(_perm) ? "" : " · " + Lang.T("tip_denied", Rules.Name(_perm))));
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_grid);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
        }
    }

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

        public static float TestTop() => Instance != null ? Ui.WorldRect((RectTransform)Instance._hostBox.transform).yMax : 0f;

        public static void TestClickVisibility(int v)
        {
            if (Instance != null) Instance._visButtons[v].GetComponent<Button>().onClick.Invoke();
        }

        public static void TestOpen() { if (Instance != null && InSetup) Instance.OpenLobby(); }
    }

    public class PasswordWindow : SingleAutoLayoutWindow<PasswordWindow>
    {
        const float Width = 190f;
        LocalizedText _what;
        TextInput _input;
        static bool _wrong;
        public static bool Pending;

        public static void Open(bool wrong)
        {
            _wrong = wrong;
            Pending = true;
            if (Instance == null) CreateWindow("wbmp_password", Lang.Id("password"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_password");
            Instance.Refresh();
        }

        protected override void Init()
        {
            layout.spacing = 6f;
            layout.padding = new RectOffset(0, 0, 10, 12);
            Image lockIcon = Ui.Image(transform, Icons.Get("lock"), new Vector2(28f, 28f));
            _what = Ui.Label(transform, null, 9, Ui.Gold, new Vector2(Width, 16f), TextAnchor.MiddleCenter);
            _input = Ui.Input(transform, "", Width - 10f, null);
            _input.input.contentType = InputField.ContentType.Password;
            _input.input.onEndEdit.AddListener(_ => { if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)) Submit(); });
            Ui.Button(transform, "join", "join", true, Submit, Width);
        }

        public override void OnNormalEnable() => Refresh();

        void Refresh()
        {
            if (_input == null) return;
            Lang.Live(_what, "password_what", Lang.T(_wrong ? "wrong_password" : "need_password"));
            _input.input.text = "";
            _input.input.Select();
            _input.input.ActivateInputField();
        }

        void Submit() => Enter(_input.input.text);

        public static void Enter(string password)
        {
            Pending = false;
            ScrollWindow.hideAllEvent(false);
            MPMod.Instance.Rejoin(password ?? "");
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

    public static class Ui
    {
        public static readonly Color Gold = new Color(1f, 0.82f, 0.36f);
        public static readonly Color Pale = new Color(0.86f, 0.84f, 0.78f);
        static Font _font;

        internal static Font GameFont()
        {
            if (_font == null)
            {
                Text sample = Resources.FindObjectsOfTypeAll<Text>().FirstOrDefault(t => t.font != null && t.gameObject.scene.IsValid());
                _font = sample != null ? sample.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            return LocalizedTextManager.current_font != null ? LocalizedTextManager.current_font : _font;
        }

        static RectTransform Child(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return rect;
        }

        public static GameObject Group(Transform parent, float width)
        {
            RectTransform rect = Child(parent, "group", new Vector2(width, 10f));
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            rect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect.gameObject;
        }

        public static Image Image(Transform parent, Sprite sprite, Vector2 size)
        {
            RectTransform rect = Child(parent, "image", size);
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        public static RectTransform Panel(Transform parent, Vector2 size)
        {
            RectTransform rect = Child(parent, "panel", size);
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            img.type = UnityEngine.UI.Image.Type.Sliced;
            img.raycastTarget = false;
            return rect;
        }

        public static void Separator(Transform parent, float width)
        {
            Image line = Image(parent, SpriteTextureLoader.getSprite("ui/special/separation_element_top"), new Vector2(width - 20f, 8f));
            line.type = UnityEngine.UI.Image.Type.Sliced;
        }

        public static LocalizedText Label(Transform parent, string key, int fontSize, Color color, Vector2 size, TextAnchor align)
        {
            RectTransform rect = Child(parent, "label", size);
            rect.gameObject.SetActive(false);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = GameFont();
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(5, fontSize - 3);
            text.resizeTextMaxSize = fontSize;
            text.raycastTarget = false;
            rect.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            var loc = rect.gameObject.AddComponent<LocalizedText>();
            loc.key = key ?? Lang.Id("live_empty");
            loc.autoField = true;
            rect.gameObject.SetActive(true);
            return loc;
        }

        public static LocalizedText Hint(Transform parent, string key, float width)
        {
            LocalizedText label = Label(parent, Lang.Id(key), 8, Pale, new Vector2(width - 8f, 12f), TextAnchor.UpperCenter);
            Text text = label.GetComponent<Text>();
            text.resizeTextForBestFit = false;
            label.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return label;
        }

        public static RectTransform Row(Transform parent, float width, float height)
        {
            RectTransform rect = Child(parent, "row", new Vector2(width, height));
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return rect;
        }

        public static Button TextButton(Transform parent, string key, UnityAction action, float width, float height)
        {
            RectTransform rect = Child(parent, "button_" + key, new Vector2(width, height));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/special_buttonGray");
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(action);
            rect.gameObject.AddComponent<ButtonSfx>();
            Label(rect, Lang.Id(key), 8, Color.white, new Vector2(width - 4f, height - 2f), TextAnchor.MiddleCenter);
            return button;
        }

        public static Button PowerCell(Transform parent, GodPower power, bool on, UnityAction action, float size)
        {
            RectTransform rect = Child(parent, "power_" + power.id, new Vector2(size, size));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/button2");
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            if (!on) bg.color = new Color(0.55f, 0.45f, 0.45f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(action);
            rect.gameObject.AddComponent<ButtonSfx>();
            var tip = rect.gameObject.AddComponent<TipButton>();
            tip.textOnClick = power.getLocaleID();
            tip.textOnClickDescription = null;
            Image icon = Image(rect, Rules.IconOf(power), new Vector2(size - 6f, size - 6f));
            if (!on)
            {
                icon.color = new Color(1f, 1f, 1f, 0.4f);
                Image(rect, Icons.Get("cross"), new Vector2(size - 4f, size - 4f));
            }
            return button;
        }

        public static Image Toggle(Transform parent, string key, UnityAction action, float width)
        {
            RectTransform rect = Child(parent, "toggle_" + key, new Vector2(width - 10f, 20f));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(action);
            rect.gameObject.AddComponent<ButtonSfx>();
            Image box = Image(rect, Icons.Get("on"), new Vector2(16f, 16f));
            ((RectTransform)box.transform).anchoredPosition = new Vector2(-(width - 10f) / 2f + 13f, 0f);
            LocalizedText label = Label(rect, Lang.Id(key), 8, Color.white, new Vector2(width - 66f, 18f), TextAnchor.MiddleLeft);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(1f, 0f);
            return box;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                child.gameObject.SetActive(false);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
        }

        public static void Relayout(RectTransform list, Transform window)
        {
            ScrollRect sr = window.GetComponentInParent<ScrollRect>();
            Vector2 keep = sr != null ? sr.content.anchoredPosition : Vector2.zero;
            Canvas.ForceUpdateCanvases();
            if (list != null) LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)window);
            if (sr != null)
            {
                sr.StopMovement();
                sr.content.anchoredPosition = keep;
            }
        }

        public static string Fit(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
        }

        public static Rect WorldRect(RectTransform r)
        {
            var c = new Vector3[4];
            r.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        public static TextInput Input(Transform parent, string text, float width, System.Action<string> onChange)
        {
            TextInput input = TextInput.Instantiate(parent, pName: "wbmp_input");
            input.Setup(text ?? "", s => onChange?.Invoke(s));
            input.SetSize(new Vector2(width, 18f));
            input.input.textComponent.fontSize = 9;
            input.input.textComponent.resizeTextForBestFit = false;
            input.input.textComponent.alignment = TextAnchor.MiddleLeft;
            input.input.characterLimit = 40;
            return input;
        }

        public static void Mark(Transform parent, string icon, string key, Vector2 at)
        {
            RectTransform rect = Child(parent, "mark_" + icon, new Vector2(12f, 12f));
            rect.anchoredPosition = at;
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = Icons.Get(icon);
            var tip = rect.gameObject.AddComponent<TipButton>();
            tip.textOnClick = Lang.Id(key);
            tip.textOnClickDescription = null;
        }

        public static Button IconButton(Transform parent, string icon, string key, UnityAction action, float size)
        {
            RectTransform rect = Child(parent, "button_" + key, new Vector2(size, size));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite("ui/special/button2");
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(action);
            rect.gameObject.AddComponent<ButtonSfx>();
            var tip = rect.gameObject.AddComponent<TipButton>();
            tip.textOnClick = Lang.Id(key);
            tip.textOnClickDescription = Lang.Has(key + "_description") ? Lang.Id(key + "_description") : null;
            Image ic = Image(rect, Icons.Get(icon), new Vector2(size - 6f, size - 6f));
            return button;
        }

        public static Button Button(Transform parent, string icon, string key, bool main, UnityAction action, float width)
        {
            RectTransform rect = Child(parent, "button_" + key, new Vector2(width, 26f));
            var bg = rect.gameObject.AddComponent<Image>();
            bg.sprite = SpriteTextureLoader.getSprite(main ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray");
            bg.type = UnityEngine.UI.Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            button.colors = colors;
            button.onClick.AddListener(action);
            rect.gameObject.AddComponent<ButtonSfx>();

            Image ic = Image(rect, Icons.Get(icon), new Vector2(18f, 18f));
            ((RectTransform)ic.transform).anchoredPosition = new Vector2(-width / 2f + 15f, 0f);
            LocalizedText label = Label(rect, Lang.Id(key), 9, Color.white, new Vector2(width - 34f, 22f), TextAnchor.MiddleCenter);
            ((RectTransform)label.transform).anchoredPosition = new Vector2(10f, 0f);
            return button;
        }
    }
}
