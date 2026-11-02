using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>
    /// Spec: Outcompeted explanation. Names the worker who competed most often, the top competing work types with
    /// their shares over the current stall, and the stalled work type. UI only (allocates strings).
    /// </summary>
    public static class OutcompetedText
    {
        private static readonly int[] Top = new int[3];

        public static string For(TrackedProblem problem, in Core.Diagnosis d)
        {
            string stalled = (d.SubjectC as WorkTypeDef)?.labelShort ?? "?";
            CompetingTally tally = problem.Competing;
            int n = tally.Total > 0 ? tally.TopWorkTypes(Top) : 0;
            if (n == 0)
            {
                // Tally reset (the problem has worked since): describe the stored sample.
                return "ColonyDoctor_Why_OutcompetedOnce".Translate(Pawn(d.SubjectA).Named("PAWN"),
                    ((d.SubjectB as WorkTypeDef)?.labelShort ?? "?").Named("WORK"), stalled.Named("STALLED"));
            }
            var parts = new System.Collections.Generic.List<string>(n);
            for (int i = 0; i < n; i++)
            {
                WorkTypeDef wt = DefDatabase<WorkTypeDef>.AllDefsListForReading.Find(w => w.index == Top[i]);
                int pct = (int)System.Math.Round(100f * tally.CountFor(Top[i]) / tally.Total);
                parts.Add("ColonyDoctor_ShareOn".Translate(pct.Named("PCT"), (wt?.labelShort ?? "?").Named("WORK")));
            }
            return "ColonyDoctor_Why_Outcompeted".Translate(Pawn(tally.TopCompetitor ?? d.SubjectA).Named("PAWN"),
                string.Join(", ", parts).Named("SHARES"), stalled.Named("STALLED"));
        }

        private static string Pawn(object o) => (o as Verse.Pawn)?.LabelShort ?? "?";
    }
}
