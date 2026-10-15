using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class MapPings
    {
        class Ping
        {
            public byte Slot;
            public Vector2 Pos;
            public float At;
        }

        const float Life = 4f;
        static readonly List<Ping> _pings = new List<Ping>();
        static readonly Queue<float> _mine = new Queue<float>();
        static readonly Dictionary<byte, Queue<float>> _recent = new Dictionary<byte, Queue<float>>();
        static Texture2D _ring, _dot;

        public static int Shown;
        public static byte LastSlot;
        public static readonly HashSet<byte> Seen = new HashSet<byte>();

        public static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        public static bool Pinging => Net.Role != Role.None && AltHeld && (Input.GetMouseButton(0) || Input.GetMouseButtonUp(0));

        public static void Clear()
        {
            _pings.Clear();
            _recent.Clear();
        }

        public static void Update()
        {
            if (Net.Role == Role.None) { if (_pings.Count > 0) Clear(); return; }
            for (int i = _pings.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - _pings[i].At > Life) _pings.RemoveAt(i);
            if (!Input.GetMouseButtonDown(0) || !AltHeld || Chat.Typing || World.world == null || Camera.main == null) return;
            if (World.world.isOverUI()) return;
            Place(World.world.getMousePos());
        }

        public static void Place(Vector2 pos)
        {
            while (_mine.Count > 0 && Time.unscaledTime - _mine.Peek() > 2f) _mine.Dequeue();
            if (_mine.Count >= 3) return;
            _mine.Enqueue(Time.unscaledTime);
            if (Net.Role == Role.Host)
            {
                Broadcast(0, pos);
                return;
            }
            if (!Net.IsClientConnected) return;
            Show(Roster.LocalSlot, pos);
            Net.Peer.Send(Msg.MapPing, Write(Roster.LocalSlot, pos));
        }

        static byte[] Write(byte slot, Vector2 pos)
        {
            var ms = new MemoryStream(9);
            var w = new BinaryWriter(ms);
            w.Write(slot);
            w.Write(pos.x);
            w.Write(pos.y);
            return ms.ToArray();
        }

        static void Broadcast(byte slot, Vector2 pos)
        {
            Show(slot, pos);
            byte[] bytes = Write(slot, pos);
            foreach (Guest g in Net.Guests) if (g.Ready && !g.Peer.Closed) g.Peer.Send(Msg.MapPing, bytes);
        }

        public static void HostReceive(byte[] data)
        {
            Guest g = Net.Current;
            if (g == null || data.Length < 9) return;
            var r = new BinaryReader(new MemoryStream(data));
            r.ReadByte();
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y)) return;
            if (!_recent.TryGetValue(g.Slot, out Queue<float> q)) _recent[g.Slot] = q = new Queue<float>();
            while (q.Count > 0 && Time.unscaledTime - q.Peek() > 2f) q.Dequeue();
            if (q.Count >= 4) return;
            q.Enqueue(Time.unscaledTime);
            Broadcast(g.Slot, pos);
        }

        public static void ClientReceive(byte[] data)
        {
            if (data.Length < 9) return;
            var r = new BinaryReader(new MemoryStream(data));
            byte slot = r.ReadByte();
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            if (slot == Roster.LocalSlot) return;
            Show(slot, pos);
        }

        static void Show(byte slot, Vector2 pos)
        {
            _pings.Add(new Ping { Slot = slot, Pos = pos, At = Time.unscaledTime });
            if (_pings.Count > 24) _pings.RemoveAt(0);
            Shown++;
            LastSlot = slot;
            Seen.Add(slot);
        }

        public static void Draw()
        {
            if (_pings.Count == 0 || Event.current.type != EventType.Repaint) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            MakeTextures();
            float scale = Mathf.Max(1f, Screen.height / 900f);
            Color old = GUI.color;
            foreach (Ping p in _pings)
            {
                float age = Time.unscaledTime - p.At;
                float fade = Mathf.Clamp01((Life - age) / 0.6f);
                Color c = Lobby.ColorOf(p.Slot);
                Vector3 s = cam.WorldToScreenPoint(new Vector3(p.Pos.x, p.Pos.y, 0f));
                float x = s.x, y = Screen.height - s.y;
                float margin = 18f * scale;
                bool off = x < margin || y < margin || x > Screen.width - margin || y > Screen.height - margin || s.z < 0f;
                if (off)
                {
                    x = Mathf.Clamp(x, margin, Screen.width - margin);
                    y = Mathf.Clamp(y, margin, Screen.height - margin);
                }
                for (int k = 0; k < (off ? 1 : 2); k++)
                {
                    float phase = (age * 1.1f + k * 0.5f) % 1f;
                    float r = Mathf.Lerp(6f, off ? 16f : 38f, phase) * scale;
                    GUI.color = new Color(c.r, c.g, c.b, (1f - phase) * fade);
                    GUI.DrawTexture(new Rect(x - r, y - r, r * 2f, r * 2f), _ring);
                }
                float d = (off ? 9f : 11f) * scale;
                GUI.color = new Color(0f, 0f, 0f, 0.6f * fade);
                GUI.DrawTexture(new Rect(x - d / 2f - 1.5f, y - d / 2f - 1.5f, d + 3f, d + 3f), _dot);
                GUI.color = new Color(c.r, c.g, c.b, fade);
                GUI.DrawTexture(new Rect(x - d / 2f, y - d / 2f, d, d), _dot);
            }
            GUI.color = old;
        }

        static void MakeTextures()
        {
            if (_ring != null) return;
            const int n = 64;
            _ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var ring = new Color32[n * n];
            var dot = new Color32[n * n];
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dist = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(dist - (c - 4f)) / 3f);
                    ring[y * n + x] = new Color32(255, 255, 255, (byte)(edge * 255));
                    dot[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(c - dist) * 255));
                }
            _ring.SetPixels32(ring);
            _ring.Apply(false, true);
            _dot.SetPixels32(dot);
            _dot.Apply(false, true);
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "updateControls")]
    static class PingClickIsNotAPower
    {
        static bool Prefix() => !MapPings.Pinging;
    }
}
