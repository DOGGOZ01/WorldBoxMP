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
                _writer = new StreamWriter(CurrentPath, false, new UTF8Encoding(false)) { AutoFlush = true };
                Write("session start as " + role + ", WorldBoxMP protocol " + Net.ProtocolVersion + ", game " +
                      Application.version + ", steam " + (SteamSession.Available ? Steamworks.SteamClient.Name : "off"));
                _dumpTimer = 2f;
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

        static void OnUnityLog(string text, string stack, LogType type)
        {
            if (_writer == null) return;
            if (type == LogType.Exception || type == LogType.Error)
            {
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
            _dumpTimer -= Time.unscaledDeltaTime;
            if (_dumpTimer > 0f) return;
            _dumpTimer = 10f;
            DumpState();
        }

        public static void DumpState()
        {
            if (_writer == null) return;
            try
            {
                MapBox w = World.world;
                var sb = new StringBuilder();
                sb.Append("STATE ").Append(Net.Role)
                  .Append(" units=").Append(w.units.Count)
                  .Append(" kingdoms=").Append(w.kingdoms.Count)
                  .Append(" cities=").Append(w.cities.Count)
                  .Append(" year=").Append(Date.getCurrentYear())
                  .Append(" checks_ok=").Append(Checksum.Matches)
                  .Append(" desyncs=").Append(Checksum.Mismatches)
                  .Append(" stream_errors=").Append(ClientSync.Failures)
                  .Append(" meta_errors=").Append(MetaSync.Failures)
                  .Append(" stream_kib_s=").Append(HostSync.LastKiBps);
                foreach (Kingdom k in w.kingdoms.OrderBy(k => k.getID()))
                {
                    int zones = 0;
                    var cities = new StringBuilder();
                    foreach (City c in k.getCities())
                    {
                        int z = Checksum.CityZoneCount(c);
                        zones += z;
                        cities.Append(" [").Append(c.getID()).Append(' ').Append(c.name).Append(" z=").Append(z).Append(']');
                    }
                    sb.Append("\n    kingdom ").Append(k.getID()).Append(" \"").Append(k.name).Append("\" zones=").Append(zones)
                      .Append(" cities:").Append(cities);
                }
                Write(sb.ToString());
            }
            catch (Exception e)
            {
                Write("state dump failed: " + e.Message);
            }
        }
    }
}
