using System;
using System.Collections.Generic;
using System.Linq;
using ColonyDoctor.Core;
using ColonyDoctor.Construction;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;

namespace ColonyDoctor.Fixes
{
    /// <summary>
    /// Which fix (if any) a stored diagnosis offers (spec: Fix offering and the per-fix requirements). Preconditions
    /// are checked against live game state, so a stale diagnosis never yields a fix that would change nothing.
    /// UI only: allocates.
    /// </summary>
    public static class FixCatalog
    {
        private static bool? workTab;

        public static bool WorkTabLoaded => workTab ?? (workTab = ModsConfig.ActiveModsInLoadOrder
            .Any(m => m.PackageIdPlayerFacing != null && m.PackageIdPlayerFacing.IndexOf("worktab", StringComparison.OrdinalIgnoreCase) >= 0)).Value;

        public static Fix For(TrackedProblem problem, in Core.Diagnosis d)
        {
            try
            {
                if (problem is TrackedBill tb) return ForBill(tb, d);
                if (problem is ConstructionSite site) return ForSite(site, d);
                if (problem is TrackedHaulGroup haul) return ForHaul(haul, d);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Building a fix failed: {e}", 0x0C0D0F01);
            }
            return null;
        }

        /// <summary>Fixes for the output side note (drop on floor / allow in stockpile).</summary>
        public static List<Fix> ForOutput(TrackedBill tb)
        {
            var list = new List<Fix>();
            if (!tb.HasOutputNote || !(tb.Bill is Bill_Production bill) || !(tb.OutputNote.SubjectA is ThingDef product)) return list;
            try
            {
                if (tb.OutputNote.Detail == Detail.SpecificStockpileRejects)
                {
                    FixActions.TargetOf(bill.GetSlotGroup(), out var zone, out var building);
                    if (zone != null || building != null) list.Add(AllowIn(() => !bill.DeletedOrDereferenced, product, zone, building));
                }
                else if (tb.OutputNote.Detail == Detail.NoStockpileAccepts)
                {
                    list.Add(DropOnFloor(bill));
                    SlotGroup best = tb.Bench.Map.haulDestinationManager.AllGroupsListForReading
                        .Where(g => g.parent is Zone_Stockpile || g.parent is Building_Storage)
                        .OrderByDescending(g => g.Settings.Priority).FirstOrDefault();
                    if (best != null) list.Add(AllowIn(() => !bill.DeletedOrDereferenced, product, best.parent as Zone_Stockpile, best.parent as Building_Storage));
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Building an output fix failed: {e}", 0x0C0D0F02);
            }
            return list;
        }

        // ---- Bills ---------------------------------------------------------------------------------------------------

        private static Fix ForBill(TrackedBill tb, Core.Diagnosis d)
        {
            Bill bill = tb.Bill;
            switch (d.Reason)
            {
                case Reason.MaterialsMissing when d.Detail == Detail.OutsideRadius:
                    return Radius(tb, d);
                case Reason.MaterialsMissing when d.Detail == Detail.Forbidden:
                    return UnforbidForBill(tb, d);
                case Reason.NoEligibleWorker when d.Detail == Detail.SkillRange:
                    return SkillRange(tb, d);
                case Reason.NoEligibleWorker when d.Detail == Detail.PawnRestriction:
                    if (FixActions.RestrictionOf(bill, out _) == FixActions.RestrictAny) return null;
                    return Restriction(tb);
                case Reason.Outcompeted:
                    return Priority(tb, d);
            }
            return null;
        }

        private static bool StillDetail(TrackedProblem p, Reason r, Detail detail)
            => p.History.LastReason == r && p.History.LastFor(r).Detail == detail;

        private static Fix Radius(TrackedBill tb, Core.Diagnosis d)
        {
            Bill bill = tb.Bill;
            int target = FixMath.RadiusFor(d.ValueA);
            if (target <= bill.ingredientSearchRadius) return null;
            string from = RadiusLabel(bill.ingredientSearchRadius);
            return new Fix
            {
                Button = "ColonyDoctor_Fix_Radius".Translate(),
                Change = "ColonyDoctor_FixChange_Radius".Translate(from.Named("FROM"), target.Named("TO")),
                SideEffect = "ColonyDoctor_FixSide_Radius".Translate(),
                StillNeeded = () => !tb.IsGone && bill.ingredientSearchRadius < target && StillDetail(tb, Reason.MaterialsMissing, Detail.OutsideRadius),
                Apply = () =>
                {
                    float old = bill.ingredientSearchRadius;
                    FixActions.SetRadius(bill, target);
                    return () => FixActions.SetRadius(bill, old);
                },
            };
        }

        private static string RadiusLabel(float r) => r >= FixMath.MaxRadius ? "∞" : ((int)r).ToString();

        private static Fix SkillRange(TrackedBill tb, Core.Diagnosis d)
        {
            Bill bill = tb.Bill;
            if (!(d.SubjectA is Pawn pawn) || pawn.Destroyed) return null;
            SkillDef skill = bill.recipe.workSkill;
            if (skill == null || bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn) != null) return null; // recipe minimum, not the bill
            int level = pawn.IsColonyMech ? pawn.RaceProps.mechFixedSkillLevel : pawn.skills?.GetSkill(skill)?.Level ?? 0;
            int min = bill.allowedSkillRange.min, max = bill.allowedSkillRange.max;
            if (level >= min && level <= max) return null;
            FixMath.WidenSkillRange(ref min, ref max, level);
            return new Fix
            {
                Button = "ColonyDoctor_Fix_SkillRange".Translate(),
                Change = "ColonyDoctor_FixChange_SkillRange".Translate(bill.allowedSkillRange.min.Named("FROMMIN"), bill.allowedSkillRange.max.Named("FROMMAX"),
                    min.Named("TOMIN"), max.Named("TOMAX"), pawn.LabelShort.Named("PAWN"), level.Named("LEVEL")),
                SideEffect = "ColonyDoctor_FixSide_SkillRange".Translate(),
                StillNeeded = () => !tb.IsGone && (level < bill.allowedSkillRange.min || level > bill.allowedSkillRange.max) && StillDetail(tb, Reason.NoEligibleWorker, Detail.SkillRange),
                Apply = () =>
                {
                    IntRange old = bill.allowedSkillRange;
                    FixActions.SetSkillRange(bill, min, max);
                    return () => FixActions.SetSkillRange(bill, old.min, old.max);
                },
            };
        }

