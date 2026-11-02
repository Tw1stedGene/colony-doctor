namespace ColonyDoctor.Core
{
    public enum TimeRange : byte
    {
        Hour = 0,
        Day = 1,
        Quadrum = 2,
    }

    public static class GameTime
    {
        public const int TicksPerSample = 250;
        public const int TicksPerHour = 2500;
        public const int HoursPerDay = 24;
        public const int TicksPerDay = TicksPerHour * HoursPerDay;
        public const int DaysPerQuadrum = 15;
        public const int SamplesPerHour = TicksPerHour / TicksPerSample;

        /// <summary>Number of timeline slices for a range (spec: Aggregates).</summary>
        public static int Slices(TimeRange range)
        {
            switch (range)
            {
                case TimeRange.Hour: return SamplesPerHour;
                case TimeRange.Day: return HoursPerDay;
                default: return DaysPerQuadrum;
            }
        }
    }
}
