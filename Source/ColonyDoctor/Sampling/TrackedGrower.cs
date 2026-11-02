using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>A growing zone or plant grower building (spec: growing-diagnosis).</summary>
    public sealed class TrackedGrower : TrackedProblem
    {
        public readonly IPlantToGrowSettable Grower;
        private readonly Map map;
        internal int SeenInDiscovery;
        internal int PendingSince = -1;

        // Filled by the diagnoser at each sample (UI reads them).
        internal int ToSow, ToHarvest, OffSeason;
        internal IntVec3 FirstPending = IntVec3.Invalid;

        public TrackedGrower(IPlantToGrowSettable grower, Map map, int phase) : base(phase)
        {
            Grower = grower;
            this.map = map;
        }

        public override ProblemKind Kind => ProblemKind.Growing;

        public Zone_Growing Zone => Grower as Zone_Growing;
        public Building_PlantGrower Building => Grower as Building_PlantGrower;

        public override bool IsGone => Zone != null ? Zone.zoneManager == null || !Zone.zoneManager.AllZones.Contains(Zone) || Zone.Cells.Count == 0
            : Building == null || Building.Destroyed || !Building.Spawned || Building.Map != map;

        public ThingDef Plant => Grower.GetPlantDefToGrow();

        public override string Title
        {
            get
            {
                string name = Zone != null ? Zone.label : Building?.LabelCap ?? "?";
                ThingDef plant = Plant;
                return plant == null ? name : name + " (" + plant.label + ")";
            }
        }

        public override string Location => "ColonyDoctor_GrowPending".Translate(ToSow.Named("SOW"), ToHarvest.Named("HARVEST"));

        public override string Extra => Zone != null ? "ColonyDoctor_GrowCells".Translate(Zone.Cells.Count.Named("COUNT")).Resolve() : null;

        public override void JumpTo()
        {
            if (Zone != null)
            {
                List<IntVec3> cells = Zone.Cells;
                if (cells.Count == 0) return;
                CameraJumper.TryJump(FirstPending.IsValid ? FirstPending : cells[cells.Count / 2], map, CameraJumper.MovementMode.Pan);
                Find.Selector.ClearSelection();
                Find.Selector.Select(Zone);
            }
            else if (Building != null && Building.Spawned)
            {
                CameraJumper.TryJumpAndSelect(new GlobalTargetInfo(Building), CameraJumper.MovementMode.Pan);
            }
        }

        public override string Explain(in Core.Diagnosis d) => UI.GrowExplanations.For(this, in d);
    }
}
