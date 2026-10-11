using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class AvatarSync
    {
        const byte KindDragon = 1, KindUfo = 2;

        static readonly AccessTools.FieldRef<Dragon, DragonState> DragonStateRef =
            AccessTools.FieldRefAccess<Dragon, DragonState>("state");
        static readonly AccessTools.FieldRef<UFO, SpriteAnimation> UfoBeam =
            AccessTools.FieldRefAccess<UFO, SpriteAnimation>("beamAnim");
        static readonly AccessTools.FieldRef<UFO, SpriteRenderer> UfoBeamRenderer =
            AccessTools.FieldRefAccess<UFO, SpriteRenderer>("beamRnd");
        static readonly Action<SpriteAnimation, float> AnimationUpdate =
            AccessTools.MethodDelegate<Action<SpriteAnimation, float>>(AccessTools.Method(typeof(SpriteAnimation), "update"));

        static readonly Dictionary<long, int> _sent = new Dictionary<long, int>();
        static readonly HashSet<long> _seen = new HashSet<long>();
        static readonly List<long> _drop = new List<long>();
        static readonly List<Actor> _avatars = new List<Actor>();

        public static int Sent, Applied;

        public static void Reset() => _sent.Clear();

        public static void Resend(long id) => _sent.Remove(id);

        static bool State(Actor a, out byte kind, out int code)
        {
            kind = 0;
            code = 0;
            Dragon dragon = a.getActorComponent<Dragon>();
            if (dragon != null) { kind = KindDragon; code = (int)DragonStateRef(dragon); return true; }
            UFO ufo = a.getActorComponent<UFO>();
            if (ufo != null) { kind = KindUfo; code = UfoBeam(ufo) != null && UfoBeam(ufo).isOn ? 1 : 0; return true; }
            return false;
        }

        public static int Write(BinaryWriter w)
        {
            _seen.Clear();
            var changed = new List<KeyValuePair<Actor, int>>();
            foreach (Actor a in World.world.units)
            {
                if (a.avatar == null || !a.isAlive() || SpecialActors.IsSpecial(a)) continue;
                if (!State(a, out byte kind, out int code)) continue;
                long id = a.getID();
                _seen.Add(id);
                int packed = kind * 1000 + code;
                if (_sent.TryGetValue(id, out int old) && old == packed) continue;
                _sent[id] = packed;
                changed.Add(new KeyValuePair<Actor, int>(a, packed));
            }
            _drop.Clear();
            foreach (long id in _sent.Keys) if (!_seen.Contains(id)) _drop.Add(id);
            foreach (long id in _drop) _sent.Remove(id);

            w.Write(changed.Count);
            foreach (var kv in changed)
            {
                w.Write(kv.Key.getID());
                w.Write(kv.Value);
            }
            Sent += changed.Count;
            return changed.Count;
        }

        public static void Read(BinaryReader r)
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long id = r.ReadInt64();
                int packed = r.ReadInt32();
                Actor a = World.world.units.get(id);
                if (a == null || !a.isAlive()) continue;
                int kind = packed / 1000, code = packed % 1000;
                try
                {
                    if (kind == KindDragon)
                    {
                        Dragon dragon = a.getActorComponent<Dragon>();
                        if (dragon == null) continue;
                        SoundSync.MuteDepth++;
                        try { dragon.setFrames((DragonState)code); }
                        finally { SoundSync.MuteDepth--; }
                    }
                    else if (kind == KindUfo)
                    {
                        UFO ufo = a.getActorComponent<UFO>();
                        if (ufo == null || UfoBeam(ufo) == null) continue;
                        bool on = code == 1;
                        if (on && !UfoBeam(ufo).isOn) UfoBeam(ufo).stopAt(0, pNow: true);
                        UfoBeam(ufo).isOn = on;
                        SpriteRenderer beam = UfoBeamRenderer(ufo);
                        if (beam != null) beam.enabled = on;
                    }
                    Applied++;
                }
                catch (Exception e)
                {
                    Debug.Log("[WorldBoxMP] avatar state " + id + ": " + e.GetBaseException().Message);
                }
            }
        }

        public static void UpdateClient()
        {
            float elapsed = Access.Elapsed(World.world);
            _avatars.Clear();
            foreach (Actor a in World.world.units)
                if (a.avatar != null && a.isAlive() && !SpecialActors.IsSpecial(a)) _avatars.Add(a);
            foreach (Actor a in _avatars)
            {
                if (a.sprite_animation != null) AnimationUpdate(a.sprite_animation, elapsed);
                UFO ufo = a.getActorComponent<UFO>();
                if (ufo != null && UfoBeam(ufo) != null && UfoBeam(ufo).isOn)
                {
                    AnimationUpdate(UfoBeam(ufo), elapsed);
                    Access.StackEffects(World.world).light_blobs.Add(new LightBlobData { position = a.current_position, radius = 1f });
                }
            }
        }
    }
}
