using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBoxMP
{
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
}
