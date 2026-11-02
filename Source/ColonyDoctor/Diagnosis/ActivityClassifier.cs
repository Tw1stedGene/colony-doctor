using ColonyDoctor.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Spec: Work rank and Worker activity. Mirrors vanilla's work order without touching
    /// <c>Pawn_WorkSettings.WorkGiversInOrderNormal</c> (which may rebuild a cache): only reads the pawn's priority,
    /// the work type's natural priority and the work giver's priority within its type.
    /// </summary>
    public static class ActivityClassifier
    {
        public static long Rank(Pawn pawn, WorkGiverDef giver)
        {
            int priority = pawn.workSettings != null && giver.workType != null ? pawn.workSettings.GetPriority(giver.workType) : 0;
            return Outcompeted.RankKey(giver.emergency, priority, giver.workType?.naturalPriority ?? 0, giver.priorityInType);
        }

        /// <summary>
        /// Classifies what <paramref name="pawn"/> is doing relative to work ranked <paramref name="stalledRank"/>.
        /// <paramref name="competing"/> is the competing work giver when the result is <see cref="Activity.HigherRanked"/>.
        /// </summary>
        public static Activity Classify(Pawn pawn, long stalledRank, out WorkGiverDef competing)
        {
            competing = null;
            Job job = pawn.CurJob;
            if (job == null || job.def.isIdle || job.def == JobDefOf.Wait_Wander || job.def == JobDefOf.GotoWander
                || job.def == JobDefOf.Wait)
            {
                return Activity.Idle;
            }
            WorkGiverDef giver = job.workGiverDef;
            if (giver != null)
            {
                if (Rank(pawn, giver) < stalledRank)
                {
                    competing = giver;
                    return Activity.HigherRanked;
                }
                return Activity.SameOrLower;
            }
            if (IsOffDutyJob(job.def)) return Activity.OffDuty;
            TimeAssignmentDef assignment = pawn.timetable?.CurrentAssignment;
            if (assignment != null && (assignment == TimeAssignmentDefOf.Sleep || assignment == TimeAssignmentDefOf.Joy
                                       || assignment == TimeAssignmentDefOf.Meditate))
            {
                return Activity.OffDuty;
            }
            // A non-work job we can't place (lord duty, mod job giver): never blamed on priorities.
            return Activity.SameOrLower;
        }

        private static bool IsOffDutyJob(JobDef def)
            => def == JobDefOf.LayDown || def == JobDefOf.Ingest || def.joyKind != null || def == JobDefOf.Meditate
               || def == JobDefOf.MeditatePray || def == JobDefOf.Wait_Asleep || def == JobDefOf.Lovin;

        /// <summary>
        /// Runs the Outcompeted decision over <paramref name="workers"/> for work given by <paramref name="giver"/> and
        /// fills <paramref name="d"/>: reason, detail, SubjectA = first competing worker, SubjectB = their competing
        /// work type, SubjectC = the stalled work type, ValueA/B/C = higher / off duty / same-or-lower+idle counts.
        /// </summary>
        public static Core.Diagnosis Decide(System.Collections.Generic.List<Pawn> workers, WorkGiverDef giver)
        {
            int higher = 0, same = 0, off = 0, idle = 0;
            Pawn competitor = null;
            WorkGiverDef competingGiver = null;
            for (int i = 0; i < workers.Count; i++)
            {
                Pawn p = workers[i];
                switch (Classify(p, Rank(p, giver), out WorkGiverDef c))
                {
                    case Activity.HigherRanked:
                        higher++;
                        if (competitor == null)
                        {
                            competitor = p;
                            competingGiver = c;
                        }
                        break;
                    case Activity.SameOrLower: same++; break;
                    case Activity.OffDuty: off++; break;
                    default: idle++; break;
                }
            }
            Reason r = Outcompeted.Decide(higher, same, off, idle, out Detail detail);
            var d = Core.Diagnosis.Of(r, detail);
            d.SubjectA = r == Reason.Outcompeted ? competitor : workers.Count > 0 ? workers[0] : null;
            d.SubjectB = r == Reason.Outcompeted ? competingGiver?.workType : null;
            d.SubjectC = giver.workType;
            d.ValueA = higher;
            d.ValueB = off;
            d.ValueC = same + idle;
            return d;
        }
    }
}
