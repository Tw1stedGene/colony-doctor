using System;

namespace ColonyDoctor.Fixes
{
    /// <summary>
    /// One offered fix (spec: problem-fixes). Built when the detail panel refreshes; nothing changes until the player
    /// confirms. <see cref="Apply"/> captures the previous values and returns the matching undo; both go through
    /// <see cref="FixActions"/> only.
    /// </summary>
    public sealed class Fix
    {
        public string Button;      // "Widen radius"
        public string Change;      // "Ingredient radius 30 → 45"
        public string SideEffect;  // "Workers may walk further for ingredients."
        public Func<bool> StillNeeded;
        public Func<Action> Apply;
    }
}
