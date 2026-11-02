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
                Core.Diagnosis d = t is TrackedBill bill
                    ? diagnoser.Diagnose(bill, tick)
                    : constructionDiagnoser.Diagnose((ConstructionSite)t, tick);
                t.History.Record(tick, d);
                t.NoteSample(in d);
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
            if (!changed) return;

            problems.Clear();
            for (int i = 0; i < tracked.Count; i++) problems.Add(tracked[i]);
            for (int i = 0; i < sites.Count; i++) problems.Add(sites[i]);
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

        internal BillDiagnoser Diagnoser => diagnoser;
        internal ConstructionDiagnoser ConstructionDiagnoser => constructionDiagnoser;

        public TrackedBill FindTracked(Bill bill) => bill != null && byBill.TryGetValue(bill, out var t) ? t : null;
    }
}
