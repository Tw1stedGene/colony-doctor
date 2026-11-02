namespace ColonyDoctor.Core
{
    /// <summary>
    /// Spec: Competing work tally. Over the current continuous stall, how many Outcompeted samples went to each
    /// competing work type, and which worker competed most often. Fixed memory: one counter per work type (allocated
    /// once, on first use) and a small competitor table. No allocation per sample.
    /// </summary>
    public sealed class CompetingTally
    {
        public const int CompetitorSlots = 8;

        private int[] byWorkType;
        private readonly object[] competitors = new object[CompetitorSlots];
        private readonly int[] competitorCounts = new int[CompetitorSlots];

        public int Total { get; private set; }

        public void Reset()
        {
            if (Total == 0) return;
            Total = 0;
            if (byWorkType != null) System.Array.Clear(byWorkType, 0, byWorkType.Length);
            for (int i = 0; i < CompetitorSlots; i++)
            {
                competitors[i] = null;
                competitorCounts[i] = 0;
            }
        }

        /// <param name="workTypeIndex">Index of the competing work type (0..workTypeCount-1).</param>
        /// <param name="workTypeCount">Number of work types; sizes the counters on first use.</param>
        /// <param name="competitor">The competing worker (compared by reference), or null.</param>
        public void Add(int workTypeIndex, int workTypeCount, object competitor)
        {
            if (byWorkType == null || byWorkType.Length < workTypeCount) Grow(workTypeCount);
            if (workTypeIndex < 0 || workTypeIndex >= byWorkType.Length) return;
            byWorkType[workTypeIndex]++;
            Total++;
            if (competitor != null) AddCompetitor(competitor);
        }

        private void Grow(int count)
        {
            var next = new int[count];
            if (byWorkType != null) System.Array.Copy(byWorkType, next, byWorkType.Length);
            byWorkType = next;
        }

        private void AddCompetitor(object who)
        {
            int free = -1, lowest = 0;
            for (int i = 0; i < CompetitorSlots; i++)
            {
                if (ReferenceEquals(competitors[i], who))
                {
                    competitorCounts[i]++;
                    return;
                }
                if (competitors[i] == null) { if (free < 0) free = i; }
                else if (competitorCounts[i] < competitorCounts[lowest] || competitors[lowest] == null) lowest = i;
            }
            int slot = free >= 0 ? free : lowest; // full: evict the least frequent
            competitors[slot] = who;
            competitorCounts[slot] = 1;
        }

        public int CountFor(int workTypeIndex)
            => byWorkType != null && workTypeIndex >= 0 && workTypeIndex < byWorkType.Length ? byWorkType[workTypeIndex] : 0;

        /// <summary>Writes the indices of the top work types (most samples first, ties by lower index) into
        /// <paramref name="result"/>; returns how many were written.</summary>
        public int TopWorkTypes(int[] result)
        {
            if (byWorkType == null) return 0;
            int n = 0;
            for (; n < result.Length; n++)
            {
                int best = -1;
                for (int i = 0; i < byWorkType.Length; i++)
                {
                    if (byWorkType[i] == 0 || Taken(result, n, i)) continue;
                    if (best < 0 || byWorkType[i] > byWorkType[best]) best = i;
                }
                if (best < 0) break;
                result[n] = best;
            }
            return n;
        }

        private static bool Taken(int[] result, int n, int index)
        {
            for (int j = 0; j < n; j++) if (result[j] == index) return true;
            return false;
        }

        /// <summary>The worker who competed most often, or null.</summary>
        public object TopCompetitor
        {
            get
            {
                int best = -1;
                for (int i = 0; i < CompetitorSlots; i++)
                {
                    if (competitors[i] != null && (best < 0 || competitorCounts[i] > competitorCounts[best])) best = i;
                }
                return best < 0 ? null : competitors[best];
            }
        }
    }
}
