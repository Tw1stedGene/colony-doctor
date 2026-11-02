namespace ColonyDoctor.Core
{
    /// <summary>Pure rules behind the one-click fixes (spec: problem-fixes).</summary>
    public static class FixMath
    {
        public const int MaxRadius = 999;

        /// <summary>Radius that covers an ingredient <paramref name="distance"/> tiles away: rounded up plus 2, capped.</summary>
        public static int RadiusFor(float distance)
        {
            int r = (int)System.Math.Ceiling(distance) + 2;
            return r > MaxRadius ? MaxRadius : r;
        }

        /// <summary>Widens [min, max] just enough to include <paramref name="level"/>, keeping the other bound.</summary>
        public static void WidenSkillRange(ref int min, ref int max, int level)
        {
            if (level < min) min = level;
            else if (level > max) max = level;
        }

        /// <summary>
        /// How many stacks (already sorted nearest first, with <paramref name="values"/> per stack) to take to cover
        /// <paramref name="need"/>. All of them when they don't cover it.
        /// </summary>
        public static int StacksToCover(float[] values, int count, float need)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += values[i];
                if (sum >= need) return i + 1;
            }
            return count;
        }

        /// <summary>
        /// Spec: Work priority fix. New priority for the stalled work type so it ranks above the competing work type, or
        /// 0 when no change of the stalled work type alone can do that. Priorities are 1 (first) to 4; 0 = disabled.
        /// With equal numbers vanilla orders by natural priority, so equal is enough only when the stalled work type
        /// has the higher natural priority.
        /// </summary>
        public static int PriorityToBeat(int stalled, int competing, bool stalledFirstAtEqual)
        {
            if (stalled <= 0 || competing <= 0) return 0;
            int target = stalledFirstAtEqual ? competing : competing - 1;
            if (target < 1) return 0;
            return target < stalled ? target : 0;
        }
    }
}
