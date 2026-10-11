using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class ClientSync
    {
        struct Sample
        {
            public double Time;
            public Vector2 Pos;
            public float Height;
            public bool Flip;
            public bool Moving;
        }

        static readonly Action<MapBox> CheckDirtyUnits =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkDirtyUnits"));
        static readonly Action<MapBox> CheckDirtyMetaObjects =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkDirtyMetaObjects"));
        static readonly Action<MapBox> CheckAnyMetaAddedRemoved =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkAnyMetaAddedRemoved"));

        static readonly AccessTools.FieldRef<MapBox, List<MapLayer>> MapLayers =
            AccessTools.FieldRefAccess<MapBox, List<MapLayer>>("_map_layers");

        static readonly string[] VisualBehaviours = { "zones_meta_data_visualizer", "waves", "clouds" };

        static readonly Dictionary<long, Actor> _possessedReloads = new Dictionary<long, Actor>();
        static readonly Dictionary<long, List<Sample>> _tracks = new Dictionary<long, List<Sample>>();
        static readonly List<long> _lost = new List<long>();
        static readonly Action<Actor, float> UpdateFlipRotation =
            AccessTools.MethodDelegate<Action<Actor, float>>(AccessTools.Method(typeof(Actor), "updateFlipRotation"));
        static double _packetTime;
        static double _newestHostTime = -1;
        static double _newestLocalTime;
        static double _renderTime;
        static double _gap = 0.1;
        static bool _clockSet;
        static int _flipFailures;
        static float _slowTimer;
        static QualityChanger _quality;
        static readonly Queue<byte[]> _pending = new Queue<byte[]>();
        static int _failures;

        static readonly AccessTools.FieldRef<MapBox, WorldTile[]> TilesList =
            AccessTools.FieldRefAccess<MapBox, WorldTile[]>("tiles_list");
        static readonly Action<ActorManager, Actor> ScheduleActorDestroy =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));
        static readonly Action<Building> RemoveBuildingFinal =
            AccessTools.MethodDelegate<Action<Building>>(AccessTools.Method(typeof(Building), "removeBuildingFinal"));
        static readonly Action<MapBox> CheckObjectsToDestroy =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkObjectsToDestroy"));

        public static int Failures => _failures;

        public static void Reset()
        {
            _tracks.Clear();
            OwnUnit.Reset();
            ActionSync.ResetClient();
            _pending.Clear();
            _failures = 0;
            _clockSet = false;
            _newestHostTime = -1;
        }

        public static void Enqueue(byte[] packet) => _pending.Enqueue(packet);

        public static void Update()
        {
            if (SmoothLoader.isLoading()) return;
            while (_pending.Count > 0)
            {
                try { Apply(HostSync.Inflate(_pending.Dequeue())); }
                catch (Exception e)
                {
                    _failures++;
                    Debug.LogError("[WorldBoxMP] delta failed: " + e);
                }
            }
            Glide();
            ActionSync.UpdateClient(_renderTime, Time.unscaledDeltaTime);
            Turn();
            AnimationHelper.updateTime(Time.deltaTime, Time.deltaTime);
            EffectSync.UpdateClient();
            BuildingZonesSystem.update();
            MapBox world = World.world;
            _slowTimer -= Time.unscaledDeltaTime;
            bool slowPass = _slowTimer <= 0f;
            if (slowPass)
            {
                _slowTimer = 0.25f;
                FixBuildingKingdoms(world);
            }
            world.checkSimManagerLists();
            world.units.prepareForMetaChecks();
            CheckDirtyUnits(world);
            CheckDirtyMetaObjects(world);
            CheckAnyMetaAddedRemoved(world);
            foreach (MapLayer layer in MapLayers(World.world)) layer.draw(Time.deltaTime);
            foreach (string id in VisualBehaviours)
            {
                WorldBehaviourAsset b = AssetManager.world_behaviours.get(id);
                if (b != null && b.enabled) b.manager.update(Time.deltaTime);
            }
            UpdateVisibility(slowPass);
        }

        static void UpdateVisibility(bool buildings)
        {
            bool gameplay = MapBox.isRenderGameplay();
            if (_quality == null) _quality = World.world.GetComponent<QualityChanger>();
            bool zoomedOut = _quality.shouldRenderBuildings() && DebugConfig.isOn(DebugOption.ScaleEffectEnabled);
            foreach (Building b in World.world.buildings)
            {
                if (buildings) Access.BuildingVisible(b) = gameplay ? b.current_tile.zone.visible : zoomedOut;
                if (Access.TweenActive(Access.ScaleHelper(b))) Access.UpdateScaleTween(b);
            }

            foreach (Actor a in World.world.units)
            {
                if (!a.asset.has_sprite_renderer) continue;
                if (Access.InMagnet(a) || a.isInsideSomething()) Access.IsVisible(a) = false;
                else if (gameplay) Access.IsVisible(a) = a.current_tile.zone.visible;
                else Access.IsVisible(a) = a.asset.visible_on_minimap;
            }
        }

        static void Apply(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            MapBox world = World.world;

            OnHostTime(r.ReadDouble());
            bool meta = r.ReadBoolean();
            if (meta) MetaSync.Read(r);

            WorldTile[] tiles = TilesList(world);
            int tileCount = r.ReadInt32();
            for (int i = 0; i < tileCount; i++)
            {
                int id = r.ReadInt32();
                string main = r.ReadString();
                string top = r.ReadString();
                bool fire = r.ReadBoolean();
                if (id < 0 || id >= tiles.Length) continue;
                ApplyTile(tiles[id], main, top, fire);
            }

            int goneBuildings = r.ReadInt32();
            for (int i = 0; i < goneBuildings; i++)
            {
                Building b = world.buildings.get(r.ReadInt64());
                if (b != null && Access.BuildingData(b).state != BuildingState.Removed) RemoveBuildingFinal(b);
            }
            var newBuildings = new List<BuildingData>();
            int newBuildingCount = r.ReadInt32();
            for (int i = 0; i < newBuildingCount; i++)
            {
                var bd = Json.Read<BuildingData>(r.ReadString());
                Building old = world.buildings.get(bd.id);
                if (old != null && Access.BuildingData(old).state != BuildingState.Removed) RemoveBuildingFinal(old);
                newBuildings.Add(bd);
            }

            int goneActors = r.ReadInt32();
            for (int i = 0; i < goneActors; i++)
            {
                long id = r.ReadInt64();
                Actor a = world.units.get(id);
                if (a != null)
                {
                    RemotePossession.OnClientActorRemoved(a);
                    ScheduleActorDestroy(world.units, a);
                }
                _tracks.Remove(id);
            }
            var newActors = new List<ActorData>();
            int newActorCount = r.ReadInt32();
            for (int i = 0; i < newActorCount; i++)
            {
                var ad = Json.Read<ActorData>(r.ReadString());
                Actor old = world.units.get(ad.id);
                if (old != null)
                {
                    if (ControllableUnit.isControllingUnit(old)) _possessedReloads[ad.id] = old;
                    ScheduleActorDestroy(world.units, old);
                }
                newActors.Add(ad);
            }

            CheckObjectsToDestroy(world);

            foreach (BuildingData bd in newBuildings)
            {
                if (world.buildings.loadObject(bd) == null)
                    Fail("building " + bd.id + " " + bd.asset_id + " at " + bd.mainX + ":" + bd.mainY +
                         (world.buildings.get(bd.id) != null ? " (id still loaded)" : " (cannot build there)"));
            }
            world.buildings.checkContainer();
            foreach (ActorData ad in newActors)
            {
                Actor a = world.units.loadObject(ad);
                if (a == null)
                {
                    Fail("actor " + ad.id + " " + ad.asset_id + " at " + ad.x + ":" + ad.y +
                         (world.units.get(ad.id) != null ? " (id still loaded)" : ""));
                    continue;
                }
                List<Sample> track = Track(ad.id);
                track.Clear();
                track.Add(new Sample { Time = _packetTime, Pos = a.current_position, Height = a.position_height, Flip = Access.Flip(a), Moving = a.is_moving });
                if (_possessedReloads.TryGetValue(ad.id, out Actor was)) RemotePossession.OnClientActorReplaced(was, a);
            }
            _possessedReloads.Clear();
            world.units.checkContainer();

            int movedCount = r.ReadInt32();
            for (int i = 0; i < movedCount; i++)
            {
                long id = r.ReadInt64();
                var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
                float height = r.ReadSingle();
                byte flags = r.ReadByte();
                Actor a = world.units.get(id);
                if (a == null) continue;
                AddSample(a, id, pos, height, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
            }

            ActionSync.Read(r, _packetTime);
            EffectSync.Read(r);
            if (meta) MetaSync.FinishApply();
            if (r.ReadBoolean()) Checksum.Compare(r);
        }

        static int _kingdomFixes;

        static void FixBuildingKingdoms(MapBox world)
        {
            foreach (Building b in world.buildings)
            {
                if (b.kingdom != null || !b.isAlive()) continue;
                BuildingData d = Access.BuildingData(b);
                City city = d.cityID.hasValue() ? world.cities.get(d.cityID) : null;
                Kingdom k = (city != null ? Access.CityKingdom(city) : null) ?? WildKingdomsManager.neutral;
                if (_kingdomFixes++ < 10)
                    Debug.Log("[WorldBoxMP] building " + b.getID() + " " + Access.BuildingAsset(b).id + " had no kingdom; city " +
                              d.cityID + (city == null ? " (missing)" : "") + " -> " + k.name);
                b.setKingdom(k);
            }
        }

        static void Fail(string what)
        {
            _failures++;
            if (_failures <= 30) Debug.Log("[WorldBoxMP] stream: could not load " + what);
        }

        static void ApplyTile(WorldTile tile, string main, string top, bool fire)
        {
            TileType mainType = AssetManager.tiles.get(main);
            if (mainType == null) return;
            TopTileType topType = string.IsNullOrEmpty(top) ? null : AssetManager.top_tiles.get(top);
            if (tile.main_type != mainType || tile.top_type != topType)
            {
                tile.setTileTypes(mainType, topType);
                tile.Height = tile.Type.height_min;
            }
            if (tile.isOnFire() != fire) tile.setFireData(fire);
            World.world.setTileDirty(tile);
        }

        static List<Sample> Track(long id)
        {
            if (!_tracks.TryGetValue(id, out List<Sample> track))
            {
                track = new List<Sample>(8);
                _tracks[id] = track;
            }
            return track;
        }

        static void OnHostTime(double hostTime)
        {
            double now = Clock.Now;
            if (_newestHostTime >= 0)
            {
                double gap = hostTime - _newestHostTime;
                if (gap > 0 && gap < 0.5) _gap += (gap - _gap) * 0.1;
            }
            _packetTime = hostTime;
            if (hostTime > _newestHostTime)
            {
                _newestHostTime = hostTime;
                _newestLocalTime = now;
            }
            if (!_clockSet)
            {
                _clockSet = true;
                _renderTime = hostTime - Delay;
            }
        }

        static double Delay => Math.Max(0.06, Math.Min(0.3, _gap * 2 + 0.02));

        static void AddSample(Actor a, long id, Vector2 pos, float height, bool flip, bool moving, bool underForces)
        {
            List<Sample> track = Track(id);
            double time = _packetTime;
            OwnUnit.OnHostPosition(a, pos, underForces);
            if (track.Count > 0)
            {
                Sample last = track[track.Count - 1];
                if ((last.Pos - pos).sqrMagnitude > 25f)
                {
                    track.Clear();
                    Place(a, pos, height);
                }
                else if (time - last.Time > _gap * 1.6)
                {
                    last.Time = time - _gap;
                    track.Add(last);
                }
            }
            track.Add(new Sample { Time = time, Pos = pos, Height = height, Flip = flip, Moving = moving });
            while (track.Count > 2 && track[1].Time < _renderTime - 1.0) track.RemoveAt(0);
        }

        static void Glide()
        {
            if (!_clockSet) return;
            double now = Clock.Now;
            double dt = Math.Min(0.25, Time.unscaledDeltaTime);
            _renderTime += dt;
            double want = _newestHostTime + (now - _newestLocalTime) - Delay;
            double off = want - _renderTime;
            if (Math.Abs(off) > 0.5) _renderTime = want;
            else _renderTime += off * Math.Min(1.0, dt * 2);

            MapBox world = World.world;
            _lost.Clear();
            foreach (var kv in _tracks)
            {
                Actor a = world.units.get(kv.Key);
                if (a == null) { _lost.Add(kv.Key); continue; }
                List<Sample> track = kv.Value;
                if (track.Count == 0) continue;

                Sample s = track[0];
                Vector2 pos = s.Pos;
                float height = s.Height;
                if (_renderTime > s.Time)
                {
                    int i = 1;
                    while (i < track.Count && track[i].Time <= _renderTime) i++;
                    if (i >= track.Count)
                    {
                        s = track[track.Count - 1];
                        pos = s.Pos;
                        height = s.Height;
                    }
                    else
                    {
                        Sample from = track[i - 1], to = track[i];
                        double span = to.Time - from.Time;
                        float t = span > 1e-5 ? (float)((_renderTime - from.Time) / span) : 1f;
                        pos = Vector2.Lerp(from.Pos, to.Pos, t);
                        height = Mathf.Lerp(from.Height, to.Height, t);
                        s = from;
                    }
                }

                if (OwnUnit.Owns(kv.Key))
                {
                    a.position_height = height;
                    continue;
                }
                Access.IsMoving(a) = s.Moving;
                if (!ControllableUnit.isControllingUnit(a)) Access.Flip(a) = s.Flip;
                if ((a.current_position - pos).sqrMagnitude > 1e-6f || Mathf.Abs(a.position_height - height) > 0.0001f)
                    Place(a, pos, height);
            }
            foreach (long id in _lost) _tracks.Remove(id);
        }

        static void Turn()
        {
            if (_flipFailures > 3) return;
            try
            {
                float elapsed = Time.unscaledDeltaTime;
                HashSet<Actor> controlled = ControllableUnit.getCotrolledUnits();
                if (controlled != null && controlled.Count > 0 && InputHelpers.mouseSupported)
                {
                    Vector2 mouse = WorldfallBridge.InFirstPerson && WorldfallBridge.TryAimPoint(out Vector2 aim) ? aim : World.world.getMousePos();
                    foreach (Actor a in controlled)
                        if (a != null && a.isAlive()) a.checkFlipAgainstTargetPosition(mouse);
                }
                foreach (Actor a in World.world.units)
                    if (a.isAlive() && !a.isInsideSomething()) UpdateFlipRotation(a, elapsed);
            }
            catch (Exception e)
            {
                _flipFailures++;
                Debug.LogError("[WorldBoxMP] turning units failed: " + e);
            }
        }

        internal static void Place(Actor a, Vector2 pos, float height)
        {
            a.current_position = pos;
            a.position_height = height;
            WorldTile tile = World.world.GetTile((int)pos.x, (int)pos.y);
            if (tile != null) a.current_tile = tile;
        }
    }
}
