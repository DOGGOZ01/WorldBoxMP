using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General.UI.Prefabs;
using NeoModLoader.General.UI.Window;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WorldBoxMP
{
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
