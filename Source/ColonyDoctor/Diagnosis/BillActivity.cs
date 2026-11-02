using RimWorld;
using Verse;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Read-only mirror of <c>Bill_Production.ShouldDoNow</c>. Vanilla's version writes <c>paused</c> as a side effect,
    /// so we cannot call it from diagnosis (spec: Read-only diagnosis).
    /// </summary>
    public static class BillActivity
    {
        public static bool IsActive(Bill bill)
        {
            if (bill.suspended) return false;
            if (!(bill is Bill_Production bp)) return false;

            BillRepeatModeDef mode = bp.repeatMode;
            if (mode == BillRepeatModeDefOf.Forever) return true;
            if (mode == BillRepeatModeDefOf.RepeatCount) return bp.repeatCount > 0;
            if (mode == BillRepeatModeDefOf.TargetCount)
            {
                int count = bp.recipe.WorkerCounter.CountProducts(bp);
                bool paused = bp.paused;
                if (bp.pauseWhenSatisfied && count >= bp.targetCount) paused = true;
                if (count <= bp.unpauseWhenYouHave || !bp.pauseWhenSatisfied) paused = false;
                return !paused && count < bp.targetCount;
            }
            // Modded repeat mode: we can't evaluate it without side effects; assume it wants work.
            return !bp.paused;
        }
    }
}
