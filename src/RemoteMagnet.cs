using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WorldBoxMP
{
    public static class RemoteMagnet
    {
        const string PowerId = "magnet";

        static readonly Action<Actor, bool> FindTile = HarmonyLib.AccessTools.MethodDelegate<Action<Actor, bool>>(HarmonyLib.AccessTools.Method(typeof(Actor), "findCurrentTile"));
        static readonly Action<Actor, WorldTile, float> SpawnOn = HarmonyLib.AccessTools.MethodDelegate<Action<Actor, WorldTile, float>>(HarmonyLib.AccessTools.Method(typeof(Actor), "spawnOn", new[] { typeof(WorldTile), typeof(float) }));
        static readonly Func<BaseSimObject, string, float, bool, bool> AddStatus = HarmonyLib.AccessTools.MethodDelegate<Func<BaseSimObject, string, float, bool, bool>>(HarmonyLib.AccessTools.Method(typeof(BaseSimObject), "addStatusEffect", new[] { typeof(string), typeof(float), typeof(bool) }));
        static readonly HarmonyLib.AccessTools.FieldRef<Actor, Vector3> TargetAngle = HarmonyLib.AccessTools.FieldRefAccess<Actor, Vector3>("target_angle");
        const byte Hold = 1, Release = 2;

        static bool _holding;
        static float _sendTimer;
        static Vector2 _lastPos;
        static readonly List<Vector2> _samples = new List<Vector2>();

        public static void ClientUpdate()
        {
            if (!Net.IsClientConnected) { _holding = false; return; }
            GodPower selected = World.world.selected_power;
            bool magnet = selected != null && selected.id == PowerId;
            Vector2 pos = World.world.getMousePos();
            if (!_holding)
            {
                if (!magnet || !Input.GetMouseButtonDown(0) || World.world.isOverUI()) return;
                if (!Rules.ClientMayPower(selected)) { Rules.Refuse(Rules.Of(selected)); return; }
                _holding = true;
                _samples.Clear();
                _lastPos = pos;
                _sendTimer = 0f;
            }
            _samples.Add((pos - _lastPos) * 60f);
            if (_samples.Count > 5) _samples.RemoveAt(0);
            _lastPos = pos;
            if (!magnet || !Input.GetMouseButton(0))
            {
                Send(Release, pos, magnet ? Throw() : Vector2.zero);
                _holding = false;
                return;
            }
            _sendTimer -= Time.unscaledDeltaTime;
            if (_sendTimer > 0f) return;
            _sendTimer = 0.05f;
            Send(Hold, pos, Vector2.zero);
        }

        static Vector2 Throw()
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2 v in _samples) sum += v;
            if (_samples.Count > 0) sum /= _samples.Count;
            Vector2 force = sum * 5f * Time.deltaTime;
            return force.magnitude > 10f ? force.normalized * 10f : force;
        }

        static void Send(byte what, Vector2 pos, Vector2 force)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(what);
            w.Write(pos.x);
            w.Write(pos.y);
            w.Write(Config.current_brush ?? "circ_5");
            w.Write(force.x);
            w.Write(force.y);
            Net.Peer?.Send(Msg.Magnet, ms.ToArray());
        }

        class Held
        {
            public readonly List<Actor> Units = new List<Actor>();
            public readonly HashSet<Actor> Set = new HashSet<Actor>();
            public Vector2 Target, Shown;
            public float Angle;
            public string Brush = "circ_5";
        }

        static readonly Dictionary<byte, Held> _held = new Dictionary<byte, Held>();

        public static void HostReceive(byte[] data)
        {
            Guest g = Net.Current;
            if (g == null) return;
            var r = new BinaryReader(new MemoryStream(data));
            byte what = r.ReadByte();
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            string brush = r.ReadString();
            var force = new Vector2(r.ReadSingle(), r.ReadSingle());
            if (!_held.TryGetValue(g.Slot, out Held h)) _held[g.Slot] = h = new Held { Shown = pos };
            if (what == Release) { Drop(h, force); return; }
            GodPower power = AssetManager.powers.get(PowerId);
            if (!Rules.HostAllowsPower(power))
            {
                Drop(h, Vector2.zero);
                Rules.Deny(Rules.Of(power), "tip_denied");
                return;
            }
            h.Target = pos;
            h.Brush = brush;
            WorldTile tile = World.world.GetTile((int)pos.x, (int)pos.y);
            int before = h.Units.Count;
            if (tile != null) Pickup(h, tile);
            if (h.Units.Count != before) SessionLog.Write("magnet of " + g.Label + " holds " + h.Units.Count + " unit(s)");
        }

        static void Pickup(Held h, WorldTile center)
        {
            BrushData brush = global::Brush.get(h.Brush) ?? global::Brush.get(5);
            foreach (BrushPixelData p in brush.pos)
            {
                WorldTile tile = World.world.GetTile(p.x + center.x, p.y + center.y);
                if (tile == null || !tile.hasUnits()) continue;
                tile.doUnits(a =>
                {
                    if (!a.asset.can_be_moved_by_powers || a.isInsideSomething() || ControllableUnit.isControllingUnit(a) || InAnyMagnet(a)) return;
                    if (!h.Set.Add(a)) return;
                    a.cancelAllBeh();
                    h.Units.Add(a);
                    Access.InMagnet(a) = true;
                });
            }
        }

        static bool InAnyMagnet(Actor a)
        {
            foreach (Held h in _held.Values) if (h.Set.Contains(a)) return true;
            return false;
        }

        public static void HostUpdate()
        {
            if (_held.Count == 0) return;
            foreach (var kv in _held)
            {
                Held h = kv.Value;
                if (Net.GuestIn(kv.Key) == null) { Drop(h, Vector2.zero); continue; }
                h.Units.RemoveAll(a => a == null || !a.isAlive());
                if (h.Units.Count == 0) continue;
                h.Shown = Vector2.Lerp(h.Shown, h.Target, Mathf.Clamp01(Time.unscaledDeltaTime * 15f));
                float n = h.Units.Count;
                float perTurn = n > 100f ? 4f : n > 50f ? 4.5f : n > 5f ? 5f : 6f;
                h.Angle += (float)Math.PI * 2f / perTurn * Time.deltaTime;
                BrushData brush = global::Brush.get(h.Brush) ?? global::Brush.get(5);
                float radius = (brush.width + 1) / 2f;
                float step = 1f / n * radius;
                for (int i = 0; i < h.Units.Count; i++)
                {
                    Actor a = h.Units[i];
                    FindTile(a, true);
                    float x = h.Shown.x + Mathf.Cos(h.Angle + i) * (step * i);
                    float y = h.Shown.y + Mathf.Sin(h.Angle + i) * (step * i);
                    a.current_position = new Vector2(x, y - a.position_height);
                    a.callbacks_magnet_update?.Invoke(a);
                }
            }
        }

        static void Drop(Held h, Vector2 force)
        {
            foreach (Actor a in h.Units)
            {
                if (a == null || !a.isAlive()) continue;
                a.current_position.y -= a.position_height;
                Access.InMagnet(a) = false;
                Access.DirtyTile(a) = true;
                FindTile(a, true);
                SpawnOn(a, a.current_tile, a.getActorAsset().default_height);
                a.makeStunned(1f);
                AddStatus(a, "magnetized", 0f, true);
                Vector3 angle = TargetAngle(a);
                angle.z = 0f;
                TargetAngle(a) = angle;
                if (force.magnitude > 0.1f)
                {
                    Vector2 v = force + new Vector2(UnityEngine.Random.Range(-0.3f, 0.3f), UnityEngine.Random.Range(-0.3f, 0.3f));
                    a.addForce(v.x, v.y, v.magnitude * 0.3f, pCheckLandCancelAllActions: true, pIgnorePosHeight: true);
                }
                else a.addForce(0f, 0f, 0.1f, pCheckLandCancelAllActions: true);
                a.addActionWaitAfterLand(0.5f);
            }
            if (h.Units.Count > 0) SessionLog.Write("magnet dropped " + h.Units.Count + " unit(s)" + (force.magnitude > 0.1f ? ", thrown" : ""));
            h.Units.Clear();
            h.Set.Clear();
        }

        const string BallId = "bowling_ball";
        static bool _pulling;
        static Vector2 _pullFrom;
        static float _manyTimer;

        public static void ClientBallUpdate()
        {
            if (!Net.IsClientConnected) { _pulling = false; return; }
            GodPower selected = World.world.selected_power;
            bool ball = selected != null && selected.id == BallId;
            Vector2 pos = World.world.getMousePos();
            if (ball && Input.GetMouseButtonDown(0) && !World.world.isOverUI())
            {
                if (!Rules.ClientMayPower(selected)) { Rules.Refuse(Rules.Of(selected)); return; }
                _pulling = true;
                _pullFrom = pos;
                _manyTimer = 0f;
            }
            if (!_pulling) return;
            if (!ball) { _pulling = false; return; }
            if (HotkeyLibrary.many_mod.isHolding() && Input.GetMouseButton(0))
            {
                _manyTimer -= Time.unscaledDeltaTime;
                if (_manyTimer <= 0f) { _manyTimer = 0.1f; SendBall(pos, pos); }
                return;
            }
            if (Input.GetMouseButton(0)) return;
            _pulling = false;
            SendBall(_pullFrom, pos);
        }

        static void SendBall(Vector2 from, Vector2 to)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(from.x); w.Write(from.y); w.Write(to.x); w.Write(to.y);
            Net.Peer?.Send(Msg.Boulder, ms.ToArray());
        }

        static readonly HarmonyLib.AccessTools.FieldRef<Vector2> BallPullFrom =
            HarmonyLib.AccessTools.StaticFieldRefAccess<Vector2>(HarmonyLib.AccessTools.Field(typeof(Boulder), "_initial_charge_position"));
        static readonly HarmonyLib.AccessTools.FieldRef<bool> BallCharging =
            HarmonyLib.AccessTools.StaticFieldRefAccess<bool>(HarmonyLib.AccessTools.Field(typeof(Boulder), "_charge_started"));

        public static void HostBall(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var from = new Vector2(r.ReadSingle(), r.ReadSingle());
            var to = new Vector2(r.ReadSingle(), r.ReadSingle());
            GodPower power = AssetManager.powers.get(BallId);
            if (!Rules.HostAllowsPower(power)) { Rules.Deny(Rules.Of(power), "tip_denied"); return; }
            Vector2 hostFrom = BallPullFrom();
            bool hostCharging = BallCharging();
            try
            {
                BallPullFrom() = from;
                BallReleasedAt = to;
                EffectsLibrary.spawnAt("fx_boulder", to, 1f);
                Balls++;
            }
            finally
            {
                BallReleasedAt = null;
                BallPullFrom() = hostFrom;
                BallCharging() = hostCharging;
            }
        }

        public static Vector2? BallReleasedAt;

        [HarmonyLib.HarmonyPatch(typeof(EffectsLibrary), nameof(EffectsLibrary.spawnBoulder))]
        static class BallFromThePlayersPointer
        {
            static bool Prefix(BaseEffect pEffect, ref BaseEffect __result)
            {
                if (!BallReleasedAt.HasValue || !(pEffect is Boulder boulder)) return true;
                boulder.spawnOn(BallReleasedAt.Value);
                LastBallStart = BallReleasedAt.Value;
                __result = pEffect;
                return false;
            }
        }

        public static Vector2 LastBallStart;

        public static void TestSend(byte what, Vector2 pos, Vector2 force) => Send(what, pos, force);
        public static void TestSendBall(Vector2 from, Vector2 to) => SendBall(from, to);

        public static int Balls;

        public static int HeldBy(byte slot) => _held.TryGetValue(slot, out Held h) ? h.Units.Count : 0;

        public static void Clear() => _held.Clear();
    }
}
