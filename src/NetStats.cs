using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace WorldBoxMP
{
    public static class NetStats
    {
        public static long BytesIn, BytesOut, PacketsIn, PacketsOut;
        static long _lastIn, _lastOut, _lastPacketsIn, _lastPacketsOut;
        static float _pingTimer;
        static double _rttSum, _rttMax, _rttLast;
        static int _rttCount;
        static float _windowStart;
        static int _steamReportCounter;

        public static void Reset()
        {
            BytesIn = BytesOut = PacketsIn = PacketsOut = 0;
            _lastIn = _lastOut = _lastPacketsIn = _lastPacketsOut = 0;
            _rttSum = _rttMax = _rttLast = 0;
            _rttCount = 0;
            _windowStart = Time.unscaledTime;
        }

        public static void CountIn(Packet p)
        {
            BytesIn += (p.Data?.Length ?? 0) + 1;
            PacketsIn++;
        }

        public static void CountOut(byte[] data)
        {
            BytesOut += (data?.Length ?? 0) + 1;
            PacketsOut++;
        }

        public static void Update()
        {
            if (Net.Peer == null || !(Net.IsHostWithPeer || Net.IsClientConnected)) return;
            _pingTimer -= Time.unscaledDeltaTime;
            if (_pingTimer > 0f) return;
            _pingTimer = 2f;
            Net.Peer.Send(Msg.Ping, BitConverter.GetBytes(Clock.Now));
        }

        public static void OnPing(byte[] data) => Net.Peer?.Send(Msg.Pong, data);

        public static int LastPingMs => (int)Math.Round(_rttLast);

        public static void OnPong(byte[] data)
        {
            if (data == null || data.Length < 8) return;
            double rtt = (Clock.Now - BitConverter.ToDouble(data, 0)) * 1000.0;
            _rttLast = rtt;
            _rttSum += rtt;
            _rttCount++;
            if (rtt > _rttMax) _rttMax = rtt;
        }

        public static string Line()
        {
            float window = Mathf.Max(0.1f, Time.unscaledTime - _windowStart);
            var sb = new StringBuilder("NET ").Append(Net.Role);
            Peer peer = Net.Peer;
            sb.Append(" remote=").Append(peer != null ? peer.Remote.Replace(' ', '_') : "none");
            sb.Append(" ping_ms=").Append(_rttCount > 0 ? (_rttSum / _rttCount).ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "-")
              .Append(" ping_max_ms=").Append(_rttCount > 0 ? _rttMax.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "-")
              .Append(" ping_last_ms=").Append(_rttCount > 0 ? _rttLast.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "-")
              .Append(" in_kib_s=").Append(((BytesIn - _lastIn) / 1024f / window).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
              .Append(" out_kib_s=").Append(((BytesOut - _lastOut) / 1024f / window).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
              .Append(" in_msgs_s=").Append(((PacketsIn - _lastPacketsIn) / window).ToString("0", System.Globalization.CultureInfo.InvariantCulture))
              .Append(" out_msgs_s=").Append(((PacketsOut - _lastPacketsOut) / window).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            if (peer is SteamPeer sp) sb.Append(" steam_queue_chunks=").Append(sp.QueuedChunks);
            if (peer != null)
                sb.Append(" backlog_kib=").Append(peer.PendingBytes / 1024)
                  .Append(" send_rate_kib_s=").Append(peer.SendRate / 1024);
            if (Net.Role == Role.Host)
                sb.Append(" flow=").Append(Flow.Level).Append(" flow_max=").Append(Flow.MaxLevelSeen)
                  .Append(" backlog_s=").Append(Flow.BacklogSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(' ').Append(ViewSync.Describe().Replace(' ', '_'));
            if (Net.IsClientConnected) sb.Append(' ').Append(ClientSync.StreamReport());
            _lastIn = BytesIn;
            _lastOut = BytesOut;
            _lastPacketsIn = PacketsIn;
            _lastPacketsOut = PacketsOut;
            _rttSum = _rttMax = 0;
            _rttCount = 0;
            _windowStart = Time.unscaledTime;

            if (peer is SteamPeer steam && _steamReportCounter++ % 3 == 0)
            {
                string detail = steam.DetailedStatus();
                if (!string.IsNullOrEmpty(detail))
                {
                    var flat = new StringBuilder();
                    foreach (string l in detail.Split('\n'))
                    {
                        string t = l.Trim();
                        if (t.Length > 0) flat.Append(t).Append(" | ");
                        if (flat.Length > 900) break;
                    }
                    sb.Append("\n    steam: ").Append(flat);
                }
            }
            return sb.ToString();
        }

        public static string SystemInfo()
        {
            var sb = new StringBuilder("SYSTEM");
            try
            {
                sb.Append(" os=\"").Append(SystemInfoText(UnityEngine.SystemInfo.operatingSystem)).Append('"')
                  .Append(" cpu=\"").Append(SystemInfoText(UnityEngine.SystemInfo.processorType)).Append("\" cores=").Append(UnityEngine.SystemInfo.processorCount)
                  .Append(" ram_mb=").Append(UnityEngine.SystemInfo.systemMemorySize)
                  .Append(" gpu=\"").Append(SystemInfoText(UnityEngine.SystemInfo.graphicsDeviceName)).Append("\" vram_mb=").Append(UnityEngine.SystemInfo.graphicsMemorySize)
                  .Append(" screen=").Append(Screen.width).Append('x').Append(Screen.height)
                  .Append(" utc_offset=").Append(TimeZoneInfo.Local.GetUtcOffset(DateTime.Now))
                  .Append(" utc_now=").Append(DateTime.UtcNow.ToString("HH:mm:ss.fff"))
                  .Append(" worldfall=").Append(WorldfallBridge.Present ? "yes" : "no");
                var cfg = MPMod.Cfg;
                if (cfg != null)
                    sb.Append(" cfg: tick_ms=").Append(cfg.TickMs).Append(" full_resync_seconds=").Append(cfg.ResyncSeconds)
                      .Append(" info_refresh_seconds=").Append(cfg.InfoSeconds).Append(" show_names=").Append(cfg.ShowNames ? 1 : 0);
                sb.Append("\n    mods: ").Append(ModList());
            }
            catch (Exception e)
            {
                sb.Append(" (failed: ").Append(e.Message).Append(')');
            }
            return sb.ToString();
        }

        static string SystemInfoText(string s) => (s ?? "").Replace('"', '\'');

        static string ModList()
        {
            try
            {
                var names = new StringBuilder();
                foreach (var mod in NeoModLoader.WorldBoxMod.LoadedMods)
                {
                    var d = mod.GetDeclaration();
                    if (names.Length > 0) names.Append(", ");
                    names.Append(d.Name).Append(' ').Append(d.Version);
                }
                return names.ToString();
            }
            catch
            {
                try { return string.Join(", ", Array.ConvertAll(Directory.GetDirectories(Path.Combine(Application.dataPath, "../Mods")), Path.GetFileName)); }
                catch { return "?"; }
            }
        }
    }
}
