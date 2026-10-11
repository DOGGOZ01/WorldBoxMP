using System;
using System.Collections.Generic;
using HarmonyLib;

namespace WorldBoxMP
{
    static class Diagnostics
    {
        static readonly HashSet<long> _reported = new HashSet<long>();
        static readonly object _lock = new object();

        public static void Reset()
        {
            lock (_lock) _reported.Clear();
        }

        [HarmonyPatch(typeof(PossessionUnitInfo), "showForUnit")]
        static class PossessionPanelGuard
        {
            static bool Prefix(Actor pActor)
            {
                if (!Net.IsClientConnected) return true;
                if (pActor == null || !pActor.isAlive() || Access.ActorData(pActor) == null) return false;
                if (pActor.kingdom == null) pActor.checkDefaultKingdom();
                return pActor.kingdom != null;
            }
        }

        static int _tooltips;

        [HarmonyPatch(typeof(TooltipLibrary), "showActor")]
        static class ActorTooltip
        {
            static Exception Finalizer(TooltipData pData, Exception __exception)
            {
                if (__exception == null || !Net.IsClientConnected || _tooltips++ >= 5) return __exception;
                try
                {
                    Actor a = pData?.actor;
                    SessionLog.Write("diag: actor tooltip failed for " + (a == null ? "null actor" :
                        a.getID() + " " + a.asset?.id + " alive=" + a.isAlive() + " kingdom=" + (a.kingdom == null ? "null" : a.kingdom.name + " color=" + (a.kingdom.getColor() == null ? "null" : "ok")) +
                        " data=" + (Access.ActorData(a) == null ? "null" : "ok") + " city=" + (a.city?.name ?? "null") +
                        " lover=" + (a.lover == null ? "none" : a.lover.getID() + " kingdom=" + (a.lover.kingdom?.name ?? "null")) +
                        " clan=" + (a.clan == null ? "none" : "ok") + " subspecies=" + (a.subspecies == null ? "none" : "ok") +
                        " culture=" + (a.culture == null ? "none" : "ok") + " in_list=" + (World.world.units.get(a.getID()) == a)) +
                        " | " + __exception.StackTrace?.Split('\n')[0]);
                }
                catch { }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.calculateMainSprite))]
        static class BuildingSprite
        {
            static Exception Finalizer(Building __instance, Exception __exception)
            {
                if (__exception == null || !Net.IsClientConnected) return __exception;
                try
                {
                    long id = __instance.getID();
                    lock (_lock)
                    {
                        if (_reported.Count >= 10 || !_reported.Add(id)) return __exception;
                    }
                    BuildingData d = Access.BuildingData(__instance);
                    BuildingAsset asset = Access.BuildingAsset(__instance);
                    var flags = d?.custom_data_flags != null ? string.Join(",", d.custom_data_flags) : "";
                    SessionLog.Write("diag: building sprite failed for " + id + " " + (asset != null ? asset.id : "no asset") +
                                     " state=" + (d != null ? d.state.ToString() : "no data") +
                                     " anim=" + (__instance.animData == null ? "null" :
                                         "main:" + (__instance.animData.main?.Length ?? -1) + " special:" + (__instance.animData.special?.Length ?? -1) +
                                         " ruins:" + (__instance.animData.ruins?.Length ?? -1) + " disabled:" + (__instance.animData.main_disabled?.Length ?? -1) +
                                         " spawn:" + (__instance.animData.spawn?.Length ?? -1)) +
                                     " index=" + __instance.animData_index + " flags=" + flags +
                                     " kingdom=" + (__instance.kingdom != null ? __instance.kingdom.name : "null") +
                                     " city=" + (__instance.city != null ? __instance.city.name : "null") +
                                     " alive=" + __instance.isAlive() + " in_list=" + (World.world.buildings.get(id) == __instance) +
                                     " | " + __exception.GetType().Name + " " + __exception.StackTrace?.Split('\n')[0]);
                }
                catch { }
                return __exception;
            }
        }
    }
}
