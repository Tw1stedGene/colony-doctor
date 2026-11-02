using ColonyDoctor.Core;

namespace ColonyDoctor.Sampling
{
    public enum ProblemKind : byte
    {
        Bill = 0,
        Construction = 1,
    }

    /// <summary>
    /// Something Colony Doctor watches over time: a bill, a construction site, later idle pawns or hauling. Shares
    /// sampling phase, fixed-size history and the UI contract (title, location, jump, explanations).
    /// </summary>
    public abstract class TrackedProblem
    {
        public readonly BillHistory History = new BillHistory();

        /// <summary>Spec: Competing work tally, over the current continuous stall.</summary>
        public readonly CompetingTally Competing = new CompetingTally();

        /// <summary>Tick offset within the 250-tick cycle at which this problem is sampled.</summary>
        public int Phase { get; internal set; }

        /// <summary>Last tick someone worked on it; samples within the grace window still count as Working.</summary>
        internal int LastWorkedTick = -1;

        protected TrackedProblem(int phase) => Phase = phase;

        public abstract ProblemKind Kind { get; }
        public abstract bool IsGone { get; }

        /// <summary>UI title, e.g. "Make components ×10" or "Granite wall ×14". May allocate; UI only.</summary>
        public abstract string Title { get; }

        /// <summary>UI location, e.g. "Fabrication bench in the workshop". May allocate; UI only.</summary>
        public abstract string Location { get; }

        /// <summary>Optional extra line under the location (e.g. "11 pieces left"), or null.</summary>
        public virtual string Extra => null;

        public abstract void JumpTo();

        /// <summary>Plain-language text for a stored diagnosis. UI only.</summary>
        public abstract string Explain(in Core.Diagnosis d);

        /// <summary>Updates the competing-work tally after a sample (spec: Competing work tally).</summary>
        internal void NoteSample(in Core.Diagnosis d)
        {
            if (d.Reason == Reason.Working) Competing.Reset();
            else if (d.Reason == Reason.Outcompeted && d.SubjectB is Verse.WorkTypeDef wt)
                Competing.Add(wt.index, Verse.DefDatabase<Verse.WorkTypeDef>.DefCount, d.SubjectA);
        }
    }
}
