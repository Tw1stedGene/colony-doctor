using System;

namespace ColonyDoctor.Core
{
    /// <summary>
    /// Fixed-size history of one tracked problem (spec: Fixed memory). Holds:
    /// - the last hour of raw samples (one per 250 ticks) for the hour timeline,
    /// - per-hour reason counts for a quadrum (360 hours), aligned to absolute game hours,
    /// - the latest diagnosis per reason, used to build explanations,
    /// - when the current continuous stall started.
    /// All arrays are allocated once; <see cref="Record"/> never allocates.
    /// </summary>
    public sealed class BillHistory
    {
        public const int HoursKept = GameTime.HoursPerDay * GameTime.DaysPerQuadrum; // 360

        private readonly byte[] recentReasons = new byte[GameTime.SamplesPerHour];
        private readonly int[] recentTicks = new int[GameTime.SamplesPerHour];

        private readonly ushort[] hourCounts = new ushort[HoursKept * Reasons.Count];
        private readonly int[] hourIds = new int[HoursKept];

        private readonly Diagnosis[] lastByReason = new Diagnosis[Reasons.Count];

        public int StallStartTick { get; private set; } = -1;
        public int LastSampleTick { get; private set; } = -1;
        public Reason LastReason { get; private set; } = Reason.Working;

        public BillHistory()
        {
            for (int i = 0; i < recentTicks.Length; i++) recentTicks[i] = -1;
            for (int i = 0; i < hourIds.Length; i++) hourIds[i] = -1;
        }

        public bool HasData => LastSampleTick >= 0;
        public bool IsCurrentlyStalled => HasData && Reasons.IsStalled(LastReason);

        public int ContinuousStallTicks(int nowTick) => StallStartTick < 0 ? 0 : Math.Max(0, nowTick - StallStartTick);

        /// <summary>The most recent diagnosis recorded for <paramref name="reason"/> (default if never seen).</summary>
        public ref readonly Diagnosis LastFor(Reason reason) => ref lastByReason[(int)reason];

        public void Record(int tick, in Diagnosis diagnosis)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            Reason reason = diagnosis.Reason;

            int slot = (tick / GameTime.TicksPerSample) % recentReasons.Length;
            recentReasons[slot] = (byte)reason;
            recentTicks[slot] = tick;

            int hour = tick / GameTime.TicksPerHour;
            int hourSlot = hour % HoursKept;
            int baseIndex = hourSlot * Reasons.Count;
            if (hourIds[hourSlot] != hour)
            {
                hourIds[hourSlot] = hour;
                for (int i = 0; i < Reasons.Count; i++) hourCounts[baseIndex + i] = 0;
            }
            if (hourCounts[baseIndex + (int)reason] < ushort.MaxValue) hourCounts[baseIndex + (int)reason]++;

            lastByReason[(int)reason] = diagnosis;
            lastByReason[(int)reason].Tick = tick;

            if (Reasons.IsStalled(reason))
            {
                if (StallStartTick < 0) StallStartTick = tick;
            }
            else
            {
                StallStartTick = -1;
            }

            LastReason = reason;
            LastSampleTick = tick;
        }

        /// <summary>
        /// Fills <paramref name="result"/> for the given range ending at <paramref name="nowTick"/>.
        /// Does not allocate: <paramref name="result"/> is reused by the caller.
        /// </summary>
        public void Aggregate(int nowTick, TimeRange range, HistoryAggregate result)
        {
            result.Reset(range);
            switch (range)
            {
                case TimeRange.Hour: AggregateHour(nowTick, result); break;
                case TimeRange.Day: AggregateHours(nowTick, result, 1); break;
                default: AggregateHours(nowTick, result, GameTime.HoursPerDay); break;
            }
            result.Finish();
        }

        private void AggregateHour(int nowTick, HistoryAggregate result)
        {
            int windowStart = nowTick - GameTime.TicksPerHour;
            for (int i = 0; i < recentReasons.Length; i++)
            {
                int t = recentTicks[i];
                if (t < 0 || t <= windowStart || t > nowTick) continue;
                int slice = (t - windowStart - 1) / GameTime.TicksPerSample;
                if (slice < 0) slice = 0;
                if (slice >= result.SliceCount) slice = result.SliceCount - 1;
                result.Add(slice, (Reason)recentReasons[i], 1);
            }
        }

        /// <summary>
        /// Sums hour buckets into <see cref="HistoryAggregate.SliceCount"/> slices of <paramref name="hoursPerSlice"/>
        /// hours each, aligned to absolute hours/days, ending with the slice that contains the current hour.
        /// </summary>
        private void AggregateHours(int nowTick, HistoryAggregate result, int hoursPerSlice)
        {
            int nowHour = nowTick / GameTime.TicksPerHour;
            int firstSlice = nowHour / hoursPerSlice - (result.SliceCount - 1);
            int firstHour = Math.Max(0, firstSlice * hoursPerSlice);

            for (int hour = firstHour; hour <= nowHour; hour++)
            {
                int hourSlot = hour % HoursKept;
                if (hourIds[hourSlot] != hour) continue;
                int slice = hour / hoursPerSlice - firstSlice;
                int baseIndex = hourSlot * Reasons.Count;
                for (int r = 0; r < Reasons.Count; r++)
                {
                    int c = hourCounts[baseIndex + r];
                    if (c > 0) result.Add(slice, (Reason)r, c);
                }
            }
        }
    }
}
