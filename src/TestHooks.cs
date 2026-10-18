using System;
using UnityEngine;

namespace WorldBoxMP
{
    public static class TestHooks
    {
        public static string Name;
        public static int LagMs, RateKiBps, Protocol;
        public static Vector2? Movement, Mouse;
        public static bool Attack, ForceInteractive, LoseLobby, NoSaveWindow;
        public static string LastLeave;
        public static Action<MPMod> Update;
        public static Action ClientDropped, WorldLoaded;
    }
}
