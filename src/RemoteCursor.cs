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
        static readonly Color HostColor = new Color(1f, 0.66f, 0.2f);
        static readonly Color ClientColor = new Color(0.45f, 0.85f, 1f);

        static float _sendTimer;
        static Vector2 _lastPos;
        static int _lastIndex = -1;
        static bool _lastVisible;
        static float _keepAlive;

        static Vector2 _target, _shown;
        static int _index;
        static bool _visible;
        static double _receivedAt = -100;
        static bool _hasShown;
        static GUIStyle _label;

        public static void Clear()
        {
            _visible = false;
            _hasShown = false;
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
            Net.Peer.Send(Msg.Cursor, ms.ToArray());
        }

        public static void Receive(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            _visible = r.ReadBoolean();
            _target = new Vector2(r.ReadSingle(), r.ReadSingle());
            _index = r.ReadByte();
            _receivedAt = Clock.Now;
            if (!_hasShown) { _shown = _target; _hasShown = true; }
        }

        public static void Draw()
        {
            if (!PlayerNames.InSession || !_visible || !_hasShown || Clock.Now - _receivedAt > 1.5) return;
            if (Event.current.type != EventType.Repaint || WorldfallBridge.InFirstPerson) return;
            Camera cam = Camera.main;
            Texture2D[] textures = Cursors();
            if (cam == null || textures == null || textures.Length == 0) return;
            Texture2D tex = _index < textures.Length && textures[_index] != null ? textures[_index] : textures[0];
            if (tex == null) return;

            _shown = Vector2.Lerp(_shown, _target, Mathf.Clamp01(Time.unscaledDeltaTime * 18f));
            if ((_shown - _target).sqrMagnitude > 400f) _shown = _target;
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(_shown.x, _shown.y, 0f));
            if (screen.z < 0f) return;
            float x = screen.x, y = Screen.height - screen.y;
            if (x < -64f || y < -64f || x > Screen.width + 64f || y > Screen.height + 64f) return;

            float scale = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 96f) : 1f;
            float w = tex.width * scale, h = tex.height * scale;
            Color old = GUI.color;
            Color tint = Net.Role == Role.Host ? ClientColor : HostColor;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(x + 2f, y + 2f, w, h), tex);
            GUI.color = tint;
            GUI.DrawTexture(new Rect(x, y, w, h), tex);
            GUI.color = old;

            string name = PlayerNames.RemoteName;
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