        private static Fix Restriction(TrackedBill tb)
        {
            Bill bill = tb.Bill;
            return new Fix
            {
                Button = "ColonyDoctor_Fix_Restriction".Translate(),
                Change = "ColonyDoctor_FixChange_Restriction".Translate(RestrictionLabel(bill).Named("FROM")),
                SideEffect = "ColonyDoctor_FixSide_Restriction".Translate(),
                StillNeeded = () => !tb.IsGone && FixActions.RestrictionOf(bill, out _) != FixActions.RestrictAny
                                    && StillDetail(tb, Reason.NoEligibleWorker, Detail.PawnRestriction),
                Apply = () =>
                {
                    int mode = FixActions.RestrictionOf(bill, out Pawn who);
                    FixActions.SetRestriction(bill, FixActions.RestrictAny, null);
                    return () => FixActions.SetRestriction(bill, mode, who);
                },
            };
        }

        private static string RestrictionLabel(Bill bill)
        {
            switch (FixActions.RestrictionOf(bill, out Pawn p))
            {
                case FixActions.RestrictPawn: return p.LabelShort;
                case FixActions.RestrictSlaves: return "ColonyDoctor_AnySlave".Translate();
                case FixActions.RestrictMechs: return "ColonyDoctor_AnyMech".Translate();
                case FixActions.RestrictNonMechs: return "ColonyDoctor_AnyNonMech".Translate();
                default: return "ColonyDoctor_AnyWorker".Translate();
            }
        }

        private static Fix DropOnFloor(Bill_Production bill)
            => new Fix
            {
                Button = "ColonyDoctor_Fix_DropOnFloor".Translate(),
                Change = "ColonyDoctor_FixChange_DropOnFloor".Translate(bill.GetStoreMode().LabelCap.Named("FROM")),
                SideEffect = "ColonyDoctor_FixSide_DropOnFloor".Translate(),
                StillNeeded = () => !bill.DeletedOrDereferenced && bill.GetStoreMode() != BillStoreModeDefOf.DropOnFloor,
                Apply = () =>
                {
                    BillStoreModeDef mode = bill.GetStoreMode();
                    FixActions.TargetOf(bill.GetSlotGroup(), out var zone, out var building);
                    FixActions.SetStoreMode(bill, BillStoreModeDefOf.DropOnFloor, null, null);
                    return () => FixActions.SetStoreMode(bill, mode, zone, building);
                },
            };

