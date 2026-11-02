using System.Collections.Generic;
using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.Construction
{
    /// <summary>
    /// Finds construction pieces, groups touching ones into sites by flood fill, and carries site identity across cycles
    /// (spec: Tracked pieces, Construction sites, Site identity over time). Runs every 250 ticks; reuses its collections.
    /// </summary>
    public sealed class SiteDiscovery
    {
        private readonly Map map;
        private readonly Dictionary<int, Thing> cellToPiece = new Dictionary<int, Thing>(512);
        private readonly HashSet<Thing> visited = new HashSet<Thing>();
        private readonly Stack<Thing> stack = new Stack<Thing>();
        private readonly List<Thing> allPieces = new List<Thing>(256);
        private readonly List<List<Thing>> groups = new List<List<Thing>>();
        private readonly List<List<int>> groupCells = new List<List<int>>();
        private readonly Dictionary<int, int> oldOwner = new Dictionary<int, int>(512);
        private readonly List<int> match = new List<int>();
        private readonly SiteMatcher matcher = new SiteMatcher();
        private readonly List<ConstructionSite> next = new List<ConstructionSite>();
        private readonly List<List<int>> liveCells = new List<List<int>>();
        private readonly List<bool> kept = new List<bool>();
        private int groupCount;

        public SiteDiscovery(Map map) => this.map = map;

        /// <summary>
        /// Rebuilds <paramref name="sites"/> in place. New sites get phases from <paramref name="nextPhase"/>.
        /// Returns true when sites were added or removed.
        /// </summary>
        public bool Discover(List<ConstructionSite> sites, ref int nextPhase, List<ConstructionSite> removed)
        {
            removed.Clear();
            CollectPieces();
            Group();

            oldOwner.Clear();
            for (int s = 0; s < sites.Count; s++)
            {
                List<int> cells = sites[s].CellKeys;
                for (int c = 0; c < cells.Count; c++) oldOwner[cells[c]] = s;
            }

            // The matcher wants exactly the live groups (the pools may hold spare lists).
            liveCells.Clear();
            for (int g = 0; g < groupCount; g++) liveCells.Add(groupCells[g]);
            matcher.Match(liveCells, oldOwner, match);

            bool changed = false;
            next.Clear();
            kept.Clear();
            for (int s = 0; s < sites.Count; s++) kept.Add(false);
            for (int g = 0; g < groupCount; g++)
            {
                ConstructionSite site;
                if (match[g] >= 0)
                {
                    site = sites[match[g]];
                    kept[match[g]] = true;
                }
                else
                {
                    site = new ConstructionSite(map, nextPhase);
                    nextPhase = (nextPhase + 1) % GameTime.TicksPerSample;
                    changed = true;
                }
                site.SetPieces(groups[g], groupCells[g]);
                next.Add(site);
            }
            for (int s = 0; s < sites.Count; s++)
            {
                if (kept[s]) continue;
                removed.Add(sites[s]);
                changed = true;
            }
            sites.Clear();
            sites.AddRange(next);
            return changed;
        }

        private void CollectPieces()
        {
            allPieces.Clear();
            Add(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
            Add(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));

            cellToPiece.Clear();
            CellIndices idx = map.cellIndices;
            for (int i = 0; i < allPieces.Count; i++)
            {
                Thing t = allPieces[i];
                foreach (IntVec3 c in t.OccupiedRect())
                {
                    if (c.InBounds(map)) cellToPiece[idx.CellToIndex(c)] = t;
                }
            }
        }

        private void Add(List<Thing> things)
        {
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                // Spec: build pieces only. Player blueprints (build, install, reinstall) and frames.
                if (t.Spawned && t.Faction == Faction.OfPlayer && t is IConstructible) allPieces.Add(t);
            }
        }

        private void Group()
        {
            visited.Clear();
            groupCount = 0;
            CellIndices idx = map.cellIndices;
            for (int i = 0; i < allPieces.Count; i++)
            {
                Thing seed = allPieces[i];
                if (!visited.Add(seed)) continue;

                List<Thing> group = Pooled(groups, groupCount);
                List<int> cells = Pooled(groupCells, groupCount);
                groupCount++;

                stack.Clear();
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    Thing t = stack.Pop();
                    group.Add(t);
                    foreach (IntVec3 c in t.OccupiedRect())
                    {
                        if (!c.InBounds(map)) continue;
                        cells.Add(idx.CellToIndex(c));
                        // 8 neighbours: diagonal touch counts (spec: Construction sites).
                        for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dz == 0) continue;
                            var n = new IntVec3(c.x + dx, 0, c.z + dz);
                            if (!n.InBounds(map)) continue;
                            if (cellToPiece.TryGetValue(idx.CellToIndex(n), out Thing other) && visited.Add(other)) stack.Push(other);
                        }
                    }
                }
            }
        }

        private static List<T> Pooled<T>(List<List<T>> pool, int index)
        {
            if (index >= pool.Count) pool.Add(new List<T>());
            List<T> l = pool[index];
            l.Clear();
            return l;
        }
    }
}
