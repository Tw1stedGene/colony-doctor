using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ColonyDoctor.Construction;
using ColonyDoctor.Core;
using ColonyDoctor.Diagnosis;
using RimWorld;
using Verse;

namespace ColonyDoctor.Sampling
{
    /// <summary>
    /// Owns tracked problems (bills, construction sites) for one map (spec: stall-history). Every 250 ticks it
    /// re-discovers them; every tick it samples the ones whose phase matches, so each is sampled once per 250 ticks
    /// and cost is spread evenly.
    /// Not a MapComponent on purpose: map components are written into the save by class name, which would leave errors
    /// behind if the mod is removed (spec: No persistence). Trackers live in a weak table keyed by Map and are driven by
    /// a Harmony postfix on <see cref="Map.MapPostTick"/>.
    /// </summary>
    public sealed class MapTracker
    {
        private static readonly ConditionalWeakTable<Map, MapTracker> Trackers = new ConditionalWeakTable<Map, MapTracker>();

        private readonly Map map;
        private readonly List<TrackedBill> tracked = new List<TrackedBill>(64);
        private readonly Dictionary<Bill, TrackedBill> byBill = new Dictionary<Bill, TrackedBill>(64);
        private readonly List<ConstructionSite> sites = new List<ConstructionSite>(16);
        private readonly List<ConstructionSite> removedSites = new List<ConstructionSite>();
        private readonly List<TrackedProblem> problems = new List<TrackedProblem>(80);
        private readonly List<TrackedPawn> pawns = new List<TrackedPawn>(16);
        private readonly Dictionary<Pawn, TrackedPawn> byPawn = new Dictionary<Pawn, TrackedPawn>(16);
        private readonly List<TrackedHaulGroup> haulGroups = new List<TrackedHaulGroup>(16);
        private readonly Dictionary<ThingDef, TrackedHaulGroup> byDef = new Dictionary<ThingDef, TrackedHaulGroup>(16);
        private Dictionary<Thing, int> spareFirstSeen = new Dictionary<Thing, int>(256);
        private readonly HaulDiagnoser haulDiagnoser;
        private readonly WorkerPool workerPool;
        private readonly GrowDiagnoser growDiagnoser;
        private readonly OrderDiagnoser orderDiagnoser;
        private readonly List<TrackedOrderGroup> orderGroups = new List<TrackedOrderGroup>(8);
        private readonly Dictionary<DesignationDef, TrackedOrderGroup> byOrderKind = new Dictionary<DesignationDef, TrackedOrderGroup>(8);
        private Dictionary<Designation, int> spareOrderSeen = new Dictionary<Designation, int>(256);
        private static Dictionary<DesignationDef, (DesignationDef kind, WorkGiverDef giver)> orderKinds;
        private readonly List<TrackedGrower> growers = new List<TrackedGrower>(8);
        private readonly Dictionary<IPlantToGrowSettable, TrackedGrower> byGrower = new Dictionary<IPlantToGrowSettable, TrackedGrower>(8);
        private readonly IdleDiagnoser idleDiagnoser;
        private readonly BillDiagnoser diagnoser;
        private readonly ConstructionDiagnoser constructionDiagnoser;
        private readonly SiteDiscovery siteDiscovery;
        private int discoveryCount;
        private int nextPhase;

        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>Dev overlay: smoothed and peak real time spent in <see cref="Tick"/>, in ms.</summary>
        public double AvgTickMs { get; private set; }
        public double PeakTickMs { get; private set; }

        /// <summary>Bumped whenever the tracked set changes, so the UI knows to rebuild.</summary>
        public int Version { get; private set; }

        public Map Map => map;
        /// <summary>Tracked bills (probe scheduler, bill tools).</summary>
        public IReadOnlyList<TrackedBill> Tracked => tracked;

        /// <summary>Construction sites.</summary>
        public IReadOnlyList<ConstructionSite> Sites => sites;

        /// <summary>Everything tracked on this map, all kinds.</summary>
        public IReadOnlyList<TrackedProblem> Problems => problems;

