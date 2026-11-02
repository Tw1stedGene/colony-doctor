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

    /// <summary>"Hauling stalled (N)" (spec: Hauling alerts).</summary>
    public class Alert_HaulingStalled : Alert_StalledBase
    {
        protected override ProblemKind Kind => ProblemKind.Hauling;
        protected override string LabelKey => "ColonyDoctor_AlertHaulingLabel";
        protected override string ExplanationKey => "ColonyDoctor_AlertHaulingExplanation";
        protected override Thing CulpritOf(TrackedProblem problem) => ((TrackedHaulGroup)problem).Oldest;
    }

    /// <summary>
    /// "Food rotting uncollected (N)" (spec: Rotting flag): hauling groups stalled now with stacks that will rot within
    /// a day. Recomputed at most once per 250 ticks.
    /// </summary>
    public class Alert_RottingUncollected : Alert
    {
        private readonly List<Thing> culprits = new List<Thing>();
        private readonly List<string> labels = new List<string>();
        private int computedAtCycle = -1;
        private Map computedFor;

        public Alert_RottingUncollected()
        {
            defaultPriority = AlertPriority.High;
        }

        public override AlertReport GetReport()
        {
            if (!ColonyDoctorMod.Settings.showStalledBillsAlert) return AlertReport.Inactive;
            Recompute();
            return culprits.Count > 0 ? AlertReport.CulpritsAre(culprits) : AlertReport.Inactive;
        }

        public override string GetLabel() => "ColonyDoctor_AlertRottingLabel".Translate(labels.Count.Named("COUNT"));

        public override TaggedString GetExplanation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ColonyDoctor_AlertRottingExplanation".Translate());
            sb.AppendLine();
            for (int i = 0; i < labels.Count; i++) sb.AppendLine("  - " + labels[i]);
            return sb.ToString().TrimEnd();
        }

        protected override void OnClick()
        {
            MainTabWindow_Bottlenecks.Category = ProblemKind.Hauling;
            MainButtonDef tab = DefDatabase<MainButtonDef>.GetNamedSilentFail("ColonyDoctor_Bottlenecks");
            if (tab != null) Find.MainTabsRoot.SetCurrentTab(tab, true);
        }

        private void Recompute()
        {
            Map map = Find.CurrentMap;
            int cycle = Find.TickManager.TicksGame / GameTime.TicksPerSample;
            if (cycle == computedAtCycle && map == computedFor) return;
            computedAtCycle = cycle;
            computedFor = map;
            culprits.Clear();
            labels.Clear();
            MapTracker tracker = MapTracker.Existing(map);
            if (tracker == null) return;
            IReadOnlyList<TrackedHaulGroup> groups = tracker.HaulGroups;
            for (int i = 0; i < groups.Count; i++)
            {
                TrackedHaulGroup g = groups[i];
                if (g.IsGone || g.Rotting == 0 || !g.History.IsCurrentlyStalled) continue;
                labels.Add(g.Title + " (" + "ColonyDoctor_HaulRotting".Translate(g.Rotting.Named("COUNT")) + ")");
                if (g.Oldest != null) culprits.Add(g.Oldest);
            }
        }
    }

    /// <summary>"Patients untended (N)" (spec: Patients untended alert).</summary>
    public class Alert_PatientsUntended : Alert
    {
        private const int MinStallTicks = 1250;
        private readonly List<Thing> culprits = new List<Thing>();
        private int computedAtCycle = -1;
        private Map computedFor;

        public Alert_PatientsUntended()
        {
            defaultPriority = AlertPriority.High;
        }

        public override AlertReport GetReport()
        {
            if (!ColonyDoctorMod.Settings.showStalledBillsAlert) return AlertReport.Inactive;
            Recompute();
            return culprits.Count > 0 ? AlertReport.CulpritsAre(culprits) : AlertReport.Inactive;
        }

        public override string GetLabel() => "ColonyDoctor_AlertPatientsLabel".Translate(culprits.Count.Named("COUNT"));

        public override TaggedString GetExplanation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ColonyDoctor_AlertPatientsExplanation".Translate());
            sb.AppendLine();
            for (int i = 0; i < culprits.Count; i++) sb.AppendLine("  - " + culprits[i].LabelShortCap);
            return sb.ToString().TrimEnd();
        }

        protected override void OnClick()
        {
            MainTabWindow_Bottlenecks.Category = ProblemKind.Patients;
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
            MapTracker tracker = MapTracker.Existing(map);
            if (tracker == null) return;
            IReadOnlyList<TrackedPatient> list = tracker.Patients;
            for (int i = 0; i < list.Count; i++)
            {
                TrackedPatient t = list[i];
                if (t.IsGone || !t.History.IsCurrentlyStalled || t.History.ContinuousStallTicks(now) < MinStallTicks) continue;
                // No care is the player's own choice (often for prisoners); the row explains it, the alert stays quiet.
                if (t.History.LastReason == Reason.Blocked && t.History.LastFor(Reason.Blocked).Detail == Detail.MedicalCareOff) continue;
                culprits.Add(t.Pawn);
            }
        }
    }
}
