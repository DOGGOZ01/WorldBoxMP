using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class EffectSync
    {
        enum Kind : byte { Spawn = 1, SpawnAt = 2, SpawnAtTile = 3, ExplosionWave = 4, Slash = 5, HitFlash = 6, Shake = 7, MetaEvent = 8, Particle = 9 }

        struct Fx
        {
            public Kind Kind;
            public string Id;
            public string P1, P2;
            public float X, Y, F1, F2;
            public int Tile;
        }

        const int MaxPerTick = 200;
        const int MaxFlashesPerTick = 150;
        const int MaxParticlesPerTick = 150;
        static int _flashes, _particles;
        static readonly List<Fx> _pending = new List<Fx>();
        static int _depth;
        static bool _inEffectUpdate;
        static bool _replaying;

        public static int SlashesShown, UnseenSlashes;

        static bool Recording => Net.IsHostWithPeer && HostSync.Tracking && !_inEffectUpdate && !_replaying;

        static void Record(Fx fx)
        {
            if (_depth != 1 || !Recording || _pending.Count >= (Flow.Level == 2 ? MaxPerTick / 4 : MaxPerTick)) return;
            _pending.Add(fx);
        }

        static void RecordDirect(Fx fx)
        {
            if (!Recording || _pending.Count >= MaxPerTick + MaxFlashesPerTick) return;
            if (Flow.Level >= 1 && (fx.Kind == Kind.HitFlash || fx.Kind == Kind.Particle || fx.Kind == Kind.MetaEvent)) return;
            if (fx.Kind == Kind.HitFlash && _flashes++ >= MaxFlashesPerTick) return;
            if (fx.Kind == Kind.Particle && _particles++ >= MaxParticlesPerTick) return;
            _pending.Add(fx);
        }

        [HarmonyPatch(typeof(Actor), nameof(Actor.startColorEffect))]
        static class CaptureHitFlash
        {
            static void Prefix(Actor __instance, ActorColorEffect pColorType)
            {
                if (__instance.asset != null && __instance.asset.effect_damage)
                    RecordDirect(new Fx { Kind = Kind.HitFlash, Id = "hit", P1 = __instance.getID().ToString(), F1 = (float)(int)pColorType, Tile = -1 });
            }
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.showMetaEventEffect), typeof(string), typeof(Vector2), typeof(TileZone), typeof(float))]
        static class CaptureMetaEvent
        {
            static void Prefix(string pID, Vector2 pPosition, float pScale)
            {
                _depth++;
                if (_depth == 1) RecordDirect(new Fx { Kind = Kind.MetaEvent, Id = pID, X = pPosition.x, Y = pPosition.y, F1 = pScale, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.showMetaEventEffect), typeof(string), typeof(Actor))]
        static class CaptureMetaEventActor
        {
            static void Prefix(string pID, Actor pActor)
            {
                _depth++;
                if (_depth == 1 && pActor != null)
                    RecordDirect(new Fx { Kind = Kind.MetaEvent, Id = pID, X = pActor.current_position.x, Y = pActor.current_position.y, F1 = pActor.current_scale.x, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(Actor), nameof(Actor.spawnParticle))]
        static class CaptureParticle
        {
            static void Prefix(Actor __instance, Color pColor)
            {
                _depth++;
                if (_depth == 1)
                    RecordDirect(new Fx { Kind = Kind.Particle, Id = "particle", P1 = __instance.getID().ToString(), X = pColor.r, Y = pColor.g, F1 = pColor.b, F2 = pColor.a, Tile = -1 });
            }
            static void Finalizer() => _depth--;
        }

        [HarmonyPatch(typeof(MapBox), nameof(MapBox.startShake))]
        static class CaptureShake
        {
            static void Prefix(float pDuration, float pInterval, float pIntensity, bool pShakeX, bool pShakeY)
            {
                RecordDirect(new Fx { Kind = Kind.Shake, Id = "shake", F1 = pDuration, F2 = pIntensity, X = pInterval, Y = (pShakeX ? 1 : 0) | (pShakeY ? 2 : 0), Tile = -1 });
            }
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
            _flashes = 0;
            _particles = 0;
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
                                _boulderPath = id == "fx_boulder" ? Null(p1) : null;
                                try { EffectsLibrary.spawnAt(id, new Vector3(x, y), f1); }
                                finally { _boulderPath = null; }
                                break;
                            case Kind.SpawnAtTile:
                                if (tile != null) EffectsLibrary.spawnAtTile(id, tile, f1);
                                break;
                            case Kind.ExplosionWave:
                                EffectsLibrary.spawnExplosionWave(new Vector3(x, y), f1, f2);
                                break;
                            case Kind.Slash:
                                SlashesShown++;
                                EffectsLibrary.spawnSlash(new Vector2(x, y), p1, f1, f2);
                                break;
                            case Kind.HitFlash:
                                Actor hit = long.TryParse(p1, out long hitId) ? World.world.units.get(hitId) : null;
                                if (hit != null && hit.isAlive()) hit.startColorEffect((ActorColorEffect)(int)f1);
                                break;
                            case Kind.Particle:
                                Actor glowing = long.TryParse(p1, out long glowId) ? World.world.units.get(glowId) : null;
                                if (glowing != null && glowing.isAlive()) glowing.spawnParticle(new Color(x, y, f1, f2));
                                break;
                            case Kind.MetaEvent:
                                WorldTile at = World.world.GetTile((int)x, (int)y);
                                if (at != null) EffectsLibrary.showMetaEventEffect(id, new Vector2(x, y), at.zone, f1);
                                break;
                            case Kind.Shake:
                                World.world.startShake(f1, x, f2, ((int)y & 1) != 0, ((int)y & 2) != 0);
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

        static string _boulderPath;

        [HarmonyPatch(typeof(Boulder), nameof(Boulder.spawnOn))]
        static class BoulderPath
        {
            static void Postfix(Boulder __instance)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (Net.Role == Role.Host && Recording)
                {
                    for (int i = _pending.Count - 1; i >= 0; i--)
                    {
                        if (_pending[i].Id != "fx_boulder" || !string.IsNullOrEmpty(_pending[i].P1)) continue;
                        var parts = new List<string>();
                        Vector2 prev = Access.BoulderPrev(__instance);
                        parts.Add(prev.x.ToString("R", ci)); parts.Add(prev.y.ToString("R", ci));
                        foreach (Vector2 b in Access.BoulderBounces(__instance)) { parts.Add(b.x.ToString("R", ci)); parts.Add(b.y.ToString("R", ci)); }
                        parts.Add(Access.BoulderForce(__instance).ToString("R", ci));
                        parts.Add(Access.BoulderSpin(__instance).ToString("R", ci));
                        Fx fx = _pending[i];
                        fx.P1 = string.Join(" ", parts.ToArray());
                        _pending[i] = fx;
                        break;
                    }
                    return;
                }
                if (_boulderPath == null) return;
                string[] v = _boulderPath.Split(' ');
                if (v.Length < 10) return;
                float F(int k) => float.Parse(v[k], ci);
                Access.BoulderPrev(__instance) = new Vector2(F(0), F(1));
                List<Vector2> bounces = Access.BoulderBounces(__instance);
                bounces.Clear();
                for (int k = 2; k + 1 < v.Length - 2; k += 2) bounces.Add(new Vector2(F(k), F(k + 1)));
                Access.BoulderForce(__instance) = F(v.Length - 2);
                Access.BoulderSpin(__instance) = F(v.Length - 1);
                Access.BoulderPlace(__instance);
            }
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

        [HarmonyPatch(typeof(Actor), nameof(Actor.spawnSlash))]
        static class CaptureUnseenSlash
        {
            static void Prefix(Actor __instance, Vector2 pTowardsPosition, string pSlashType, float pDistMod, float pTargetZ, float pStartZ, float? pAngle) =>
                RecordUnseen(__instance, pTowardsPosition, pSlashType, pDistMod, pTargetZ, pStartZ, pAngle);

            public static void RecordUnseen(Actor a, Vector2 towards, string slashType, float distMod, float targetZ, float startZ, float? angle)
            {
                if (!Recording || a == null || Access.IsVisible(a) || ViewSync.Priority(a.current_position) != 0) return;
                string type = string.IsNullOrEmpty(slashType) ? Access.AttackAsset(a)?.path_slash_animation : slashType;
                if (string.IsNullOrEmpty(type)) return;
                Vector2 pos = a.getSlashPosition(a, towards, distMod, targetZ, startZ);
                float rot = angle ?? a.getSlashAngle(pos, towards);
                UnseenSlashes++;
                RecordDirect(new Fx { Kind = Kind.Slash, Id = "fx_slash", P1 = type, X = pos.x, Y = pos.y, F1 = rot, F2 = Access.ActorScale(a), Tile = -1 });
            }
        }

        [HarmonyPatch(typeof(CombatActionLibrary), nameof(CombatActionLibrary.attackMeleeAction))]
        static class CaptureUnseenMeleeSlash
        {
            static void Postfix(AttackData pData)
            {
                Actor a = pData.initiator as Actor;
                if (a != null && !Access.IsVisible(a)) CaptureUnseenSlash.RecordUnseen(a, pData.hit_position, null, 2f, 0f, 0f, null);
            }
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

        [HarmonyPatch(typeof(ActorManager), nameof(ActorManager.createNewUnit))]
        static class NoNewUnits
        {
            static bool Prefix(ref Actor __result)
            {
                if (!Off) return true;
                __result = null;
                return false;
            }
        }

        [HarmonyPatch(typeof(NukeFlash), "crabzilla_bomb_action")]
        static class NoCrabFromFlash { static bool Prefix() => !Off; }

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
