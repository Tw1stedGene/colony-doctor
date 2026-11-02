using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>The current research project, tracked on one home map (spec: Research tracking).</summary>
    public sealed class TrackedResearch : TrackedProblem
    {
        public readonly ResearchProjectDef Project;
        private readonly Map map;
        internal int WaitSince = -1;

        /// <summary>Nearest suitable bench at the last sample, for Jump to; may be null.</summary>
        internal Building_ResearchBench Bench;

        public TrackedResearch(ResearchProjectDef project, Map map, int phase) : base(phase)
        {
            Project = project;
            this.map = map;
        }

        public override ProblemKind Kind => ProblemKind.Research;

        public override bool IsGone => Project.IsFinished || Find.ResearchManager.GetProject() != Project;

        public override string Title => Project.LabelCap;

        public override string Location => "ColonyDoctor_ResearchProgress".Translate((Project.ProgressPercent * 100f).ToString("0").Named("PCT")).Resolve();

        public override string Extra => Bench != null && Bench.Spawned ? Bench.LabelCap : null;

        public override void JumpTo()
        {
            if (Bench != null && Bench.Spawned && Bench.Map == map)
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(Bench), CameraJumper.MovementMode.Pan);
                return;
            }
            Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Research);
        }

        public override string Explain(in Core.Diagnosis d) => UI.ResearchExplanations.For(this, in d);
    }
}
