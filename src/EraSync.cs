using System;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class EraSync
    {
        static readonly AccessTools.FieldRef<MapBox, WorldAgeManager> Manager =
            AccessTools.FieldRefAccess<MapBox, WorldAgeManager>("era_manager");
        static readonly Action<WorldAgeManager, WorldAgeAsset, bool> SetCurrentAge =
            AccessTools.MethodDelegate<Action<WorldAgeManager, WorldAgeAsset, bool>>(AccessTools.Method(typeof(WorldAgeManager), "setCurrentAge"));
        static readonly Action<WorldAgeManager, float> UpdateEffects =
            AccessTools.MethodDelegate<Action<WorldAgeManager, float>>(AccessTools.Method(typeof(WorldAgeManager), "updateEffects"));
        static readonly AccessTools.FieldRef<WorldAgeManager, float> SpecialTimer =
            AccessTools.FieldRefAccess<WorldAgeManager, float>("_timer_special_action");

        public static int Changes;

        public static string HostAge() => Access.MapStats(World.world)?.world_age_id ?? "";

        public static void Apply(string ageId)
        {
            if (string.IsNullOrEmpty(ageId)) return;
            WorldAgeManager m = Manager(World.world);
            if (m == null) return;
            WorldAgeAsset current = m.getCurrentAge();
            if (current != null && current.id == ageId) return;
            WorldAgeAsset asset = AssetManager.era_library.get(ageId);
            if (asset == null) return;
            MapStats stats = Access.MapStats(World.world);
            if (stats != null) stats.world_age_id = ageId;
            SetCurrentAge(m, asset, false);
            Changes++;
            SessionLog.Write("world age now " + ageId + (current != null ? " (was " + current.id + ")" : ""));
        }

        public static void UpdateClient()
        {
            WorldAgeManager m = Manager(World.world);
            if (m == null || m.getCurrentAge() == null) return;
            SpecialTimer(m) = 1000f;
            UpdateEffects(m, Time.deltaTime);
            WorldAgeEffects.instance?.update(Time.deltaTime);
        }
    }
}
