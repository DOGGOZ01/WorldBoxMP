using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class RemoteCursor
    {
        static readonly AccessTools.FieldRef<Texture2D[]> Cursors =
            AccessTools.StaticFieldRefAccess<Texture2D[]>(AccessTools.Field(typeof(MouseCursor), "_cursors"));
        static readonly AccessTools.FieldRef<int> CurrentCursor =
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(MouseCursor), "_cur_cursor"));

        const float SendInterval = 0.05f;

        static float _sendTimer;
        static Vector2 _lastPos;
        static int _lastIndex = -1;
        static bool _lastVisible;
        static float _keepAlive;

        class Hand
        {
            public Vector2 Target, Shown;
            public int Index;
            public bool Visible, HasShown;
            public double ReceivedAt = -100;
        }
        static readonly Dictionary<byte, Hand> _hands = new Dictionary<byte, Hand>();
        static GUIStyle _label;

        public static bool HasHand(byte slot) => _hands.TryGetValue(slot, out Hand h) && h.HasShown && Clock.Now - h.ReceivedAt < 2.0;

        public static void Clear()
        {
            _hands.Clear();
            _lastIndex = -1;
        }

        public static void Update()
        {
            if (!PlayerNames.InSession || Net.Peer == null) { Clear(); return; }
            _sendTimer -= Time.unscaledDeltaTime;
            _keepAlive -= Time.unscaledDeltaTime;
            if (_sendTimer > 0f) return;
            _sendTimer = SendInterval;

            bool visible = Application.isFocused && Input.mousePresent && !ControllableUnit.isControllingUnit() &&
                           !World.world.isOverUI() && !WorldfallBridge.InFirstPerson;
            Vector2 pos = World.world.getMousePos();
            int index = CurrentCursor();
            if (TestHarness.CursorTest)
            {
                Vector3 c = Camera.main.transform.position;
                float t = Time.unscaledTime;
                pos = new Vector2(c.x + Mathf.Cos(t) * 6f, c.y + Mathf.Sin(t) * 4f);
                index = (int)(t * 4f) % 6;
                visible = true;
            }
            bool changed = visible != _lastVisible || index != _lastIndex || (visible && (pos - _lastPos).sqrMagnitude > 0.0025f);
            if (!changed && _keepAlive > 0f) return;
            _keepAlive = 0.5f;
            _lastVisible = visible;
            _lastIndex = index;
            _lastPos = pos;

            var ms = new MemoryStream(16);
            var w = new BinaryWriter(ms);
            w.Write(visible);
            w.Write(pos.x);
            w.Write(pos.y);
            w.Write((byte)Mathf.Clamp(index, 0, 255));
            byte[] payload = ms.ToArray();
            if (Net.Role == Role.Host) Net.Peer.Send(Msg.Cursor, WithSlot(0, payload));
            else Net.Peer.Send(Msg.Cursor, payload);
        }

        static byte[] WithSlot(byte slot, byte[] payload)
        {
            var b = new byte[payload.Length + 1];
            b[0] = slot;
            System.Buffer.BlockCopy(payload, 0, b, 1, payload.Length);
            return b;
        }

        static void Relay(byte from, byte[] payload)
        {
            byte[] marked = WithSlot(from, payload);
            foreach (Guest g in Net.Guests)
                if (g.Ready && g.Slot != from && !g.Peer.Closed) g.Peer.Send(Msg.Cursor, marked);
        }

        public static void Receive(byte[] data)
        {
            byte slot;
            byte[] payload;
            if (Net.Role == Role.Host)
            {
                if (Net.Current == null) return;
                slot = Net.Current.Slot;
                payload = data;
                Relay(slot, data);
            }
            else
            {
                if (data.Length < 1) return;
                slot = data[0];
                payload = new byte[data.Length - 1];
                System.Buffer.BlockCopy(data, 1, payload, 0, payload.Length);
                if (slot == Roster.LocalSlot) return;
            }
            var r = new BinaryReader(new MemoryStream(payload));
            if (!_hands.TryGetValue(slot, out Hand h)) _hands[slot] = h = new Hand();
            h.Visible = r.ReadBoolean();
            h.Target = new Vector2(r.ReadSingle(), r.ReadSingle());
            h.Index = r.ReadByte();
            h.ReceivedAt = Clock.Now;
            if (!h.HasShown) { h.Shown = h.Target; h.HasShown = true; }
        }

        public static void Draw()
        {
            if (!PlayerNames.InSession || Event.current.type != EventType.Repaint || WorldfallBridge.InFirstPerson) return;
            foreach (var kv in _hands) DrawHand(kv.Key, kv.Value);
        }

        static void DrawHand(byte slot, Hand hand)
        {
            if (!hand.Visible || !hand.HasShown || Clock.Now - hand.ReceivedAt > 1.5) return;
            PlayerInfo player = Roster.Get(slot);
            if (Net.Role == Role.Host && Net.GuestIn(slot) == null) return;
            Camera cam = Camera.main;
            Texture2D[] textures = Cursors();
            if (cam == null || textures == null || textures.Length == 0) return;
            Texture2D tex = hand.Index < textures.Length && textures[hand.Index] != null ? textures[hand.Index] : textures[0];
            if (tex == null) return;

            hand.Shown = Vector2.Lerp(hand.Shown, hand.Target, Mathf.Clamp01(Time.unscaledDeltaTime * 18f));
            if ((hand.Shown - hand.Target).sqrMagnitude > 400f) hand.Shown = hand.Target;
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(hand.Shown.x, hand.Shown.y, 0f));
            if (screen.z < 0f) return;
            float x = screen.x, y = Screen.height - screen.y;
            if (x < -64f || y < -64f || x > Screen.width + 64f || y > Screen.height + 64f) return;

            float scale = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 96f) : 1f;
            float w = tex.width * scale, h = tex.height * scale;
            Color old = GUI.color;
            Color tint = Lobby.ColorOf(slot);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(x + 2f, y + 2f, w, h), tex);
            GUI.color = tint;
            GUI.DrawTexture(new Rect(x, y, w, h), tex);
            GUI.color = old;

            string name = player?.Name;
            if (!string.IsNullOrEmpty(name) && MPMod.Cfg != null && MPMod.Cfg.ShowNames)
            {
                if (_label == null)
                    _label = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, wordWrap = false };
                var content = new GUIContent(name);
                Vector2 size = _label.CalcSize(content);
                var rect = new Rect(x + w * 0.6f, y + h * 0.8f, size.x, size.y);
                _label.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), content, _label);
                _label.normal.textColor = tint;
                GUI.Label(rect, content, _label);
            }
        }
    }
}
