using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>Spec: growing-diagnosis. One instance per map; read-only.</summary>
    public sealed class GrowDiagnoser
    {
        private const int WorkedGrace = 500;

        private readonly Map map;
        private readonly WorkerPool pool;
        private readonly HashSet<IntVec3> workedCells = new HashSet<IntVec3>();
        private readonly List<Pawn> skilled = new List<Pawn>(16);
        private int workedCycle = -1;

        private static WorkGiverDef sowGiver, harvestGiver;
        private static JobDef harvestDesignated;

        public GrowDiagnoser(Map map, WorkerPool pool)
        {
            this.map = map;
            this.pool = pool;
        }

        public Core.Diagnosis Diagnose(TrackedGrower g, int tick)
        {
            g.ToSow = g.ToHarvest = g.OffSeason = 0;
            g.FirstPending = IntVec3.Invalid;
            ThingDef plantDef = g.Plant;
            if (plantDef?.plant == null) return Core.Diagnosis.Of(Reason.Suspended);

            EnsureWorkedCells(tick);
            bool allowSow = g.Zone == null || g.Zone.allowSow;
            bool worked = false;
            foreach (IntVec3 c in g.Grower.Cells)
            {
                if (!worked && workedCells.Contains(c)) worked = true;
                Plant plant = c.GetPlant(map);
                if (plant != null && plant.def == plantDef)
                {
                    if (plant.def.plant.autoHarvestable && plant.HarvestableNow && plant.LifeStage == PlantLifeStage.Mature)
                    {
                        g.ToHarvest++;
                        if (!g.FirstPending.IsValid) g.FirstPending = c;
                    }
                    continue;
                }
                if (!allowSow) continue;
                if (!PlantUtility.GrowthSeasonNow(c, map, plantDef))
                {
                    g.OffSeason++;
                    continue;
                }
                g.ToSow++;
                if (!g.FirstPending.IsValid) g.FirstPending = c;
            }

            if (worked) g.LastWorkedTick = tick;
            int pending = g.ToSow + g.ToHarvest;
            if (pending == 0)
            {
                g.PendingSince = -1;
                return Core.Diagnosis.Of(g.OffSeason > 0 ? Reason.Suspended : Reason.Working);
            }
            if (g.PendingSince < 0) g.PendingSince = tick;
            if (worked || (g.LastWorkedTick >= 0 && tick - g.LastWorkedTick < WorkedGrace)
                || tick - g.PendingSince < HaulReason.WaitingAfterTicks)
            {
                return Core.Diagnosis.Of(Reason.Working);
            }

            Core.Diagnosis d = Decide(g, plantDef, tick);
            d.ValueA = g.ToSow;
            d.ValueB = g.ToHarvest;
            return d;
        }

        private Core.Diagnosis Decide(TrackedGrower g, ThingDef plantDef, int tick)
        {
            bool sowOnly = g.ToHarvest == 0;
            if (sowOnly && g.Building != null && !g.Grower.CanAcceptSowNow())
                return Core.Diagnosis.Of(Reason.BenchProblem, Detail.Unpowered);

            List<Pawn> available = pool.Available(WorkTypeDefOf.Growing, tick, out Detail none);
            if (available.Count == 0)
            {
                var n = Core.Diagnosis.Of(Reason.NoEligibleWorker, none);
                n.SubjectC = WorkTypeDefOf.Growing;
                return n;
            }

            List<Pawn> workers = available;
            int minSkill = plantDef.plant.sowMinSkill;
            if (sowOnly && minSkill > 0)
            {
                skilled.Clear();
                Pawn best = null;
                int bestLevel = -1;
                for (int i = 0; i < available.Count; i++)
                {
                    int level = WorkerPool.SkillLevel(available[i], SkillDefOf.Plants);
                    if (level >= minSkill) skilled.Add(available[i]);
                    if (level > bestLevel)
                    {
                        bestLevel = level;
                        best = available[i];
                    }
                }
                if (skilled.Count == 0)
                {
                    var s = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.SkillTooLow);
                    s.SubjectA = best;
                    s.SubjectB = plantDef;
                    s.PieceCount = bestLevel;
                    s.PieceTotal = minSkill;
                    return s;
                }
                workers = skilled;
            }

            // The first pending cell stands for the grower; if nobody may enter it, any cell someone may enter does.
            IntVec3 target = g.FirstPending;
            if (!AreaGate.AnyAllowed(workers, target))
            {
                target = IntVec3.Invalid;
                foreach (IntVec3 c in g.Grower.Cells)
                {
                    if (!AreaGate.AnyAllowed(workers, c)) continue;
                    target = c;
                    break;
                }
                if (!target.IsValid) return AreaGate.Diagnosis(workers, WorkTypeDefOf.Growing);
            }
            bool reached = false;
            for (int i = 0; i < workers.Count && !reached; i++)
            {
                Pawn p = workers[i];
                reached = AreaGate.Allowed(p, target) && map.reachability.CanReach(p.Position, target, PathEndMode.Touch, TraverseParms.For(p, Danger.Some));
            }
            if (!reached) return Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Unreachable);

            ResolveDefs();
            WorkGiverDef giver = sowOnly ? sowGiver : harvestGiver;
            return giver != null ? ActivityClassifier.Decide(workers, giver) : Core.Diagnosis.Of(Reason.WaitingForWorker);
        }

        private static void ResolveDefs()
        {
            if (sowGiver != null) return;
            sowGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerSow");
            harvestGiver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("GrowerHarvest");
        }

        /// <summary>Cells targeted by player pawns' sowing, harvesting and cutting jobs, once per cycle.</summary>
        private void EnsureWorkedCells(int tick)
        {
            int cycle = tick / GameTime.TicksPerSample;
            if (cycle == workedCycle) return;
            workedCycle = cycle;
            workedCells.Clear();
            if (harvestDesignated == null) harvestDesignated = DefDatabase<JobDef>.GetNamedSilentFail("HarvestDesignated");
            Add(map.mapPawns.FreeColonistsSpawned);
            Add(map.mapPawns.SpawnedColonyMechs);
        }

        private void Add(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Job job = pawns[i].CurJob;
                if (job == null) continue;
                JobDef def = job.def;
                if (def != JobDefOf.Sow && def != JobDefOf.Harvest && def != JobDefOf.CutPlant && def != harvestDesignated) continue;
                if (job.targetA.IsValid) workedCells.Add(job.targetA.Cell);
                if (job.targetQueueA == null) continue;
                for (int q = 0; q < job.targetQueueA.Count; q++) workedCells.Add(job.targetQueueA[q].Cell);
            }
        }
    }
}
