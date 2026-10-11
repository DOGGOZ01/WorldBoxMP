using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class Powers
    {
        static readonly AccessTools.FieldRef<PlayerControl, float> ClickTimer =
            AccessTools.FieldRefAccess<PlayerControl, float>("_click_timer");
        static readonly AccessTools.FieldRef<PlayerControl, WorldTile> FirstTile =
            AccessTools.FieldRefAccess<PlayerControl, WorldTile>("first_pressed_tile");
        static readonly AccessTools.FieldRef<PlayerControl, TileType> FirstType =
            AccessTools.FieldRefAccess<PlayerControl, TileType>("first_pressed_type");
        static readonly AccessTools.FieldRef<PlayerControl, TopTileType> FirstTopType =
            AccessTools.FieldRefAccess<PlayerControl, TopTileType>("first_pressed_top_type");

        static readonly System.Action<PlayerControl, Vector2Int, GodPower, bool> ClickedFinal =
            AccessTools.MethodDelegate<System.Action<PlayerControl, Vector2Int, GodPower, bool>>(
                AccessTools.Method(typeof(PlayerControl), "clickedFinal"));

        static WorldTile _remoteFirstTile;
        static TileType _remoteFirstType;
        static TopTileType _remoteFirstTopType;
        static float _remoteTimer = -1f;
        static float _lastRemoteClick = -10f;
        const float StrokeGap = 0.25f;

        public static void SendClick(Vector2Int pos, GodPower power)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(pos.x);
            w.Write(pos.y);
            w.Write(power.id);
            w.Write(Config.current_brush ?? "");
            Net.Peer.Send(Msg.Power, ms.ToArray());
        }

        public static void ApplyRemoteClick(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int x = r.ReadInt32();
            int y = r.ReadInt32();
            string id = r.ReadString();
            string brush = r.ReadString();

            if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height) return;
            GodPower power = AssetManager.powers.get(id);
            if (power == null)
            {
                Debug.Log("[WorldBoxMP] unknown power from client: " + id);
                return;
            }

            if (Time.unscaledTime - _lastRemoteClick > StrokeGap)
            {
                _remoteFirstTile = null;
                _remoteFirstType = null;
                _remoteFirstTopType = null;
                _remoteTimer = -1f;
            }
            _lastRemoteClick = Time.unscaledTime;

            PlayerControl pc = World.world.player_control;
            string hostBrush = Config.current_brush;
            float hostTimer = ClickTimer(pc);
            WorldTile hostTile = FirstTile(pc);
            TileType hostType = FirstType(pc);
            TopTileType hostTopType = FirstTopType(pc);
            try
            {
                if (!string.IsNullOrEmpty(brush)) Config.current_brush = brush;
                ClickTimer(pc) = _remoteTimer;
                FirstTile(pc) = _remoteFirstTile;
                FirstType(pc) = _remoteFirstType;
                FirstTopType(pc) = _remoteFirstTopType;

                ClickedFinal(pc, new Vector2Int(x, y), power, true);
                Debug.Log("[WorldBoxMP] remote power " + id + " at " + x + ":" + y);

                _remoteTimer = ClickTimer(pc);
                _remoteFirstTile = FirstTile(pc);
                _remoteFirstType = FirstType(pc);
                _remoteFirstTopType = FirstTopType(pc);
            }
            finally
            {
                Config.current_brush = hostBrush;
                ClickTimer(pc) = hostTimer;
                FirstTile(pc) = hostTile;
                FirstType(pc) = hostType;
                FirstTopType(pc) = hostTopType;
            }
        }
    }
}
