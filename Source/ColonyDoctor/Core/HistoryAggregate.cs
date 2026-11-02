namespace ColonyDoctor.Core
{
    /// <summary>
    /// Reusable result of <see cref="BillHistory.Aggregate"/>: stalled %, share per reason, dominant reason and a timeline
    /// (spec: Aggregates). Allocated once by the caller and reused.
    /// </summary>
    public sealed class HistoryAggregate
    {
        private const int MaxSlices = 24;

        private readonly int[] counts = new int[Reasons.Count];
        private readonly int[] sliceCounts = new int[MaxSlices * Reasons.Count];
        private readonly byte[] timeline = new byte[MaxSlices];

        public TimeRange Range { get; private set; }
        public int SliceCount { get; private set; }

        /// <summary>Samples that were not suspended.</summary>
        public int ActiveSamples { get; private set; }
        public int StalledSamples { get; private set; }

        /// <summary>0–100, or -1 when there is no active sample in the range.</summary>
        public int StalledPercent { get; private set; }

        /// <summary>The most frequent stalled reason, or null when never stalled.</summary>
        public Reason? Dominant { get; private set; }

        public bool HasData => ActiveSamples > 0;

        public int Count(Reason reason) => counts[(int)reason];

        /// <summary>Share of active samples, 0–100, rounded.</summary>
        public int Percent(Reason reason)
            => ActiveSamples == 0 || reason == Reason.Suspended ? 0 : (counts[(int)reason] * 100 + ActiveSamples / 2) / ActiveSamples;

        /// <summary>Dominant reason in a timeline slice (oldest first), or <see cref="Reasons.NoData"/>.</summary>
        public byte Slice(int index) => timeline[index];

        internal void Reset(TimeRange range)
        {
            Range = range;
            SliceCount = GameTime.Slices(range);
            ActiveSamples = 0;
            StalledSamples = 0;
            StalledPercent = -1;
            Dominant = null;
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
            for (int i = 0; i < sliceCounts.Length; i++) sliceCounts[i] = 0;
            for (int i = 0; i < timeline.Length; i++) timeline[i] = Reasons.NoData;
        }

        internal void Add(int slice, Reason reason, int n)
        {
            counts[(int)reason] += n;
            sliceCounts[slice * Reasons.Count + (int)reason] += n;
        }

        internal void Finish()
        {
            int best = 0;
            for (int r = 0; r < Reasons.Count; r++)
            {
                var reason = (Reason)r;
                int c = counts[r];
                if (reason == Reason.Suspended) continue;
                ActiveSamples += c;
                if (!Reasons.IsStalled(reason)) continue;
                StalledSamples += c;
                if (c > best)
                {
                    best = c;
                    Dominant = reason;
                }
            }
            if (ActiveSamples > 0) StalledPercent = (StalledSamples * 100 + ActiveSamples / 2) / ActiveSamples;

            for (int s = 0; s < SliceCount; s++)
            {
                int top = 0;
                byte topReason = Reasons.NoData;
                for (int r = 0; r < Reasons.Count; r++)
                {
                    int c = sliceCounts[s * Reasons.Count + r];
                    if (c > top)
                    {
                        top = c;
                        topReason = (byte)r;
                    }
                }
                timeline[s] = topReason;
            }
        }
    }
}
