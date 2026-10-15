using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;

namespace WorldBoxMP
{
    public static class Census
    {
        static readonly AccessTools.FieldRef<StackEffects, List<BaseEffectController>> Controllers =
            AccessTools.FieldRefAccess<StackEffects, List<BaseEffectController>>("list");
        static readonly AccessTools.FieldRef<DropManager, int> ActiveDrops =
            AccessTools.FieldRefAccess<DropManager, int>("_activeIndex");

        public static int CountEffects()
        {
            int n = 0;
            foreach (BaseEffectController c in Controllers(Access.StackEffects(World.world))) n += c.getActiveIndex();
            return n;
        }

        public static string Line()
        {
            MapBox w = World.world;
            var sb = new StringBuilder("census");
            try
            {
                var effects = new SortedDictionary<string, int>();
                foreach (BaseEffectController c in Controllers(Access.StackEffects(w)))
                {
                    int n = c.getActiveIndex();
                    if (n > 0 && c.asset != null) effects[c.asset.id] = n;
                }
                sb.Append(" effects=").Append(Join(effects));
                sb.Append(" drops=").Append(ActiveDrops(w.drop_manager));
                sb.Append(" projectiles=").Append(w.projectiles.Count);
                sb.Append(" statuses=").Append(w.statuses.Count);
                var specials = new SortedDictionary<string, int>();
                var avatars = new SortedDictionary<string, int>();
                foreach (Actor a in w.units)
                {
                    if (!a.isAlive() || a.asset == null) continue;
                    if (a.asset.skip_save) Add(specials, a.asset.id);
                    else if (a.avatar != null) Add(avatars, a.asset.id);
                }
                sb.Append(" specials=").Append(Join(specials));
                sb.Append(" avatars=").Append(Join(avatars));
                sb.Append(" hit_flashes=").Append(Access.StackEffects(w).actor_effect_hit.Count);
                sb.Append(LiveState.CensusPart());
            }
            catch (System.Exception e)
            {
                sb.Append(" failed=").Append(e.GetType().Name);
            }
            return sb.ToString();
        }

        static void Add(SortedDictionary<string, int> d, string k)
        {
            d.TryGetValue(k, out int n);
            d[k] = n + 1;
        }

        static string Join(SortedDictionary<string, int> d) =>
            d.Count == 0 ? "-" : string.Join(",", d.Select(kv => kv.Key + ":" + kv.Value).ToArray());
    }
}
