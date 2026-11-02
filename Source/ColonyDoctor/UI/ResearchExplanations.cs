using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Plain-language text for research diagnoses (spec: Research reason). UI only.</summary>
    public static class ResearchExplanations
    {
        public static string For(TrackedResearch r, in Core.Diagnosis d)
        {
            string project = r.Project.LabelCap;
            switch (d.Reason)
            {
                case Reason.Working: return "ColonyDoctor_Research_Working".Translate();
                case Reason.BenchProblem:
                    switch (d.Detail)
                    {
                        case Detail.NoResearchBench:
                            return d.SubjectB is ThingDef need
                                ? "ColonyDoctor_Research_NeedBench".Translate(project.Named("PROJECT"), need.label.Named("BENCH"))
                                : "ColonyDoctor_Research_NoBench".Translate(project.Named("PROJECT"));
                        case Detail.FacilityMissing:
                            return "ColonyDoctor_Research_Facility".Translate(project.Named("PROJECT"),
                                ((d.SubjectB as ThingDef)?.label ?? "?").Named("FACILITY"), ((d.SubjectA as Thing)?.LabelShort ?? "?").Named("BENCH"));
                        default:
                            return "ColonyDoctor_Research_Unpowered".Translate(((d.SubjectA as Thing)?.LabelShort ?? "?").Named("BENCH"));
                    }
                case Reason.Blocked:
                    return "ColonyDoctor_Research_Forbidden".Translate(((d.SubjectA as Thing)?.LabelShort ?? "?").Named("BENCH"));
                case Reason.NoEligibleWorker:
                    switch (d.Detail)
                    {
                        case Detail.AreaRestriction: return AreaText.For(in d);
                        case Detail.Unreachable: return "ColonyDoctor_Research_Unreachable".Translate();
                        case Detail.NoColonists: return "ColonyDoctor_Haul_NoColonists".Translate();
                        case Detail.Incapable: return "ColonyDoctor_Order_Incapable".Translate(Work(d).Named("WORK"));
                        case Detail.WorkTypeDisabled: return "ColonyDoctor_Order_Disabled".Translate(Work(d).Named("WORK"));
                        default: return "ColonyDoctor_Order_Unavailable".Translate(Work(d).Named("WORK"));
                    }
                case Reason.Outcompeted: return OutcompetedText.For(r, in d);
                case Reason.WaitingForWorker:
                    return (d.Detail == Detail.OffDuty ? "ColonyDoctor_Why_OffDuty" : "ColonyDoctor_Why_SameOrLowerRank").Translate();
            }
            return "";
        }

        private static string Work(in Core.Diagnosis d) => (d.SubjectC as WorkTypeDef)?.labelShort ?? "research";
    }
}