        private MapTracker(Map map)
        {
            this.map = map;
            diagnoser = new BillDiagnoser(map);
            constructionDiagnoser = new ConstructionDiagnoser(map, diagnoser.Reach);
            siteDiscovery = new SiteDiscovery(map);
            idleDiagnoser = new IdleDiagnoser(map, this);
            haulDiagnoser = new HaulDiagnoser(map, diagnoser.Reach);
            workerPool = new WorkerPool(map);
            growDiagnoser = new GrowDiagnoser(map, workerPool);
            orderDiagnoser = new OrderDiagnoser(map, workerPool);
        }

        public static MapTracker For(Map map) => map == null ? null : Trackers.GetValue(map, m => new MapTracker(m));

        public static MapTracker Existing(Map map) => map != null && Trackers.TryGetValue(map, out var t) ? t : null;

        internal void Tick()
        {
            if (!map.IsPlayerHome) return;
            long start = Stopwatch.GetTimestamp();
            TickInner();
            double ms = (Stopwatch.GetTimestamp() - start) * TicksToMs;
            AvgTickMs = AvgTickMs * 0.99 + ms * 0.01;
            if (ms > PeakTickMs) PeakTickMs = ms;
        }

        private void TickInner()
        {
            int tick = Find.TickManager.TicksGame;
            int phase = tick % GameTime.TicksPerSample;

            if (phase == 0)
            {
                Discover();
                diagnoser.BeginCycle();
            }

            for (int i = 0; i < problems.Count; i++)
            {
                TrackedProblem t = problems[i];
                if (t.Phase == phase) Sample(t, tick);
            }
        }

        private void Sample(TrackedProblem t, int tick)
        {
            if (t.IsGone) return;
            try
            {
                Core.Diagnosis d = t is TrackedBill bill ? diagnoser.Diagnose(bill, tick)
                    : t is TrackedPawn pawn ? idleDiagnoser.Diagnose(pawn, tick)
                    : t is TrackedHaulGroup haul ? haulDiagnoser.Diagnose(haul, tick)
                    : t is TrackedGrower grower ? growDiagnoser.Diagnose(grower, tick)
                    : t is TrackedOrderGroup order ? orderDiagnoser.Diagnose(order, tick)
                    : constructionDiagnoser.Diagnose((ConstructionSite)t, tick);
                t.History.Record(tick, d);
                t.NoteSample(tick, in d);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Failed to diagnose {t.Kind} '{SafeTitle(t)}': {e}", 0x0C0D0C70 ^ t.GetHashCode());
            }
        }

        private static string SafeTitle(TrackedProblem t)
        {
            try { return t.Title; }
            catch { return "?"; }
        }

        private void Discover()
        {
            bool changed = DiscoverBills();
            try
            {
                changed |= siteDiscovery.Discover(sites, ref nextPhase, removedSites);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Construction site discovery failed: {e}", 0x0C0D0C75);
            }
            changed |= DiscoverPawns();
            try
            {
                changed |= DiscoverHauling();
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Hauling discovery failed: {e}", 0x0C0D0C76);
            }
            changed |= DiscoverGrowers();
            try
            {
                changed |= DiscoverOrders();
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Orders discovery failed: {e}", 0x0C0D0C77);
            }
            if (!changed) return;

            problems.Clear();
            for (int i = 0; i < tracked.Count; i++) problems.Add(tracked[i]);
            for (int i = 0; i < sites.Count; i++) problems.Add(sites[i]);
            for (int i = 0; i < pawns.Count; i++) problems.Add(pawns[i]);
            for (int i = 0; i < haulGroups.Count; i++) problems.Add(haulGroups[i]);
            for (int i = 0; i < growers.Count; i++) problems.Add(growers[i]);
            for (int i = 0; i < orderGroups.Count; i++) problems.Add(orderGroups[i]);
            Version++;
        }

