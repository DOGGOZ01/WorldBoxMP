using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class RemotePossession
    {
        static AccessTools.FieldRef<T> Ref<T>(string field) =>
            AccessTools.StaticFieldRefAccess<T>(AccessTools.Field(typeof(ControllableUnit), field));

        static readonly AccessTools.FieldRef<Vector2> MovementVector = Ref<Vector2>("_movement_vector");
        static readonly AccessTools.FieldRef<Vector2> ClickVector = Ref<Vector2>("_click_vector");
        static readonly AccessTools.FieldRef<bool>[] Flags =
        {
            Ref<bool>("_attack_pressed_button_left"),
            Ref<bool>("_attack_pressed_button_right"),
            Ref<bool>("_attack_just_pressed_button_left"),
            Ref<bool>("_attack_just_pressed_button_right"),
            Ref<bool>("_action_pressed_jump"),
            Ref<bool>("_action_pressed_talk"),
            Ref<bool>("_action_pressed_dash"),
            Ref<bool>("_action_pressed_backstep"),
            Ref<bool>("_action_pressed_steal"),
            Ref<bool>("_action_pressed_swear"),
        };
        const int FirstEventFlag = 2;

        static readonly AccessTools.FieldRef<Actor> UnitMain = Ref<Actor>("_unit_main");
        static readonly AccessTools.FieldRef<HashSet<Actor>> Units = Ref<HashSet<Actor>>("_units");
        static readonly Action<Actor> AddPossessedStatus =
            AccessTools.MethodDelegate<Action<Actor>>(AccessTools.Method(typeof(ControllableUnit), "addStatus"));

        struct InputState
        {
            public Vector2 Movement, Click, Mouse;
            public bool[] Flags;
        }

        class Seat
        {
            public Guest Guest;
            public readonly HashSet<Actor> Units = new HashSet<Actor>();
            public InputState Input = new InputState { Flags = new bool[10] };
            public readonly HashSet<Actor> EventsConsumed = new HashSet<Actor>();
            public long MainId = -1;
            public Vector2 MainPos;
            public bool Walking;
            public double LastTime = -1000;
            public float StatusTimer, LogTimer;
        }

        static readonly Dictionary<Guest, Seat> _seats = new Dictionary<Guest, Seat>();
        static Seat _acting;
        static bool _inRemoteAction;

        static Seat SeatOf(Guest g)
        {
            if (g == null) return null;
            if (!_seats.TryGetValue(g, out Seat s)) _seats[g] = s = new Seat { Guest = g };
            return s;
        }

        static Seat SeatOf(Actor a)
        {
            if (a == null) return null;
            foreach (Seat s in _seats.Values) if (s.Units.Contains(a)) return s;
            return null;
        }

        static bool _suppressSend;
        static bool _wasControlling;
        static float _hostLogTimer;
        static float _inputTimer;
        static byte[] _lastInput;

        public static Vector2? TestMovement;

        public static bool IsRemote(Actor a) => SeatOf(a) != null;

        public static byte HolderSlot(Actor a) => SeatOf(a)?.Guest.Slot ?? 0;

        public static void FriendInput(byte slot, out Vector2 movement, out Vector2 mouse, out bool attackLeft)
        {
            Seat s = SeatOf(Net.GuestIn(slot));
            movement = s != null ? s.Input.Movement : Vector2.zero;
            mouse = s != null ? s.Input.Mouse : Vector2.zero;
            attackLeft = s != null && s.Input.Flags[0];
        }

        public static bool HasRemote
        {
            get
            {
                foreach (Seat s in _seats.Values) if (s.Units.Count > 0 || Clock.Now - s.LastTime < 60.0) return true;
                return false;
            }
        }

        static bool FollowsClient(Seat s, Actor a) => a != null && s != null && s.MainId >= 0 && a.getID() == s.MainId;

        public static void ClientUpdate()
        {
            bool controlling = ControllableUnit.isControllingUnit();
            if (controlling)
            {
                ControllableUnit.updateControllableUnit();
                if (TestMovement.HasValue) MovementVector() = TestMovement.Value;
                OwnUnit.Step();
                SendInput();
            }
            else if (_wasControlling)
            {
                Net.Peer?.Send(Msg.Unpossess, null);
                OwnUnit.Reset();
            }
            else if (OwnUnit.Main != null) OwnUnit.Reset();
            _wasControlling = controlling;
        }

        static void SendInput()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            Vector2 mv = MovementVector(), cv = ClickVector();
            Vector2 mouse = WorldfallBridge.InFirstPerson && WorldfallBridge.TryAimPoint(out Vector2 aim) ? aim : World.world.getMousePos();
            w.Write(mv.x); w.Write(mv.y);
            w.Write(cv.x); w.Write(cv.y);
            w.Write(mouse.x); w.Write(mouse.y);
            ushort bits = 0;
            for (int i = 0; i < Flags.Length; i++)
                if (Flags[i]()) bits |= (ushort)(1 << i);
            w.Write(bits);
            Actor main = OwnUnit.Main;
            w.Write(main != null);
            if (main != null)
            {
                w.Write(main.getID());
                w.Write(main.current_position.x);
                w.Write(main.current_position.y);
                w.Write(OwnUnit.Walking);
            }
            byte[] data = ms.ToArray();

            _inputTimer -= Time.unscaledDeltaTime;
            bool pressed = (bits >> FirstEventFlag) != 0;
            bool changed = _lastInput == null || !SameBytes(_lastInput, data);
            if (pressed || (changed && _inputTimer <= 0f) || _inputTimer <= -0.25f)
            {
                _inputTimer = 1f / 60f;
                _lastInput = data;
                Net.Peer?.Send(Msg.PossessInput, data);
            }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        static void SendPossess(IEnumerable<Actor> actors)
        {
            if (_suppressSend || !Net.IsClientConnected) return;
            var ids = new List<long>();
            foreach (Actor a in actors) if (a != null) ids.Add(a.getID());
            if (ids.Count == 0) return;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(ids.Count);
            foreach (long id in ids) w.Write(id);
            Net.Peer.Send(Msg.Possess, ms.ToArray());
            SessionLog.Write("possess sent: " + string.Join(",", ids));
        }

        public static void OnClientActorReplaced(Actor old, Actor fresh)
        {
            HashSet<Actor> units = Units();
            if (!units.Remove(old)) return;
            units.Add(fresh);
            if (UnitMain() == old) UnitMain() = fresh;
        }

        public static void OnClientActorRemoved(Actor a)
        {
            if (!ControllableUnit.isControllingUnit(a)) return;
            _suppressSend = true;
            try { ControllableUnit.remove(a); }
            finally { _suppressSend = false; }
        }

        public static bool HostControls(Actor a) => a != null && Units().Contains(a);

        public static void HostReceivePossess(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            var names = new List<string>();
            var refused = new List<long>();
            bool busy = false;
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                Actor a = World.world.units.get(id);
                if (a == null || !a.isAlive() || !a.canBePossessed()) continue;
                if (!SpecialActors.IsSpecial(a) && !Rules.HostAllows(Perm.Possess)) { refused.Add(id); continue; }
                Seat holder = SeatOf(a);
                if (HostControls(a) || (holder != null && holder.Guest != Net.Current)) { refused.Add(id); busy = true; continue; }
                SeatOf(Net.Current)?.Units.Add(a);
                AddPossessedStatus(a);
                names.Add(a.getID() + " " + a.asset.id);
            }
            if (names.Count > 0) SessionLog.Write(Net.Current?.Label + " possessed: " + string.Join(", ", names));
            if (refused.Count > 0) Rules.Deny(Perm.Possess, busy ? "tip_unit_busy" : "tip_denied", refused);
        }

        public static void HostReceiveUnpossess() => ReleaseGuest(Net.Current);

        public static void ReleaseGuest(Guest g)
        {
            Seat s = g != null && _seats.TryGetValue(g, out Seat found) ? found : null;
            if (s == null) return;
            foreach (Actor a in s.Units)
            {
                if (a == null || !a.isAlive()) continue;
                a.finishStatusEffect("possessed");
                a.cancelAllBeh();
                a.setPossessedMovement(false);
            }
            if (s.Units.Count > 0) SessionLog.Write(g.Label + " released " + s.Units.Count + " unit(s)");
            s.Units.Clear();
            s.MainId = -1;
            s.Walking = false;
        }

        public static void ForgetGuest(Guest g)
        {
            ReleaseGuest(g);
            if (g != null) _seats.Remove(g);
        }

        public static void HostReceiveInput(byte[] data)
        {
            Seat seat = SeatOf(Net.Current);
            if (seat == null || (seat.Units.Count == 0 && !Rules.HostAllows(Perm.Possess))) return;
            ref InputState input = ref seat.Input;
            var r = new BinaryReader(new MemoryStream(data));
            input.Movement = new Vector2(r.ReadSingle(), r.ReadSingle());
            input.Click = new Vector2(r.ReadSingle(), r.ReadSingle());
            input.Mouse = new Vector2(r.ReadSingle(), r.ReadSingle());
            ushort bits = r.ReadUInt16();
            for (int i = 0; i < 10; i++)
            {
                bool on = (bits & (1 << i)) != 0;
                if (i < FirstEventFlag) input.Flags[i] = on;
                else if (on) input.Flags[i] = true;
            }
            if ((bits >> FirstEventFlag) != 0) seat.EventsConsumed.Clear();
            if (r.ReadBoolean())
            {
                seat.MainId = r.ReadInt64();
                seat.MainPos = new Vector2(r.ReadSingle(), r.ReadSingle());
                seat.Walking = r.ReadBoolean();
            }
            else
            {
                seat.MainId = -1;
                seat.Walking = false;
            }
        }

        public static void HostUpdate()
        {
            foreach (Seat seat in _seats.Values)
            {
                seat.Units.RemoveWhere(a => a == null || !a.isAlive());
                if (seat.Units.Count == 0) continue;
                seat.LastTime = Clock.Now;
                seat.LogTimer -= Time.unscaledDeltaTime;
                if (seat.LogTimer <= 0f)
                {
                    seat.LogTimer = 1f;
                    foreach (Actor a in seat.Units)
                        SessionLog.Write(seat.Guest.Label + "'s unit " + a.getID() + " at " + a.current_position.x.ToString("0.0") + ":" +
                                         a.current_position.y.ToString("0.0") + " input " + seat.Input.Movement);
                }
                seat.StatusTimer -= Time.unscaledDeltaTime;
                bool active = seat.Input.Movement != Vector2.zero || seat.Input.Flags[0] || seat.Input.Flags[1];
                for (int i = FirstEventFlag; i < 10 && !active; i++) active = seat.Input.Flags[i];
                if (active || seat.StatusTimer <= 0f)
                {
                    seat.StatusTimer = 2f;
                    foreach (Actor a in seat.Units)
                    {
                        AddPossessedStatus(a);
                        a.stopSleeping();
                        a.next_step_position = a.next_step_position_possession;
                    }
                }
                HostFollow(seat);
            }
        }

        static void HostFollow(Seat seat)
        {
            if (seat.MainId < 0) return;
            Actor a = World.world.units.get(seat.MainId);
            if (a == null || !seat.Units.Contains(a) || Access.UnderForces(a)) return;
            Vector2 cur = a.current_position;
            Vector2 diff = seat.MainPos - cur;
            if (diff.sqrMagnitude < 1e-6f) return;
            Vector2 pos = diff.sqrMagnitude > 64f ? seat.MainPos : cur + diff * Mathf.Min(1f, Time.unscaledDeltaTime * 20f);
            a.current_position = pos;
            a.next_step_position = pos;
            a.next_step_position_possession = pos;
            Access.DirtyTile(a) = true;
            if (InputHelpers.mouseSupported) a.checkFlipAgainstTargetPosition(seat.Input.Mouse);
        }

        public static void HostClear()
        {
            _seats.Clear();
            _acting = null;
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.setControllableCreature))]
        static class ClientReportsPossession
        {
            static void Postfix(Actor pActor)
            {
                if (Net.IsClientConnected && ControllableUnit.isControllingUnit(pActor)) SendPossess(new[] { pActor });
            }
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.isControllingUnit), typeof(Actor))]
        static class HostCountsRemoteUnits
        {
            static void Postfix(Actor pUnit, ref bool __result)
            {
                if (!__result && _seats.Count > 0 && SeatOf(pUnit) != null) __result = true;
            }
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.remove))]
        static class HostForgetsDeadRemoteUnits
        {
            static void Postfix(Actor pActor) => SeatOf(pActor)?.Units.Remove(pActor);
        }

        [HarmonyPatch(typeof(StatusLibrary), "possessedAction")]
        static class SwapInputForRemoteUnits
        {
            struct Saved
            {
                public bool Active;
                public Vector2 Movement, Click;
                public bool[] Flags;
                public Actor Actor;
            }

            static void Prefix(BaseSimObject pTarget, out Saved __state)
            {
                __state = default;
                Actor a = pTarget as Actor;
                Seat seat = SeatOf(a);
                if (seat == null) return;
                __state = new Saved { Active = true, Movement = MovementVector(), Click = ClickVector(), Flags = new bool[10], Actor = a };
                for (int i = 0; i < 10; i++) __state.Flags[i] = Flags[i]();

                MovementVector() = FollowsClient(seat, a) ? Vector2.zero : seat.Input.Movement;
                ClickVector() = seat.Input.Click;
                bool fresh = seat.EventsConsumed.Add(a);
                for (int i = 0; i < 10; i++)
                    Flags[i]() = i < FirstEventFlag ? seat.Input.Flags[i] : fresh && seat.Input.Flags[i];
                _acting = seat;
                _inRemoteAction = true;
            }

            static void Finalizer(Saved __state)
            {
                if (!__state.Active) return;
                Seat seat = _acting;
                _inRemoteAction = false;
                _acting = null;
                if (seat != null && FollowsClient(seat, __state.Actor))
                {
                    __state.Actor.setPossessedMovement(seat.Walking);
                    Access.IsMoving(__state.Actor) = seat.Walking;
                }
                MovementVector() = __state.Movement;
                ClickVector() = __state.Click;
                for (int i = 0; i < 10; i++) Flags[i]() = __state.Flags[i];
                if (seat != null && seat.EventsConsumed.IsSupersetOf(seat.Units))
                    for (int i = FirstEventFlag; i < 10; i++) seat.Input.Flags[i] = false;
            }
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.isMovementActionActive))]
        static class RemoteMovementKeys
        {
            static void Postfix(ref bool __result)
            {
                if (_inRemoteAction && _acting != null) __result = _acting.Input.Movement != Vector2.zero;
            }
        }

        [HarmonyPatch(typeof(MapBox), nameof(MapBox.getMousePos))]
        static class RemoteMouse
        {
            static void Postfix(ref Vector2 __result)
            {
                if (_inRemoteAction && _acting != null) __result = _acting.Input.Mouse;
            }
        }
    }
}
