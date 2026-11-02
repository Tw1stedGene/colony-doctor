using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Plain-language text for growing diagnoses (spec: Grower reason). UI only.</summary>
    public static class GrowExplanations
    {
        public static string For(TrackedGrower g, in Core.Diagnosis d)
        {
            string plant = g.Plant?.label ?? "?";
            switch (d.Reason)
            {
                case Reason.Working: return "ColonyDoctor_Grow_Working".Translate();
                case Reason.BenchProblem: return "ColonyDoctor_Grow_Unpowered".Translate(plant.Named("PLANT"));
                case Reason.NoEligibleWorker:
                    switch (d.Detail)
                    {
                        case Detail.SkillTooLow:
                            return "ColonyDoctor_Grow_Skill".Translate(plant.Named("PLANT"), d.PieceTotal.Named("NEED"),
                                ((d.SubjectA as Pawn)?.LabelShort ?? "?").Named("PAWN"), d.PieceCount.Named("HAVE"));
                        case Detail.AreaRestriction: return AreaText.For(in d);
                        case Detail.Unreachable: return "ColonyDoctor_Grow_Unreachable".Translate();
                        case Detail.NoColonists: return "ColonyDoctor_Haul_NoColonists".Translate();
                        case Detail.Incapable: return "ColonyDoctor_Grow_Incapable".Translate();
                        case Detail.WorkTypeDisabled: return "ColonyDoctor_Grow_Disabled".Translate();
                        default: return "ColonyDoctor_Grow_Unavailable".Translate();
                    }
                case Reason.Outcompeted: return OutcompetedText.For(g, in d);
                case Reason.WaitingForWorker:
                    return (d.Detail == Detail.OffDuty ? "ColonyDoctor_Why_OffDuty" : "ColonyDoctor_Why_SameOrLowerRank").Translate();
            }
            return "";
        }
    }
}
