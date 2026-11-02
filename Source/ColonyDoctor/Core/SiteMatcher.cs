using System.Collections.Generic;

namespace ColonyDoctor.Core
{
    /// <summary>
    /// Carries construction-site identity across discovery cycles (spec: Site identity over time). Sites are sets of
    /// cell keys; a blueprint turning into a frame keeps its cells, so cells (not things) are the identity.
    /// New sites are processed largest first; each takes the old site it shares the most cells with, if that old site
    /// hasn't been claimed by a larger new site. Merges therefore keep the larger part's history, and on a split the
    /// largest part keeps it while the others start fresh.
    /// </summary>
    public sealed class SiteMatcher
    {
        private readonly List<int> order = new List<int>();
        private readonly Dictionary<int, int> overlap = new Dictionary<int, int>();
        private readonly HashSet<int> claimed = new HashSet<int>();

        /// <param name="newSites">Cell keys of each new site.</param>
        /// <param name="oldOwner">Cell key → index of the old site that covered it last cycle.</param>
        /// <param name="result">Filled with, per new site, the matched old index or -1. Resized as needed.</param>
        public void Match(IReadOnlyList<List<int>> newSites, Dictionary<int, int> oldOwner, List<int> result)
        {
            result.Clear();
            order.Clear();
            claimed.Clear();
            for (int i = 0; i < newSites.Count; i++)
            {
                result.Add(-1);
                order.Add(i);
            }
            // Largest first; index as tie-break keeps it deterministic.
            order.Sort((a, b) =>
            {
                int c = newSites[b].Count.CompareTo(newSites[a].Count);
                return c != 0 ? c : a.CompareTo(b);
            });

            for (int o = 0; o < order.Count; o++)
            {
                int ni = order[o];
                overlap.Clear();
                List<int> cells = newSites[ni];
                for (int c = 0; c < cells.Count; c++)
                {
                    if (oldOwner.TryGetValue(cells[c], out int old))
                    {
                        overlap.TryGetValue(old, out int n);
                        overlap[old] = n + 1;
                    }
                }

                int best = -1, bestN = 0;
                foreach (var kv in overlap)
                {
                    if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best))
                    {
                        best = kv.Key;
                        bestN = kv.Value;
                    }
                }
                if (best >= 0 && claimed.Add(best)) result[ni] = best;
            }
        }
    }
}
