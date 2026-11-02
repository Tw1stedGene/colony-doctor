using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>Spec: orders-diagnosis. One instance per map; read-only.</summary>
    public sealed class OrderDiagnoser
    {
        private const int WorkedGrace = 500;
        private const int MaxReachChecks = 200;

        private readonly Map map;
        private readonly WorkerPool pool;

        /// <summary>Tick each designation was first seen; rebuilt at every discovery.</summary>
        internal Dictionary<Designation, int> FirstSeen = new Dictionary<Designation, int>(256);

        /// <summary>(work giver, cell) pairs targeted by player pawns' current jobs, once per cycle.</summary>
        private readonly HashSet<long> worked = new HashSet<long>();
        private readonly List<Pawn> workers = new List<Pawn>(16);
        private static WorkGiverDef hunt, tame;
        private int workedCycle = -1;

        public OrderDiagnoser(Map map, WorkerPool pool)
        {
            this.map = map;
            this.pool = pool;
        }

        public Core.Diagnosis Diagnose(TrackedOrderGroup g, int tick)
        {
            g.WaitingCount = 0;
            g.OldestWaitTicks = 0;
            g.Oldest = null;
            List<Designation> items = g.Items;
            if (items.Count == 0) return Core.Diagnosis.Of(Reason.Working);

            EnsureWorked(tick);
            bool isWorked = false;
            for (int i = 0; i < items.Count; i++)
            {
                Designation d = items[i];
                IntVec3 c = TrackedOrderGroup.CellOf(d);
                if (!isWorked && worked.Contains(Key(g.Giver, c))) isWorked = true;
                if (!FirstSeen.TryGetValue(d, out int seen) || !HaulReason.IsWaiting(seen, tick)) continue;
                g.WaitingCount++;
                if (tick - seen > g.OldestWaitTicks)
                {
                    g.OldestWaitTicks = tick - seen;
                    g.Oldest = d;
                }
            }
            if (isWorked) g.LastWorkedTick = tick;
            if (isWorked || (g.LastWorkedTick >= 0 && tick - g.LastWorkedTick < WorkedGrace) || g.WaitingCount == 0)
                return Core.Diagnosis.Of(Reason.Working);

            WorkTypeDef work = g.Giver.workType;
            List<Pawn> available = pool.Available(work, tick, out Detail none);
            if (available.Count == 0)
            {
                var n = Core.Diagnosis.Of(Reason.NoEligibleWorker, none);
                n.SubjectC = work;
                return n;
            }

            Core.Diagnosis animal = AnimalWorkers(g, available, tick);
            if (animal.Reason != Reason.Working) return animal;

            // Forbidden things block their orders; cell orders never are.
            int forbidden = 0, open = 0;
            bool reachable = false, anyAllowed = false;
            int checks = 0;
            Thing firstForbidden = null;
            for (int i = 0; i < items.Count; i++)
            {
                Designation d = items[i];
                if (!FirstSeen.TryGetValue(d, out int seen) || !HaulReason.IsWaiting(seen, tick)) continue;
                if (d.target.HasThing && d.target.Thing.IsForbidden(Faction.OfPlayer))
                {
                    forbidden++;
                    if (firstForbidden == null) firstForbidden = d.target.Thing;
                    continue;
                }
                open++;
                if (reachable || checks >= MaxReachChecks) continue;
                checks++;
                IntVec3 c = TrackedOrderGroup.CellOf(d);
                for (int p = 0; p < workers.Count && !reachable; p++)
                {
                    Pawn pawn = workers[p];
                    if (!AreaGate.Allowed(pawn, c)) continue;
                    anyAllowed = true;
                    reachable = map.reachability.CanReach(pawn.Position, c, PathEndMode.Touch, TraverseParms.For(pawn, Danger.Some));
                }
            }
            if (open == 0 && forbidden > 0)
            {
                var f = Core.Diagnosis.Of(Reason.Blocked, Detail.Forbidden);
                f.SubjectA = firstForbidden;
                f.ValueA = forbidden;
                return f;
            }
            if (!reachable && checks >= open && !anyAllowed && open > 0) return AreaGate.Diagnosis(workers, work);
            if (!reachable && checks >= open)
            {
                var u = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Unreachable);
                u.SubjectC = work;
                return u;
            }
            return ActivityClassifier.Decide(workers, g.Giver);
        }

        /// <summary>
        /// Spec: Animal worker checks. Fills <see cref="workers"/> with the available pawns who could take the group's
        /// orders; returns Working when there are some, else the No eligible worker diagnosis.
        /// </summary>
        private Core.Diagnosis AnimalWorkers(TrackedOrderGroup g, List<Pawn> available, int tick)
        {
            workers.Clear();
            if (hunt == null)
            {
                hunt = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HunterHunt");
                tame = DefDatabase<WorkGiverDef>.GetNamedSilentFail("Tame");
            }
            if (g.Giver == hunt && hunt != null)
            {
                for (int i = 0; i < available.Count; i++)
                {
                    Pawn p = available[i];
                    if (WorkGiver_HunterHunt.HasHuntingWeapon(p) && !WorkGiver_HunterHunt.HasShieldAndRangedWeapon(p)) workers.Add(p);
                }
                if (workers.Count > 0) return Core.Diagnosis.Of(Reason.Working);
                var h = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.NoHuntingWeapon);
                h.SubjectA = available[0];
                h.SubjectC = g.Giver.workType;
                return h;
            }
            if (g.Giver == tame && tame != null)
            {
                int need = int.MaxValue;
                Pawn needAnimal = null;
                for (int i = 0; i < g.Items.Count; i++)
                {
                    Designation d = g.Items[i];
                    if (!(d.target.Thing is Pawn animal) || !FirstSeen.TryGetValue(d, out int seen) || !HaulReason.IsWaiting(seen, tick)) continue;
                    int req = TrainableUtility.MinimumHandlingSkill(animal);
                    if (req >= need) continue;
                    need = req;
                    needAnimal = animal;
                }
                if (needAnimal == null)
                {
                    workers.AddRange(available);
                    return Core.Diagnosis.Of(Reason.Working);
                }
                Pawn best = null;
                int bestLevel = -1;
                for (int i = 0; i < available.Count; i++)
                {
                    int level = WorkerPool.SkillLevel(available[i], SkillDefOf.Animals);
                    if (level >= need) workers.Add(available[i]);
                    if (level > bestLevel)
                    {
                        bestLevel = level;
                        best = available[i];
                    }
                }
                if (workers.Count > 0) return Core.Diagnosis.Of(Reason.Working);
                var s = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.SkillTooLow);
                s.SubjectA = best;
                s.SubjectB = needAnimal;
                s.SubjectC = g.Giver.workType;
                s.PieceCount = bestLevel;
                s.PieceTotal = need;
                return s;
            }
            workers.AddRange(available);
            return Core.Diagnosis.Of(Reason.Working);
        }

        private long Key(WorkGiverDef giver, IntVec3 c) => ((long)giver.index << 32) | (uint)map.cellIndices.CellToIndex(c);

        private void EnsureWorked(int tick)
        {
            int cycle = tick / GameTime.TicksPerSample;
            if (cycle == workedCycle) return;
            workedCycle = cycle;
            worked.Clear();
            Add(map.mapPawns.FreeColonistsSpawned);
            Add(map.mapPawns.SpawnedColonyMechs);
        }

        private void Add(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Job job = pawns[i].CurJob;
                if (job?.workGiverDef == null) continue;
                if (job.targetA.IsValid && job.targetA.Cell.InBounds(map)) worked.Add(Key(job.workGiverDef, job.targetA.Cell));
                if (job.targetQueueA == null) continue;
                for (int q = 0; q < job.targetQueueA.Count; q++)
                {
                    if (job.targetQueueA[q].IsValid && job.targetQueueA[q].Cell.InBounds(map)) worked.Add(Key(job.workGiverDef, job.targetQueueA[q].Cell));
                }
            }
        }
    }
}
