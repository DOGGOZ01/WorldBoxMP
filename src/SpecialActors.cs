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
        static readonly Dictionary<long, byte> _friendOwned = new Dictionary<long, byte>();
        static string _expectedAsset;
        static byte _expectedSlot = 1;
        static float _expectedUntil;

        public static void ExpectFriendCreature(string assetId)
        {
            _expectedAsset = assetId;
            _expectedSlot = Net.Current != null ? Net.Current.Slot : (byte)1;
            _expectedUntil = Time.unscaledTime + 15f;
        }
        static readonly List<long> _gone = new List<long>();
        static readonly Dictionary<long, State> _mirrors = new Dictionary<long, State>();
        static bool _spawning;
        static bool _driving;
        static State _drive;

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
            foreach (long gone in new List<long>(_friendOwned.Keys)) if (!ids.Contains(gone)) _friendOwned.Remove(gone);
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
                w.Write(_friendOwned.TryGetValue(id, out byte owner) ? owner : (byte)0);
                if (isNew)
                {
                    a.prepareForSave();
                    w.Write(Json.Write(Access.ActorData(a)));
                    SessionLog.Write("special creature sent: " + id + " " + a.asset.id);
                }
                w.Write(a.current_position.x);
                w.Write(a.current_position.y);
                if (log) SessionLog.Write("special creature " + id + " " + a.asset.id + " at " + a.current_position.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                                          a.current_position.y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + (_friendOwned.ContainsKey(id) ? " (slot " + _friendOwned[id] + ")" : " (host's)") + " crab updates " + _crabUpdates);
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
                Access.SpecialActorsScheduleActorDestroy(world.units, a);
            }
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                bool isNew = r.ReadBoolean();
                byte ownerSlot = r.ReadByte();
                bool mine = ownerSlot != 0 && ownerSlot == Roster.LocalSlot;
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
            }
            WorldTile tile = World.world.GetTile((int)pos.x, (int)pos.y);
            if (tile != null) a.current_tile = tile;
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
                    _friendOwned[pActor.getID()] = Net.Current != null ? Net.Current.Slot : _expectedSlot;
                    _expectedAsset = null;
                    SessionLog.Write("special creature " + pActor.getID() + " " + pActor.asset.id + " belongs to a player");
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
                if (Net.Role != Role.Host || _driving || Access.ComponentActor(__instance) == null || !_friendOwned.TryGetValue(Access.ComponentActor(__instance).getID(), out byte slot)) return;
                RemotePossession.FriendInput(slot, out Vector2 move, out Vector2 mouse, out bool attack);
                BeginDrive(new State { Pos = Access.ComponentActor(__instance).current_position, Beam = attack, Move = move, Moving = move != Vector2.zero, Aim = mouse });
                __state = true;
            }

            static void Finalizer(bool __state)
            {
                if (__state) EndDrive();
            }
        }

        static Vector2 _savedMove;
        static bool _savedAttack;

        static void BeginDrive(State s)
        {
            _drive = s;
            _savedMove = Access.MovementField();
            _savedAttack = Access.AttackLeftField();
            Access.MovementField() = s.Move;
            Access.AttackLeftField() = s.Beam;
            _driving = true;
        }

        static void EndDrive()
        {
            Access.MovementField() = _savedMove;
            Access.AttackLeftField() = _savedAttack;
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
