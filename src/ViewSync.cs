using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class ViewSync
    {
        const float Near = 24f;

        static float _sendTimer;

        public static void Reset() { }

        public static void UpdateClient()
        {
            if (!Net.IsClientConnected || Net.Peer == null) return;
            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer > 0f) return;
            _sendTimer = 0.25f;
            Camera cam = Camera.main;
            if (cam == null) return;
            float minX, minY, maxX, maxY;
            Actor own = ControllableUnit.getControllableUnit();
            if (WorldfallBridge.InFirstPerson && own != null)
            {
                Vector2 p = own.current_position;
                minX = p.x - 40f; maxX = p.x + 40f; minY = p.y - 40f; maxY = p.y + 40f;
            }
            else
            {
                float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
                Vector3 c = cam.transform.position;
                minX = c.x - halfW; maxX = c.x + halfW; minY = c.y - halfH; maxY = c.y + halfH;
            }
            var ms = new MemoryStream(16);
            var w = new BinaryWriter(ms);
            w.Write(minX); w.Write(minY); w.Write(maxX); w.Write(maxY);
            Net.Peer.Send(Msg.View, ms.ToArray());
        }

        public static void Receive(byte[] data)
        {
            Guest g = Net.Current;
            if (g == null) return;
            var r = new BinaryReader(new MemoryStream(data));
            g.MinX = r.ReadSingle(); g.MinY = r.ReadSingle(); g.MaxX = r.ReadSingle(); g.MaxY = r.ReadSingle();
            g.ViewKnown = true;
        }

        public static int Priority(Vector2 p)
        {
            int best = 2;
            bool any = false;
            foreach (Guest g in Net.Guests)
            {
                if (!g.Ready) continue;
                any = true;
                if (!g.ViewKnown) return 0;
                float padX = (g.MaxX - g.MinX) * 0.15f, padY = (g.MaxY - g.MinY) * 0.15f;
                if (p.x >= g.MinX - padX && p.x <= g.MaxX + padX && p.y >= g.MinY - padY && p.y <= g.MaxY + padY) return 0;
                if (p.x >= g.MinX - Near && p.x <= g.MaxX + Near && p.y >= g.MinY - Near && p.y <= g.MaxY + Near) best = 1;
            }
            return any ? best : 0;
        }

        public static string Describe()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (Guest g in Net.Guests)
                if (g.ViewKnown) parts.Add(g.Slot + ":" + g.MinX.ToString("0") + ":" + g.MinY.ToString("0") + "-" + g.MaxX.ToString("0") + ":" + g.MaxY.ToString("0"));
            return parts.Count == 0 ? "view unknown" : "view " + string.Join(",", parts.ToArray());
        }
    }
}
