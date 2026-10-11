using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class EffectSync
    {
        enum Kind : byte { Spawn = 1, SpawnAt = 2, SpawnAtTile = 3, ExplosionWave = 4, Slash = 5 }

        struct Fx
        {
            public Kind Kind;
            public string Id;
            public string P1, P2;
            public float X, Y, F1, F2;
            public int Tile;
        }

        const int MaxPerTick = 200;
        static readonly List<Fx> _pending = new List<Fx>();
        static int _depth;
        static bool _inEffectUpdate;
        static bool _replaying;

        static bool Recording => Net.IsHostWithPeer && HostSync.Tracking && !_inEffectUpdate && !_replaying;

        static void Record(Fx fx)
        {
            if (_depth != 1 || !Recording || _pending.Count >= MaxPerTick) return;
            _pending.Add(fx);
        }

        public static void Write(BinaryWriter w)
        {
            w.Write(_pending.Count);
            foreach (Fx f in _pending)
            {
                w.Write((byte)f.Kind);
                w.Write(f.Id ?? "");
                w.Write(f.P1 ?? "");
                w.Write(f.P2 ?? "");
                w.Write(f.X);
                w.Write(f.Y);
                w.Write(f.F1);
                w.Write(f.F2);
                w.Write(f.Tile);
            }
            _pending.Clear();
        }

        public static void Reset() => _pending.Clear();
        public static int PendingCount => _pending.Count;

        public static void Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            WorldTile[] tiles = Access.TilesList(World.world);
            _replaying = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var kind = (Kind)r.ReadByte();
                    string id = r.ReadString();
                    string p1 = r.ReadString();
                    string p2 = r.ReadString();
                    float x = r.ReadSingle(), y = r.ReadSingle(), f1 = r.ReadSingle(), f2 = r.ReadSingle();
                    int tileId = r.ReadInt32();
                    WorldTile tile = tileId >= 0 && tileId < tiles.Length ? tiles[tileId] : null;
                    try
                    {
                        switch (kind)
                        {
                            case Kind.Spawn:
                                EffectsLibrary.spawn(id, tile, Null(p1), Null(p2), f1, x, y);
                                break;
                            case Kind.SpawnAt:
                                EffectsLibrary.spawnAt(id, new Vector3(x, y), f1);
                                break;
                            case Kind.SpawnAtTile:
                                if (tile != null) EffectsLibrary.spawnAtTile(id, tile, f1);
                                break;
                            case Kind.ExplosionWave:
                                EffectsLibrary.spawnExplosionWave(new Vector3(x, y), f1, f2);
                                break;
                            case Kind.Slash:
                                EffectsLibrary.spawnSlash(new Vector2(x, y), p1, f1, f2);
                                break;
                        }
                    }
                    catch (System.Exception e)
                    {
                        SessionLog.Write("effect " + kind + " " + id + " failed: " + e.Message);
                    }
                }
            }
            finally { _replaying = false; }
        }

        static string Null(string s) => string.IsNullOrEmpty(s) ? null : s;

        public static void UpdateClient()
        {
            Access.StackEffects(World.world).update(Access.Elapsed(World.world));
        }

        [HarmonyPatch(typeof(StackEffects), nameof(StackEffects.update))]
        static class MarkEffectUpdate
        {
            static void Prefix() => _inEffectUpdate = true;
            static void Finalizer() => _inEffectUpdate = false;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawn))]
        static class CaptureSpawn
        {
            static void Prefix(string pID, WorldTile pTile, string pParam1, string pParam2, float pFloatParam1, float pX, float pY)
            {
                _depth++;
                Record(new Fx { Kind = Kind.Spawn, Id = pID, P1 = pParam1, P2 = pParam2, F1 = pFloatParam1, X = pX, Y = pY, Tile = pTile != null ? pTile.data.tile_id : -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnAt), typeof(string), typeof(Vector3), typeof(float))]
        static class CaptureSpawnAt3
        {
            static void Prefix(string pID, Vector3 pPos, float pScale)
            {
                _depth++;
                Record(new Fx { Kind = Kind.SpawnAt, Id = pID, X = pPos.x, Y = pPos.y, F1 = pScale, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnAt), typeof(string), typeof(Vector2), typeof(float))]
        static class CaptureSpawnAt2
        {
            static void Prefix(string pID, Vector2 pPos, float pScale)
            {
                _depth++;
                Record(new Fx { Kind = Kind.SpawnAt, Id = pID, X = pPos.x, Y = pPos.y, F1 = pScale, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnAtTile))]
        static class CaptureSpawnAtTile
        {
            static void Prefix(string pID, WorldTile pTile, float pScale)
            {
                _depth++;
                Record(new Fx { Kind = Kind.SpawnAtTile, Id = pID, F1 = pScale, Tile = pTile != null ? pTile.data.tile_id : -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnExplosionWave))]
        static class CaptureExplosionWave
        {
            static void Prefix(Vector3 pVec, float pRadius, float pSpeed)
            {
                _depth++;
                Record(new Fx { Kind = Kind.ExplosionWave, Id = "fx_explosion_wave", X = pVec.x, Y = pVec.y, F1 = pRadius, F2 = pSpeed, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnSlash))]
        static class CaptureSlash
        {
            static void Prefix(Vector2 pVec, string pPathSprites, float pAngle, float pScaleMod)
            {
                _depth++;
                Record(new Fx { Kind = Kind.Slash, Id = "fx_slash", P1 = pPathSprites, X = pVec.x, Y = pVec.y, F1 = pAngle, F2 = pScaleMod, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }
    }

    static class WorldMutatorsOffOnClient
    {
        static bool Off => Net.IsClientConnected;

        [HarmonyPatch(typeof(MapAction), nameof(MapAction.damageWorld))]
        static class NoDamageWorld { static bool Prefix() => !Off; }

        [HarmonyPatch]
        static class NoTerraform
        {
            static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(MapAction)))
                    if (m.Name == "terraformMain" || m.Name == "terraformTop" || m.Name == "decreaseTile") yield return m;
            }
            static bool Prefix() => !Off;
        }

        [HarmonyPatch(typeof(WorldTile), "startFire")]
        static class NoFire
        {
            static bool Prefix(ref bool __result)
            {
                if (!Off) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        static class NoNewBuildings
        {
            static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(BuildingManager)))
                    if (m.Name == "addBuilding") yield return m;
            }
            static bool Prefix(ref Building __result)
            {
                if (!Off) return true;
                __result = null;
                return false;
            }
        }

        [HarmonyPatch]
        static class NoHits
        {
            static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                foreach (var m in AccessTools.GetDeclaredMethods(typeof(Actor)))
                    if (m.Name == "getHit" || m.Name == "getHitFullHealth") yield return m;
            }
            static bool Prefix() => !Off;
        }
    }
}
