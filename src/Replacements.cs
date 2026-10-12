using System.Collections.Generic;
using DG.Tweening;
using HarmonyLib;

namespace WorldBoxMP
{
    public static class Replacements
    {
        static readonly AccessTools.FieldRef<Army, Actor> ArmyCaptain = AccessTools.FieldRefAccess<Army, Actor>("_captain");
        static readonly AccessTools.FieldRef<Family, Actor> FamilyAlpha = AccessTools.FieldRefAccess<Family, Actor>("_cached_alpha");
        static readonly AccessTools.FieldRef<Family, Actor> FamilyFounder1 = AccessTools.FieldRefAccess<Family, Actor>("_founder_1");
        static readonly AccessTools.FieldRef<Family, Actor> FamilyFounder2 = AccessTools.FieldRefAccess<Family, Actor>("_founder_2");
        static readonly AccessTools.FieldRef<List<Tweener>> WindowAnimations =
            AccessTools.StaticFieldRefAccess<List<Tweener>>(AccessTools.Field(typeof(ScrollWindow), "_animations_list"));

        public static int Fixed, StuckAnimations;
        public static bool Disabled;

        public static int WindowAnimationCount => WindowAnimations()?.Count ?? 0;

        public static void Actors(Dictionary<Actor, Actor> map)
        {
            if (map.Count == 0 || Disabled) return;
            MapBox w = World.world;
            Actor Swap(Actor a) => a != null && map.TryGetValue(a, out Actor n) ? n : a;

            Actor main = SelectedUnit.unit;
            var selected = new List<Actor>(SelectedUnit.getAllSelected());
            foreach (Actor a in selected)
            {
                if (!map.TryGetValue(a, out Actor n)) continue;
                SelectedUnit.unselect(a);
                SelectedUnit.select(n, a == main);
                Fixed++;
            }
            if (SelectedObjects.getSelectedNanoObject() is Actor sel && map.TryGetValue(sel, out Actor selNew))
            {
                SelectedObjects.setNanoObject(selNew);
                Fixed++;
            }
            Actor focus = MoveCamera.getFocusUnit();
            if (focus != null && map.TryGetValue(focus, out Actor focusNew))
            {
                MoveCamera.setFocusUnit(focusNew);
                Fixed++;
            }

            foreach (Kingdom k in w.kingdoms)
                if (k.king != null && map.TryGetValue(k.king, out Actor n)) { k.king = n; Fixed++; }
            foreach (City c in w.cities)
                if (c.leader != null && map.TryGetValue(c.leader, out Actor n)) { c.leader = n; Fixed++; }
            foreach (Army a in w.armies)
            {
                Actor cap = ArmyCaptain(a);
                if (cap != null && map.TryGetValue(cap, out Actor n)) { ArmyCaptain(a) = n; Fixed++; }
            }
            foreach (Family f in w.families)
            {
                FamilyAlpha(f) = Swap(FamilyAlpha(f));
                FamilyFounder1(f) = Swap(FamilyFounder1(f));
                FamilyFounder2(f) = Swap(FamilyFounder2(f));
            }
            foreach (Actor u in w.units)
                if (u.lover != null && map.TryGetValue(u.lover, out Actor n)) { u.lover = n; Fixed++; }
        }

        public static void DropStuckWindowAnimations()
        {
            List<Tweener> list = WindowAnimations();
            if (Disabled || list == null || list.Count == 0) return;
            int removed = list.RemoveAll(t => t == null || !t.IsActive());
            if (removed > 0)
            {
                StuckAnimations += removed;
                SessionLog.Write("window: dropped " + removed + " stuck window animation(s)");
            }
        }
    }
}
