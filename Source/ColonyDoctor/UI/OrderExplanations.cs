using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Plain-language text for order diagnoses (spec: Order group reason). UI only.</summary>
    public static class OrderExplanations
    {
        public static string For(TrackedOrderGroup g, in Core.Diagnosis d)
        {
            string work = g.Giver.workType?.labelShort ?? "?";
            switch (d.Reason)
            {
                case Reason.Working: return "ColonyDoctor_Order_Working".Translate();
                case Reason.Blocked:
                    return "ColonyDoctor_Order_Forbidden".Translate(d.ValueA.Named("COUNT"));
                case Reason.NoEligibleWorker:
                    switch (d.Detail)
                    {
                        case Detail.Unreachable: return "ColonyDoctor_Order_Unreachable".Translate();
                        case Detail.NoColonists: return "ColonyDoctor_Haul_NoColonists".Translate();
                        case Detail.Incapable: return "ColonyDoctor_Order_Incapable".Translate(work.Named("WORK"));
                        case Detail.WorkTypeDisabled: return "ColonyDoctor_Order_Disabled".Translate(work.Named("WORK"));
                        default: return "ColonyDoctor_Order_Unavailable".Translate(work.Named("WORK"));
                    }
                case Reason.Outcompeted: return OutcompetedText.For(g, in d);
                case Reason.WaitingForWorker:
                    return (d.Detail == Detail.OffDuty ? "ColonyDoctor_Why_OffDuty" : "ColonyDoctor_Why_SameOrLowerRank").Translate();
            }
            return "";
        }
    }
}
