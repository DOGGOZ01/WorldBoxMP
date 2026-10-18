using ai.behaviours;
using DG.Tweening;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System;
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

        public static readonly AccessTools.FieldRef<Projectile, Kingdom> ProjectileKingdom =
            AccessTools.FieldRefAccess<Projectile, Kingdom>("kingdom");

        public static readonly AccessTools.FieldRef<Dragon, DragonState> DragonStateRef =
            AccessTools.FieldRefAccess<Dragon, DragonState>("state");

        public static readonly AccessTools.FieldRef<UFO, SpriteAnimation> UfoBeam =
            AccessTools.FieldRefAccess<UFO, SpriteAnimation>("beamAnim");

        public static readonly AccessTools.FieldRef<UFO, SpriteRenderer> UfoBeamRenderer =
            AccessTools.FieldRefAccess<UFO, SpriteRenderer>("beamRnd");

        public static readonly Action<SpriteAnimation, float> AnimationUpdate =
            AccessTools.MethodDelegate<Action<SpriteAnimation, float>>(AccessTools.Method(typeof(SpriteAnimation), "update"));

        public static readonly AccessTools.FieldRef<StackEffects, List<BaseEffectController>> Controllers =
            AccessTools.FieldRefAccess<StackEffects, List<BaseEffectController>>("list");

        public static readonly AccessTools.FieldRef<DropManager, int> ActiveDrops =
            AccessTools.FieldRefAccess<DropManager, int>("_activeIndex");

        public static readonly HarmonyLib.AccessTools.FieldRef<City, System.Collections.Generic.List<TileZone>> CityZones =
            HarmonyLib.AccessTools.FieldRefAccess<City, System.Collections.Generic.List<TileZone>>("zones");

        public static readonly Action<MapBox> CheckDirtyUnits =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkDirtyUnits"));

        public static readonly Action<MapBox> CheckDirtyMetaObjects =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkDirtyMetaObjects"));

        public static readonly Action<MapBox> CheckAnyMetaAddedRemoved =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkAnyMetaAddedRemoved"));

        public static readonly AccessTools.FieldRef<MapBox, List<MapLayer>> MapLayers =
            AccessTools.FieldRefAccess<MapBox, List<MapLayer>>("_map_layers");

        public static readonly Action<Actor, float> UpdateRotations =
            AccessTools.MethodDelegate<Action<Actor, float>>(AccessTools.Method(typeof(Actor), "updateRotations"));

        public static readonly Action<Actor, float> UpdateFlipRotation =
            AccessTools.MethodDelegate<Action<Actor, float>>(AccessTools.Method(typeof(Actor), "updateFlipRotation"));

        public static readonly AccessTools.FieldRef<MapBox, WorldTile[]> ClientSyncTilesList =
            AccessTools.FieldRefAccess<MapBox, WorldTile[]>("tiles_list");

        public static readonly Action<ActorManager, Actor> ScheduleActorDestroy =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));

        public static readonly Action<Building> RemoveBuildingFinal =
            AccessTools.MethodDelegate<Action<Building>>(AccessTools.Method(typeof(Building), "removeBuildingFinal"));

        public static readonly Action<MapBox> CheckObjectsToDestroy =
            AccessTools.MethodDelegate<Action<MapBox>>(AccessTools.Method(typeof(MapBox), "checkObjectsToDestroy"));

        public static readonly AccessTools.FieldRef<MapBox, BurnedTilesLayer> BurnedLayer =
            AccessTools.FieldRefAccess<MapBox, BurnedTilesLayer>("burned_layer");

        public static readonly AccessTools.FieldRef<float> AnimTimeSimulation =
            AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(AnimationHelper), "_time_simulation"));

        public static readonly AccessTools.FieldRef<float> AnimTimeSession =
            AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(AnimationHelper), "_time_session"));

        public static readonly AccessTools.FieldRef<Drop, float> HeightZ =
            AccessTools.FieldRefAccess<Drop, float>("_currentHeightZ");

        public static readonly AccessTools.FieldRef<Drop, bool> Landed =
            AccessTools.FieldRefAccess<Drop, bool>("_landed");

        public static readonly AccessTools.FieldRef<Drop, Vector2> TargetPosition =
            AccessTools.FieldRefAccess<Drop, Vector2>("_targetPosition");

        public static readonly AccessTools.FieldRef<Drop, Vector2> StartPosition =
            AccessTools.FieldRefAccess<Drop, Vector2>("_startPosition");

        public static readonly AccessTools.FieldRef<Drop, float> TargetHeight =
            AccessTools.FieldRefAccess<Drop, float>("_targetHeight");

        public static readonly AccessTools.FieldRef<Drop, float> TimeToTarget =
            AccessTools.FieldRefAccess<Drop, float>("_timeToTarget");

        public static readonly AccessTools.FieldRef<Drop, float> FallingSpeed =
            AccessTools.FieldRefAccess<Drop, float>("_falling_speed");

        public static readonly AccessTools.FieldRef<Boulder, Vector2> BoulderPrev =
            AccessTools.FieldRefAccess<Boulder, Vector2>("_previous_bounce_position");

        public static readonly AccessTools.FieldRef<Boulder, List<Vector2>> BoulderBounces =
            AccessTools.FieldRefAccess<Boulder, List<Vector2>>("_bounce_positions");

        public static readonly AccessTools.FieldRef<Boulder, float> BoulderForce =
            AccessTools.FieldRefAccess<Boulder, float>("_force_timer");

        public static readonly AccessTools.FieldRef<Boulder, float> BoulderSpin =
            AccessTools.FieldRefAccess<Boulder, float>("angleRotation");

        public static readonly System.Action<Boulder> BoulderPlace =
            AccessTools.MethodDelegate<System.Action<Boulder>>(AccessTools.Method(typeof(Boulder), "updateCurrentPosition"));

        public static readonly AccessTools.FieldRef<Actor, EquipmentAsset> AttackAsset =
            AccessTools.FieldRefAccess<Actor, EquipmentAsset>("_attack_asset");

        public static readonly AccessTools.FieldRef<Actor, float> ActorScale =
            AccessTools.FieldRefAccess<Actor, float>("actor_scale");

        public static readonly AccessTools.FieldRef<MapBox, WorldAgeManager> Manager =
            AccessTools.FieldRefAccess<MapBox, WorldAgeManager>("era_manager");

        public static readonly Action<WorldAgeManager, WorldAgeAsset, bool> SetCurrentAge =
            AccessTools.MethodDelegate<Action<WorldAgeManager, WorldAgeAsset, bool>>(AccessTools.Method(typeof(WorldAgeManager), "setCurrentAge"));

        public static readonly Action<WorldAgeManager, float> UpdateEffects =
            AccessTools.MethodDelegate<Action<WorldAgeManager, float>>(AccessTools.Method(typeof(WorldAgeManager), "updateEffects"));

        public static readonly AccessTools.FieldRef<WorldAgeManager, float> SpecialTimer =
            AccessTools.FieldRefAccess<WorldAgeManager, float>("_timer_special_action");

        public static readonly AccessTools.FieldRef<MapBox, WorldLaws> Laws =
            AccessTools.FieldRefAccess<MapBox, WorldLaws>("world_laws");

        public static readonly HarmonyLib.AccessTools.FieldRef<LocalizedTextManager, Dictionary<string, string>> Texts =
            HarmonyLib.AccessTools.FieldRefAccess<LocalizedTextManager, Dictionary<string, string>>("_localized_text");

        static readonly Type AiBase = typeof(AiSystemActor).BaseType;

        public static readonly FieldInfo AiTask =
            AccessTools.Field(AiBase, "task");

        public static readonly FieldInfo AiAction =
            AccessTools.Field(AiBase, "action");

        public static readonly FieldInfo AiActionIndex =
            AccessTools.Field(AiBase, "action_index");

        public static readonly AccessTools.FieldRef<Actor, AiSystemActor> ActorAi =
            AccessTools.FieldRefAccess<Actor, AiSystemActor>("ai");

        public static readonly AccessTools.FieldRef<Actor, double> SocialTween =
            AccessTools.FieldRefAccess<Actor, double>("timestamp_tween_session_social");

        public static readonly AccessTools.FieldRef<Actor, double> AteAt =
            AccessTools.FieldRefAccess<Actor, double>("timestamp_session_ate_food");

        public static readonly AccessTools.FieldRef<Actor, string> AteWhat =
            AccessTools.FieldRefAccess<Actor, string>("ate_last_item_id");

        public static readonly AccessTools.FieldRef<Actor, WorldTile> BehTileTarget =
            AccessTools.FieldRefAccess<Actor, WorldTile>("beh_tile_target");

        public static readonly AccessTools.FieldRef<City, City> TargetAttackCity =
            AccessTools.FieldRefAccess<City, City>("target_attack_city");

        public static readonly AccessTools.FieldRef<City, Kingdom> BeingCapturedBy =
            AccessTools.FieldRefAccess<City, Kingdom>("being_captured_by");

        public static readonly AccessTools.FieldRef<BehaviourActionActor, bool> Socialize =
            AccessTools.FieldRefAccess<BehaviourActionActor, bool>("socialize");

        public static readonly FieldInfo DeathsCivs =
            AccessTools.Field(typeof(BattleContainer), "_deaths_civs");

        public static readonly FieldInfo DeathsMobs =
            AccessTools.Field(typeof(BattleContainer), "_deaths_mobs");

        public static readonly AccessTools.FieldRef<MapBox, MapStats> MapStatsRef =
            AccessTools.FieldRefAccess<MapBox, MapStats>("map_stats");

        public static readonly Action<MapStats> RecalcCounters =
            AccessTools.MethodDelegate<Action<MapStats>>(AccessTools.Method(typeof(MapStats), "recalcCounters"));

        public static readonly Action<City, TileZone> CityRemoveZone =
            AccessTools.MethodDelegate<Action<City, TileZone>>(AccessTools.Method(typeof(City), "removeZone"));

        public static readonly Action<Kingdom> KingdomLoad2 =
            AccessTools.MethodDelegate<Action<Kingdom>>(AccessTools.Method(typeof(Kingdom), "load2"));

        public static readonly AccessTools.FieldRef<City, List<TileZone>> MetaSyncCityZones =
            AccessTools.FieldRefAccess<City, List<TileZone>>("zones");

        public static readonly Action<ZoneCalculator> SetDrawnZonesDirty =
            AccessTools.MethodDelegate<Action<ZoneCalculator>>(AccessTools.Method(typeof(ZoneCalculator), "setDrawnZonesDirty"));

        public static readonly Action<ActorManager, Actor> ObjectRepairScheduleActorDestroy =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));

        public static readonly Action<Building> ObjectRepairRemoveBuildingFinal =
            AccessTools.MethodDelegate<Action<Building>>(AccessTools.Method(typeof(Building), "removeBuildingFinal"));

        public static readonly Func<PlayerControl, Actor, WorldTile, bool, bool> MoveOne =
            AccessTools.MethodDelegate<Func<PlayerControl, Actor, WorldTile, bool, bool>>(AccessTools.Method(typeof(PlayerControl), "tryToMoveSelectedUnit"));

        public static readonly AccessTools.FieldRef<PlayerControl, Vector2> SquareStart =
            AccessTools.FieldRefAccess<PlayerControl, Vector2>("square_selection_position_current");

        public static readonly AccessTools.FieldRef<PlayerControl, float> ClickTimer =
            AccessTools.FieldRefAccess<PlayerControl, float>("_click_timer");

        public static readonly AccessTools.FieldRef<PlayerControl, WorldTile> FirstTile =
            AccessTools.FieldRefAccess<PlayerControl, WorldTile>("first_pressed_tile");

        public static readonly AccessTools.FieldRef<PlayerControl, TileType> FirstType =
            AccessTools.FieldRefAccess<PlayerControl, TileType>("first_pressed_type");

        public static readonly AccessTools.FieldRef<PlayerControl, TopTileType> FirstTopType =
            AccessTools.FieldRefAccess<PlayerControl, TopTileType>("first_pressed_top_type");

        public static readonly System.Action<PlayerControl, Vector2Int, GodPower, bool> ClickedFinal =
            AccessTools.MethodDelegate<System.Action<PlayerControl, Vector2Int, GodPower, bool>>( AccessTools.Method(typeof(PlayerControl), "clickedFinal"));

        public static readonly AccessTools.FieldRef<Texture2D[]> Cursors =
            AccessTools.StaticFieldRefAccess<Texture2D[]>(AccessTools.Field(typeof(MouseCursor), "_cursors"));

        public static readonly AccessTools.FieldRef<int> CurrentCursor =
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(MouseCursor), "_cur_cursor"));

        public static readonly Action<Actor, bool> FindTile =
            HarmonyLib.AccessTools.MethodDelegate<Action<Actor, bool>>(HarmonyLib.AccessTools.Method(typeof(Actor), "findCurrentTile"));

        public static readonly Action<Actor, WorldTile, float> SpawnOn =
            HarmonyLib.AccessTools.MethodDelegate<Action<Actor, WorldTile, float>>(HarmonyLib.AccessTools.Method(typeof(Actor), "spawnOn", new[] { typeof(WorldTile), typeof(float) }));

        public static readonly Func<BaseSimObject, string, float, bool, bool> AddStatus =
            HarmonyLib.AccessTools.MethodDelegate<Func<BaseSimObject, string, float, bool, bool>>(HarmonyLib.AccessTools.Method(typeof(BaseSimObject), "addStatusEffect", new[] { typeof(string), typeof(float), typeof(bool) }));

        public static readonly HarmonyLib.AccessTools.FieldRef<Actor, Vector3> TargetAngle =
            HarmonyLib.AccessTools.FieldRefAccess<Actor, Vector3>("target_angle");

        public static readonly HarmonyLib.AccessTools.FieldRef<Vector2> BallPullFrom =
            HarmonyLib.AccessTools.StaticFieldRefAccess<Vector2>(HarmonyLib.AccessTools.Field(typeof(Boulder), "_initial_charge_position"));

        public static readonly HarmonyLib.AccessTools.FieldRef<bool> BallCharging =
            HarmonyLib.AccessTools.StaticFieldRefAccess<bool>(HarmonyLib.AccessTools.Field(typeof(Boulder), "_charge_started"));

        public static readonly Action<Actor> AddPossessedStatus =
            AccessTools.MethodDelegate<Action<Actor>>(AccessTools.Method(typeof(ControllableUnit), "addStatus"));

        public static readonly AccessTools.FieldRef<Army, Actor> ArmyCaptain =
            AccessTools.FieldRefAccess<Army, Actor>("_captain");

        public static readonly AccessTools.FieldRef<Family, Actor> FamilyAlpha =
            AccessTools.FieldRefAccess<Family, Actor>("_cached_alpha");

        public static readonly AccessTools.FieldRef<Family, Actor> FamilyFounder1 =
            AccessTools.FieldRefAccess<Family, Actor>("_founder_1");

        public static readonly AccessTools.FieldRef<Family, Actor> FamilyFounder2 =
            AccessTools.FieldRefAccess<Family, Actor>("_founder_2");

        public static readonly AccessTools.FieldRef<List<Tweener>> WindowAnimations =
            AccessTools.StaticFieldRefAccess<List<Tweener>>(AccessTools.Field(typeof(ScrollWindow), "_animations_list"));

        public static readonly AccessTools.FieldRef<PowersTab, PowerTabAsset> TabAssetRef =
            AccessTools.FieldRefAccess<PowersTab, PowerTabAsset>("_asset");

        public static readonly AccessTools.FieldRef<PowerButton, GodPower> ButtonPowerRef =
            AccessTools.FieldRefAccess<PowerButton, GodPower>("godPower");

        public static readonly AccessTools.FieldRef<PowerButtonSelector> SelectorInstance =
            AccessTools.StaticFieldRefAccess<PowerButtonSelector>(AccessTools.Field(typeof(PowerButtonSelector), "instance"));

        public static readonly AccessTools.FieldRef<BaseActorComponent, Actor> ComponentActor =
            AccessTools.FieldRefAccess<BaseActorComponent, Actor>("actor");

        public static readonly Action<ActorManager, Actor> SpecialActorsScheduleActorDestroy =
            AccessTools.MethodDelegate<Action<ActorManager, Actor>>(AccessTools.Method(typeof(ActorManager), "scheduleDestroyOnPlay"));

        public static readonly AccessTools.FieldRef<Vector2> MovementField =
            AccessTools.StaticFieldRefAccess<Vector2>(AccessTools.Field(typeof(ControllableUnit), "_movement_vector"));

        public static readonly AccessTools.FieldRef<bool> AttackLeftField =
            AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(ControllableUnit), "_attack_pressed_button_left"));

        public static readonly AccessTools.FieldRef<BaseSimObject, Dictionary<string, Status>> ActiveStatuses =
            AccessTools.FieldRefAccess<BaseSimObject, Dictionary<string, Status>>("_active_status_dict");

        public static readonly AccessTools.FieldRef<BaseSimObject, bool> HasAnyStatus =
            AccessTools.FieldRefAccess<BaseSimObject, bool>("_has_any_status_cached");

        public static readonly Action<BaseSimObject> UpdateCachedStatuses =
            AccessTools.MethodDelegate<Action<BaseSimObject>>(AccessTools.Method(typeof(BaseSimObject), "updateCachedStatusEffects"));

        public static readonly Action<Status, float> UpdateAnimationFrame =
            AccessTools.MethodDelegate<Action<Status, float>>(AccessTools.Method(typeof(Status), "updateAnimationFrame"));
    }
}
