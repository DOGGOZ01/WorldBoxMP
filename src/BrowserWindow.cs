using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public class BrowserWindow : SingleAutoLayoutWindow<BrowserWindow>
    {
        const float Width = 190f;
        RectTransform _list;
        LocalizedText _status;
        TextInput _code, _ip;
        bool _searching;
        float _autoRefresh;
#if WBMP_TESTS
        public static int FakeLobbies;
#endif

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
#if WBMP_TESTS
            if (FakeLobbies > 0) { Show(Fake(FakeLobbies)); return; }
#endif
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

#if WBMP_TESTS
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
#endif

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

#if WBMP_TESTS
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
#endif
    }
}
