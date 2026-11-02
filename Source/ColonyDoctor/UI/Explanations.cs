using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>
    /// Turns a stored <see cref="Core.Diagnosis"/> into plain-language text (spec: Reason categories, explanations name
    /// concrete pawns, items, distances and settings). Runs only when the UI draws, never during sampling.
    /// </summary>
    public static class Explanations
    {
        public static string For(in Core.Diagnosis d, Bill bill, Thing bench)
        {
            string benchLabel = bench.LabelShort;
            switch (d.Detail)
            {
                case Detail.Unpowered: return "ColonyDoctor_Why_Unpowered".Translate(benchLabel.Named("BENCH"));
                case Detail.Unfueled: return "ColonyDoctor_Why_Unfueled".Translate(benchLabel.Named("BENCH"));
                case Detail.BrokenDown: return "ColonyDoctor_Why_BrokenDown".Translate(benchLabel.Named("BENCH"));
                case Detail.InteractionBlocked:
                    return d.SubjectA is Thing blocker
                        ? "ColonyDoctor_Why_InteractionBlockedBy".Translate(benchLabel.Named("BENCH"), blocker.LabelShort.Named("THING"))
                        : "ColonyDoctor_Why_InteractionBlocked".Translate(benchLabel.Named("BENCH"));
                case Detail.BenchUnreachable: return "ColonyDoctor_Why_BenchUnreachable".Translate(benchLabel.Named("BENCH"));

                case Detail.NoColonists: return "ColonyDoctor_Why_NoColonists".Translate();
                case Detail.Incapable: return "ColonyDoctor_Why_Incapable".Translate(WorkLabel(d.SubjectB).Named("WORK"));
                case Detail.WorkTypeDisabled: return "ColonyDoctor_Why_WorkTypeDisabled".Translate(WorkLabel(d.SubjectB).Named("WORK"));
                case Detail.PawnRestriction: return "ColonyDoctor_Why_PawnRestriction".Translate(PawnLabel(d.SubjectA).Named("PAWN"));
                case Detail.SkillRange:
                    string skill = (d.SubjectB as SkillDef)?.LabelCap ?? "";
                    return d.ValueC >= 20
                        ? "ColonyDoctor_Why_SkillMin".Translate(PawnLabel(d.SubjectA).Named("PAWN"), skill.Named("SKILL"), d.ValueA.Named("LEVEL"), d.ValueB.Named("MIN"))
                        : "ColonyDoctor_Why_SkillRange".Translate(PawnLabel(d.SubjectA).Named("PAWN"), skill.Named("SKILL"), d.ValueA.Named("LEVEL"), d.ValueB.Named("MIN"), d.ValueC.Named("MAX"));
                case Detail.AreaRestriction:
                    return "ColonyDoctor_Why_AreaRestriction".Translate(PawnLabel(d.SubjectA).Named("PAWN"),
                        ((d.SubjectB as Area)?.Label ?? "?").Named("AREA"), benchLabel.Named("BENCH"));
                case Detail.WorkersUnavailable: return "ColonyDoctor_Why_WorkersUnavailable".Translate(PawnLabel(d.SubjectA).Named("PAWN"));

                case Detail.NoneOnMap: return "ColonyDoctor_Why_NoneOnMap".Translate(Ingredient(d).Named("ING"));
                case Detail.ExcludedByBillFilter: return "ColonyDoctor_Why_ExcludedByBillFilter".Translate(Ingredient(d).Named("ING"));
                case Detail.Forbidden: return "ColonyDoctor_Why_Forbidden".Translate(Ingredient(d).Named("ING"));
                case Detail.OutsideRadius:
                    return "ColonyDoctor_Why_OutsideRadius".Translate(d.ValueC.Named("COUNT"), Ingredient(d).Named("ING"),
                        d.ValueA.Named("DIST"), d.ValueB.Named("RADIUS"));
                case Detail.Reserved:
                    return d.SubjectB is Pawn reserver
                        ? "ColonyDoctor_Why_ReservedBy".Translate(Ingredient(d).Named("ING"), reserver.LabelShort.Named("PAWN"))
                        : "ColonyDoctor_Why_Reserved".Translate(Ingredient(d).Named("ING"));
                case Detail.Unreachable: return "ColonyDoctor_Why_Unreachable".Translate(Ingredient(d).Named("ING"));

                case Detail.NoStockpileAccepts:
                    return "ColonyDoctor_Why_NoStockpileAccepts".Translate(((d.SubjectA as ThingDef)?.label ?? "?").Named("PRODUCT"));
                case Detail.SpecificStockpileRejects:
                    return "ColonyDoctor_Why_SpecificStockpileRejects".Translate(((d.SubjectA as ThingDef)?.label ?? "?").Named("PRODUCT"),
                        ((d.SubjectB as ISlotGroup)?.GroupingLabel ?? "?").Named("STOCKPILE"));

                case Detail.BenchBusy:
                    return "ColonyDoctor_Why_BenchBusy".Translate(PawnLabel(d.SubjectA).Named("PAWN"),
                        ((d.SubjectB as Bill)?.LabelCap ?? "?").Named("BILL"));

                case Detail.OffDuty: return "ColonyDoctor_Why_OffDuty".Translate();
                case Detail.SameOrLowerRank: return "ColonyDoctor_Why_SameOrLowerRank".Translate();

                case Detail.FailReasonText: return d.Text ?? "";
                case Detail.Generic: return "ColonyDoctor_Why_Generic".Translate();
            }

            switch (d.Reason)
            {
                case Reason.Working: return "ColonyDoctor_Why_Working".Translate();
                case Reason.WaitingForWorker: return "ColonyDoctor_Why_Waiting".Translate();
                default: return "";
            }
        }

        private static string PawnLabel(object o) => (o as Pawn)?.LabelShort ?? "?";

        private static string WorkLabel(object o) => (o as WorkTypeDef)?.labelShort ?? "?";

        private static string Ingredient(in Core.Diagnosis d)
            => (d.SubjectA as IngredientCount)?.filter?.Summary ?? "ColonyDoctor_Ingredients".Translate().Resolve();
    }
}
