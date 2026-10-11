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

        static readonly HashSet<Actor> _remote = new HashSet<Actor>();
        static InputState _remoteInput = new InputState { Flags = new bool[10] };
        static readonly HashSet<Actor> _eventsConsumed = new HashSet<Actor>();
        static bool _inRemoteAction;

        static bool _suppressSend;
        static bool _wasControlling;
        static float _hostLogTimer;
        static float _inputTimer;
        static byte[] _lastInput;

        static long _clientMainId = -1;
        static Vector2 _clientPos;
        static bool _clientWalking;
        static double _lastRemoteTime = -1000;
        static float _statusTimer;

        public static Vector2? TestMovement;

        public static bool IsRemote(Actor a) => a != null && _remote.Contains(a);

        public static bool HasRemote => _remote.Count > 0 || Clock.Now - _lastRemoteTime < 60.0;

        static bool FollowsClient(Actor a) => a != null && _clientMainId >= 0 && a.getID() == _clientMainId;

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
            }
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

        public static void HostReceivePossess(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            var names = new List<string>();
            for (int i = 0; i < n; i++)
            {
                Actor a = World.world.units.get(r.ReadInt64());
                if (a == null || !a.isAlive() || !a.canBePossessed()) continue;
                _remote.Add(a);
                AddPossessedStatus(a);
                names.Add(a.getID() + " " + a.asset.id);
            }
            SessionLog.Write("friend possessed: " + string.Join(", ", names));
        }

        public static void HostReceiveUnpossess()
        {
            foreach (Actor a in _remote)
            {
                if (a == null || !a.isAlive()) continue;
                a.finishStatusEffect("possessed");
                a.cancelAllBeh();
                a.setPossessedMovement(false);
            }
            if (_remote.Count > 0) SessionLog.Write("friend released " + _remote.Count + " unit(s)");
            _remote.Clear();
            _clientMainId = -1;
            _clientWalking = false;
        }

        public static void HostReceiveInput(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            _remoteInput.Movement = new Vector2(r.ReadSingle(), r.ReadSingle());
            _remoteInput.Click = new Vector2(r.ReadSingle(), r.ReadSingle());
            _remoteInput.Mouse = new Vector2(r.ReadSingle(), r.ReadSingle());
            ushort bits = r.ReadUInt16();
            for (int i = 0; i < 10; i++)
            {
                bool on = (bits & (1 << i)) != 0;
                if (i < FirstEventFlag) _remoteInput.Flags[i] = on;
                else if (on) _remoteInput.Flags[i] = true;
            }
            if ((bits >> FirstEventFlag) != 0) _eventsConsumed.Clear();
            if (r.ReadBoolean())
            {
                _clientMainId = r.ReadInt64();
                _clientPos = new Vector2(r.ReadSingle(), r.ReadSingle());
                _clientWalking = r.ReadBoolean();
            }
            else
            {
                _clientMainId = -1;
                _clientWalking = false;
            }
        }

        public static void HostUpdate()
        {
            _remote.RemoveWhere(a => a == null || !a.isAlive());
            if (_remote.Count == 0) return;
            _lastRemoteTime = Clock.Now;
            _hostLogTimer -= Time.unscaledDeltaTime;
            if (_hostLogTimer <= 0f)
            {
                _hostLogTimer = 1f;
                foreach (Actor a in _remote)
                    SessionLog.Write("friend's unit " + a.getID() + " at " + a.current_position.x.ToString("0.0") + ":" +
                                     a.current_position.y.ToString("0.0") + " input " + _remoteInput.Movement);
            }
            _statusTimer -= Time.unscaledDeltaTime;
            bool active = _remoteInput.Movement != Vector2.zero || _remoteInput.Flags[0] || _remoteInput.Flags[1];
            for (int i = FirstEventFlag; i < 10 && !active; i++) active = _remoteInput.Flags[i];
            if (active || _statusTimer <= 0f)
            {
                _statusTimer = 2f;
                foreach (Actor a in _remote)
                {
                    AddPossessedStatus(a);
                    a.stopSleeping();
                    a.next_step_position = a.next_step_position_possession;
                }
            }
            HostFollow();
        }

        static void HostFollow()
        {
            if (_clientMainId < 0) return;
            Actor a = World.world.units.get(_clientMainId);
            if (a == null || !_remote.Contains(a) || Access.UnderForces(a)) return;
            Vector2 cur = a.current_position;
            Vector2 diff = _clientPos - cur;
            if (diff.sqrMagnitude < 1e-6f) return;
            Vector2 pos = diff.sqrMagnitude > 64f ? _clientPos : cur + diff * Mathf.Min(1f, Time.unscaledDeltaTime * 20f);
            a.current_position = pos;
            a.next_step_position = pos;
            a.next_step_position_possession = pos;
            Access.DirtyTile(a) = true;
            if (InputHelpers.mouseSupported) a.checkFlipAgainstTargetPosition(_remoteInput.Mouse);
        }

        public static void HostClear()
        {
            _remote.Clear();
            _clientMainId = -1;
            _clientWalking = false;
            for (int i = 0; i < 10; i++) _remoteInput.Flags[i] = false;
            _remoteInput.Movement = Vector2.zero;
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
                if (!__result && _remote.Count > 0 && _remote.Contains(pUnit)) __result = true;
            }
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.remove))]
        static class HostForgetsDeadRemoteUnits
        {
            static void Postfix(Actor pActor) => _remote.Remove(pActor);
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
                if (a == null || !_remote.Contains(a)) return;
                __state = new Saved { Active = true, Movement = MovementVector(), Click = ClickVector(), Flags = new bool[10], Actor = a };
                for (int i = 0; i < 10; i++) __state.Flags[i] = Flags[i]();

                MovementVector() = FollowsClient(a) ? Vector2.zero : _remoteInput.Movement;
                ClickVector() = _remoteInput.Click;
                bool fresh = _eventsConsumed.Add(a);
                for (int i = 0; i < 10; i++)
                    Flags[i]() = i < FirstEventFlag ? _remoteInput.Flags[i] : fresh && _remoteInput.Flags[i];
                _inRemoteAction = true;
            }

            static void Finalizer(Saved __state)
            {
                if (!__state.Active) return;
                _inRemoteAction = false;
                if (FollowsClient(__state.Actor))
                {
                    __state.Actor.setPossessedMovement(_clientWalking);
                    Access.IsMoving(__state.Actor) = _clientWalking;
                }
                MovementVector() = __state.Movement;
                ClickVector() = __state.Click;
                for (int i = 0; i < 10; i++) Flags[i]() = __state.Flags[i];
                if (_eventsConsumed.IsSupersetOf(_remote))
                    for (int i = FirstEventFlag; i < 10; i++) _remoteInput.Flags[i] = false;
            }
        }

        [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.isMovementActionActive))]
        static class RemoteMovementKeys
        {
            static void Postfix(ref bool __result)
            {
                if (_inRemoteAction) __result = _remoteInput.Movement != Vector2.zero;
            }
        }

        [HarmonyPatch(typeof(MapBox), nameof(MapBox.getMousePos))]
        static class RemoteMouse
        {
            static void Postfix(ref Vector2 __result)
            {
                if (_inRemoteAction) __result = _remoteInput.Mouse;
            }
        }
    }
}
