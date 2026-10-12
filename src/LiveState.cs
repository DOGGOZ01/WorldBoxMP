using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ai.behaviours;

namespace WorldBoxMP
{
    public static class LiveState
    {
        static readonly Type AiBase = typeof(AiSystemActor).BaseType;
        static readonly FieldInfo AiTask = AccessTools.Field(AiBase, "task");
        static readonly FieldInfo AiAction = AccessTools.Field(AiBase, "action");
        static readonly FieldInfo AiActionIndex = AccessTools.Field(AiBase, "action_index");
        static readonly AccessTools.FieldRef<Actor, AiSystemActor> ActorAi = AccessTools.FieldRefAccess<Actor, AiSystemActor>("ai");
        static readonly AccessTools.FieldRef<Actor, double> SocialTween = AccessTools.FieldRefAccess<Actor, double>("timestamp_tween_session_social");
        static readonly AccessTools.FieldRef<Actor, double> AteAt = AccessTools.FieldRefAccess<Actor, double>("timestamp_session_ate_food");
        static readonly AccessTools.FieldRef<Actor, string> AteWhat = AccessTools.FieldRefAccess<Actor, string>("ate_last_item_id");
        static readonly AccessTools.FieldRef<Actor, WorldTile> BehTileTarget = AccessTools.FieldRefAccess<Actor, WorldTile>("beh_tile_target");
        static readonly AccessTools.FieldRef<City, City> TargetAttackCity = AccessTools.FieldRefAccess<City, City>("target_attack_city");
        static readonly AccessTools.FieldRef<City, Kingdom> BeingCapturedBy = AccessTools.FieldRefAccess<City, Kingdom>("being_captured_by");
        static readonly AccessTools.FieldRef<BehaviourActionActor, bool> Socialize = AccessTools.FieldRefAccess<BehaviourActionActor, bool>("socialize");
        static readonly FieldInfo DeathsCivs = AccessTools.Field(typeof(BattleContainer), "_deaths_civs");
        static readonly FieldInfo DeathsMobs = AccessTools.Field(typeof(BattleContainer), "_deaths_mobs");

        static readonly Dictionary<long, string> _sentAi = new Dictionary<long, string>();
        static readonly Dictionary<long, string> _sentCity = new Dictionary<long, string>();
        static readonly Dictionary<long, int> _sentCaptain = new Dictionary<long, int>();
        static readonly List<KeyValuePair<long, string>> _ate = new List<KeyValuePair<long, string>>();
        static readonly List<KeyValuePair<int, bool>> _kills = new List<KeyValuePair<int, bool>>();
        static readonly List<string> _log = new List<string>();
        static readonly HashSet<long> _seen = new HashSet<long>();
        static readonly List<long> _drop = new List<long>();
        static float _warTimer;

        public static int Sent, Applied;

        public static void ResetHost()
        {
            _sentAi.Clear();
            _sentCity.Clear();
            _sentCaptain.Clear();
            _ate.Clear();
            _kills.Clear();
            _log.Clear();
        }

        static AiSystemActor Ai(Actor a) => ActorAi(a);

        public static void ResendActor(long id) => _sentAi.Remove(id);

        static string AiKey(Actor a)
        {
            AiSystemActor ai = Ai(a);
            if (ai == null) return "";
            var task = AiTask.GetValue(ai) as BehaviourTaskActor;
            if (task == null) return "";
            return task.id + "|" + (int)AiActionIndex.GetValue(ai);
        }