        private static Fix AllowIn(Func<bool> alive, ThingDef product, Zone_Stockpile zone, Building_Storage building)
        {
            StorageSettings settings = zone?.GetStoreSettings() ?? building?.storageGroup?.GetStoreSettings() ?? building?.GetStoreSettings();
            if (settings == null || settings.filter.Allows(product)) return null;
            string where = zone?.label ?? building?.LabelCap ?? "?";
            return new Fix
            {
                Button = "ColonyDoctor_Fix_AllowIn".Translate(where.Named("WHERE")),
                Change = "ColonyDoctor_FixChange_AllowIn".Translate(product.label.Named("PRODUCT"), where.Named("WHERE")),
                SideEffect = "ColonyDoctor_FixSide_AllowIn".Translate(product.label.Named("PRODUCT"), where.Named("WHERE")),
                StillNeeded = () => alive() && !settings.filter.Allows(product),
                Apply = () =>
                {
                    FixActions.SetFilterAllow(zone, building, product, true);
                    return () => FixActions.SetFilterAllow(zone, building, product, false);
                },
            };
        }

        private static Fix UnforbidForBill(TrackedBill tb, Core.Diagnosis d)
        {
            if (!(d.SubjectA is IngredientCount ic)) return null;
            Bill bill = tb.Bill;
            Map map = tb.Bench.Map;
            IntVec3 root = tb.Bench.Position;
            var valueGetter = bill.recipe.IngredientValueGetter;
            List<Thing> stacks = map.listerThings.ThingsMatching(ic.filter.BestThingRequest)
                .Where(t => t.Spawned && t.IsForbidden(Faction.OfPlayer) && ic.filter.Allows(t) && bill.ingredientFilter.Allows(t)
                            && bill.recipe.fixedIngredientFilter.Allows(t) && !t.Position.Fogged(map))
                .OrderBy(t => t.Position.DistanceToSquared(root)).ToList();
            return Unforbid(tb, stacks, ic.GetBaseCount(), t => valueGetter.ValuePerUnitOf(t.def) * t.stackCount,
                ic.filter.Summary, Reason.MaterialsMissing, Detail.Forbidden);
        }

        // ---- Construction -----------------------------------------------------------------------------------------

        private static Fix ForSite(ConstructionSite site, Core.Diagnosis d)
        {
            if (d.Reason == Reason.MaterialsMissing && d.Detail == Detail.Forbidden && d.SubjectA is ThingDef mat && site.Pieces.Count > 0)
            {
                Map map = site.Pieces[0].Map;
                if (map == null) return null;
                IntVec3 root = site.Pieces[0].Position;
                List<Thing> stacks = map.listerThings.ThingsOfDef(mat)
                    .Where(t => t.Spawned && t.IsForbidden(Faction.OfPlayer) && !t.Position.Fogged(map))
                    .OrderBy(t => t.Position.DistanceToSquared(root)).ToList();
                return Unforbid(site, stacks, d.ValueA > 0 ? d.ValueA : 1, t => t.stackCount, mat.label,
                    Reason.MaterialsMissing, Detail.Forbidden);
            }
            if (d.Reason == Reason.Outcompeted) return Priority(site, d);
            return null;
        }

        // ---- Hauling (spec: Group reason) ----------------------------------------------------------------------------

