using System;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class EraSync
    {
        public static int Changes;

        public static string HostAge() => Access.MapStats(World.world)?.world_age_id ?? "";

        public static void Apply(string ageId)
        {
            if (string.IsNullOrEmpty(ageId)) return;
            WorldAgeManager m = Access.Manager(World.world);
            if (m == null) return;
            WorldAgeAsset current = m.getCurrentAge();
            if (current != null && current.id == ageId) return;
            WorldAgeAsset asset = AssetManager.era_library.get(ageId);
            if (asset == null) return;
            MapStats stats = Access.MapStats(World.world);
            if (stats != null) stats.world_age_id = ageId;
            Access.SetCurrentAge(m, asset, false);
            Changes++;
            SessionLog.Write("world age now " + ageId + (current != null ? " (was " + current.id + ")" : ""));
        }

        public static void UpdateClient()
        {
            WorldAgeManager m = Access.Manager(World.world);
            if (m == null || m.getCurrentAge() == null) return;
            Access.SpecialTimer(m) = 1000f;
            Access.UpdateEffects(m, Time.deltaTime);
            WorldAgeEffects.instance?.update(Time.deltaTime);
        }
    }
}