        public static void Write(BinaryWriter w, float elapsed)
        {
            var changes = new List<KeyValuePair<long, string>>();
            _seen.Clear();
            foreach (Actor a in World.world.units)
            {
                if (!HostSync.Syncable(a) || ViewSync.Priority(a.current_position) > 1) continue;
                long id = a.getID();
                _seen.Add(id);
                string key = AiKey(a);
                if (_sentAi.TryGetValue(id, out string old) && old == key) continue;
                _sentAi[id] = key;
                changes.Add(new KeyValuePair<long, string>(id, key));
            }
            _drop.Clear();
            foreach (long id in _sentAi.Keys) if (!_seen.Contains(id)) _drop.Add(id);
            foreach (long id in _drop) _sentAi.Remove(id);
            w.Write(changes.Count);
            foreach (var kv in changes) { w.Write(kv.Key); w.Write(kv.Value); }
            w.Write(_ate.Count);
            foreach (var kv in _ate) { w.Write(kv.Key); w.Write(kv.Value ?? ""); }
            Sent += changes.Count + _ate.Count;
            _ate.Clear();

            _warTimer -= elapsed;
            bool war = _warTimer <= 0f;
            w.Write(war);
            if (war)
            {
                _warTimer = 0.3f;
                var cities = new List<KeyValuePair<long, string>>();
                var captains = new List<KeyValuePair<long, int>>();
                foreach (City c in World.world.cities)
                {
                    City target = TargetAttackCity(c);
                    Kingdom capturer = BeingCapturedBy(c);
                    string key = (target != null && target.isAlive() ? target.getID() : -1) + "|" +
                                 (capturer != null && capturer.isAlive() ? capturer.getID() : -1) + "|" + c.last_visual_capture_ticks;
                    if (!_sentCity.TryGetValue(c.getID(), out string was) || was != key)
                    {
                        _sentCity[c.getID()] = key;
                        cities.Add(new KeyValuePair<long, string>(c.getID(), key));
                    }
                    if (c.army != null && c.army.hasCaptain())
                    {
                        Actor cap = c.army.getCaptain();
                        WorldTile dest = cap != null ? BehTileTarget(cap) : null;
                        int tile = dest != null ? dest.data.tile_id : -1;
                        if (cap != null && (!_sentCaptain.TryGetValue(cap.getID(), out int t) || t != tile))
                        {
                            _sentCaptain[cap.getID()] = tile;
                            captains.Add(new KeyValuePair<long, int>(cap.getID(), tile));
                        }
                    }
                }
                w.Write(cities.Count);
                foreach (var kv in cities) { w.Write(kv.Key); w.Write(kv.Value); }
                w.Write(captains.Count);
                foreach (var kv in captains) { w.Write(kv.Key); w.Write(kv.Value); }
                Sent += cities.Count + captains.Count;
            }

            w.Write(_kills.Count);
            foreach (var k in _kills) { w.Write(k.Key); w.Write(k.Value); }
            _kills.Clear();

            w.Write(_log.Count);
            foreach (string m in _log) w.Write(m);
            Sent += _log.Count;
            _log.Clear();
        }

        public static void Read(BinaryReader r)
        {
            MapBox world = World.world;
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                string key = r.ReadString();
                Actor a = world.units.get(id);
                if (a != null && a.isAlive()) SetAi(a, key);
            }
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                Actor a = world.units.get(r.ReadInt64());
                string food = r.ReadString();
                if (a == null || AssetManager.resources.get(food) == null) continue;
                AteWhat(a) = food;
                AteAt(a) = world.getCurSessionTime();
            }

