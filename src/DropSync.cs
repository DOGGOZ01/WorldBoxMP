using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class DropSync
    {
        struct Rec
        {
            public int Tile;
            public string Asset;
            public float Height, Scale;
            public bool Arc;
            public float ArcStart, ArcHeight;
            public Vector2 ArcTarget;
        }

        const int MaxPerTick = 300;
        static readonly List<Rec> _pending = new List<Rec>();
        static readonly Dictionary<Drop, int> _index = new Dictionary<Drop, int>();
        static bool _replaying;

        static readonly AccessTools.FieldRef<Drop, float> HeightZ = AccessTools.FieldRefAccess<Drop, float>("_currentHeightZ");
        static readonly AccessTools.FieldRef<Drop, bool> Landed = AccessTools.FieldRefAccess<Drop, bool>("_landed");
        static readonly AccessTools.FieldRef<Drop, Vector2> TargetPosition = AccessTools.FieldRefAccess<Drop, Vector2>("_targetPosition");
        static readonly AccessTools.FieldRef<Drop, Vector2> StartPosition = AccessTools.FieldRefAccess<Drop, Vector2>("_startPosition");
        static readonly AccessTools.FieldRef<Drop, float> TargetHeight = AccessTools.FieldRefAccess<Drop, float>("_targetHeight");
        static readonly AccessTools.FieldRef<Drop, float> TimeToTarget = AccessTools.FieldRefAccess<Drop, float>("_timeToTarget");
        static readonly AccessTools.FieldRef<Drop, float> FallingSpeed = AccessTools.FieldRefAccess<Drop, float>("_falling_speed");

        public static int Sent, Shown;

        static bool Recording => HostSync.Tracking && !_replaying;

        public static void Reset()
        {
            _pending.Clear();
            _index.Clear();
        }

        public static int PendingCount => _pending.Count;

        public static void Write(BinaryWriter w)
        {
            w.Write(_pending.Count);
            foreach (Rec d in _pending)
            {
                w.Write(d.Tile);
                w.Write(d.Asset);
                w.Write(d.Height);
                w.Write(d.Scale);
                w.Write(d.Arc);
                if (d.Arc)
                {
                    w.Write(d.ArcStart);
                    w.Write(d.ArcHeight);
                    w.Write(d.ArcTarget.x);
                    w.Write(d.ArcTarget.y);
                }
            }
            Sent += _pending.Count;
            _pending.Clear();
            _index.Clear();
        }

        public static void Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            WorldTile[] tiles = Access.TilesList(World.world);
            _replaying = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    int tileId = r.ReadInt32();
                    string assetId = r.ReadString();
                    float height = r.ReadSingle(), scale = r.ReadSingle();
                    bool arc = r.ReadBoolean();
                    float arcStart = 0f, arcHeight = 0f;
                    Vector2 target = default;
                    if (arc)
                    {
                        arcStart = r.ReadSingle();
                        arcHeight = r.ReadSingle();
                        target = new Vector2(r.ReadSingle(), r.ReadSingle());
                    }
                    DropAsset asset = AssetManager.drops.get(assetId);
                    if (asset == null || tileId < 0 || tileId >= tiles.Length) continue;
                    try
                    {
                        Drop drop = World.world.drop_manager.spawn(tiles[tileId], asset, height, scale);
                        if (arc && drop != null) Aim(drop, arcStart, arcHeight, target);
                        Shown++;
                    }
                    catch (System.Exception e)
                    {
                        SessionLog.Write("drop " + assetId + " failed: " + e.Message);
                    }
                }
            }
            finally { _replaying = false; }
        }

        static void Aim(Drop drop, float start, float height, Vector2 target)
        {
            drop.launchParabolic(start, height, height, 0f, 0f);
            TargetPosition(drop) = target;
            float dist = Toolbox.DistVec2Float(StartPosition(drop), target);
            float t = (dist + height * 3f) * 0.25f / Mathf.Max(0.01f, FallingSpeed(drop));
            if (t < 1f) t += 0.5f;
            TimeToTarget(drop) = t;
        }

        public static void UpdateClient()
        {
            World.world.drop_manager.update(Access.Elapsed(World.world));
        }

        [HarmonyPatch(typeof(DropManager), nameof(DropManager.spawn), typeof(WorldTile), typeof(DropAsset), typeof(float), typeof(float), typeof(bool), typeof(long))]
        static class CaptureDrop
        {
            static void Postfix(WorldTile pTile, DropAsset pAsset, Drop __result)
            {
                if (!Recording || __result == null || pTile == null || pAsset == null || _pending.Count >= MaxPerTick) return;
                _index[__result] = _pending.Count;
                _pending.Add(new Rec
                {
                    Tile = pTile.data.tile_id,
                    Asset = pAsset.id,
                    Height = HeightZ(__result),
                    Scale = __result.transform.localScale.x,
                });
            }
        }

        [HarmonyPatch(typeof(Drop), nameof(Drop.launchParabolic))]
        static class CaptureArc
        {
            static void Postfix(Drop __instance, float pStartHeight)
            {
                if (!Recording || !_index.TryGetValue(__instance, out int i)) return;
                Rec d = _pending[i];
                d.Arc = true;
                d.ArcStart = pStartHeight;
                d.ArcHeight = TargetHeight(__instance);
                d.ArcTarget = TargetPosition(__instance);
                _pending[i] = d;
            }
        }

        [HarmonyPatch(typeof(Drop), "land")]
        static class ClientDropsOnlyLand
        {
            static bool Prefix(Drop __instance)
            {
                if (!Net.IsClientConnected) return true;
                Landed(__instance) = true;
                World.world.drop_manager.landDrop(__instance);
                return false;
            }
        }
    }
}
