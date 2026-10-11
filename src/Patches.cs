using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    [HarmonyPatch(typeof(MapBox), "checkMainSimulationUpdate")]
    static class FreezeClientSimulation
    {
        static bool Prefix() => !Net.IsClientConnected;
    }

    [HarmonyPatch(typeof(AutoSaveManager), "update")]
    static class NoClientAutosave
    {
        static bool Prefix() => !Net.IsClientConnected;
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
            Powers.SendClick(pPos, pPower);
            return false;
        }
    }
}
