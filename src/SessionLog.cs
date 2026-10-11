using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace WorldBoxMP
{
    public static class SessionLog
    {
        static StreamWriter _writer;
        static string _folder;
        static float _dumpTimer;
        static float _countersTimer;
        static bool _hooked;

        public static string CurrentPath { get; private set; }

        public static void Init(string modFolder)
        {
            _folder = Path.Combine(modFolder, "logs");
            if (_hooked) return;
            _hooked = true;
            Application.logMessageReceived += OnUnityLog;
        }

        public static void Start(string role)
        {
            Stop();
            try
            {
                Directory.CreateDirectory(_folder);
                CurrentPath = Path.Combine(_folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "-" + role + ".log");
                var file = new FileStream(CurrentPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(file, new UTF8Encoding(false)) { AutoFlush = true };
                NetStats.Reset();
                _repeats.Clear();
                Write("session start as " + role + ", WorldBoxMP protocol " + Net.ProtocolVersion + ", game " +
                      Application.version + ", steam " + (SteamSession.Available ? Steamworks.SteamClient.Name : "off"));
                _dumpTimer = 2f;
                _countersTimer = 10f;
                Write(NetStats.SystemInfo());
            }
            catch (Exception e)
            {
                Debug.Log("[WorldBoxMP] session log not available: " + e.Message);
                _writer = null;
            }
        }

        public static void Stop()
        {
            if (_writer == null) return;
            Write("session end");
            try { _writer.Dispose(); } catch { }
            _writer = null;
        }

        public static void Write(string line)
        {
            if (_writer == null) return;
            try { _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + line); }
            catch { }
        }

        const int MaxRepeats = 20;
        static readonly System.Collections.Generic.Dictionary<string, int> _repeats = new System.Collections.Generic.Dictionary<string, int>();

        static void OnUnityLog(string text, string stack, LogType type)
        {
            if (_writer == null) return;
            if (type == LogType.Exception || type == LogType.Error)
            {
                string key = text.Length > 200 ? text.Substring(0, 200) : text;
                lock (_repeats)
                {
                    _repeats.TryGetValue(key, out int n);
                    _repeats[key] = ++n;
                    if (n > MaxRepeats) return;
                    if (n == MaxRepeats) text += "  (repeated " + MaxRepeats + " times; further ones only counted in REPEATS lines)";
                }
                Write("[" + type + "] " + text + (string.IsNullOrEmpty(stack) ? "" : "\n    " + stack.Replace("\n", "\n    ")));
            }
            else if (text.StartsWith("[WorldBoxMP]") && !text.StartsWith("[WorldBoxMP] render") &&
                     !text.StartsWith("[WorldBoxMP] building sample") && !text.StartsWith("[WorldBoxMP] borders"))
            {
                Write(text.Substring("[WorldBoxMP] ".Length));
            }
        }

        public static void Update()
        {
            if (_writer == null || SmoothLoader.isLoading()) return;
            float dt = Time.unscaledDeltaTime;
            _countersTimer -= dt;
            if (_countersTimer <= 0f)
            {
                _countersTimer = 10f;
                WriteCounters();
            }
            if (Net.IsHostWithPeer || Net.IsClientConnected) return;
            _dumpTimer -= dt;
            if (_dumpTimer > 0f) return;
            _dumpTimer = 30f;
            DumpState(-1);
        }

        static void WriteCounters()
        {
            Write(NetStats.Line());
            lock (_repeats)
            {
                foreach (var kv in _repeats)
                    if (kv.Value > MaxRepeats) Write("REPEATS " + kv.Value + "x so far: " + kv.Key.Replace('\n', ' '));
            }
            var sb = new StringBuilder("COUNTERS ").Append(Net.Role);
            if (Net.Role == Role.Host)
                sb.Append(" stream_kib_s=").Append(HostSync.LastKiBps).Append(" sections_kib=").Append(HostSync.SectionReport())
                  .Append(" sections_ms_per_s=").Append(HostSync.TimeReport());
            sb.Append(" soft_units=").Append(InfoSweep.SoftActors)
              .Append(" soft_buildings=").Append(InfoSweep.SoftBuildings)
              .Append(" gear_reloads=").Append(InfoSweep.GearReloads)
              .Append(" statuses=").Append(Net.Role == Role.Host ? StatusSync.Sent : StatusSync.Applied)
              .Append(" status_errors=").Append(StatusSync.Failures)
              .Append(" drops=").Append(Net.Role == Role.Host ? DropSync.Sent : DropSync.Shown)
              .Append(" sounds=").Append(Net.Role == Role.Host ? SoundSync.Sent : SoundSync.Played)
              .Append(" meta_repairs=").Append(MetaSync.Repairs)
              .Append(" relinks=").Append(MetaSync.Relinks)
              .Append(Net.Role == Role.Host
                  ? " pos_records=" + PositionSync.Records + " pos_msgs=" + PositionSync.Messages + " pos_kib=" + PositionSync.Bytes / 1024
                  : " pos_applied=" + PositionSync.Applied + " pos_late=" + PositionSync.Late)
              .Append(" object_repairs=").Append(ObjectRepair.Repaired)
              .Append(" live=").Append(Net.Role == Role.Host ? LiveState.Sent : LiveState.Applied)
              .Append(" orders=").Append(Net.Role == Role.Host ? Orders.Applied : Orders.Sent)
              .Append(" avatar_states=").Append(Net.Role == Role.Host ? AvatarSync.Sent : AvatarSync.Applied)
              .Append(" edits=").Append(Net.Role == Role.Host ? PlayerEdits.Applied : PlayerEdits.Sent)
              .Append(" checks_ok=").Append(Checksum.Matches)
              .Append(" desyncs=").Append(Checksum.Mismatches)
              .Append(" stream_errors=").Append(ClientSync.Failures)
              .Append(" meta_errors=").Append(MetaSync.Failures);
            Write(sb.ToString());
        }

        public static void DumpState(int seq)
        {
            if (_writer == null) return;
            try
            {
                MapBox w = World.world;
                var sb = new StringBuilder();
                int buildings = 0;
                foreach (Building b in w.buildings)
                    if (Access.BuildingData(b).state != BuildingState.Removed) buildings++;
                int units = 0, statusUnits = 0, statuses = 0;
                long names = 0, levels = 0, health = 0, traits = 0;
                foreach (Actor a in w.units)
                {
                    if (!a.isAlive() || a.asset.skip_save) continue;
                    units++;
                    ActorData d = Access.ActorData(a);
                    unchecked
                    {
                        names += a.getID() * 31L ^ (d.name ?? "").GetHashCode();
                        levels += d.level;
                        health += d.health;
                        if (d.saved_traits != null) traits += d.saved_traits.Count;
                    }
                    int n = 0;
                    foreach (string id in a.getStatusesIds()) if (id != "possessed") n++;
                    if (n > 0) { statusUnits++; statuses += n; }
                }
                sb.Append("STATE seq=").Append(seq < 0 ? "-" : seq.ToString()).Append(' ').Append(Net.Role)
                  .Append(" units=").Append(units)
                  .Append(" buildings=").Append(buildings)
                  .Append(" kingdoms=").Append(w.kingdoms.Count)
                  .Append(" cities=").Append(w.cities.Count)
                  .Append(" year=").Append(Date.getCurrentYear())
                  .Append(" checks_ok=").Append(Checksum.Matches)
                  .Append(" desyncs=").Append(Checksum.Mismatches)
                  .Append(" stream_errors=").Append(ClientSync.Failures)
                  .Append(" meta_errors=").Append(MetaSync.Failures)
                  .Append(" meta_repairs=").Append(MetaSync.Repairs)
                  .Append(" stream_kib_s=").Append(HostSync.LastKiBps);
                sb.Append("\n    units_info names=").Append(names.ToString("x")).Append(" levels=").Append(levels)
                  .Append(" health=").Append(health).Append(" traits=").Append(traits)
                  .Append(" units_with_status=").Append(statusUnits).Append(" statuses=").Append(statuses);
                sb.Append("\n    ").Append(Census.Line());
                sb.Append("\n    meta");
                for (int i = 0; i < MetaSync.ChannelCount; i++)
                    sb.Append(' ').Append(MetaSync.ChannelName(i)).Append('=').Append(MetaSync.ChannelSize(i));
                foreach (Kingdom k in w.kingdoms.OrderBy(k => k.getID()))
                {
                    int zones = 0;
                    foreach (City c in k.getCities()) zones += Checksum.CityZoneCount(c);
                    sb.Append("\n    kingdom ").Append(k.getID()).Append(" \"").Append(k.name).Append("\" color=").Append(k.data.color_id)
                      .Append(" zones=").Append(zones).Append(' ').Append(MetaSync.DescribeKingdom(k));
                }
                foreach (City c in w.cities.OrderBy(c => c.getID()))
                    sb.Append("\n    city ").Append(c.getID()).Append(" \"").Append(c.name).Append("\" ").Append(MetaSync.DescribeCity(c));
                Write(sb.ToString());
            }
            catch (Exception e)
            {
                Write("state dump failed: " + e.Message);
            }
        }
    }
}
