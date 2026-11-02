using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>A colonist, slave or prisoner who has needed tending (spec: Patients).</summary>
    public sealed class TrackedPatient : TrackedProblem
    {
        public readonly Pawn Pawn;
        private readonly Map map;
        internal int NeedSince = -1;
        internal int FineSince = -1;

        public TrackedPatient(Pawn pawn, Map map, int phase) : base(phase)
        {
            Pawn = pawn;
            this.map = map;
        }

        public override ProblemKind Kind => ProblemKind.Patients;

        public override bool IsGone => Pawn.Dead || Pawn.Destroyed || !Pawn.Spawned || Pawn.Map != map
                                       || (FineSince >= 0 && Find.TickManager.TicksGame - FineSince > Core.HaulReason.KeepEmptyTicks);

        public override string Title => Pawn.LabelShortCap;

        public override string Location
        {
            get
            {
                string who = Pawn.IsPrisonerOfColony ? "ColonyDoctor_PatientPrisoner".Translate().Resolve()
                    : Pawn.IsSlaveOfColony ? "ColonyDoctor_PatientSlave".Translate().Resolve()
                    : "ColonyDoctor_PatientColonist".Translate().Resolve();
                if (Pawn.health?.hediffSet == null || Pawn.health.hediffSet.BleedRateTotal <= 0f) return who;
                int ticks = HealthUtility.TicksUntilDeathDueToBloodLoss(Pawn);
                return who + " · " + "ColonyDoctor_PatientBleeding".Translate(ticks.ToStringTicksToPeriod(allowSeconds: false, shortForm: true).Named("TIME"));
            }
        }

        public bool Bleeding => Pawn.health?.hediffSet != null && Pawn.health.hediffSet.BleedRateTotal > 0f;

        public override void JumpTo()
        {
            if (Pawn.Spawned) CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(Pawn), CameraJumper.MovementMode.Pan);
        }

        public override string Explain(in Core.Diagnosis d) => UI.PatientExplanations.For(this, in d);
    }
}
