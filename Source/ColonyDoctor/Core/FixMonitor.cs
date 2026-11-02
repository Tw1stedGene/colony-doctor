namespace ColonyDoctor.Core
{
    public enum FixState : byte { None, Monitoring, Fixed }

    /// <summary>Spec: Monitoring. "Monitoring" until a Working sample after the fix, then "Fixed" for one in-game hour;
    /// monitoring gives up after one in-game day.</summary>
    public static class FixMonitor
    {
        public const int FixedShownTicks = 2500;
        public const int MonitoringTicks = 60000;

        public static FixState StateAt(int fixedAtTick, int workedAfterFixTick, int now)
        {
            if (fixedAtTick < 0) return FixState.None;
            if (workedAfterFixTick >= 0) return now - workedAfterFixTick <= FixedShownTicks ? FixState.Fixed : FixState.None;
            return now - fixedAtTick <= MonitoringTicks ? FixState.Monitoring : FixState.None;
        }
    }
}
