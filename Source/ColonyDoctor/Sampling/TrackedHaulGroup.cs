using System.Collections.Generic;
using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>
    /// Loose items of one def waiting to be hauled (spec: Hauling groups). Discovery replaces <see cref="Items"/> every
    /// cycle; the diagnoser fills the per-sample counts the UI shows.
    /// </summary>
    public sealed class TrackedHaulGroup : TrackedProblem
    {
        private const int MaxSelect = 80;

        public readonly ThingDef Def;
        private readonly Map map;
        internal readonly List<Thing> Items = new List<Thing>();
        internal int SeenInDiscovery;
        internal int EmptySinceTick = -1;

        // Filled by the diagnoser at each sample (UI reads them).
        internal int TotalCount;
        internal int WaitingStacks;
        internal int OldestWaitTicks;
        internal int RottingStacks;
        internal Thing Oldest;

        public TrackedHaulGroup(ThingDef def, Map map, int phase) : base(phase)
        {
            Def = def;
            this.map = map;
        }

        public override ProblemKind Kind => ProblemKind.Hauling;

        /// <summary>Gone only after it has been empty for a day (spec: Hauling groups).</summary>
        public override bool IsGone => Items.Count == 0 && EmptySinceTick >= 0
                                       && Find.TickManager.TicksGame - EmptySinceTick > HaulReason.KeepEmptyTicks;

        public int Rotting => RottingStacks;

        public override string Title => TotalCount > 0 ? Def.LabelCap + " ×" + TotalCount : Def.LabelCap.Resolve();

        public override string Location
        {
            get
            {
                if (Items.Count == 0) return "ColonyDoctor_HaulNone".Translate();
                string age = OldestWaitTicks.ToStringTicksToPeriod(allowSeconds: false, shortForm: true);
                string s = "ColonyDoctor_HaulOldest".Translate(age.Named("AGE"));
                if (RottingStacks > 0) s += " · " + "ColonyDoctor_HaulRotting".Translate(RottingStacks.Named("COUNT"));
                return s;
            }
        }

        public override string Extra => Items.Count == 0 ? null
            : "ColonyDoctor_HaulStacks".Translate(Items.Count.Named("STACKS"), WaitingStacks.Named("WAITING"));

        public override void JumpTo()
        {
            Thing focus = Oldest != null && Oldest.Spawned ? Oldest : Items.Find(t => t.Spawned);
            if (focus == null) return;
            CameraJumper.TryJump(focus.Position, map, CameraJumper.MovementMode.Pan);
            Find.Selector.ClearSelection();
            int n = 0;
            for (int i = 0; i < Items.Count && n < MaxSelect; i++)
            {
                if (!Items[i].Spawned) continue;
                Find.Selector.Select(Items[i], false, true);
                n++;
            }
        }

        public override string Explain(in Core.Diagnosis d) => UI.HaulExplanations.For(this, in d);
    }
}