            if (r.ReadBoolean())
            {
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    City c = world.cities.get(r.ReadInt64());
                    string[] parts = r.ReadString().Split('|');
                    if (c == null || parts.Length < 3) continue;
                    long target = long.Parse(parts[0]), capturer = long.Parse(parts[1]);
                    TargetAttackCity(c) = target >= 0 ? world.cities.get(target) : null;
                    BeingCapturedBy(c) = capturer >= 0 ? world.kingdoms.get(capturer) : null;
                    c.last_visual_capture_ticks = int.Parse(parts[2]);
                    Applied++;
                }
                n = r.ReadInt32();
                WorldTile[] tiles = Access.TilesList(world);
                for (int i = 0; i < n; i++)
                {
                    Actor cap = world.units.get(r.ReadInt64());
                    int tile = r.ReadInt32();
                    if (cap != null) BehTileTarget(cap) = tile >= 0 && tile < tiles.Length ? tiles[tile] : null;
                }
            }

            n = r.ReadInt32();
            for (int i = 0; i < n; i++) Battle(r.ReadInt32(), r.ReadBoolean());

            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                string json = r.ReadString();
                try
                {
                    WorldLogMessage m = ReadLog(json);
                    _replayingLog = true;
                    m.add();
                    Applied++;
                    SessionLog.Write("world log: " + m.asset_id + " " + m.special1 + (m.x.HasValue ? " at " + m.x + ":" + m.y : ""));
                }
                catch (Exception e)
                {
                    SessionLog.Write("world log message failed: " + e.GetBaseException().Message);
                }
                finally { _replayingLog = false; }
            }
        }

        static void SetAi(Actor a, string key)
        {
            AiSystemActor ai = Ai(a);
            if (ai == null) return;
            var wasAction = AiAction.GetValue(ai) as BehaviourActionActor;
            bool wasSocial = wasAction != null && Socialize(wasAction);
            BehaviourTaskActor task = null;
            BehaviourActionActor action = null;
            int index = 0;
            int bar = key.LastIndexOf('|');
            if (bar > 0)
            {
                task = AssetManager.tasks_actor.get(key.Substring(0, bar));
                int.TryParse(key.Substring(bar + 1), out index);
                if (task != null && index >= 0 && index < task.list.Count) action = task.list[index];
            }
            AiTask.SetValue(ai, task);
            AiAction.SetValue(ai, action);
            AiActionIndex.SetValue(ai, index);
            if (action != null && Socialize(action) && !wasSocial) SocialTween(a) = World.world.getCurSessionTime();
            Applied++;
        }

        static void Battle(int tileId, bool sapient)
        {
            WorldTile[] tiles = Access.TilesList(World.world);
            if (tileId < 0 || tileId >= tiles.Length) return;
            WorldTile tile = tiles[tileId];
            HashSet<BattleContainer> all = BattleKeeperManager.get();
            if (all == null) return;
            BattleContainer near = null;
            foreach (BattleContainer b in all)
                if (Toolbox.SquaredDistTile(b.tile, tile) < 1600f) { near = b; break; }
            if (near == null && !sapient) return;
            if (near == null)
            {
                near = new BattleContainer { tile = tile };
                all.Add(near);
            }
            FieldInfo f = sapient ? DeathsCivs : DeathsMobs;
            f.SetValue(near, (int)f.GetValue(near) + 1);
            if (near.tile != tile && (Toolbox.SquaredDistTile(near.tile, tile) < 25f || near.getDeathsTotal() < 3)) near.tile = tile;
            near.timer = 1.2f;
            near.timeout = 1f;
            if (near.frame >= 7) near.frame = 0;
        }

        public static string CensusPart()
        {
            int talking = 0, targets = 0, capturing = 0;
            foreach (Actor a in World.world.units)
            {
                AiSystemActor ai = a.isAlive() ? Ai(a) : null;
                var action = ai != null ? AiAction.GetValue(ai) as BehaviourActionActor : null;
                if (action != null && Socialize(action)) talking++;
            }
            foreach (City c in World.world.cities)
            {
                if (TargetAttackCity(c) != null) targets++;
                if (BeingCapturedBy(c) != null) capturing++;
            }
            int battles = BattleKeeperManager.get()?.Count ?? 0;
            return " talking=" + talking + " battles=" + battles + " war_targets=" + targets + " capturing=" + capturing;
        }

        public static void UpdateClient()
        {
            if (BattleKeeperManager.get() != null) BattleKeeperManager.update(Access.Elapsed(World.world));
        }

        static bool Recording => HostSync.Tracking;
        static bool _replayingLog;

        [HarmonyPatch(typeof(Actor), nameof(Actor.consumeFoodResource))]
        static class CaptureAte
        {
            static void Postfix(Actor __instance, ResourceAsset pAsset)
            {
                if (Recording && pAsset != null && _ate.Count < 200 && ViewSync.Priority(__instance.current_position) <= 1)
                    _ate.Add(new KeyValuePair<long, string>(__instance.getID(), pAsset.id));
            }
        }

        [HarmonyPatch(typeof(BattleKeeperManager), nameof(BattleKeeperManager.addUnitKilled))]
        static class CaptureKill
        {
            static void Prefix(Actor pActor)
            {
                if (Recording && pActor != null && pActor.current_tile != null && _kills.Count < 300)
                    _kills.Add(new KeyValuePair<int, bool>(pActor.current_tile.data.tile_id, pActor.isSapient()));
            }
        }

        static string WriteLog(WorldLogMessage m)
        {
            var o = new Newtonsoft.Json.Linq.JObject
            {
                ["a"] = m.asset_id, ["t"] = m.timestamp,
                ["s1"] = m.special1, ["s2"] = m.special2, ["s3"] = m.special3,
                ["c1"] = m.color_special_1, ["c2"] = m.color_special_2, ["c3"] = m.color_special_3,
                ["u"] = m.unit_id, ["k"] = m.kingdom_id, ["x"] = m.x, ["y"] = m.y
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        static WorldLogMessage ReadLog(string json)
        {
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            var m = new WorldLogMessage();
            m.asset_id = (string)o["a"];
            m.timestamp = (int)o["t"];
            m.special1 = (string)o["s1"]; m.special2 = (string)o["s2"]; m.special3 = (string)o["s3"];
            m.color_special_1 = (string)o["c1"]; m.color_special_2 = (string)o["c2"]; m.color_special_3 = (string)o["c3"];
            m.unit_id = (long)o["u"];
            m.kingdom_id = (long)o["k"];
            m.x = (int?)o["x"];
            m.y = (int?)o["y"];
            return m;
        }

        [HarmonyPatch(typeof(WorldLogMessageExtensions), nameof(WorldLogMessageExtensions.add))]
        static class CaptureLog
        {
            static void Prefix(WorldLogMessage pMessage)
            {
                if (!Recording || _replayingLog || pMessage == null || _log.Count >= 50) return;
                try { _log.Add(WriteLog(pMessage)); }
                catch (Exception e) { Debug.Log("[WorldBoxMP] world log capture: " + e.GetBaseException().Message); }
            }
        }
    }
}
