using System.Collections.Generic;
using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Spec: Area restriction detail. Which available workers may enter a cell under their allowed area, and which
    /// restricted worker to name when none may. Read-only.
    /// </summary>
    public static class AreaGate
    {
        public static bool Allowed(Pawn p, IntVec3 c) => ForbidUtility.InAllowedArea(c, p);

        /// <summary>True when at least one of <paramref name="pawns"/> may enter <paramref name="c"/>.</summary>
        public static bool AnyAllowed(List<Pawn> pawns, IntVec3 c)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                if (Allowed(pawns[i], c)) return true;
            }
            return false;
        }

        /// <summary>Copies the pawns allowed at <paramref name="c"/> into <paramref name="into"/> (cleared first).</summary>
        public static void FilterAllowed(List<Pawn> pawns, IntVec3 c, List<Pawn> into)
        {
            into.Clear();
            for (int i = 0; i < pawns.Count; i++)
            {
                if (Allowed(pawns[i], c)) into.Add(pawns[i]);
            }
        }

        /// <summary>
        /// No eligible worker / Area restriction: SubjectA = the restricted pawn with the best priority for the work
        /// type, SubjectB = their area, SubjectC = the work type, ValueC = how many available pawns are restricted.
        /// </summary>
        public static Core.Diagnosis Diagnosis(List<Pawn> pawns, WorkTypeDef work)
        {
            Pawn best = null;
            int bestPriority = int.MaxValue;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                int priority = p.workSettings?.GetPriority(work) ?? 0;
                if (priority <= 0) priority = 5;
                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    best = p;
                }
            }
            var d = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.AreaRestriction);
            d.SubjectA = best;
            d.SubjectB = best?.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;
            d.SubjectC = work;
            d.ValueC = pawns.Count;
            return d;
        }
    }
}