        private bool DiscoverBills()
        {
            discoveryCount++;
            bool changed = false;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is IBillGiver giver) || giver.BillStack == null) continue;
                List<Bill> bills = giver.BillStack.Bills;
                for (int b = 0; b < bills.Count; b++)
                {
                    if (!(bills[b] is Bill_Production bill)) continue;
                    if (!byBill.TryGetValue(bill, out TrackedBill t))
                    {
                        Thing bench = buildings[i];
                        t = new TrackedBill(bill, bench, nextPhase, !BillWorkGivers.IsVanillaBench(bench));
                        nextPhase = (nextPhase + 1) % GameTime.TicksPerSample;
                        byBill[bill] = t;
                        tracked.Add(t);
                        changed = true;
                    }
                    t.SeenInDiscovery = discoveryCount;
                }
            }

            // Spec: Bill lifecycle. Drop bills that were deleted or whose bench is gone or no longer lists them.
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                TrackedBill t = tracked[i];
                if (t.SeenInDiscovery == discoveryCount && !t.IsGone) continue;
                byBill.Remove(t.Bill);
                tracked.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        /// <summary>Spec: Tracked colonists. Free colonists (incl. slaves) on this map who can ever work.</summary>
        private bool DiscoverPawns()
        {
            bool changed = false;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.workSettings == null || !p.workSettings.EverWork) continue;
                if (!byPawn.TryGetValue(p, out TrackedPawn t))
                {
                    t = new TrackedPawn(p, map, nextPhase);
                    nextPhase = (nextPhase + 1) % GameTime.TicksPerSample;
                    byPawn[p] = t;
                    pawns.Add(t);
                    changed = true;
                }
                t.SeenInDiscovery = discoveryCount;
            }
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                TrackedPawn t = pawns[i];
                if (t.SeenInDiscovery == discoveryCount && !t.IsGone) continue;
                byPawn.Remove(t.Pawn);
                pawns.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Spec: Hauling groups. Loose items vanilla lists as needing hauling, grouped by def, with the tick each was
        /// first seen. First-seen ticks are carried into a fresh dictionary each time, which also drops hauled items.
        /// </summary>
        private bool DiscoverHauling()
        {
            bool changed = false;
            int tick = Find.TickManager.TicksGame;
            Dictionary<Thing, int> old = haulDiagnoser.FirstSeen;
            Dictionary<Thing, int> next = spareFirstSeen;
            next.Clear();
            foreach (Thing t in map.listerHaulables.ThingsPotentiallyNeedingHauling())
            {
                if (t == null || !t.Spawned || t.IsInAnyStorage() || t.Position.Fogged(map)) continue;
                if (!byDef.TryGetValue(t.def, out TrackedHaulGroup g))
                {
                    g = new TrackedHaulGroup(t.def, map, nextPhase);
                    nextPhase = (nextPhase + 1) % GameTime.TicksPerSample;
                    byDef[t.def] = g;
                    haulGroups.Add(g);
                    changed = true;
                }
                if (g.SeenInDiscovery != discoveryCount)
                {
                    g.SeenInDiscovery = discoveryCount;
                    g.Items.Clear();
                }
                g.Items.Add(t);
                next[t] = old.TryGetValue(t, out int seen) ? seen : tick;
            }
            haulDiagnoser.FirstSeen = next;
            spareFirstSeen = old;
            old.Clear();

            for (int i = haulGroups.Count - 1; i >= 0; i--)
            {
                TrackedHaulGroup g = haulGroups[i];
                if (g.SeenInDiscovery != discoveryCount) g.Items.Clear();
                if (g.Items.Count > 0) g.EmptySinceTick = -1;
                else if (g.EmptySinceTick < 0) g.EmptySinceTick = tick;
                if (!g.IsGone) continue;
                byDef.Remove(g.Def);
                haulGroups.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        /// <summary>Spec: Growers. Player growing zones and plant grower buildings.</summary>
        private bool DiscoverGrowers()
        {
            bool changed = false;
            List<Zone> zones = map.zoneManager.AllZones;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] is Zone_Growing z) changed |= SeeGrower(z);
            }
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_PlantGrower b) changed |= SeeGrower(b);
            }
            for (int i = growers.Count - 1; i >= 0; i--)
            {
                TrackedGrower t = growers[i];
                if (t.SeenInDiscovery == discoveryCount && !t.IsGone) continue;
                byGrower.Remove(t.Grower);
                growers.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        private bool SeeGrower(IPlantToGrowSettable g)
        {
            bool added = false;
            if (!byGrower.TryGetValue(g, out TrackedGrower t))
            {
                t = new TrackedGrower(g, map, nextPhase);
                nextPhase = (nextPhase + 1) % GameTime.TicksPerSample;
                byGrower[g] = t;
                growers.Add(t);
                added = true;
            }
            t.SeenInDiscovery = discoveryCount;
            return added;
        }

        private static void ResolveOrderKinds()
        {
            orderKinds = new Dictionary<DesignationDef, (DesignationDef, WorkGiverDef)>();
            void Add(string designation, string group, string giver)
            {
                DesignationDef d = DefDatabase<DesignationDef>.GetNamedSilentFail(designation);
                DesignationDef g = DefDatabase<DesignationDef>.GetNamedSilentFail(group);
                WorkGiverDef w = DefDatabase<WorkGiverDef>.GetNamedSilentFail(giver);
                if (d != null && g != null && w?.workType != null) orderKinds[d] = (g, w);
            }
            Add("Mine", "Mine", "Mine");
            Add("MineVein", "Mine", "Mine");
            Add("CutPlant", "CutPlant", "PlantsCut");
            Add("HarvestPlant", "HarvestPlant", "PlantsCut");
            Add("Deconstruct", "Deconstruct", "Deconstruct");
            Add("Uninstall", "Uninstall", "Uninstall");
            Add("SmoothFloor", "SmoothFloor", "ConstructSmoothFloors");
            Add("SmoothWall", "SmoothWall", "ConstructSmoothWalls");
        }

        /// <summary>Spec: Order groups. Designations grouped by order type, with first-seen ticks.</summary>
        private bool DiscoverOrders()
        {
            if (orderKinds == null) ResolveOrderKinds();
            bool changed = false;
            int tick = Find.TickManager.TicksGame;
            Dictionary<Designation, int> old = orderDiagnoser.FirstSeen;
            Dictionary<Designation, int> next = spareOrderSeen;
            next.Clear();
            List<Designation> all = map.designationManager.AllDesignations;
            for (int i = 0; i < all.Count; i++)
            {
                Designation d = all[i];
                if (!orderKinds.TryGetValue(d.def, out var kind)) continue;
                IntVec3 c = TrackedOrderGroup.CellOf(d);
                if (!c.InBounds(map) || c.Fogged(map)) continue;
                if (!byOrderKind.TryGetValue(kind.kind, out TrackedOrderGroup g))
                {
                    g = new TrackedOrderGroup(kind.kind, kind.giver, map, nextPhase);
                    nextPhase = (nextPhase + 1) % GameTime.TicksPerSample;
                    byOrderKind[kind.kind] = g;
                    orderGroups.Add(g);
                    changed = true;
                }
                if (g.SeenInDiscovery != discoveryCount)
                {
                    g.SeenInDiscovery = discoveryCount;
                    g.Items.Clear();
                }
                g.Items.Add(d);
                next[d] = old.TryGetValue(d, out int seen) ? seen : tick;
            }
            orderDiagnoser.FirstSeen = next;
            spareOrderSeen = old;
            old.Clear();

            for (int i = orderGroups.Count - 1; i >= 0; i--)
            {
                TrackedOrderGroup g = orderGroups[i];
                if (g.SeenInDiscovery != discoveryCount) g.Items.Clear();
                if (g.Items.Count > 0) g.EmptySinceTick = -1;
                else if (g.EmptySinceTick < 0) g.EmptySinceTick = tick;
                if (!g.IsGone) continue;
                byOrderKind.Remove(g.Kind0);
                orderGroups.RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        public IReadOnlyList<TrackedOrderGroup> OrderGroups => orderGroups;
        internal WorkerPool WorkerPool => workerPool;
        public IReadOnlyList<TrackedGrower> Growers => growers;
        internal HaulDiagnoser HaulDiagnoser => haulDiagnoser;
        public IReadOnlyList<TrackedHaulGroup> HaulGroups => haulGroups;
        internal IdleDiagnoser IdleDiagnoser => idleDiagnoser;
        internal BillDiagnoser Diagnoser => diagnoser;
        internal ConstructionDiagnoser ConstructionDiagnoser => constructionDiagnoser;

        public TrackedBill FindTracked(Bill bill) => bill != null && byBill.TryGetValue(bill, out var t) ? t : null;
    }
}
