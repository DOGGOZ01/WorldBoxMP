using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    static class Access
    {
        public static readonly AccessTools.FieldRef<Actor, ActorData> ActorData =
            AccessTools.FieldRefAccess<Actor, ActorData>("data");
        public static readonly AccessTools.FieldRef<Actor, bool> Flip =
            AccessTools.FieldRefAccess<Actor, bool>("flip");
        public static readonly AccessTools.FieldRef<Actor, bool> IsMoving =
            AccessTools.FieldRefAccess<Actor, bool>("_is_moving");
        public static readonly AccessTools.FieldRef<Actor, bool> IsVisible =
            AccessTools.FieldRefAccess<Actor, bool>("is_visible");
        public static readonly AccessTools.FieldRef<Actor, bool> InMagnet =
            AccessTools.FieldRefAccess<Actor, bool>("is_in_magnet");
        public static readonly AccessTools.FieldRef<Actor, UnitProfession> Profession =
            AccessTools.FieldRefAccess<Actor, UnitProfession>("_profession");
        public static readonly AccessTools.FieldRef<Building, bool> BuildingVisible =
            AccessTools.FieldRefAccess<Building, bool>("is_visible");
        public static readonly AccessTools.FieldRef<Building, BuildingTweenScaleHelper> ScaleHelper =
            AccessTools.FieldRefAccess<Building, BuildingTweenScaleHelper>("scale_helper");
        public static readonly AccessTools.FieldRef<BuildingTweenScaleHelper, bool> TweenActive =
            AccessTools.FieldRefAccess<BuildingTweenScaleHelper, bool>("active");
        public static readonly System.Action<Building> UpdateScaleTween =
            AccessTools.MethodDelegate<System.Action<Building>>(AccessTools.Method(typeof(BuildingTweenExtension), "updateScale"));
        public static readonly AccessTools.FieldRef<Building, BuildingData> BuildingData =
            AccessTools.FieldRefAccess<Building, BuildingData>("data");
        public static readonly AccessTools.FieldRef<Building, BuildingAsset> BuildingAsset =
            AccessTools.FieldRefAccess<Building, BuildingAsset>("asset");
        public static readonly AccessTools.FieldRef<City, Kingdom> CityKingdom =
            AccessTools.FieldRefAccess<City, Kingdom>("kingdom");
        public static readonly AccessTools.FieldRef<MapBox, StackEffects> StackEffects =
            AccessTools.FieldRefAccess<MapBox, StackEffects>("stack_effects");
        public static readonly AccessTools.FieldRef<MapBox, float> Elapsed =
            AccessTools.FieldRefAccess<MapBox, float>("elapsed");
        public static readonly AccessTools.FieldRef<MapBox, ZoneCalculator> ZoneCalculator =
            AccessTools.FieldRefAccess<MapBox, ZoneCalculator>("zone_calculator");
        public static MoveCamera Camera => AccessTools.StaticFieldRefAccess<MoveCamera>(typeof(MoveCamera), "instance");
        public static readonly AccessTools.FieldRef<MapBox, WorldTile[]> TilesList =
            AccessTools.FieldRefAccess<MapBox, WorldTile[]>("tiles_list");
        public static readonly AccessTools.FieldRef<MapBox, MapStats> MapStats =
            AccessTools.FieldRefAccess<MapBox, MapStats>("map_stats");

        public static readonly AccessTools.FieldRef<Actor, bool> UnderForces =
            AccessTools.FieldRefAccess<Actor, bool>("under_forces");
        public static readonly AccessTools.FieldRef<Actor, double> LastAttack =
            AccessTools.FieldRefAccess<Actor, double>("last_attack_timestamp");
        public static readonly AccessTools.FieldRef<Actor, bool> DirtyTile =
            AccessTools.FieldRefAccess<Actor, bool>("dirty_current_tile");
        public static readonly AccessTools.FieldRef<Actor, BaseSimObject> AttackTarget =
            AccessTools.FieldRefAccess<Actor, BaseSimObject>("attack_target");
        public static readonly AccessTools.FieldRef<Actor, bool> Immovable =
            AccessTools.FieldRefAccess<Actor, bool>("is_immovable");
        public static readonly System.Action<Actor, Vector3, bool, bool, float> Punch =
            AccessTools.MethodDelegate<System.Action<Actor, Vector3, bool, bool, float>>(AccessTools.Method(typeof(Actor), "punchTargetAnimation"));
        public static readonly System.Func<Actor, float, Vector2, float> PossessedStep =
            AccessTools.MethodDelegate<System.Func<Actor, float, Vector2, float>>(AccessTools.Method(typeof(Actor), "updatePossessedMovementTowards"));
        public static readonly AccessTools.FieldRef<Projectile, float> ProjectileSpeed =
            AccessTools.FieldRefAccess<Projectile, float>("_speed");
        public static readonly System.Action<Projectile> ProjectileVelocities =
            AccessTools.MethodDelegate<System.Action<Projectile>>(AccessTools.Method(typeof(Projectile), "calculateVelocities"));
        public static readonly AccessTools.FieldRef<Projectile, Vector3> ProjectilePos =
            AccessTools.FieldRefAccess<Projectile, Vector3>("_current_position_3d");
    }
}
