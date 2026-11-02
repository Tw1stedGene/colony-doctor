using ColonyDoctor.Core;
using ColonyDoctor.UI;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>A bill under observation, with its fixed-size history.</summary>
    public sealed class TrackedBill : TrackedProblem
    {
        public readonly Bill Bill;
        public readonly Thing Bench;

        /// <summary>True when the bench is not served by a vanilla bill work giver (spec: Fallback for non-vanilla benches).</summary>
        public readonly bool UsesProbe;

        internal int SeenInDiscovery;

        /// <summary>
        /// Side note about where products go (not a stall reason): set when no storage accepts the product, so vanilla
        /// drops it next to the bench. Updated every sample of an active bill.
        /// </summary>
        public Core.Diagnosis OutputNote;
        public bool HasOutputNote;

        /// <summary>Latest probe outcome for probe-path bills; read by the diagnoser, written by the probe scheduler.</summary>
        internal Core.Diagnosis ProbeResult;
        internal bool HasProbeResult;
        internal int LastProbeTick = -1;

        public TrackedBill(Bill bill, Thing bench, int phase, bool usesProbe) : base(phase)
        {
            Bill = bill;
            Bench = bench;
            UsesProbe = usesProbe;
        }

        public override ProblemKind Kind => ProblemKind.Bill;

        public override bool IsGone => Bill.DeletedOrDereferenced || Bench.Destroyed || !Bench.Spawned;

        public override string Title
            => Bill is Bill_Production bp && bp.repeatMode != BillRepeatModeDefOf.Forever
                ? Bill.LabelCap + " (" + bp.RepeatInfoText + ")"
                : (string)Bill.LabelCap;

        public override string Location
        {
            get
            {
                Room room = Bench.GetRoom();
                string roomLabel = room?.Role != null && room.Role != RoomRoleDefOf.None ? room.Role.label : null;
                return roomLabel != null
                    ? "ColonyDoctor_BenchInRoom".Translate(Bench.LabelCap.Named("BENCH"), roomLabel.Named("ROOM"))
                    : (string)Bench.LabelCap;
            }
        }

        public override void JumpTo()
        {
            if (Bench.Spawned) CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(Bench), CameraJumper.MovementMode.Pan);
        }

        public override string Explain(in Core.Diagnosis d)
            => d.Reason == Core.Reason.Outcompeted ? OutcompetedText.For(this, in d) : Explanations.For(in d, Bill, Bench);
    }
}
