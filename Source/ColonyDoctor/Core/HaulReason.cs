namespace ColonyDoctor.Core
{
    /// <summary>Spec: Waiting items, Group reason, Rotting flag. Pure rules for hauling groups.</summary>
    public static class HaulReason
    {
        /// <summary>An item counts as waiting after one in-game hour (spec: Waiting items).</summary>
        public const int WaitingAfterTicks = 2500;

        /// <summary>Perishables rotting within a day are flagged (spec: Rotting flag).</summary>
        public const int RotWithinTicks = 60000;

        /// <summary>Empty groups are kept this long so history survives brief gaps (spec: Hauling groups).</summary>
        public const int KeepEmptyTicks = 60000;

        public static readonly Reason[] Precedence =
        {
            Reason.NoStorage, Reason.NoEligibleWorker, Reason.Outcompeted, Reason.WaitingForWorker, Reason.Unknown,
        };

        public static bool IsWaiting(int firstSeenTick, int now) => now - firstSeenTick >= WaitingAfterTicks;

        public static bool IsRotting(bool fresh, int ticksUntilRot) => fresh && ticksUntilRot < RotWithinTicks;

        /// <summary>Cause with the most items, ties by precedence; Working when no cause has an item.</summary>
        public static Reason Majority(int[] counts)
        {
            Reason best = Reason.Working;
            int bestCount = 0;
            for (int i = 0; i < Precedence.Length; i++)
            {
                int c = counts[(int)Precedence[i]];
                if (c > bestCount)
                {
                    best = Precedence[i];
                    bestCount = c;
                }
            }
            return best;
        }
    }
}
