using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Plain-language text for hauling diagnoses (spec: Group reason). UI only.</summary>
    public static class HaulExplanations
    {
        public static string For(TrackedHaulGroup g, in Core.Diagnosis d)
        {
            if (d.Reason == Reason.Working) return "ColonyDoctor_Haul_Working".Translate();
            string body = Body(g, in d);
            if (d.PieceTotal > 0 && d.PieceCount < d.PieceTotal)
                body += " " + "ColonyDoctor_Haul_StacksOf".Translate(d.PieceCount.Named("COUNT"), d.PieceTotal.Named("TOTAL"));
            return body;
        }

        private static string Body(TrackedHaulGroup g, in Core.Diagnosis d)
        {
            string thing = g.Def.label;
            switch (d.Reason)
            {
                case Reason.NoStorage:
                    return (d.Detail == Detail.StorageFull ? "ColonyDoctor_Haul_StorageFull" : "ColonyDoctor_Haul_NoStorage")
                        .Translate(thing.Named("THING"));
                case Reason.NoEligibleWorker:
                    switch (d.Detail)
                    {
                        case Detail.Unreachable: return "ColonyDoctor_Haul_Unreachable".Translate(thing.Named("THING"));
                        case Detail.NoColonists: return "ColonyDoctor_Haul_NoColonists".Translate();
                        case Detail.Incapable: return "ColonyDoctor_Haul_Incapable".Translate();
                        case Detail.WorkTypeDisabled: return "ColonyDoctor_Haul_Disabled".Translate();
                        default: return "ColonyDoctor_Haul_Unavailable".Translate();
                    }
                case Reason.Outcompeted:
                    return OutcompetedText.For(g, in d);
                case Reason.WaitingForWorker:
                    if (d.Detail == Detail.OffDuty) return "ColonyDoctor_Why_OffDuty".Translate();
                    return "ColonyDoctor_Why_SameOrLowerRank".Translate();
            }
            return "";
        }
    }
}
