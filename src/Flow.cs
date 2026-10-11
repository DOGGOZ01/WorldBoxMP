using UnityEngine;

namespace WorldBoxMP
{
    public static class Flow
    {
        public static int Level { get; private set; }
        public static float BacklogSeconds { get; private set; }
        public static long BacklogBytes { get; private set; }
        public static int MaxLevelSeen;
        static float _timer, _calmFor;

        public static float Scale => Level == 0 ? 1f : Level == 1 ? 0.5f : 0.2f;

        public static void Reset()
        {
            Level = 0;
            BacklogSeconds = 0f;
            BacklogBytes = 0;
            _calmFor = 0f;
        }

        public static void UpdateHost()
        {
            Peer peer = Net.Peer;
            if (peer == null || !Net.IsHostWithPeer) { Reset(); return; }
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.1f;
            BacklogBytes = peer.PendingBytes;
            int rate = peer.SendRate > 0 ? peer.SendRate : 256 * 1024;
            BacklogSeconds = BacklogBytes / (float)Mathf.Max(32 * 1024, rate);
            int want = BacklogSeconds > 0.3f ? 2 : BacklogSeconds > 0.1f ? 1 : 0;
            if (want > Level)
            {
                Log(want);
                Level = want;
                _calmFor = 0f;
            }
            else if (want < Level)
            {
                _calmFor += 0.1f;
                if (_calmFor >= 1f) { Log(Level - 1); Level--; _calmFor = 0f; }
            }
            else _calmFor = 0f;
            if (Level > MaxLevelSeen) MaxLevelSeen = Level;
        }

        static void Log(int to)
        {
            SessionLog.Write("flow level " + Level + " -> " + to + ": backlog " + (BacklogBytes / 1024) + " KiB, " +
                             BacklogSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s");
        }
    }
}
