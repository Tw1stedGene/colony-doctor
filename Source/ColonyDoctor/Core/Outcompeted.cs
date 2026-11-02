namespace ColonyDoctor.Core
{
    /// <summary>What an eligible worker was doing during a sample that would otherwise be Waiting for a worker.</summary>
    public enum Activity : byte
    {
        HigherRanked = 0,
        SameOrLower = 1,
        OffDuty = 2,
        Idle = 3,
    }

    /// <summary>Pure rules of the outcompeted-diagnosis spec (Work rank, Outcompeted decision).</summary>
    public static class Outcompeted
    {
        /// <summary>
        /// Comparable work-order key; lower is done first. Mirrors vanilla's order: emergency work givers first, then
        /// the pawn's priority number (1 first), then the work type's natural priority (higher first), then the work
        /// giver's priority within its type (higher first).
        /// </summary>
        public static long RankKey(bool emergency, int priority, int naturalPriority, int priorityInType)
        {
            long e = emergency ? 0 : 1;
            long p = priority <= 0 ? 255 : priority > 255 ? 255 : priority; // 0 = disabled: last
            long n = 65535 - Clamp(naturalPriority, 0, 65535);
            long g = 65535 - Clamp(priorityInType, 0, 65535);
            return (e << 48) | (p << 32) | (n << 16) | g;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// Spec: Outcompeted decision. Outcompeted when at least one eligible worker is on higher-ranked work and all
        /// others are on higher-ranked work or off duty. Otherwise Waiting for a worker: Off duty when everyone is off
        /// duty, else Same or lower rank. (Bench busy is decided before this.)
        /// </summary>
        public static Reason Decide(int higher, int sameOrLower, int offDuty, int idle, out Detail detail)
        {
            if (higher > 0 && sameOrLower == 0 && idle == 0)
            {
                detail = Detail.None;
                return Reason.Outcompeted;
            }
            detail = higher == 0 && sameOrLower == 0 && idle == 0 && offDuty > 0 ? Detail.OffDuty : Detail.SameOrLowerRank;
            return Reason.WaitingForWorker;
        }
    }
}
