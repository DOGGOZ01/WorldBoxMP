using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class Powers
    {
        class Stroke
        {
            public WorldTile FirstTile;
            public TileType FirstType;
            public TopTileType FirstTopType;
            public float Timer = -1f;
            public float LastClick = -10f;
        }
        static readonly System.Collections.Generic.Dictionary<byte, Stroke> _strokes = new System.Collections.Generic.Dictionary<byte, Stroke>();
        static float _lastDenied = -10f;
        const float StrokeGap = 0.25f;

        public static int SentCount;

        public static readonly System.Collections.Generic.Dictionary<byte, int> AppliedBySlot = new System.Collections.Generic.Dictionary<byte, int>();

        public static void SendClick(Vector2Int pos, GodPower power)
        {
            SentCount++;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(pos.x);
            w.Write(pos.y);
            w.Write(power.id);
            w.Write(Config.current_brush ?? "");
            Net.Peer.Send(Msg.Power, ms.ToArray());
        }

        public static bool ApplyingRemote;

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
            Perm need = Rules.Of(power);
            if (!Rules.HostAllowsPower(power))
            {
                if (Time.unscaledTime - _lastDenied > 1f) Rules.Deny(need, "tip_denied");
                _lastDenied = Time.unscaledTime;
                return;
            }
            ActorAsset spawned = AssetManager.actor_library.get(id);
            if (spawned != null && spawned.skip_save) SpecialActors.ExpectFriendCreature(id);

            byte slot = Net.Current != null ? Net.Current.Slot : (byte)1;
            if (!_strokes.TryGetValue(slot, out Stroke stroke)) _strokes[slot] = stroke = new Stroke();
            if (Time.unscaledTime - stroke.LastClick > StrokeGap)
            {
                stroke.FirstTile = null;
                stroke.FirstType = null;
                stroke.FirstTopType = null;
                stroke.Timer = -1f;
            }
            stroke.LastClick = Time.unscaledTime;

            PlayerControl pc = World.world.player_control;
            string hostBrush = Config.current_brush;
            float hostTimer = Access.ClickTimer(pc);
            WorldTile hostTile = Access.FirstTile(pc);
            TileType hostType = Access.FirstType(pc);
            TopTileType hostTopType = Access.FirstTopType(pc);
            ApplyingRemote = true;
            try
            {
                if (!string.IsNullOrEmpty(brush)) Config.current_brush = brush;
                Access.ClickTimer(pc) = stroke.Timer;
                Access.FirstTile(pc) = stroke.FirstTile;
                Access.FirstType(pc) = stroke.FirstType;
                Access.FirstTopType(pc) = stroke.FirstTopType;

                Access.ClickedFinal(pc, new Vector2Int(x, y), power, true);
                Debug.Log("[WorldBoxMP] remote power " + id + " at " + x + ":" + y);
                if (Net.Current != null)
                {
                    AppliedBySlot.TryGetValue(Net.Current.Slot, out int n);
                    AppliedBySlot[Net.Current.Slot] = n + 1;
                }

                stroke.Timer = Access.ClickTimer(pc);
                stroke.FirstTile = Access.FirstTile(pc);
                stroke.FirstType = Access.FirstType(pc);
                stroke.FirstTopType = Access.FirstTopType(pc);
            }
            finally
            {
                ApplyingRemote = false;
                Config.current_brush = hostBrush;
                Access.ClickTimer(pc) = hostTimer;
                Access.FirstTile(pc) = hostTile;
                Access.FirstType(pc) = hostType;
                Access.FirstTopType(pc) = hostTopType;
            }
        }
    }
}
