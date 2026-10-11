using System.Diagnostics;

namespace WorldBoxMP
{
    public static class Clock
    {
        private static readonly Stopwatch _watch = Stopwatch.StartNew();

        public static double Now => _watch.Elapsed.TotalSeconds;
    }
}
