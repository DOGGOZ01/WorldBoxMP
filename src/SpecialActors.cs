using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class SpecialActors
    {
        struct State
        {
            public Vector2 Pos;
            public bool Beam;
            public Vector2 Move;
            public bool Moving;
            public Vector2 Aim;
        }

        static readonly HashSet<long> _sent = new HashSet<long>();
        static readonly HashSet<long> _friendOwned = new HashSet<long>();
        static string _expectedAsset;
        static float _expectedUntil;

        public static void ExpectFriendCreature(string assetId)
        {
            _expectedAsset = assetId;
            _expectedUntil = Time.unscaledTime + 15f;
        }
        static readonly List<long> _gone = new List<long>();
        static readonly Dictionary<long, State> _mirrors = new Dictionary<long, State>();
        static bool _spawning;
        static bool _driving;
        static State _drive;

        static readonly AccessTools.FieldRef<BaseActorComponent, Actor> ComponentActor =
            AccessTools.FieldRefAccess<BaseActorComponent, Actor>("actor");
        static readonly Action<ActorManager, Actor> ScheduleActorDestroy =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));

        public static int Count => Net.Role == Role.Host ? _sent.Count : _mirrors.Count;

        public static bool IsSpecial(Actor a) => a != null && a.asset != null && a.asset.skip_save;

        public static void ResetHost() => _sent.Clear();

        static readonly HashSet<long> _ownedHere = new HashSet<long>();

        public static void ResetClient()
        {
            _mirrors.Clear();
            _driving = false;
        }

        static float _logTimer;
        static int _crabUpdates;

        public static void Write(BinaryWriter w)
        {
            _logTimer -= Time.unscaledDeltaTime;
            bool log = _logTimer <= 0f;
            if (log) _logTimer = 2f;
            var now = new List<Actor>();
            foreach (Actor a in World.world.units)
                if (a.isAlive() && IsSpecial(a)) now.Add(a);
            var ids = new HashSet<long>();
            foreach (Actor a in now) ids.Add(a.getID());
            _gone.Clear();
            foreach (long id in _sent) if (!ids.Contains(id)) _gone.Add(id);
            _friendOwned.RemoveWhere(id => !ids.Contains(id));
            w.Write(_gone.Count);
            foreach (long id in _gone)
            {
                w.Write(id);
                _sent.Remove(id);
                SessionLog.Write("special creature gone: " + id);
            }
            w.Write(now.Count);
            foreach (Actor a in now)
            {
                long id = a.getID();
                bool isNew = _sent.Add(id);
                w.Write(id);
                w.Write(isNew);
                w.Write(_friendOwned.Contains(id));
                if (isNew)
                {
                    a.prepareForSave();
                    w.Write(Json.Write(Access.ActorData(a)));
                    SessionLog.Write("special creature sent: " + id + " " + a.asset.id);
                }
                w.Write(a.current_position.x);
                w.Write(a.current_position.y);
                if (log) SessionLog.Write("special creature " + id + " " + a.asset.id + " at " + a.current_position.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                                          a.current_position.y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + (_friendOwned.Contains(id) ? " (second player's)" : " (host's)") + " crab updates " + _crabUpdates);
                Crabzilla crab = a.getActorComponent<Crabzilla>();
                bool controlled = ControllableUnit.isControllingUnit(a);
                w.Write(crab != null && crab.isBeamEnabled());
                Vector2 move = controlled ? ControllableUnit.getMovementVector() : Vector2.zero;
                w.Write(move.x);
                w.Write(move.y);
                w.Write(controlled && ControllableUnit.isMovementActionActive());
                Vector2 aim = crab != null && crab.armTarget != null ? (Vector2)crab.armTarget.transform.position : a.current_position;
                w.Write(aim.x);
                w.Write(aim.y);
            }
        }

        public static void Read(BinaryReader r)
        {
            MapBox world = World.world;
            int gone = r.ReadInt32();
            for (int i = 0; i < gone; i++)
            {
                long id = r.ReadInt64();
                _mirrors.Remove(id);
                Actor a = world.units.get(id);
                if (a == null) continue;
                try { MusicBox.inst.stopDrawingSounds(); } catch { }
                ScheduleActorDestroy(world.units, a);
            }
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                bool isNew = r.ReadBoolean();
                bool mine = r.ReadBoolean();
                string json = isNew ? r.ReadString() : null;
                var s = new State
                {
                    Pos = new Vector2(r.ReadSingle(), r.ReadSingle()),
                    Beam = r.ReadBoolean(),
                    Move = new Vector2(r.ReadSingle(), r.ReadSingle()),
                    Moving = r.ReadBoolean(),
                    Aim = new Vector2(r.ReadSingle(), r.ReadSingle()),
                };
                if (json != null && world.units.get(id) == null) Spawn(json, mine);
                _mirrors[id] = s;
            }
        }

        static void Spawn(string json, bool mine)
        {
            ActorData d = Json.Read<ActorData>(json);
            _spawning = !mine;
            try
            {
                Actor a = World.world.units.loadObject(d);
                World.world.units.checkContainer();
                SessionLog.Write("special creature shown: " + d.id + " " + d.asset_id + (a == null ? " (could not load)" : ""));
            }
            catch (Exception e)
            {
                SessionLog.Write("special creature " + d.asset_id + " failed: " + e.GetBaseException().Message);
            }
            finally { _spawning = false; }
        }

        public static void UpdateClient()
        {
            if (_mirrors.Count == 0) return;
            float elapsed = Access.Elapsed(World.world);
            foreach (var kv in _mirrors)
            {
                Actor a = World.world.units.get(kv.Key);
                if (a == null || !a.isAlive()) continue;
                BeginDrive(kv.Value);
                try { a.u2_updateChildren(elapsed); }
                catch (Exception e) { Debug.Log("[WorldBoxMP] special creature update: " + e.GetBaseException().Message); }
                finally { EndDrive(); }
                Place(a, kv.Value.Pos);
            }
        }

        static void Place(Actor a, Vector2 pos)
        {
            Crabzilla crab = a.getActorComponent<Crabzilla>();
            if (crab != null)
            {
                crab.transform.position = new Vector3(pos.x, pos.y, crab.z_pos);
                a.current_position = crab.transform.position;
            }
            else
            {
                a.current_position = pos;
                if (a.avatar != null) a.avatar.transform.position = new Vector3(pos.x, pos.y, a.avatar.transform.position.z);
            }
            Access.DirtyTile(a) = true;
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.setControllableCreature))]
        static class CopyIsNotPossessed
        {
            static bool Prefix(Actor pActor)
            {
                if (_spawning) return false;
                bool expected = _expectedAsset != null && Time.unscaledTime < _expectedUntil && pActor != null && pActor.asset.id == _expectedAsset;
                if (Net.Role == Role.Host && (Powers.ApplyingRemote || expected) && IsSpecial(pActor))
                {
                    _friendOwned.Add(pActor.getID());
                    _expectedAsset = null;
                    SessionLog.Write("special creature " + pActor.getID() + " " + pActor.asset.id + " belongs to the second player");
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Crabzilla), nameof(Crabzilla.update))]
        static class FriendSteersTheirCrab
        {
            static void Prefix(Crabzilla __instance, out bool __state)
            {
                __state = false;
                _crabUpdates++;
                if (Net.Role != Role.Host || _driving || ComponentActor(__instance) == null || !_friendOwned.Contains(ComponentActor(__instance).getID())) return;
                RemotePossession.FriendInput(out Vector2 move, out Vector2 mouse, out bool attack);
                BeginDrive(new State { Pos = ComponentActor(__instance).current_position, Beam = attack, Move = move, Moving = move != Vector2.zero, Aim = mouse });
                __state = true;
            }

            static void Finalizer(bool __state)
            {
                if (__state) EndDrive();
            }
        }

        static readonly AccessTools.FieldRef<Vector2> MovementField =
            AccessTools.StaticFieldRefAccess<Vector2>(AccessTools.Field(typeof(ControllableUnit), "_movement_vector"));
        static readonly AccessTools.FieldRef<bool> AttackLeftField =
            AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(ControllableUnit), "_attack_pressed_button_left"));
        static Vector2 _savedMove;
        static bool _savedAttack;

        static void BeginDrive(State s)
        {
            _drive = s;
            _savedMove = MovementField();
            _savedAttack = AttackLeftField();
            MovementField() = s.Move;
            AttackLeftField() = s.Beam;
            _driving = true;
        }

        static void EndDrive()
        {
            MovementField() = _savedMove;
            AttackLeftField() = _savedAttack;
            _driving = false;
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.isMovementActionActive))]
        static class HostMoving
        {
            static void Postfix(ref bool __result) { if (_driving) __result = _drive.Moving; }
        }

        [HarmonyPatch(typeof(MapBox), nameof(MapBox.getMousePos))]
        static class HostAim
        {
            static void Postfix(ref Vector2 __result) { if (_driving) __result = _drive.Aim; }
        }
    }
}
