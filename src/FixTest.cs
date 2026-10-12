using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class FixTest
    {
        public static bool On;
        static float _clock = -1f;
        static long _held = -1;
        static string _hostPath;
        static Vector2 _sheep;

        static readonly AccessTools.FieldRef<Boulder, List<Vector2>> Bounces = AccessTools.FieldRefAccess<Boulder, List<Vector2>>("_bounce_positions");
        static readonly AccessTools.FieldRef<Vector2> PullFrom =
            AccessTools.StaticFieldRefAccess<Vector2>(AccessTools.Field(typeof(Boulder), "_initial_charge_position"));

        static void Check(string what, bool ok, string detail = "") =>
            Debug.Log("[WorldBoxMP] test: fix " + (ok ? "PASS " : "FAIL ") + what + (detail.Length > 0 ? " (" + detail + ")" : ""));

        static Vector2 Center => new Vector2(MapBox.width / 2f, MapBox.height / 2f);

        static Actor Pick(int index)
        {
            var list = new List<Actor>();
            foreach (Actor a in World.world.units)
                if (a.isAlive() && a.asset.allow_possession && a.canBePossessed() && !SpecialActors.IsSpecial(a) && !a.isInsideSomething()) list.Add(a);
            list.Sort((x, y) => x.getID().CompareTo(y.getID()));
            return index < list.Count ? list[index] : null;
        }

        static string PathOf(Boulder b)
        {
            var parts = new List<string>();
            foreach (Vector2 v in Bounces(b)) parts.Add(v.x.ToString("0.0") + ":" + v.y.ToString("0.0"));
            return string.Join(" ", parts.ToArray());
        }

        static Boulder Newest()
        {
            Boulder[] all = Object.FindObjectsOfType<Boulder>();
            return all.Length > 0 ? all[all.Length - 1] : null;
        }

        public static void Update(float dt)
        {
            if (!On || World.world == null || SmoothLoader.isLoading() || !(Net.IsHostWithPeer || Net.IsClientConnected)) return;
            if (_clock < 0f) _clock = 0f;
            float before = _clock;
            _clock += dt;
            bool At(float t) => before < t && _clock >= t;
            if (Net.Role == Role.Host) Host(At);
            else Client(At);
        }

        delegate bool AtFn(float t);

        static void Host(AtFn At)
        {
            if (At(4f))
            {
                var click = AccessTools.MethodDelegate<System.Action<PlayerControl, Vector2Int, GodPower, bool>>(AccessTools.Method(typeof(PlayerControl), "clickedFinal"));
                for (int i = 0; i < 6; i++) click(World.world.player_control, new Vector2Int((int)Center.x - 20 + i, (int)Center.y - 20), AssetManager.powers.get("sheep"), false);
            }
            if (At(29f))
            {
                var near = new List<string>();
                foreach (Actor a in World.world.units)
                    if (a.isAlive() && a.asset.id == "sheep") near.Add(a.current_position.x.ToString("0") + ":" + a.current_position.y.ToString("0"));
                Debug.Log("[WorldBoxMP] test: fix host sheep at " + string.Join(" ", near.ToArray()) + ", target " + (Center + new Vector2(-17f, -20f)));
            }
            if (At(36f)) Check("host: the player's magnet holds units", RemoteMagnet.HeldBy(1) > 0, RemoteMagnet.HeldBy(1).ToString());
            if (At(44f)) Check("host: and let them go", RemoteMagnet.HeldBy(1) == 0);
            if (At(50f))
            {
                Vector2 at = Center + new Vector2(10f, 15f);
                PullFrom() = at + new Vector2(-12f, 0f);
                EffectsLibrary.spawnAt("fx_boulder", at, 1f);
                Boulder b = Newest();
                _hostPath = b != null ? PathOf(b) : "-";
                Debug.Log("[WorldBoxMP] test: fix host ball path " + _hostPath);
            }
            if (At(66f))
            {
                Check("host: the player's ball was thrown", RemoteMagnet.Balls >= 1, RemoteMagnet.Balls.ToString());
                Debug.Log("[WorldBoxMP] test: fix host done");
            }
        }

        static void Client(AtFn At)
        {
            if (At(10f))
            {
                Actor a = Pick(0);
                _held = a != null ? a.getID() : -1;
                ControllableUnit.setControllableCreature(a);
                RemotePossession.TestMovement = new Vector2(1f, 0f);
                Check("client: holds A", a != null && ControllableUnit.isControllingUnit(a), _held.ToString());
            }
            if (At(14f))
            {
                RemotePossession.TestMovement = null;
                ControllableUnit.clear(pCallKill: false);
            }
            if (At(16f))
            {
                Check("client: let go of A everywhere", !OwnUnit.Owns(_held) && OwnUnit.Main == null, "own " + OwnUnit.Owns(_held));
                Actor a = World.world.units.get(_held);
                Debug.Log("[WorldBoxMP] test: fix client A at " + (a != null ? a.current_position.ToString() : "-"));
            }
            if (At(30f))
            {
                foreach (Actor a in World.world.units)
                    if (a.isAlive() && a.asset.id == "sheep") { _sheep = a.current_position; break; }
                RemoteMagnet.TestSend(1, _sheep, Vector2.zero);
            }
            Vector2 sheep = _sheep;
            if (_clock > 30f && _clock < 38f && Mathf.Repeat(_clock, 0.1f) < 0.02f)
                RemoteMagnet.TestSend(1, sheep + new Vector2(0f, (_clock - 30f) * 1.2f), Vector2.zero);
            if (At(40f)) RemoteMagnet.TestSend(2, sheep + new Vector2(0f, 10f), new Vector2(3f, 0f));
            if (_clock > 48f && _clock < 60f && _hostPath == null)
            {
                Boulder b = Newest();
                if (b != null)
                {
                    _hostPath = PathOf(b);
                    Debug.Log("[WorldBoxMP] test: fix client ball path " + _hostPath);
                }
            }
            if (At(62f)) RemoteMagnet.TestSendBall(Center + new Vector2(-15f, 10f), Center + new Vector2(-5f, 10f));
            if (At(70f)) Debug.Log("[WorldBoxMP] test: fix client done");
        }
    }
}
