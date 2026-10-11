using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class ViewSync
    {
        const float Near = 24f;

        static bool _known;
        static float _minX, _minY, _maxX, _maxY;

        static float _sendTimer;

        public static void Reset() => _known = false;

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
            var r = new BinaryReader(new MemoryStream(data));
            _minX = r.ReadSingle(); _minY = r.ReadSingle(); _maxX = r.ReadSingle(); _maxY = r.ReadSingle();
            _known = true;
        }

        public static int Priority(Vector2 p)
        {
            if (!_known) return 0;
            float padX = (_maxX - _minX) * 0.15f, padY = (_maxY - _minY) * 0.15f;
            if (p.x >= _minX - padX && p.x <= _maxX + padX && p.y >= _minY - padY && p.y <= _maxY + padY) return 0;
            if (p.x >= _minX - Near && p.x <= _maxX + Near && p.y >= _minY - Near && p.y <= _maxY + Near) return 1;
            return 2;
        }

        public static string Describe() => _known
            ? "view " + _minX.ToString("0") + ":" + _minY.ToString("0") + "-" + _maxX.ToString("0") + ":" + _maxY.ToString("0")
            : "view unknown";
    }
}
