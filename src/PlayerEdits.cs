using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace WorldBoxMP
{
    public static class PlayerEdits
    {
        const string Units = "units";
        static readonly string[] MetaFields = { "name", "motto", "color", "banner_bg", "banner_icon", "favorite" };
        static readonly string[] UnitFields = { "name", "favorite", "traits" };

        static readonly Dictionary<string, string[]> _baseline = new Dictionary<string, string[]>();
        static readonly Dictionary<string, float> _pendingUntil = new Dictionary<string, float>();
        static float _metaTimer, _unitTimer;
        static float _lastWindow = -100f;
        static float _lastInput = -100f;

        public static bool ForceInteractive;

        static bool Interactive => ForceInteractive || Time.unscaledTime - _lastWindow < 2f;

        public static int Sent, Applied;

        static string Key(string kind, long id) => kind + ":" + id;

        public static void Reset()
        {
            _baseline.Clear();
            _pendingUntil.Clear();
        }

        static readonly Dictionary<Type, PropertyInfo[]> _props = new Dictionary<Type, PropertyInfo[]>();

        static PropertyInfo[] Props(Type t)
        {
            if (_props.TryGetValue(t, out var p)) return p;
            p = new[] { "motto", "banner_background_id", "banner_icon_id" }
                .Select(n => t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance)).ToArray();
            _props[t] = p;
            return p;
        }

        static string[] ReadMeta(BaseSystemData d)
        {
            PropertyInfo[] p = Props(d.GetType());
            return new[]
            {
                d.name ?? "",
                p[0] != null ? (string)p[0].GetValue(d) ?? "" : "",
                d is MetaObjectData md ? md.color_id.ToString() : "",
                p[1] != null ? p[1].GetValue(d).ToString() : "",
                p[2] != null ? p[2].GetValue(d).ToString() : "",
                d.favorite ? "1" : "0",
            };
        }

        static string[] ReadUnit(Actor a)
        {
            ActorData d = Access.ActorData(a);
            var traits = new List<string>();
            foreach (ActorTrait t in a.getTraits()) traits.Add(t.id);
            traits.Sort(StringComparer.Ordinal);
            return new[] { d?.name ?? "", a.isFavorite() ? "1" : "0", string.Join(",", traits) };
        }

        public static void HostApplied(string kind, long id, object o, BaseSystemData d)
        {
            if (!Net.IsClientConnected || d == null) return;
            _baseline[Key(kind, id)] = ReadMeta(d);
        }

        public static void HostAppliedUnit(Actor a)
        {
            if (!Net.IsClientConnected || a == null) return;
            _baseline[Key(Units, a.getID())] = ReadUnit(a);
        }

        static readonly string[] MetaProps = { "name", "motto", "color_id", "banner_background_id", "banner_icon_id", "favorite" };
        static readonly string[] UnitProps = { "name", "favorite", "saved_traits" };

        public static void HostAppliedPartial(string kind, long id, BaseSystemData d, string partJson) =>
            Merge(Key(kind, id), d != null ? ReadMeta(d) : null, MetaProps, partJson);

        public static void HostAppliedUnitPartial(Actor a, string partJson) =>
            Merge(Key(Units, a.getID()), ReadUnit(a), UnitProps, partJson);

        static void Merge(string key, string[] now, string[] props, string partJson)
        {
            if (!Net.IsClientConnected || now == null) return;
            if (!_baseline.TryGetValue(key, out string[] was)) { _baseline[key] = now; return; }
            var merged = (string[])was.Clone();
            for (int i = 0; i < props.Length && i < merged.Length; i++)
                if (partJson.Contains("\"" + props[i] + "\":")) merged[i] = now[i];
            _baseline[key] = merged;
        }

        const string World_ = "world";
        static readonly string[] WorldFields = { "speed", "paused" };

        static string[] ReadWorld() => new[]
        {
            Config.time_scale_asset != null ? Config.time_scale_asset.id : "x1",
            Config.paused ? "1" : "0",
        };

        public static void HostWorld(string speed, bool paused)
        {
            string key = Key(World_, 0);
            var host = new[] { speed, paused ? "1" : "0" };
            if (IsPending(World_, 0)) return;
            _baseline[key] = host;
            if (Config.time_scale_asset == null || Config.time_scale_asset.id != speed)
            {
                WorldTimeScaleAsset asset = AssetManager.time_scales.get(speed);
                if (asset != null) Config.setWorldSpeed(asset);
            }
            if (Config.paused != paused) Config.paused = paused;
        }

        public static bool IsPending(string kind, long id) =>
            _pendingUntil.TryGetValue(Key(kind, id), out float until) && Time.unscaledTime < until;

        public static void UpdateClient()
        {
            if (!Net.IsClientConnected || SmoothLoader.isLoading()) return;
            float dt = Time.unscaledDeltaTime;
            if (ScrollWindow.isWindowActive()) _lastWindow = Time.unscaledTime;
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) _lastInput = Time.unscaledTime;
            _metaTimer -= dt;
            _unitTimer -= dt;
            if (_metaTimer <= 0f) { _metaTimer = 1f; ScanMeta(); }
            if (_unitTimer <= 0f) { _unitTimer = 0.25f; ScanSelectedUnits(); }
        }

        public static void ScanNow()
        {
            if (!Net.IsClientConnected) return;
            ScanMeta();
            ScanSelectedUnits();
        }

        static void ScanMeta()
        {
            var w = new BinaryWriter(new MemoryStream());
            int count = 0;
            MetaSync.EachObject((kind, id, o, d) =>
            {
                if (d == null) return;
                count += Compare(w, kind, id, ReadMeta(d), MetaFields);
            });
            Send(w, count);
        }

        static void ScanSelectedUnits()
        {
            var w = new BinaryWriter(new MemoryStream());
            int count = 0;
            var seen = new HashSet<Actor>();
            if (SelectedUnit.isSet())
                foreach (Actor a in SelectedUnit.getAllSelected())
                    if (a != null && a.isAlive() && seen.Add(a)) count += Compare(w, Units, a.getID(), ReadUnit(a), UnitFields);
            Actor main = SelectedUnit.unit;
            if (main != null && main.isAlive() && seen.Add(main)) count += Compare(w, Units, main.getID(), ReadUnit(main), UnitFields);
            bool pressed = ForceInteractive || Time.unscaledTime - _lastInput < 0.5f;
            count += Compare(w, World_, 0, ReadWorld(), WorldFields, pressed);
            Send(w, count);
        }

        static int Compare(BinaryWriter w, string kind, long id, string[] now, string[] fields, bool always = false)
        {
            string key = Key(kind, id);
            if (!_baseline.TryGetValue(key, out string[] was))
            {
                _baseline[key] = now;
                return 0;
            }
            int changed = 0;
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] == was[i]) continue;
                if (kind == World_ ? !always : !always && !Interactive && fields[i] != "favorite")
                {
                    now[i] = was[i];
                    continue;
                }
                changed++;
            }
            if (changed == 0) return 0;
            w.Write(kind);
            w.Write(id);
            w.Write((byte)changed);
            var log = new List<string>();
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] == was[i]) continue;
                w.Write(fields[i]);
                w.Write(now[i]);
                log.Add(fields[i] + " '" + was[i] + "' -> '" + now[i] + "'");
            }
            SessionLog.Write("edit sent: " + kind + " " + id + " " + string.Join(", ", log.ToArray()));
            _baseline[key] = now;
            _pendingUntil[key] = Time.unscaledTime + 4f;
            Sent++;
            return 1;
        }

        static void Send(BinaryWriter w, int count)
        {
            if (count == 0) return;
            var ms = new MemoryStream();
            var head = new BinaryWriter(ms);
            head.Write(count);
            head.Write(((MemoryStream)w.BaseStream).ToArray());
            Net.Peer?.Send(Msg.Edit, ms.ToArray());
        }

        public static void HostReceive(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int records = r.ReadInt32();
            for (int k = 0; k < records; k++)
            {
                string kind = r.ReadString();
                long id = r.ReadInt64();
                int n = r.ReadByte();
                var fields = new List<KeyValuePair<string, string>>();
                for (int i = 0; i < n; i++) fields.Add(new KeyValuePair<string, string>(r.ReadString(), r.ReadString()));
                try
                {
                    if (kind == Units) ApplyUnit(id, fields);
                    else if (kind == World_) ApplyWorld(fields);
                    else ApplyMeta(kind, id, fields);
                }
                catch (Exception e)
                {
                    SessionLog.Write("edit from the second player failed: " + kind + " " + id + ": " + e.GetBaseException().Message);
                }
            }
        }

        static void ApplyWorld(List<KeyValuePair<string, string>> fields)
        {
            foreach (var f in fields)
            {
                if (f.Key == "speed")
                {
                    WorldTimeScaleAsset asset = AssetManager.time_scales.get(f.Value);
                    if (asset != null) Config.setWorldSpeed(asset);
                }
                else if (f.Key == "paused") Config.paused = f.Value == "1";
            }
            Applied++;
            SessionLog.Write("edit from the second player: world " + Describe(fields));
        }

        static void ApplyUnit(long id, List<KeyValuePair<string, string>> fields)
        {
            Actor a = World.world.units.get(id);
            if (a == null || !a.isAlive()) { SessionLog.Write("edit for unit " + id + " ignored: no longer here"); return; }
            bool reload = false;
            foreach (var f in fields)
            {
                switch (f.Key)
                {
                    case "name":
                        a.setName(f.Value);
                        Access.ActorData(a).custom_name = true;
                        break;
                    case "favorite":
                        if (a.isFavorite() != (f.Value == "1")) a.switchFavorite();
                        break;
                    case "traits":
                        var want = new HashSet<string>(f.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                        var have = new HashSet<string>();
                        foreach (ActorTrait t in a.getTraits()) have.Add(t.id);
                        foreach (string t in have) if (!want.Contains(t)) a.removeTrait(t);
                        foreach (string t in want) if (!have.Contains(t)) a.addTrait(t);
                        a.setStatsDirty();
                        reload = true;
                        break;
                }
            }
            if (reload) HostSync.Reload(id); else InfoSweep.Push(a);
            Applied++;
            SessionLog.Write("edit from the second player: unit " + id + " " + Describe(fields));
        }

        static void ApplyMeta(string kind, long id, List<KeyValuePair<string, string>> fields)
        {
            object o = MetaSync.Find(kind, id, out BaseSystemData d);
            if (o == null || d == null) { SessionLog.Write("edit for " + kind + " " + id + " ignored: no longer here"); return; }
            PropertyInfo[] p = Props(d.GetType());
            foreach (var f in fields)
            {
                switch (f.Key)
                {
                    case "name":
                        if (o is NanoObject nano) nano.setName(f.Value); else d.name = f.Value;
                        d.custom_name = true;
                        break;
                    case "motto":
                        p[0]?.SetValue(d, f.Value);
                        break;
                    case "color":
                        if (int.TryParse(f.Value, out int c)) MetaSync.RefreshColor(o, c);
                        break;
                    case "banner_bg":
                        if (p[1] != null && int.TryParse(f.Value, out int bg)) p[1].SetValue(d, bg);
                        break;
                    case "banner_icon":
                        if (p[2] != null && int.TryParse(f.Value, out int icon)) p[2].SetValue(d, icon);
                        break;
                    case "favorite":
                        d.favorite = f.Value == "1";
                        break;
                }
            }
            MetaSync.ForceResend(kind, id);
            Applied++;
            SessionLog.Write("edit from the second player: " + kind + " " + id + " " + Describe(fields));
        }

        static string Describe(List<KeyValuePair<string, string>> fields) =>
            string.Join(", ", fields.Select(f => f.Key + "='" + (f.Value.Length > 80 ? f.Value.Substring(0, 80) + "..." : f.Value) + "'").ToArray());
    }
}
