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

            // Forbidden things block their orders; cell orders never are.
            int forbidden = 0, open = 0;
            bool reachable = false;
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
                for (int p = 0; p < available.Count && !reachable; p++)
                {
                    Pawn pawn = available[p];
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
            if (!reachable && checks >= open)
            {
                var u = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Unreachable);
                u.SubjectC = work;
                return u;
            }
            Core.Diagnosis dec = ActivityClassifier.Decide(available, g.Giver);
            return dec;
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
