using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>Spec: research-diagnosis. One instance per map; read-only.</summary>
    public sealed class ResearchDiagnoser
    {
        private const int WorkedGrace = 500;

        private readonly Map map;
        private readonly WorkerPool pool;
        private readonly ReachCache reach;
        private readonly List<Building_ResearchBench> suitable = new List<Building_ResearchBench>(4);
        private readonly List<Building_ResearchBench> open = new List<Building_ResearchBench>(4);
        private readonly List<Pawn> allowed = new List<Pawn>(16);
        private static WorkGiverDef giver;

        public ResearchDiagnoser(Map map, WorkerPool pool, ReachCache reach)
        {
            this.map = map;
            this.pool = pool;
            this.reach = reach;
        }

        public Core.Diagnosis Diagnose(TrackedResearch t, int tick)
        {
            ResearchProjectDef project = t.Project;
            t.Bench = null;
            if (project.IsFinished) return Core.Diagnosis.Of(Reason.Working);
            if (AnyoneResearching()) t.LastWorkedTick = tick;
            if (t.LastWorkedTick >= 0 && tick - t.LastWorkedTick < WorkedGrace)
            {
                t.WaitSince = -1;
                FindBenches(project);
                return Core.Diagnosis.Of(Reason.Working);
            }
            if (t.WaitSince < 0) t.WaitSince = tick;

            Core.Diagnosis bench = FindBenches(project);
            if (bench.Reason != Reason.Working) return bench;
            if (tick - t.WaitSince < HaulReason.WaitingAfterTicks) return Core.Diagnosis.Of(Reason.Working);

            // Powered, suitable benches; forbidden ones block.
            open.Clear();
            Building_ResearchBench unpowered = null, forbidden = null;
            for (int i = 0; i < suitable.Count; i++)
            {
                Building_ResearchBench b = suitable[i];
                if (!project.CanBeResearchedAt(b, false))
                {
                    if (unpowered == null) unpowered = b;
                    continue;
                }
                if (b.IsForbidden(Faction.OfPlayer))
                {
                    if (forbidden == null) forbidden = b;
                    continue;
                }
                open.Add(b);
            }
            if (open.Count == 0)
            {
                if (forbidden != null)
                {
                    var f = Core.Diagnosis.Of(Reason.Blocked, Detail.Forbidden);
                    f.SubjectA = forbidden;
                    return f;
                }
                var u = Core.Diagnosis.Of(Reason.BenchProblem, Detail.Unpowered);
                u.SubjectA = unpowered;
                return u;
            }
            t.Bench = open[0];

            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Research");
            if (work == null) return Core.Diagnosis.Of(Reason.WaitingForWorker);
            List<Pawn> available = pool.Available(work, tick, out Detail none);
            if (available.Count == 0)
            {
                var n = Core.Diagnosis.Of(Reason.NoEligibleWorker, none);
                n.SubjectC = work;
                return n;
            }

            allowed.Clear();
            for (int i = 0; i < available.Count; i++)
            {
                for (int b = 0; b < open.Count; b++)
                {
                    if (!AreaGate.Allowed(available[i], open[b].Position)) continue;
                    allowed.Add(available[i]);
                    break;
                }
            }
            if (allowed.Count == 0) return AreaGate.Diagnosis(available, work);

            bool reached = false;
            for (int i = 0; i < allowed.Count && !reached; i++)
            {
                Pawn p = allowed[i];
                for (int b = 0; b < open.Count && !reached; b++)
                {
                    reached = AreaGate.Allowed(p, open[b].Position) && reach.CanReach(p, p.Position, open[b], PathEndMode.InteractionCell);
                }
            }
            if (!reached)
            {
                var r = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Unreachable);
                r.SubjectA = open[0];
                r.SubjectC = work;
                return r;
            }

            if (giver == null) giver = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Research");
            return giver != null ? ActivityClassifier.Decide(allowed, giver) : Core.Diagnosis.Of(Reason.WaitingForWorker);
        }

        /// <summary>
        /// Fills <see cref="suitable"/> with benches the project can be researched at (power aside). Returns Working
        /// when there are some, else Bench problem with detail No research bench or Facility missing.
        /// </summary>
        private Core.Diagnosis FindBenches(ResearchProjectDef project)
        {
            suitable.Clear();
            Building_ResearchBench rightType = null;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is Building_ResearchBench b) || !b.Spawned) continue;
                if (project.CanBeResearchedAt(b, true))
                {
                    suitable.Add(b);
                    continue;
                }
                if (rightType == null && (project.requiredResearchBuilding == null || b.def == project.requiredResearchBuilding)) rightType = b;
            }
            if (suitable.Count > 0) return Core.Diagnosis.Of(Reason.Working);
            if (rightType != null)
            {
                var f = Core.Diagnosis.Of(Reason.BenchProblem, Detail.FacilityMissing);
                f.SubjectA = rightType;
                f.SubjectB = MissingFacility(project, rightType);
                return f;
            }
            var d = Core.Diagnosis.Of(Reason.BenchProblem, Detail.NoResearchBench);
            d.SubjectB = project.requiredResearchBuilding;
            return d;
        }

        private static ThingDef MissingFacility(ResearchProjectDef project, Building_ResearchBench bench)
        {
            if (project.requiredResearchFacilities == null) return null;
            List<Thing> linked = bench.TryGetComp<CompAffectedByFacilities>()?.LinkedFacilitiesListForReading;
            for (int i = 0; i < project.requiredResearchFacilities.Count; i++)
            {
                ThingDef need = project.requiredResearchFacilities[i];
                bool found = false;
                for (int l = 0; linked != null && l < linked.Count && !found; l++) found = linked[l].def == need;
                if (!found) return need;
            }
            return null;
        }

        private static bool AnyoneResearching()
        {
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                if (Researching(maps[m].mapPawns.FreeColonistsSpawned) || Researching(maps[m].mapPawns.SpawnedColonyMechs)) return true;
            }
            return false;
        }

        private static bool Researching(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i].CurJob?.def == JobDefOf.Research) return true;
            }
            return false;
        }
    }
}