        private static Fix ForHaul(TrackedHaulGroup g, Core.Diagnosis d)
        {
            if (d.Reason == Reason.Outcompeted) return Priority(g, d);
            if (d.Reason != Reason.NoStorage || d.Detail != Detail.NoStockpileAccepts || g.Items.Count == 0) return null;
            Map map = g.Items[0].Map;
            if (map == null) return null;
            ThingDef def = g.Def;
            SlotGroup best = map.haulDestinationManager.AllGroupsListForReading
                .Where(sg => sg.parent is Zone_Stockpile || sg.parent is Building_Storage)
                .Where(sg => sg.parent is Zone_Stockpile || ((Building_Storage)sg.parent).def.building?.fixedStorageSettings?.filter?.Allows(def) != false)
                .OrderByDescending(sg => sg.Settings.Priority).ThenByDescending(sg => sg.parent is Zone_Stockpile)
                .FirstOrDefault();
            if (best == null) return null;
            return AllowIn(() => !g.IsGone && g.Items.Count > 0, def, best.parent as Zone_Stockpile, best.parent as Building_Storage);
        }

        // ---- Shared ---------------------------------------------------------------------------------------------

        private static Fix Unforbid(TrackedProblem p, List<Thing> stacks, float need, Func<Thing, float> value, string what,
            Reason r, Detail detail)
        {
            if (stacks.Count == 0) return null;
            var values = new float[stacks.Count];
            for (int i = 0; i < stacks.Count; i++) values[i] = value(stacks[i]);
            int take = FixMath.StacksToCover(values, stacks.Count, need);
            List<Thing> chosen = stacks.GetRange(0, take);
            int total = chosen.Sum(t => t.stackCount);
            return new Fix
            {
                Button = "ColonyDoctor_Fix_Unforbid".Translate(),
                Change = "ColonyDoctor_FixChange_Unforbid".Translate(chosen.Count.Named("STACKS"), total.Named("COUNT"), what.Named("WHAT"),
                    chosen[0].Position.ToString().Named("WHERE")),
                SideEffect = "ColonyDoctor_FixSide_Unforbid".Translate(),
                StillNeeded = () => !p.IsGone && chosen.Any(t => t.Spawned && t.IsForbidden(Faction.OfPlayer)) && StillDetail(p, r, detail),
                Apply = () =>
                {
                    List<Thing> changed = chosen.Where(t => t.Spawned && t.IsForbidden(Faction.OfPlayer)).ToList();
                    FixActions.SetForbidden(changed, false);
                    return () => FixActions.SetForbidden(changed, true);
                },
            };
        }

        private static Fix Priority(TrackedProblem p, Core.Diagnosis d)
        {
            if (!Find.PlaySettings.useWorkPriorities || WorkTabLoaded) return null; // spec: Work priority fix
            Pawn pawn = (p.Competing.TopCompetitor ?? d.SubjectA) as Pawn;
            var top = new int[1];
            WorkTypeDef competing = p.Competing.TopWorkTypes(top) > 0
                ? DefDatabase<WorkTypeDef>.AllDefsListForReading.Find(w => w.index == top[0])
                : d.SubjectB as WorkTypeDef;
            WorkTypeDef stalled = d.SubjectC as WorkTypeDef;
            if (pawn?.workSettings == null || pawn.Destroyed || competing == null || stalled == null || pawn.WorkTypeIsDisabled(stalled)) return null;
            int cur = pawn.workSettings.GetPriority(stalled);
            int comp = pawn.workSettings.GetPriority(competing);
            int target = FixMath.PriorityToBeat(cur, comp, stalled.naturalPriority > competing.naturalPriority);
            if (target == 0) return null;
            return new Fix
            {
                Button = "ColonyDoctor_Fix_Priority".Translate(),
                Change = "ColonyDoctor_FixChange_Priority".Translate(pawn.LabelShort.Named("PAWN"), stalled.labelShort.Named("WORK"),
                    cur.Named("FROM"), target.Named("TO"), competing.labelShort.Named("OTHER"), comp.Named("OTHERPRI")),
                SideEffect = "ColonyDoctor_FixSide_Priority".Translate(competing.labelShort.Named("OTHER"), pawn.LabelShort.Named("PAWN")),
                StillNeeded = () => !p.IsGone && !pawn.Destroyed && Find.PlaySettings.useWorkPriorities
                                    && pawn.workSettings.GetPriority(stalled) > target && p.History.LastReason == Reason.Outcompeted,
                Apply = () =>
                {
                    int old = pawn.workSettings.GetPriority(stalled);
                    FixActions.SetPriority(pawn, stalled, target);
                    return () => FixActions.SetPriority(pawn, stalled, old);
                },
            };
        }
    }
}
