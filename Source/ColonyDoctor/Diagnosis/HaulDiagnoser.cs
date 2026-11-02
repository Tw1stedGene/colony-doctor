using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Spec: hauling-diagnosis. One instance per map. Reads only: lister, reservations, storage settings, the thing grid
    /// and reachability. Haulers and storage room are computed once per sampling cycle.
    /// </summary>
    public sealed class HaulDiagnoser
    {
        private readonly Map map;
        private readonly ReachCache reach;

        /// <summary>Tick each loose item was first seen needing hauling; rebuilt at every discovery.</summary>
        internal Dictionary<Thing, int> FirstSeen = new Dictionary<Thing, int>(256);

        private readonly WorkerPool pool;
        private List<Pawn> available;
        private Detail workerDetail;
        private bool anyAvailable;

        private readonly Dictionary<ThingDef, bool> roomByDef = new Dictionary<ThingDef, bool>(32);
        private int roomCycle = -1;
        private readonly int[] counts = new int[Reasons.Count];

        private static WorkGiverDef haulGeneral, haulCorpses;

        public HaulDiagnoser(Map map, ReachCache reach, WorkerPool pool)
        {
            this.map = map;
            this.reach = reach;
            this.pool = pool;
        }

        public Core.Diagnosis Diagnose(TrackedHaulGroup g, int tick)
        {
            List<Thing> items = g.Items;
            g.TotalCount = 0;
            g.WaitingStacks = 0;
            g.OldestWaitTicks = 0;
            g.RottingStacks = 0;
            g.Oldest = null;
            if (items.Count == 0) return Core.Diagnosis.Of(Reason.Working);

            bool beingHauled = false;
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.Spawned) continue;
                g.TotalCount += t.stackCount;
                if (map.reservationManager.IsReservedByAnyoneOf(t, Faction.OfPlayer)) beingHauled = true;
                CompRottable rot = t.TryGetComp<CompRottable>();
                if (rot != null && HaulReason.IsRotting(rot.Stage == RotStage.Fresh, rot.TicksUntilRotAtCurrentTemp)) g.RottingStacks++;
                if (!FirstSeen.TryGetValue(t, out int seen) || !HaulReason.IsWaiting(seen, tick)) continue;
                g.WaitingStacks++;
                if (tick - seen > g.OldestWaitTicks)
                {
                    g.OldestWaitTicks = tick - seen;
                    g.Oldest = t;
                }
            }
            if (beingHauled || g.WaitingStacks == 0) return Core.Diagnosis.Of(Reason.Working);

            available = pool.Available(WorkTypeDefOf.Hauling, tick, out workerDetail);
            anyAvailable = available.Count > 0;
            System.Array.Clear(counts, 0, counts.Length);
            int noAccept = 0, full = 0, reachable = 0, outsideArea = 0;
            Thing unreachable = null;
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (!t.Spawned || !FirstSeen.TryGetValue(t, out int seen) || !HaulReason.IsWaiting(seen, tick)) continue;
                switch (Storage(t, tick))
                {
                    case 0:
                        counts[(int)Reason.NoStorage]++;
                        noAccept++;
                        continue;
                    case 1:
                        counts[(int)Reason.NoStorage]++;
                        full++;
                        continue;
                }
                if (!anyAvailable)
                {
                    counts[(int)Reason.NoEligibleWorker]++;
                    continue;
                }
                if (!AreaGate.AnyAllowed(available, t.Position))
                {
                    counts[(int)Reason.NoEligibleWorker]++;
                    outsideArea++;
                    if (unreachable == null) unreachable = t;
                    continue;
                }
                if (!AnyHaulerReaches(t))
                {
                    counts[(int)Reason.NoEligibleWorker]++;
                    if (unreachable == null) unreachable = t;
                    continue;
                }
                reachable++;
            }

            Core.Diagnosis decision = default;
            if (reachable > 0)
            {
                decision = ActivityClassifier.Decide(available, GiverFor(g.Def));
                counts[(int)decision.Reason] += reachable;
            }

            Reason r = HaulReason.Majority(counts);
            Core.Diagnosis d;
            switch (r)
            {
                case Reason.NoStorage:
                    d = Core.Diagnosis.Of(r, full > noAccept ? Detail.StorageFull : Detail.NoStockpileAccepts);
                    d.SubjectA = g.Def;
                    break;
                case Reason.NoEligibleWorker:
                    if (anyAvailable && outsideArea * 2 >= counts[(int)r])
                    {
                        d = AreaGate.Diagnosis(available, WorkTypeDefOf.Hauling);
                        break;
                    }
                    d = Core.Diagnosis.Of(r, anyAvailable ? Detail.Unreachable : workerDetail);
                    d.SubjectA = unreachable;
                    d.SubjectC = WorkTypeDefOf.Hauling;
                    break;
                case Reason.Working:
                    return Core.Diagnosis.Of(Reason.Working);
                default:
                    d = decision;
                    break;
            }
            d.PieceCount = counts[(int)r];
            d.PieceTotal = g.WaitingStacks;
            return d;
        }

        private static WorkGiverDef GiverFor(ThingDef def)
        {
            if (haulGeneral == null)
            {
                haulGeneral = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulGeneral");
                haulCorpses = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HaulCorpses");
            }
            return def.IsCorpse && haulCorpses != null ? haulCorpses : haulGeneral;
        }

        // ---- Haulers (spec: hauler decision) -------------------------------------------------------------------------

        private bool AnyHaulerReaches(Thing t)
        {
            for (int i = 0; i < available.Count; i++)
            {
                Pawn p = available[i];
                if (AreaGate.Allowed(p, t.Position) && reach.CanReach(p, p.Position, t, PathEndMode.ClosestTouch)) return true;
            }
            return false;
        }

        // ---- Storage (spec: No storage) ------------------------------------------------------------------------------

        /// <summary>0 = nothing accepts it, 1 = every accepting storage is full, 2 = room somewhere.</summary>
        private int Storage(Thing t, int tick)
        {
            int cycle = tick / GameTime.TicksPerSample;
            if (cycle != roomCycle)
            {
                roomCycle = cycle;
                roomByDef.Clear();
            }

            StoragePriority current = StoreUtility.CurrentStoragePriorityOf(t);
            bool accepts = false;
            List<SlotGroup> groups = map.haulDestinationManager.AllGroupsListInPriorityOrder;
            for (int i = 0; i < groups.Count; i++)
            {
                SlotGroup g = groups[i];
                if (g.Settings.Priority <= current) break; // sorted best first
                if (!g.parent.Accepts(t)) continue;
                accepts = true;
                break;
            }
            // Haul destinations without slot groups (containers): capacity unknown, assume room.
            List<IHaulDestination> dests = map.haulDestinationManager.AllHaulDestinationsListForReading;
            for (int i = 0; i < dests.Count; i++)
            {
                IHaulDestination dest = dests[i];
                if (dest is ISlotGroupParent) continue;
                StorageSettings s = dest.GetStoreSettings();
                if (s != null && s.Priority > current && dest.Accepts(t)) return 2;
            }
            if (!accepts) return 0;

            if (!roomByDef.TryGetValue(t.def, out bool room))
            {
                room = AnyRoom(t, current, groups);
                roomByDef[t.def] = room;
            }
            return room ? 2 : 1;
        }

        private bool AnyRoom(Thing t, StoragePriority current, List<SlotGroup> groups)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                SlotGroup g = groups[i];
                if (g.Settings.Priority <= current) break;
                if (!g.parent.Accepts(t)) continue;
                List<IntVec3> cells = g.CellsList;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (NoStorageBlockersIn(cells[c], t)) return true;
                }
            }
            return false;
        }

        /// <summary>Mirror of the private <c>StoreUtility.NoStorageBlockersIn</c> (1.6), read-only.</summary>
        private bool NoStorageBlockersIn(IntVec3 c, Thing thing)
        {
            List<Thing> list = map.thingGrid.ThingsListAt(c);
            bool stackable = false;
            for (int i = 0; i < list.Count; i++)
            {
                Thing other = list[i];
                if (!stackable && other.def.EverStorable(false) && other.CanStackWith(thing) && other.stackCount < other.def.stackLimit)
                    stackable = true;
                if (other.def.entityDefToBuild != null && other.def.entityDefToBuild.passability != Traversability.Standable) return false;
                if (other.def.surfaceType == SurfaceType.None && other.def.passability != Traversability.Standable
                    && (c.GetMaxItemsAllowedInCell(map) <= 1 || other.def.category != ThingCategory.Item))
                    return false;
            }
            return stackable || c.GetItemCount(map) < c.GetMaxItemsAllowedInCell(map);
        }
    }
}
