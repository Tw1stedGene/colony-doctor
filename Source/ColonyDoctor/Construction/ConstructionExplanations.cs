using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.Construction
{
    /// <summary>
    /// Plain-language text for a construction site's stored diagnosis, prefixed with how many pieces it applies to
    /// (spec: Site reason, "12 of 14 pieces"). UI only.
    /// </summary>
    public static class ConstructionExplanations
    {
        public static string For(in Core.Diagnosis d, Sampling.TrackedProblem site = null)
        {
            string body = d.Reason == Reason.Outcompeted && site != null ? UI.OutcompetedText.For(site, in d) : Body(in d);
            if (body.NullOrEmpty() || d.PieceTotal <= 1) return body;
            return "ColonyDoctor_PiecesPrefix".Translate(d.PieceCount.Named("COUNT"), d.PieceTotal.Named("TOTAL")) + " " + body;
        }

        private static string Body(in Core.Diagnosis d)
        {
            switch (d.Reason)
            {
                case Reason.Working: return "ColonyDoctor_Site_Working".Translate();
                case Reason.WaitingForWorker:
                    if (d.Detail == Detail.OffDuty) return "ColonyDoctor_Why_OffDuty".Translate();
                    if (d.Detail == Detail.SameOrLowerRank) return "ColonyDoctor_Why_SameOrLowerRank".Translate();
                    return "ColonyDoctor_Site_Waiting".Translate();
                case Reason.NoEligibleWorker: return Worker(in d);
                case Reason.Blocked: return Blocked(in d);
                case Reason.MaterialsMissing: return Materials(in d);
                default: return "";
            }
        }

        private static string Worker(in Core.Diagnosis d)
        {
            string thing = (d.SubjectC as BuildableDef)?.label ?? "ColonyDoctor_ThisPiece".Translate().Resolve();
            switch (d.Detail)
            {
                case Detail.Incapable: return "ColonyDoctor_Site_Incapable".Translate();
                case Detail.WorkTypeDisabled: return "ColonyDoctor_Site_WorkTypeDisabled".Translate();
                case Detail.SkillTooLow:
                    return "ColonyDoctor_Site_SkillTooLow".Translate(Pawn(d.SubjectA).Named("PAWN"),
                        ((d.SubjectB as SkillDef)?.LabelCap ?? "").Named("SKILL"), d.ValueA.Named("LEVEL"),
                        thing.Named("THING"), d.ValueB.Named("MIN"));
                case Detail.Unreachable: return "ColonyDoctor_Site_Unreachable".Translate();
                case Detail.WorkersUnavailable: return "ColonyDoctor_Site_WorkersUnavailable".Translate(Pawn(d.SubjectA).Named("PAWN"));
                default: return "";
            }
        }

        private static string Blocked(in Core.Diagnosis d)
        {
            string blocker = (d.SubjectA as Thing)?.LabelShort ?? "?";
            switch (d.Detail)
            {
                case Detail.BlockedPlant: return "ColonyDoctor_Site_BlockedPlant".Translate(blocker.Named("THING"));
                case Detail.BlockedItem: return "ColonyDoctor_Site_BlockedItem".Translate(blocker.Named("THING"));
                case Detail.BlockedBuilding:
                    return (d.ValueA == 1 ? "ColonyDoctor_Site_BlockedBuildingStuck" : "ColonyDoctor_Site_BlockedBuilding").Translate(blocker.Named("THING"));
                case Detail.BlockedRock:
                    return (d.ValueA == 1 ? "ColonyDoctor_Site_BlockedRockNoMiner" : "ColonyDoctor_Site_BlockedRock").Translate(blocker.Named("THING"));
                default: return "";
            }
        }

        private static string Materials(in Core.Diagnosis d)
        {
            string mat = (d.SubjectA as ThingDef)?.label ?? "ColonyDoctor_Ingredients".Translate().Resolve();
            NamedArgument m = mat.Named("MAT"), shortBy = d.ValueA.Named("SHORT"), usable = d.ValueB.Named("USABLE");
            switch (d.Detail)
            {
                case Detail.NoneOnMap: return "ColonyDoctor_Site_NotEnough".Translate(m, shortBy, usable);
                case Detail.Forbidden: return "ColonyDoctor_Site_Forbidden".Translate(m, shortBy);
                case Detail.Unreachable: return "ColonyDoctor_Site_MatUnreachable".Translate(m, shortBy);
                case Detail.Reserved: return "ColonyDoctor_Site_Reserved".Translate(m, shortBy);
                default: return "";
            }
        }

        private static string Pawn(object o) => (o as Pawn)?.LabelShort ?? "?";
    }
}
