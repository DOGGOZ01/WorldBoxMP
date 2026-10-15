using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    [HarmonyPatch(typeof(MapBox), "checkMainSimulationUpdate")]
    static class FreezeClientSimulation
    {
        static bool Prefix() => !Net.IsClientConnected && !Net.MirrorWorld;
    }

    [HarmonyPatch(typeof(AutoSaveManager), "update")]
    static class NoClientAutosave
    {
        static bool Prefix() => !Net.IsClientConnected && !Net.MirrorWorld;
    }

    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    static class OwnWorldAgain
    {
        static void Prefix()
        {
            if (Net.IsClientConnected || !Net.MirrorWorld) return;
            Net.MirrorWorld = false;
            Debug.Log("[WorldBoxMP] the host's world copy is replaced; this world simulates on its own again");
        }
    }

    [HarmonyPatch(typeof(BuildingManager), "canBuildFrom")]
    static class ClientLoadsEveryBuilding
    {
        static bool Prefix(BuildPlacingType pType, ref bool __result)
        {
            if (!Net.IsClientConnected || pType != BuildPlacingType.Load) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), "clickedFinal")]
    static class SendClientPowers
    {
        static bool Prefix(Vector2Int pPos, GodPower pPower)
        {
            if (!Net.IsClientConnected) return true;
            if (pPower == null) pPower = World.world.selected_power;
            if (pPower == null) return false;
            if (pPower.id == "magnet" || pPower.id == "bowling_ball") return false;
            Perm need = Rules.Of(pPower);
            if (!Rules.ClientMayPower(pPower)) { Rules.Refuse(need); return false; }
            Powers.SendClick(pPos, pPower);
            return false;
        }
    }

    [HarmonyPatch(typeof(ControllableUnit), "setControllableCreatures")]
    static class PlayerPossessesOne
    {
        static bool Prefix(object __0)
        {
            if (!Net.IsClientConnected) return true;
            Actor one = null;
            if (__0 is System.Collections.IEnumerable list)
                foreach (object o in list)
                    if (o is Actor a && a.canBePossessed() && (one == null || a == SelectedUnit.unit)) one = a;
            if (one != null) ControllableUnit.setControllableCreature(one);
            return false;
        }
    }

    [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.setControllableCreatureAndSelected))]
    static class PlayerPossessesOneOfSelected
    {
        static bool Prefix(Actor pActor)
        {
            if (!Net.IsClientConnected) return true;
            SelectedUnit.clear();
            ControllableUnit.setControllableCreature(pActor);
            return false;
        }
    }

    [HarmonyPatch(typeof(ControllableUnit), nameof(ControllableUnit.setControllableCreature))]
    static class PossessingKeepsTheSpeed
    {
        static void Prefix(out WorldTimeScaleAsset __state)
        {
            __state = Net.IsClientConnected ? Config.time_scale_asset : null;
            PlayerEdits.SpeedByGame = __state != null;
        }

        static void Postfix(WorldTimeScaleAsset __state)
        {
            try { if (__state != null && Config.time_scale_asset != __state) Config.setWorldSpeed(__state); }
            finally { PlayerEdits.SpeedByGame = false; }
        }
    }

    [HarmonyPatch(typeof(Config), nameof(Config.setWorldSpeed), typeof(WorldTimeScaleAsset), typeof(bool))]
    static class PlayerSetsTheSpeed
    {
        static void Prefix(out WorldTimeScaleAsset __state) => __state = Config.time_scale_asset;

        static void Postfix(WorldTimeScaleAsset __state) => PlayerEdits.SpeedSet(__state);
    }

    [HarmonyPatch(typeof(ZoneCalculator), nameof(ZoneCalculator.drawZoneAlliance))]
    static class PlayerAllianceBorders
    {
        static bool Prefix(TileZone pZone) => !Net.IsClientConnected || pZone.city == null || Access.CityKingdom(pZone.city) != null;

        static System.Exception Finalizer(System.Exception __exception) => Net.IsClientConnected ? null : __exception;
    }
}
