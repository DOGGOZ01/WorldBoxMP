using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class ActionSync
    {
        private struct Attack
        {
            public double Time;

            public long Id;

            public double Stamp;

            public Vector2 Target;

            public bool HasTarget;
        }

        private struct Shot
        {
            public double Time;

            public string Asset;

            public long From;

            public Vector3 Launch;

            public Vector3 Target;

            public float TargetZ;

            public float StartZ;

            public float Speed;
        }

        [HarmonyPatch(typeof(ProjectileManager), "spawn")]
        private static class HostRecordsShots
        {
            private static void Postfix(BaseSimObject pInitiator, string pAssetID, Vector3 pLaunchPosition, Vector3 pTargetPosition, float pTargetZ, float pStartPosZ, Projectile __result)
            {
                if (__result == null || !HostSync.Tracking || _spawningCopy)
                {
                    return;
                }
                _hostShots.Add(new Shot
                {
                    Time = Clock.Now,
                    Asset = pAssetID,
                    From = ((pInitiator is Actor) ? pInitiator.getID() : (-1)),
                    Launch = pLaunchPosition,
                    Target = pTargetPosition,
                    TargetZ = pTargetZ,
                    StartZ = pStartPosZ,
                    Speed = Access.ProjectileSpeed(__result)
                });
            }
        }

        [HarmonyPatch(typeof(Projectile), "checkHitOnNearbyUnits")]
        private static class ClientShotsDoNotHit
        {
            private static bool Prefix(Projectile __instance, ref AttackDataResult __result)
            {
                if (!Net.IsClientConnected)
                {
                    return true;
                }
                __result = ((Access.ProjectilePos(__instance).z > 0f) ? AttackDataResult.Continue : AttackDataResult.Miss);
                return false;
            }
        }

        [HarmonyPatch(typeof(Projectile), "targetReached")]
        private static class ClientShotsDoNotLand
        {
            private static bool Prefix()
            {
                return !Net.IsClientConnected;
            }
        }

        private static readonly AccessTools.FieldRef<Projectile, Kingdom> ProjectileKingdom = AccessTools.FieldRefAccess<Projectile, Kingdom>("kingdom");

        private static readonly Dictionary<long, double> _stamps = new Dictionary<long, double>();

        private static readonly List<Attack> _hostAttacks = new List<Attack>();

        private static readonly List<Shot> _hostShots = new List<Shot>();

        private static readonly List<Attack> _attacks = new List<Attack>();

        private static readonly List<Shot> _shots = new List<Shot>();

        private static bool _spawningCopy;

        public static int ShownAttacks;

        public static int ShownShots;

        private static double _worldClockA = -1.0;

        private static double _worldTimeA;

        private static double _worldClockB = -1.0;

        private static double _worldTimeB;

        public static int PendingCount => _hostAttacks.Count + _hostShots.Count;

        public static void ResetHost()
        {
            _stamps.Clear();
            _hostAttacks.Clear();
            _hostShots.Clear();
            foreach (Actor unit in World.world.units)
            {
                _stamps[unit.getID()] = Access.LastAttack(unit);
            }
        }

        public static void ResetClient()
        {
            _attacks.Clear();
            _shots.Clear();
            _worldClockA = (_worldClockB = -1.0);
        }

        public static void Watch(Actor unit, long id)
        {
            double num = Access.LastAttack(unit);
            if (_stamps.TryGetValue(id, out var value) && value == num)
            {
                return;
            }
            bool flag = _stamps.ContainsKey(id);
            _stamps[id] = num;
            if (!flag || num <= 0.0)
            {
                return;
            }
            BaseSimObject baseSimObject = Access.AttackTarget(unit);
            bool flag2 = baseSimObject != null && baseSimObject.isAlive();
            _hostAttacks.Add(new Attack
            {
                Time = Clock.Now,
                Id = id,
                Stamp = num,
                Target = (flag2 ? baseSimObject.current_position : Vector2.zero),
                HasTarget = flag2
            });
        }

        public static void Forget(long id)
        {
            _stamps.Remove(id);
        }

        public static void Write(BinaryWriter bw)
        {
            if (Flow.Level == 2) _hostAttacks.Clear();
            bw.Write(World.world.getCurWorldTime());
            bw.Write(_hostAttacks.Count);
            foreach (Attack hostAttack in _hostAttacks)
            {
                bw.Write(hostAttack.Time);
                bw.Write(hostAttack.Id);
                bw.Write(hostAttack.Stamp);
                bw.Write(hostAttack.HasTarget);
                if (hostAttack.HasTarget)
                {
                    bw.Write(hostAttack.Target.x);
                    bw.Write(hostAttack.Target.y);
                }
            }
            _hostAttacks.Clear();
            bw.Write(_hostShots.Count);
            foreach (Shot hostShot in _hostShots)
            {
                bw.Write(hostShot.Time);
                bw.Write(hostShot.Asset ?? "");
                bw.Write(hostShot.From);
                bw.Write(hostShot.Launch.x);
                bw.Write(hostShot.Launch.y);
                bw.Write(hostShot.Launch.z);
                bw.Write(hostShot.Target.x);
                bw.Write(hostShot.Target.y);
                bw.Write(hostShot.Target.z);
                bw.Write(hostShot.TargetZ);
                bw.Write(hostShot.StartZ);
                bw.Write(hostShot.Speed);
            }
            _hostShots.Clear();
        }

        public static void Read(BinaryReader br, double hostClock)
        {
            double worldTimeB = br.ReadDouble();
            if (hostClock > _worldClockB)
            {
                _worldClockA = _worldClockB;
                _worldTimeA = _worldTimeB;
                _worldClockB = hostClock;
                _worldTimeB = worldTimeB;
            }
            int num = br.ReadInt32();
            for (int i = 0; i < num; i++)
            {
                Attack item = new Attack
                {
                    Time = br.ReadDouble(),
                    Id = br.ReadInt64(),
                    Stamp = br.ReadDouble(),
                    HasTarget = br.ReadBoolean()
                };
                if (item.HasTarget)
                {
                    item.Target = new Vector2(br.ReadSingle(), br.ReadSingle());
                }
                _attacks.Add(item);
            }
            int num2 = br.ReadInt32();
            for (int j = 0; j < num2; j++)
            {
                _shots.Add(new Shot
                {
                    Time = br.ReadDouble(),
                    Asset = br.ReadString(),
                    From = br.ReadInt64(),
                    Launch = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    Target = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    TargetZ = br.ReadSingle(),
                    StartZ = br.ReadSingle(),
                    Speed = br.ReadSingle()
                });
            }
            if (_attacks.Count > 400)
            {
                _attacks.RemoveRange(0, _attacks.Count - 400);
            }
            if (_shots.Count > 400)
            {
                _shots.RemoveRange(0, _shots.Count - 400);
            }
        }

        public static void UpdateClient(double renderTime, float dt)
        {
            MapBox world = World.world;
            if (_worldClockB >= 0.0)
            {
                double num = 1.0;
                if (_worldClockA >= 0.0 && _worldClockB - _worldClockA > 0.001)
                {
                    num = Math.Max(0.0, Math.Min(50.0, (_worldTimeB - _worldTimeA) / (_worldClockB - _worldClockA)));
                }
                double num2 = _worldTimeB + (renderTime - _worldClockB) * num;
                MapStats mapStats = Access.MapStats(world);
                if (mapStats != null && (num2 > mapStats.world_time || mapStats.world_time - num2 > 5.0))
                {
                    mapStats.world_time = num2;
                }
            }
            int num3 = 0;
            while (num3 < _attacks.Count)
            {
                Attack attack = _attacks[num3];
                if (attack.Time > renderTime)
                {
                    num3++;
                    continue;
                }
                _attacks.RemoveAt(num3);
                Actor actor = world.units.get(attack.Id);
                if (actor == null || !actor.isAlive())
                {
                    continue;
                }
                Access.LastAttack(actor) = attack.Stamp;
                ShownAttacks++;
                if (attack.HasTarget)
                {
                    try
                    {
                        Access.Punch(actor, attack.Target, true, false, 40f);
                    }
                    catch
                    {
                    }
                }
            }
            int num4 = 0;
            while (num4 < _shots.Count)
            {
                Shot shot = _shots[num4];
                if (shot.Time > renderTime)
                {
                    num4++;
                    continue;
                }
                _shots.RemoveAt(num4);
                Spawn(shot);
            }
            if (world.projectiles != null && dt > 0f)
            {
                foreach (Projectile p in world.projectiles.list)
                    if (ProjectileKingdom(p) == null) ProjectileKingdom(p) = WildKingdomsManager.neutral;
                world.projectiles.update(Access.Elapsed(world) * (float)Mathf.Clamp(Config.time_scale_asset?.ticks ?? 1, 1, 8));
            }
        }

        private static void Spawn(Shot shot)
        {
            if (string.IsNullOrEmpty(shot.Asset) || AssetManager.projectiles.get(shot.Asset) == null)
            {
                return;
            }
            Actor actor = ((shot.From >= 0) ? World.world.units.get(shot.From) : null);
            _spawningCopy = true;
            try
            {
                Projectile projectile = World.world.projectiles.spawn(actor, null, shot.Asset, shot.Launch, shot.Target, shot.TargetZ, shot.StartZ,
                    null, actor != null && actor.kingdom != null ? actor.kingdom : WildKingdomsManager.neutral);
                if (projectile != null && shot.Speed > 0f)
                {
                    Access.ProjectileSpeed(projectile) = shot.Speed;
                    Access.ProjectileVelocities(projectile);
                }
                if (projectile != null)
                {
                    ShownShots++;
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[WorldBoxMP] could not show shot " + shot.Asset + ": " + ex.GetBaseException().Message);
            }
            finally
            {
                _spawningCopy = false;
            }
        }
    }
}
