using System.Collections.Generic;
using ColonyDoctor.Construction;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Spec: Work presence. Once per sampling cycle and only read, whether work of each work type exists on the map
    /// and whether it is stalled. Built lazily on the first idle sample of a cycle.
    /// Hauling comes from the hauling groups (spec: hauling-diagnosis).
    /// </summary>
    public sealed class WorkPresence
    {
        private readonly Map map;
        private Presence[] state;
        private TrackedProblem[] longestBlocked;
        private int[] longestStall;
        private int builtCycle = -1;

        // Work types whose every work giver is a bill giver: we see all their work through tracked bills.
        private static readonly List<WorkTypeDef> billOnlyTypes = new List<WorkTypeDef>();
        private static WorkTypeDef cleaning, plantCutting, firefighter, patient, bedRest, basicWorker, research, doctor, hunting, mining;

        public WorkPresence(Map map) => this.map = map;

        public Presence Of(WorkTypeDef w) => state[w.index];

        /// <summary>The blocked bill or site of this work type with the longest current stall, or null.</summary>
        public TrackedProblem LongestBlocked(WorkTypeDef w) => longestBlocked[w.index];

        public int LongestStall(WorkTypeDef w) => longestStall[w.index];

        public void EnsureBuilt(MapTracker tracker, int tick)
        {
            int cycle = tick / GameTime.TicksPerSample;
            if (cycle == builtCycle) return;
            builtCycle = cycle;
            int n = DefDatabase<WorkTypeDef>.DefCount;
            if (state == null || state.Length != n)
            {
                state = new Presence[n];
                longestBlocked = new TrackedProblem[n];
                longestStall = new int[n];
                ResolveDefs();
            }
            List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++) state[all[i].index] = Presence.Unknown;
            System.Array.Clear(longestBlocked, 0, n);
            System.Array.Clear(longestStall, 0, n);

            // Work types we can see but that start empty.
            Set(WorkTypeDefOf.Hauling, Presence.None);
            Set(WorkTypeDefOf.Construction, Presence.None);
            for (int i = 0; i < billOnlyTypes.Count; i++) Set(billOnlyTypes[i], Presence.None);
            Set(cleaning, Presence.None);
            Set(mining, Presence.None);
            Set(plantCutting, Presence.None);
            Set(hunting, Presence.None);
            Set(research, Presence.None);
            Set(doctor, Presence.None);
            Set(firefighter, Presence.None);
            Set(patient, Presence.None);
            Set(bedRest, Presence.None);
            Set(basicWorker, Presence.None);

            // Bills and construction sites, from their latest diagnosis.
            IReadOnlyList<TrackedProblem> problems = tracker.Problems;
            for (int i = 0; i < problems.Count; i++)
            {
                TrackedProblem p = problems[i];
                if (p.IsGone || !p.History.HasData) continue;
                WorkTypeDef w = WorkTypeOf(p);
                if (w == null) continue;
                Reason r = p.History.LastReason;
                if (r == Reason.Suspended || r == Reason.Idle) continue;
                if (r == Reason.Working || r == Reason.Outcompeted || r == Reason.WaitingForWorker || r == Reason.Unknown)
                {
                    state[w.index] = Presence.Open;
                }
                else
                {
                    if (state[w.index] != Presence.Open) state[w.index] = Presence.Blocked;
                    int stall = p.History.ContinuousStallTicks(tick);
                    if (longestBlocked[w.index] == null || stall > longestStall[w.index])
                    {
                        longestBlocked[w.index] = p;
                        longestStall[w.index] = stall;
                    }
                }
            }

            if (cleaning != null && map.listerFilthInHomeArea.FilthInHomeArea.Count > 0) Set(cleaning, Presence.Open);
            if (mining != null && (map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Mine)
                                   || map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.MineVein))) Set(mining, Presence.Open);
            if (plantCutting != null && (map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.CutPlant)
                                         || map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.HarvestPlant))) Set(plantCutting, Presence.Open);
            if (hunting != null && map.designationManager.AnySpawnedDesignationOfDef(DesignationDefOf.Hunt)) Set(hunting, Presence.Open);
            if (research != null && Find.ResearchManager.GetProject() != null && HasResearchBench()) Set(research, Presence.Open);
            if (doctor != null && AnyNeedsTending()) Set(doctor, Presence.Open);
            if (firefighter != null && AnyHomeFire()) Set(firefighter, Presence.Open);
        }

        private void Set(WorkTypeDef w, Presence p)
        {
            if (w != null) state[w.index] = p;
        }

        private static void ResolveDefs()
        {
            WorkTypeDef Named(string n) => DefDatabase<WorkTypeDef>.GetNamedSilentFail(n);
            cleaning = Named("Cleaning");
            plantCutting = Named("PlantCutting");
            firefighter = Named("Firefighter");
            patient = Named("Patient");
            bedRest = Named("PatientBedRest");
            basicWorker = Named("BasicWorker");
            research = Named("Research");
            doctor = Named("Doctor");
            hunting = Named("Hunting");
            mining = Named("Mining");
            billOnlyTypes.Clear();
            foreach (WorkTypeDef w in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                bool any = false, allBills = true;
                foreach (WorkGiverDef g in DefDatabase<WorkGiverDef>.AllDefsListForReading)
                {
                    if (g.workType != w) continue;
                    any = true;
                    if (g.giverClass == null || !typeof(WorkGiver_DoBill).IsAssignableFrom(g.giverClass)) allBills = false;
                }
                if (any && allBills) billOnlyTypes.Add(w);
            }
        }

        private static WorkTypeDef WorkTypeOf(TrackedProblem p)
        {
            if (p is ConstructionSite) return WorkTypeDefOf.Construction;
            if (p is TrackedHaulGroup h) return h.Items.Count > 0 ? WorkTypeDefOf.Hauling : null;
            if (p is TrackedBill b)
            {
                return b.UsesProbe ? b.Bill.recipe.requiredGiverWorkType : BillWorkGivers.For(b.Bench, b.Bill)?.workType;
            }
            return null;
        }

        private bool HasResearchBench()
        {
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_ResearchBench) return true;
            }
            return false;
        }

        private bool AnyNeedsTending()
        {
            List<Pawn> pawns = map.mapPawns.FreeColonistsAndPrisonersSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (HealthAIUtility.ShouldBeTendedNowByPlayer(pawns[i])) return true;
            }
            return false;
        }

        private bool AnyHomeFire()
        {
            List<Thing> fires = map.listerThings.ThingsOfDef(ThingDefOf.Fire);
            for (int i = 0; i < fires.Count; i++)
            {
                if (map.areaManager.Home[fires[i].Position]) return true;
            }
            return false;
        }
    }
}
