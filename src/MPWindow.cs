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
        enum View { Offline, Connecting, Hosting, Client }

        LocalizedText _state, _detail;
        Image _stateIcon;
        GameObject _offline, _connecting, _hosting, _client;
        LocalizedText _offList;
        string _offShown;
        TextInput _ip;
        View _shown = (View)(-1);
        float _timer;

        public static void Open()
        {
            if (Instance == null) CreateWindow("wbmp_window", Lang.Id("tab"));
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
            Ui.Button(_offline.transform, "host", "host", true, () => MPMod.Instance.Host(), Width);
            Ui.Button(_offline.transform, "on", "perm_title", false, RulesWindow.Open, Width);
            Ui.Button(_offline.transform, "key", "unlock_all", false, HostOnly.UnlockEverything, Width);
            Ui.Hint(_offline.transform, "hint_host", Width);
            Ui.Separator(_offline.transform, Width);
            Ui.Label(_offline.transform, Lang.Id("join_ip"), 9, Ui.Gold, new Vector2(Width, 14f), TextAnchor.MiddleCenter);
            _ip = TextInput.Instantiate(_offline.transform, pName: "wbmp_ip");
            _ip.Setup(MPMod.Cfg.HostIp + ":" + MPMod.Cfg.Port, SetAddress);
            _ip.SetSize(new Vector2(Width - 10f, 18f));
            _ip.input.textComponent.fontSize = 9;
            _ip.input.textComponent.resizeTextForBestFit = false;
            _ip.input.textComponent.alignment = TextAnchor.MiddleLeft;
            Ui.Button(_offline.transform, "join", "join", false, Join, Width);
            Ui.Hint(_offline.transform, "hint_steam_join", Width);

            _connecting = Ui.Group(transform, Width);
            Ui.Button(_connecting.transform, "leave", "disconnect", false, () => MPMod.Instance.StopSession(), Width);

            _hosting = Ui.Group(transform, Width);
            Ui.Button(_hosting.transform, "invite", "invite", true, SteamSession.Invite, Width);
            Ui.Hint(_hosting.transform, "hint_invite", Width);
            Ui.Button(_hosting.transform, "on", "perm_title", false, RulesWindow.Open, Width);
            Ui.Button(_hosting.transform, "key", "unlock_all", false, HostOnly.UnlockEverything, Width);
            Ui.Button(_hosting.transform, "reload", "resync", false, () => MPMod.Instance.Resync(), Width);
            Ui.Button(_hosting.transform, "leave", "stop_host", false, () => MPMod.Instance.StopSession(), Width);

            _client = Ui.Group(transform, Width);
            Ui.Button(_client.transform, "reload", "resync", false, () => MPMod.Instance.Resync(), Width);
            Ui.Button(_client.transform, "leave", "disconnect", false, () => MPMod.Instance.StopSession(), Width);
            _offList = Ui.Hint(_client.transform, "live_empty", Width);
            _offList.GetComponent<Text>().color = Ui.Gold;

            Refresh(true);
        }

        public override void OnNormalEnable()
        {
            if (_ip != null) _ip.input.text = MPMod.Cfg.HostIp + ":" + MPMod.Cfg.Port;
            Refresh(true);
        }

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
            View view = Net.IsHostWithPeer || Net.Role == Role.Host ? View.Hosting
                : Net.IsClientConnected && ClientConnectedAndLoaded() ? View.Client
                : Net.Role == Role.Client ? View.Connecting
                : View.Offline;
            if (view != _shown || force)
            {
                _shown = view;
                _offline.SetActive(view == View.Offline);
                _connecting.SetActive(view == View.Connecting);
                _hosting.SetActive(view == View.Hosting);
                _client.SetActive(view == View.Client);
                _stateIcon.sprite = Icons.Get(view == View.Hosting ? "host" : view == View.Offline ? "tab" : "join");
            }
            if (view == View.Client)
            {
                string off = Rules.OffList();
                Lang.Live(_offList, "window_off", off);
                if (off != _offShown)
                {
                    _offShown = off;
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_offList.transform);
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_client.transform);
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
                }
            }
            Lang.Live(_state, "window_state", SessionStatus.State());
            string detail = SessionStatus.Detail();
            Lang.Live(_detail, "window_detail", detail);
            ((RectTransform)_state.transform).anchoredPosition = new Vector2(4f, detail.Length > 0 ? 6f : 0f);
        }

        static bool ClientConnectedAndLoaded() => !SmoothLoader.isLoading();

        static void SetAddress(string text)
        {
            string ip = (text ?? "").Trim();
            int port = MPMod.Cfg.Port;
            int colon = ip.LastIndexOf(':');
            if (colon > 0 && ip.IndexOf(':') == colon && int.TryParse(ip.Substring(colon + 1), out int p) && p > 0 && p < 65536)
            {
                port = p;
                ip = ip.Substring(0, colon);
            }
            if (ip.Length == 0) return;
            MPMod.SaveAddress(ip, port);
        }

        void Join()
        {
            SetAddress(_ip.input.text);
            MPMod.Instance.Join();
        }
    }

    public class RulesWindow : SingleAutoLayoutWindow<RulesWindow>
    {
        const float Width = 190f;
        readonly List<KeyValuePair<Perm, Image>> _toggles = new List<KeyValuePair<Perm, Image>>();

        public static void Open()
        {
            if (Instance == null) CreateWindow("wbmp_rules", Lang.Id("perm_title"));
            if (ScrollWindow.isAnimationActive()) ScrollWindow.finishAnimations();
            ScrollWindow.showWindow("wbmp_rules");
        }

        protected override void Init()
        {
            layout.spacing = 4f;
            layout.padding = new RectOffset(0, 0, 8, 10);
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

        void Refresh()
        {
            foreach (var t in _toggles) t.Value.sprite = Icons.Get(Rules.HostAllows(t.Key) ? "on" : "off");
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
            for (int i = _grid.childCount - 1; i >= 0; i--) Destroy(_grid.GetChild(i).gameObject);
            List<GodPower> powers = Rules.PowersOf(_perm);
            int allowed = 0;
            foreach (GodPower gp in powers)
            {
                bool on = !Rules.HostDenied.Contains(gp.id);
                if (on) allowed++;
                if (_filter == Filter.Allowed && !on || _filter == Filter.Denied && on) continue;
                GodPower power = gp;
                Ui.PowerCell(_grid, power, on, () => { Rules.TogglePower(power.id); Rebuild(); }, Cell);
            }
            Lang.Live(_count, "power_count", Lang.T("allowed_count", allowed, powers.Count) + (Rules.HostAllows(_perm) ? "" : " · " + Lang.T("tip_denied", Rules.Name(_perm))));
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_grid);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
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

        static Font GameFont()
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
