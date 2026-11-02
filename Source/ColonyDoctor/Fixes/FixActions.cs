using System.Collections.Generic;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace ColonyDoctor.Fixes
{
    /// <summary>
    /// Every game-state change Colony Doctor makes, as idempotent setters with arguments the Multiplayer mod can sync
    /// (spec: Multiplayer sync). Under Multiplayer, a call becomes a command that runs on every client in the same
    /// tick; without it, the methods run directly. Nothing else in the mod writes game state.
    /// </summary>
    public static class FixActions
    {
        public const int RestrictAny = 0, RestrictPawn = 1, RestrictSlaves = 2, RestrictMechs = 3, RestrictNonMechs = 4;

        [SyncMethod]
        public static void SetRadius(Bill bill, float radius)
        {
            if (bill != null) bill.ingredientSearchRadius = radius;
        }

        [SyncMethod]
        public static void SetSkillRange(Bill bill, int min, int max)
        {
            if (bill != null) bill.allowedSkillRange = new IntRange(min, max);
        }

        [SyncMethod]
        public static void SetRestriction(Bill bill, int mode, Pawn pawn)
        {
            if (bill == null) return;
            switch (mode)
            {
                case RestrictPawn when pawn != null: bill.SetPawnRestriction(pawn); break;
                case RestrictSlaves: bill.SetAnySlaveRestriction(); break;
                case RestrictMechs: bill.SetAnyMechRestriction(); break;
                case RestrictNonMechs: bill.SetAnyNonMechRestriction(); break;
                default: bill.SetAnyPawnRestriction(); break;
            }
        }

        public static int RestrictionOf(Bill bill, out Pawn pawn)
        {
            pawn = bill.PawnRestriction;
            if (pawn != null) return RestrictPawn;
            if (bill.SlavesOnly) return RestrictSlaves;
            if (bill.MechsOnly) return RestrictMechs;
            if (bill.NonMechsOnly) return RestrictNonMechs;
            return RestrictAny;
        }

        /// <summary>Store mode with an optional target: a stockpile zone or a storage building (one of them, or neither).</summary>
        [SyncMethod]
        public static void SetStoreMode(Bill_Production bill, BillStoreModeDef mode, Zone_Stockpile zone, Building_Storage building)
        {
            if (bill == null || mode == null) return;
            ISlotGroup group = zone?.GetSlotGroup() ?? (ISlotGroup)building?.storageGroup ?? building?.GetSlotGroup();
            bill.SetStoreMode(mode, group);
        }

        /// <summary>Splits a slot group into the sync-able target it belongs to (stockpile zone or storage building).</summary>
        public static void TargetOf(ISlotGroup group, out Zone_Stockpile zone, out Building_Storage building)
        {
            zone = null;
            building = null;
            if (group is SlotGroup sg)
            {
                zone = sg.parent as Zone_Stockpile;
                building = sg.parent as Building_Storage;
            }
            else if (group is StorageGroup g && g.members != null)
            {
                for (int i = 0; i < g.members.Count && building == null; i++) building = g.members[i] as Building_Storage;
            }
        }

        [SyncMethod]
        public static void SetFilterAllow(Zone_Stockpile zone, Building_Storage building, ThingDef def, bool allow)
        {
            StorageSettings settings = zone?.GetStoreSettings() ?? building?.storageGroup?.GetStoreSettings() ?? building?.GetStoreSettings();
            if (settings != null && def != null) settings.filter.SetAllow(def, allow);
        }

        [SyncMethod]
        public static void SetForbidden(List<Thing> things, bool forbidden)
        {
            if (things == null) return;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t != null && t.Spawned) t.SetForbidden(forbidden, warnOnFail: false);
            }
        }

        /// <summary>Sets the pawn's allowed area on its current map (null = unrestricted).</summary>
        [SyncMethod]
        public static void SetArea(Pawn pawn, Area area)
        {
            if (pawn?.playerSettings == null || !pawn.Spawned) return;
            if (area != null && area.Map != pawn.Map) return;
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
        }

        [SyncMethod]
        public static void SetPriority(Pawn pawn, WorkTypeDef workType, int priority)
        {
            if (pawn?.workSettings == null || workType == null || pawn.WorkTypeIsDisabled(workType)) return;
            pawn.workSettings.SetPriority(workType, priority);
        }
    }
}
