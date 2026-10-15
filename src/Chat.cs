using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WorldBoxMP
{
    public static class Chat
    {
        public class Line
        {
            public byte Slot;
            public string Name, Text;
            public float At;
        }

        public static readonly List<Line> Lines = new List<Line>();
        const int Keep = 40, ShownClosed = 6, ShownOpen = 12;
        const float Visible = 12f, Fade = 2f;
        const int MaxLength = 160;

        static GameObject _root;
        static Text _log;
        static Image _logBg;
        static InputField _input;
        static bool _open;
        static int _openFrames;
        static string _shown;
        static readonly Dictionary<byte, Queue<float>> _recent = new Dictionary<byte, Queue<float>>();

        public static bool Typing => _open;

        public static void Clear()
        {
            Lines.Clear();
            _recent.Clear();
            if (_open) Close();
            _shown = null;
            if (_log != null) _log.text = "";
            if (_logBg != null) _logBg.enabled = false;
        }

        public static void Update()
        {
            bool session = Net.Role != Role.None;
            if (!session)
            {
                if (Lines.Count > 0 || _open) Clear();
                return;
            }
            if (!_open && Input.GetKeyDown(KeyCode.T) && CanOpen()) Open();
            else if (_open && Input.GetKeyDown(KeyCode.Escape)) Close();
            if (_open && _openFrames > 0)
            {
                _openFrames--;
                _input.text = "";
            }
            Redraw();
        }

        static bool CanOpen()
        {
            if (ControllableUnit.isControllingUnit()) return false;
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField other = selected != null ? selected.GetComponent<InputField>() : null;
            return other == null || !other.isFocused;
        }

        static void Open()
        {
            Build();
            _open = true;
            _openFrames = 2;
            _input.gameObject.SetActive(true);
            _input.text = "";
            _input.Select();
            _input.ActivateInputField();
            _shown = null;
        }

        static void Close()
        {
            _open = false;
            if (_input == null) return;
            _input.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            _input.gameObject.SetActive(false);
            _shown = null;
        }

        static void OnEndEdit(string text)
        {
            if (!_open) return;
            bool enter = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (enter) Send(text);
            Close();
        }

        public static void Send(string text)
        {
            text = Clean(text);
            if (text.Length == 0 || Net.Role == Role.None) return;
            if (Net.Role == Role.Host)
            {
                Broadcast(0, PlayerNames.LocalName, text);
                return;
            }
            if (!Net.IsClientConnected) return;
            var ms = new MemoryStream();
            new BinaryWriter(ms).Write(text);
            Net.Peer.Send(Msg.Chat, ms.ToArray());
        }

        static string Clean(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length > MaxLength ? text.Substring(0, MaxLength) : text;
        }

        public static void HostReceive(byte[] data)
        {
            Guest g = Net.Current;
            if (g == null) return;
            string text;
            try { text = Clean(new BinaryReader(new MemoryStream(data)).ReadString()); }
            catch { return; }
            if (text.Length == 0) return;
            if (!_recent.TryGetValue(g.Slot, out Queue<float> q)) _recent[g.Slot] = q = new Queue<float>();
            while (q.Count > 0 && Time.unscaledTime - q.Peek() > 5f) q.Dequeue();
            if (q.Count >= 6) return;
            q.Enqueue(Time.unscaledTime);
            Broadcast(g.Slot, g.Label, text);
        }

        static void Broadcast(byte slot, string name, string text)
        {
            Add(slot, name, text);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(slot);
            w.Write(name ?? "");
            w.Write(text);
            byte[] bytes = ms.ToArray();
            foreach (Guest g in Net.Guests) if (g.Ready && !g.Peer.Closed) g.Peer.Send(Msg.Chat, bytes);
        }

        public static void ClientReceive(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                byte slot = r.ReadByte();
                string name = r.ReadString();
                Add(slot, name, Clean(r.ReadString()));
            }
            catch { }
        }

        static void Add(byte slot, string name, string text)
        {
            Lines.Add(new Line { Slot = slot, Name = name, Text = text, At = Time.unscaledTime });
            if (Lines.Count > Keep) Lines.RemoveAt(0);
            SessionLog.Write("chat " + name + ": " + text);
            _shown = null;
        }

        static void Redraw()
        {
            if (Lines.Count == 0 && !_open)
            {
                if (_log != null && _log.text.Length > 0) { _log.text = ""; _logBg.enabled = false; }
                return;
            }
            Build();
            var sb = new StringBuilder();
            int first = Mathf.Max(0, Lines.Count - (_open ? ShownOpen : ShownClosed));
            bool any = false;
            for (int i = first; i < Lines.Count; i++)
            {
                Line l = Lines[i];
                float age = Time.unscaledTime - l.At;
                float alpha = _open ? 1f : Mathf.Clamp01((Visible + Fade - age) / Fade);
                if (alpha <= 0f) continue;
                any = true;
                Color c = Lobby.ColorOf(l.Slot);
                string a = ((int)(alpha * 255)).ToString("X2");
                if (sb.Length > 0) sb.Append('\n');
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(c)).Append(a).Append("><b>").Append(Escape(l.Name)).Append("</b></color>")
                  .Append("<color=#FFFFFF").Append(a).Append(">: ").Append(Escape(l.Text)).Append("</color>");
            }
            string text = sb.ToString();
            if (text == _shown) return;
            _shown = text;
            _log.text = text;
            _logBg.enabled = _open || any;
            Color bg = _logBg.color;
            bg.a = _open ? 0.55f : 0.3f;
            _logBg.color = bg;
            if (!any && !_open) _logBg.enabled = false;
        }

        static string Escape(string s) => (s ?? "").Replace("<", "<​");

        static void Build()
        {
            if (_root != null) return;
            _root = new GameObject("wbmp_chat", typeof(RectTransform));
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();

            var logGo = new GameObject("log", typeof(RectTransform));
            logGo.transform.SetParent(_root.transform, false);
            var logRect = (RectTransform)logGo.transform;
            logRect.anchorMin = logRect.anchorMax = new Vector2(0f, 0f);
            logRect.pivot = new Vector2(0f, 0f);
            logRect.anchoredPosition = new Vector2(10f, 150f);
            logRect.sizeDelta = new Vector2(380f, 10f);
            _logBg = logGo.AddComponent<Image>();
            _logBg.color = new Color(0f, 0f, 0f, 0.3f);
            _logBg.raycastTarget = false;
            _logBg.enabled = false;
            var fit = logGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var pad = logGo.AddComponent<VerticalLayoutGroup>();
            pad.padding = new RectOffset(6, 6, 4, 4);
            pad.childControlHeight = true;
            pad.childControlWidth = true;

            var textGo = new GameObject("text", typeof(RectTransform));
            textGo.transform.SetParent(logGo.transform, false);
            _log = textGo.AddComponent<Text>();
            _log.font = Ui.GameFont();
            _log.fontSize = 14;
            _log.supportRichText = true;
            _log.color = Color.white;
            _log.alignment = TextAnchor.LowerLeft;
            _log.horizontalOverflow = HorizontalWrapMode.Wrap;
            _log.verticalOverflow = VerticalWrapMode.Overflow;
            _log.raycastTarget = false;
            textGo.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

            var inGo = new GameObject("input", typeof(RectTransform));
            inGo.transform.SetParent(_root.transform, false);
            var inRect = (RectTransform)inGo.transform;
            inRect.anchorMin = inRect.anchorMax = new Vector2(0f, 0f);
            inRect.pivot = new Vector2(0f, 0f);
            inRect.anchoredPosition = new Vector2(10f, 120f);
            inRect.sizeDelta = new Vector2(380f, 26f);
            var inBg = inGo.AddComponent<Image>();
            inBg.color = new Color(0f, 0f, 0f, 0.7f);

            Text MakeText(string name, Color color, FontStyle style)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(inGo.transform, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = new Vector2(8f, 2f);
                r.offsetMax = new Vector2(-8f, -2f);
                var t = go.AddComponent<Text>();
                t.font = Ui.GameFont();
                t.fontSize = 14;
                t.fontStyle = style;
                t.color = color;
                t.alignment = TextAnchor.MiddleLeft;
                t.supportRichText = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                return t;
            }
            Text placeholder = MakeText("placeholder", new Color(1f, 1f, 1f, 0.4f), FontStyle.Italic);
            placeholder.text = Lang.T("chat_hint");
            Text typed = MakeText("text", Color.white, FontStyle.Normal);
            _input = inGo.AddComponent<InputField>();
            _input.textComponent = typed;
            _input.placeholder = placeholder;
            _input.characterLimit = MaxLength;
            _input.lineType = InputField.LineType.SingleLine;
            _input.targetGraphic = inBg;
            _input.onEndEdit.AddListener(OnEndEdit);
            inGo.SetActive(false);
        }
    }
}
