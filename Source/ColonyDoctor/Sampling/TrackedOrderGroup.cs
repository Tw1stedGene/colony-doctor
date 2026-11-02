using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>Designations of one order type, e.g. all mining orders (spec: Order groups).</summary>
    public sealed class TrackedOrderGroup : TrackedProblem
    {
        public readonly DesignationDef Kind0;
        public readonly WorkGiverDef Giver;
        private readonly Map map;
        internal readonly List<Designation> Items = new List<Designation>();
        internal int SeenInDiscovery;
        internal int EmptySinceTick = -1;

        // Filled by the diagnoser at each sample (UI reads them).
        internal int WaitingCount;
        internal int OldestWaitTicks;
        internal Designation Oldest;

        public TrackedOrderGroup(DesignationDef kind, WorkGiverDef giver, Map map, int phase) : base(phase)
        {
            Kind0 = kind;
            Giver = giver;
            this.map = map;
        }

        public override ProblemKind Kind => ProblemKind.Orders;

        public override bool IsGone => Items.Count == 0 && EmptySinceTick >= 0
                                       && Find.TickManager.TicksGame - EmptySinceTick > Core.HaulReason.KeepEmptyTicks;

        public override string Title
        {
            get
            {
                string key = "ColonyDoctor_Order_" + Kind0.defName;
                string label = key.CanTranslate() ? key.Translate().Resolve() : Kind0.defName;
                return Items.Count > 0 ? label + " ×" + Items.Count : label;
            }
        }

        public override string Location => Items.Count == 0 ? "ColonyDoctor_OrderNone".Translate().Resolve()
            : WaitingCount == 0 ? "ColonyDoctor_OrderFresh".Translate().Resolve()
            : "ColonyDoctor_HaulOldest".Translate(OldestWaitTicks.ToStringTicksToPeriod(allowSeconds: false, shortForm: true).Named("AGE")).Resolve();

        public override string Extra => Items.Count == 0 ? null
            : "ColonyDoctor_OrderCounts".Translate(Items.Count.Named("COUNT"), WaitingCount.Named("WAITING")).Resolve();

        public override void JumpTo()
        {
            Designation d = Oldest ?? (Items.Count > 0 ? Items[0] : null);
            if (d == null) return;
            if (d.target.HasThing && d.target.Thing.Spawned)
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(d.target.Thing), CameraJumper.MovementMode.Pan);
                return;
            }
            CameraJumper.TryJump(d.target.Cell, map, CameraJumper.MovementMode.Pan);
        }

        public override string Explain(in Core.Diagnosis d) => UI.OrderExplanations.For(this, in d);

        internal static IntVec3 CellOf(Designation d) => d.target.HasThing ? d.target.Thing.Position : d.target.Cell;
    }
}
