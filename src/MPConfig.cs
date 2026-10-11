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
        public float ResyncSeconds = 120f;
        public int TickMs = 50;
        public string PlayerName = "";
        public bool ShowNames = true;

        static readonly string[] Keys = { "host_ip", "port", "full_resync_seconds", "tick_ms", "player_name", "show_names" };

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
                            case "full_resync_seconds": cfg.ResyncSeconds = float.Parse(val, CultureInfo.InvariantCulture); break;
                            case "tick_ms": cfg.TickMs = Math.Max(20, int.Parse(val, CultureInfo.InvariantCulture)); break;
                            case "player_name": cfg.PlayerName = val; break;
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

        void Save(string path)
        {
            File.WriteAllText(path,
                "# WorldBoxMP settings\n" +
                "# the host's IP address (the second player sets this before pressing F8)\n" +
                "host_ip=" + HostIp + "\n" +
                "port=" + Port + "\n" +
                "# changes are streamed live; the whole world is also re-sent this often to catch up kingdoms,\n" +
                "# cities and other details the stream does not carry yet (0 = only on connect and F9)\n" +
                "full_resync_seconds=" + ResyncSeconds.ToString(CultureInfo.InvariantCulture) + "\n" +
                "# how often the host sends changes, in milliseconds\n" +
                "tick_ms=" + TickMs + "\n" +
                "# the name shown above the units you control (empty = your Steam name)\n" +
                "player_name=" + PlayerName + "\n" +
                "# show players' names above the units they control (1 = on, 0 = off)\n" +
                "show_names=" + (ShowNames ? "1" : "0") + "\n");
        }
    }
}
