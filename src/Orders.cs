using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class Orders
    {
        public static int Sent, Applied;

        public static bool Replaying;

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.getUnitsToBeSelected))]
        static class ClientBoxSelection
        {
            static bool Prefix(PlayerControl __instance, ref ListPool<Actor> __result)
            {
                if (!Net.IsClientConnected) return true;
                Vector2 a = Access.SquareStart(__instance), b = __instance.getMousePos();
                int x0 = Mathf.FloorToInt(a.x), y0 = Mathf.FloorToInt(a.y), x1 = Mathf.FloorToInt(b.x), y1 = Mathf.FloorToInt(b.y);
                __result = null;
                if (x0 == -1 || y0 == -1 || x1 == -1 || y1 == -1) return false;
                int minX = Mathf.Min(x0, x1), maxX = Mathf.Max(x0, x1), minY = Mathf.Min(y0, y1), maxY = Mathf.Max(y0, y1);
                if (maxX - minX < 2 && maxY - minY < 2) return false;
                var list = new ListPool<Actor>();
                foreach (Actor u in World.world.units)
                {
                    if (!u.isAlive() || u.isInsideSomething() || !u.asset.can_be_inspected) continue;
                    int tx = Mathf.FloorToInt(u.current_position.x), ty = Mathf.FloorToInt(u.current_position.y);
                    if (tx >= minX && tx <= maxX && ty >= minY && ty <= maxY) list.Add(u);
                }
                __result = list;
                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerControl), "tryToMoveSelectedUnit")]
        static class ClientSkipsLocalMove
        {
            static bool Prefix(ref bool __result)
            {
                if (!Net.IsClientConnected) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerControl), "tryToMoveSelectedUnits")]
        static class ClientSendsOrder
        {
            static void Postfix(bool __result)
            {
                if (!__result || !Net.IsClientConnected || !SelectedUnit.isSet()) return;
                WorldTile tile = World.world.getMouseTilePosCachedFrame();
                if (tile == null) return;
                bool attack = HotkeyLibrary.isHoldingControlForSelection();
                Actor target = attack ? World.world.getActorNearCursor() : null;
                if (!Rules.ClientMay(Perm.Orders)) { Rules.Refuse(Perm.Orders); return; }
                var units = new List<Actor>(SelectedUnit.getAllSelected());
                units.Remove(SelectedUnit.unit);
                units.Insert(0, SelectedUnit.unit);
                int before = units.Count;
                units.RemoveAll(a => a == null || PlayerNames.OtherControls(a.getID()) || ControllableUnit.isControllingUnit(a));
                if (units.Count < before) Rules.Busy();
                if (units.Count == 0) return;
                Send(tile, attack, target, units);
            }
        }

        public static void Send(WorldTile tile, bool attack, Actor target, List<Actor> units)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(tile.data.tile_id);
            w.Write(attack);
            w.Write(target != null && target.isAlive() ? target.getID() : -1L);
            int count = 0;
            foreach (Actor a in units) if (a != null) count++;
            w.Write(count);
            foreach (Actor a in units) if (a != null) w.Write(a.getID());
            Net.Peer?.Send(Msg.Order, ms.ToArray());
            Sent++;
            SessionLog.Write("order sent: " + count + " unit(s) " + (attack && target != null ? "attack " + target.getID() : "go") + " to " + tile.x + ":" + tile.y);
        }

        public static void HostReceive(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int tileId = r.ReadInt32();
            bool attack = r.ReadBoolean();
            long targetId = r.ReadInt64();
            int n = r.ReadInt32();
            if (!Rules.HostAllows(Perm.Orders)) { Rules.Deny(Perm.Orders, "tip_denied"); return; }
            var units = new List<Actor>();
            int busy = 0;
            for (int i = 0; i < n; i++)
            {
                Actor a = World.world.units.get(r.ReadInt64());
                if (a == null || !a.isAlive()) continue;
                if (ControllableUnit.isControllingUnit(a)) { busy++; continue; }
                units.Add(a);
            }
            if (busy > 0) Rules.Deny(Perm.Orders, "tip_unit_busy");
            WorldTile[] tiles = Access.TilesList(World.world);
            if (tileId < 0 || tileId >= tiles.Length || units.Count == 0) return;
            WorldTile tile = tiles[tileId];
            Actor target = targetId >= 0 ? World.world.units.get(targetId) : null;
            if (target != null && !target.isAlive()) target = null;
            PlayerControl pc = World.world.player_control;
            int moved = 0;
            Replaying = true;
            try
            {
            foreach (Actor a in units)
            {
                bool next = false;
                if (attack && target != null)
                {
                    a.cancelAllBeh();
                    a.addAggro(target);
                    a.startFightingWith(target);
                    if (Toolbox.DistTile(a.current_tile, target.current_tile) > 7f) Access.MoveOne(pc, a, tile, false);
                }
                else next = Access.MoveOne(pc, a, tile, true);
                if (next) { moved++; tile = tile.getNeighbourTileSameIsland(); }
            }
            }
            finally { Replaying = false; }
            Applied++;
            SessionLog.Write("order from a player: " + units.Count + " unit(s) " + (attack && target != null ? "attack " + target.getID() : "go, " + moved + " moving") + " to " + tiles[tileId].x + ":" + tiles[tileId].y);
        }
    }
}
