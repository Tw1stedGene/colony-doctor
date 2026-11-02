using System.Collections.Generic;
using System.Text;
using ColonyDoctor.Construction;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>
    /// Shared logic for "… stalled (N)" alerts: problems of one kind on the current map stalled at least 50% of the
    /// last day and stalled right now. Clicking opens the Bottlenecks tab on that category. Recomputed at most once per
    /// 250 ticks.
    /// </summary>
    public abstract class Alert_StalledBase : Alert
    {
        private const int Threshold = 50;

        private readonly List<Thing> culprits = new List<Thing>();
        private readonly List<string> labels = new List<string>();
        private readonly HistoryAggregate agg = new HistoryAggregate();
        private int computedAtCycle = -1;
        private Map computedFor;

        protected Alert_StalledBase()
        {
            defaultPriority = AlertPriority.Medium;
        }

        protected abstract ProblemKind Kind { get; }
        protected abstract string LabelKey { get; }
        protected abstract string ExplanationKey { get; }
        protected abstract Thing CulpritOf(TrackedProblem problem);

        public override AlertReport GetReport()
        {
            if (!ColonyDoctorMod.Settings.showStalledBillsAlert) return AlertReport.Inactive;
            Recompute();
            return culprits.Count > 0 ? AlertReport.CulpritsAre(culprits) : AlertReport.Inactive;
        }

        public override string GetLabel() => LabelKey.Translate(labels.Count.Named("COUNT"));

        public override TaggedString GetExplanation()
        {
            var sb = new StringBuilder();
            sb.AppendLine(ExplanationKey.Translate());
            sb.AppendLine();
            for (int i = 0; i < labels.Count; i++) sb.AppendLine("  - " + labels[i]);
            return sb.ToString().TrimEnd();
        }

        protected override void OnClick()
        {
            MainTabWindow_Bottlenecks.Category = Kind;
            MainButtonDef tab = DefDatabase<MainButtonDef>.GetNamedSilentFail("ColonyDoctor_Bottlenecks");
            if (tab != null) Find.MainTabsRoot.SetCurrentTab(tab, true);
        }

        private void Recompute()
        {
            Map map = Find.CurrentMap;
            int now = Find.TickManager.TicksGame;
            int cycle = now / GameTime.TicksPerSample;
            if (cycle == computedAtCycle && map == computedFor) return;
            computedAtCycle = cycle;
            computedFor = map;
            culprits.Clear();
            labels.Clear();

            MapTracker tracker = MapTracker.Existing(map);
            if (tracker == null) return;
            IReadOnlyList<TrackedProblem> problems = tracker.Problems;
            for (int i = 0; i < problems.Count; i++)
            {
                TrackedProblem t = problems[i];
                if (t.Kind != Kind || t.IsGone || !t.History.IsCurrentlyStalled) continue;
                t.History.Aggregate(now, TimeRange.Day, agg);
                if (agg.StalledPercent < Threshold) continue;
                labels.Add(t.Title);
                Thing culprit = CulpritOf(t);
                if (culprit != null && !culprits.Contains(culprit)) culprits.Add(culprit);
            }
        }
    }

    /// <summary>"Bills stalled (N)" (spec: Stalled bills alert).</summary>
    public class Alert_BillsStalled : Alert_StalledBase
    {
        protected override ProblemKind Kind => ProblemKind.Bill;
        protected override string LabelKey => "ColonyDoctor_AlertLabel";
        protected override string ExplanationKey => "ColonyDoctor_AlertExplanation";
        protected override Thing CulpritOf(TrackedProblem problem) => ((TrackedBill)problem).Bench;
    }

    /// <summary>"Construction stalled (N)" (spec: Construction stalled alert).</summary>
    public class Alert_ConstructionStalled : Alert_StalledBase
    {
        protected override ProblemKind Kind => ProblemKind.Construction;
        protected override string LabelKey => "ColonyDoctor_AlertConstructionLabel";
        protected override string ExplanationKey => "ColonyDoctor_AlertConstructionExplanation";

        protected override Thing CulpritOf(TrackedProblem problem)
        {
            var site = (ConstructionSite)problem;
            return site.Pieces.Count > 0 ? site.Pieces[0] : null;
        }
    }

    /// <summary>"Colonists idle (N)" (spec: Colonists idle alert).</summary>
    public class Alert_ColonistsIdle : Alert_StalledBase
    {
        protected override ProblemKind Kind => ProblemKind.Idle;
        protected override string LabelKey => "ColonyDoctor_AlertIdleLabel";
        protected override string ExplanationKey => "ColonyDoctor_AlertIdleExplanation";
        protected override Thing CulpritOf(TrackedProblem problem) => ((TrackedPawn)problem).Pawn;
    }
}
