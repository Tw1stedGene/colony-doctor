using System.Linq;
using ColonyDoctor.Core;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>A colonist watched for idleness (spec: idle-diagnosis).</summary>
    public sealed class TrackedPawn : TrackedProblem
    {
        public readonly Pawn Pawn;
        private readonly Map map;
        internal int SeenInDiscovery;

        public TrackedPawn(Pawn pawn, Map map, int phase) : base(phase)
        {
            Pawn = pawn;
            this.map = map;
        }

        public override ProblemKind Kind => ProblemKind.Idle;

        public override bool IsGone => Pawn.Dead || Pawn.Destroyed || !Pawn.Spawned || Pawn.Map != map || !Pawn.IsFreeColonist;

        public override string Title => Pawn.LabelShortCap;

        /// <summary>Up to four active work types, best priority first.</summary>
        public override string Location
        {
            get
            {
                if (Pawn.workSettings == null) return "";
                var active = DefDatabase<WorkTypeDef>.AllDefsListForReading
                    .Where(w => !Pawn.WorkTypeIsDisabled(w) && Pawn.workSettings.WorkIsActive(w) && w.visible)
                    .OrderBy(w => Pawn.workSettings.GetPriority(w)).ThenByDescending(w => w.naturalPriority)
                    .Select(w => w.labelShort).ToList();
                if (active.Count == 0) return "ColonyDoctor_NoWorkTypes".Translate();
                string list = string.Join(", ", active.Take(4));
                return active.Count > 4 ? list + " …" : list;
            }
        }

        public override void JumpTo()
        {
            if (Pawn.Spawned) CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(Pawn), CameraJumper.MovementMode.Pan);
        }

        public override string Explain(in Core.Diagnosis d)
        {
            string name = Pawn.LabelShort;
            switch (d.Detail)
            {
                case Detail.IdleNoWork:
                    return "ColonyDoctor_Idle_NoWork".Translate(name.Named("PAWN"));
                case Detail.IdleWorkBlocked:
                    if (d.SubjectA is TrackedProblem blocked && !blocked.IsGone)
                    {
                        return "ColonyDoctor_Idle_Blocked".Translate(name.Named("PAWN"), blocked.Title.Named("PROBLEM"),
                            UI.ReasonStyle.LabelOf(blocked.History.LastReason).Named("REASON"));
                    }
                    return "ColonyDoctor_Idle_BlockedHauling".Translate(name.Named("PAWN"));
                case Detail.IdleNothingToDo:
                    return "ColonyDoctor_Idle_Nothing".Translate(name.Named("PAWN"));
                case Detail.IdleWorkNotTaken:
                    if (d.SubjectB is WorkTypeDef open)
                    {
                        return "ColonyDoctor_Idle_NotTaken".Translate(name.Named("PAWN"), More(open, d.ValueA).Named("WORK"));
                    }
                    return "ColonyDoctor_Idle_Unseen".Translate(name.Named("PAWN"), More(d.SubjectC as WorkTypeDef, d.ValueB).Named("WORK"));
            }
            return d.Reason == Reason.Working ? "ColonyDoctor_Idle_Busy".Translate().Resolve() : "";
        }

        private static string More(WorkTypeDef first, int count)
        {
            string label = first?.labelShort ?? "?";
            return count > 1 ? "ColonyDoctor_AndMore".Translate(label.Named("FIRST"), (count - 1).Named("COUNT")).Resolve() : label;
        }
    }
}
