namespace ColonyDoctor.Core
{
    /// <summary>
    /// A construction site's headline reason (spec: Site reason): the reason that applies to the most pieces, ties
    /// broken by piece precedence (No eligible worker, Blocked, Materials missing, Waiting for a worker, Unknown).
    /// </summary>
    public static class SiteReason
    {
        public static readonly Reason[] PiecePrecedence =
        {
            Reason.NoEligibleWorker, Reason.Blocked, Reason.MaterialsMissing, Reason.WaitingForWorker, Reason.Unknown,
        };

        /// <summary>
        /// <paramref name="pieceCounts"/> is indexed by <see cref="Reason"/>. Returns <see cref="Reason.Working"/> when
        /// no stalled reason has any piece.
        /// </summary>
        public static Reason Majority(int[] pieceCounts)
        {
            Reason best = Reason.Working;
            int bestCount = 0;
            for (int i = 0; i < PiecePrecedence.Length; i++)
            {
                Reason r = PiecePrecedence[i];
                int c = pieceCounts[(int)r];
                if (c > bestCount) // strictly greater: earlier precedence wins ties
                {
                    best = r;
                    bestCount = c;
                }
            }
            return best;
        }
    }
}
