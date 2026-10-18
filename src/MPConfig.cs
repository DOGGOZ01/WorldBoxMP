using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WorldBoxMP
{
    public class MPConfig
    {
        public string HostIp = "127.0.0.1";
        public int Port = 7777;
        public float ResyncSeconds = 0f;
        public int TickMs = 50;
        public bool ShowNames = true;
        public float InfoSeconds = 20f;

        static readonly string[] Keys = { "host_ip", "port", "full_resync_seconds", "tick_ms", "show_names", "info_refresh_seconds" };

        public static MPConfig Load(string folder)
        {
            var cfg = new MPConfig();
            string path = Path.Combine(folder, "WorldBoxMP.cfg");
            var found = new HashSet<string>();
            if (File.Exists(path))
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    try
                    {
                        switch (key)
                        {
                            case "host_ip": cfg.HostIp = val; break;
                            case "port": cfg.Port = int.Parse(val, CultureInfo.InvariantCulture); break;
                            case "full_resync_seconds":
                                cfg.ResyncSeconds = float.Parse(val, CultureInfo.InvariantCulture);
                                if (cfg.ResyncSeconds == 120f) { cfg.ResyncSeconds = 0f; continue; }
                                break;
                            case "tick_ms": cfg.TickMs = Math.Max(20, int.Parse(val, CultureInfo.InvariantCulture)); break;
                            case "info_refresh_seconds":
                                cfg.InfoSeconds = Math.Max(1f, float.Parse(val, CultureInfo.InvariantCulture));
                                if (cfg.InfoSeconds == 10f) { cfg.InfoSeconds = 20f; continue; }
                                break;
                            case "show_names": cfg.ShowNames = val != "0" && val.ToLowerInvariant() != "false"; break;
                        }
                        found.Add(key);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.Log("[WorldBoxMP] bad setting " + key + ": " + e.Message);
                    }
                }
            }
            if (!found.SetEquals(Keys)) cfg.Save(path);
            return cfg;
        }

        internal void Save(string path)
        {
            File.WriteAllText(path,
                "# WorldBoxMP settings\n" +
                "# the last address typed in \"Join by IP\"\n" +
                "host_ip=" + HostIp + "\n" +
                "port=" + Port + "\n" +
                "# the whole world is sent on connect and on F9; differences are repaired object by object.\n" +
                "# A periodic full re-send (seconds) is only a last resort for a broken stream (0 = off)\n" +
                "full_resync_seconds=" + ResyncSeconds.ToString(CultureInfo.InvariantCulture) + "\n" +
                "# how often the host sends changes, in milliseconds\n" +
                "tick_ms=" + TickMs + "\n" +
                "# show players' names above the units they control (1 = on, 0 = off)\n" +
                "show_names=" + (ShowNames ? "1" : "0") + "\n" +
                "# units a player is not looking at are re-checked (name, health, level, hunger...) this often,\n" +
                "# in seconds; on their screen every 2 s, near it every 5 s; buildings 2.5x slower\n" +
                "info_refresh_seconds=" + InfoSeconds.ToString(CultureInfo.InvariantCulture) + "\n");
        }
    }
}
