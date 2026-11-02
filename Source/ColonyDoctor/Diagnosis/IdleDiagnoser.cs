using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>Spec: Idle sample and Idle detail. One instance per map; no allocation per sample.</summary>
    public sealed class IdleDiagnoser
    {
        private readonly WorkPresence presence;
        private readonly MapTracker tracker;

        public IdleDiagnoser(Map map, MapTracker tracker)
        {
            presence = new WorkPresence(map);
            this.tracker = tracker;
        }

        public WorkPresence Presence => presence;

        public Core.Diagnosis Diagnose(TrackedPawn tracked, int tick)
        {
            Pawn p = tracked.Pawn;
            if (p.Downed || p.Drafted || p.InMentalState || HealthAIUtility.ShouldSeekMedicalRest(p)) return Core.Diagnosis.Of(Reason.Suspended);
            Activity a = ActivityClassifier.Classify(p, long.MaxValue, out _);
            if (a == Activity.OffDuty) return Core.Diagnosis.Of(Reason.Suspended);
            if (a != Activity.Idle) return Core.Diagnosis.Of(Reason.Working);

            presence.EnsureBuilt(tracker, tick);
            int active = 0, open = 0, blocked = 0, unknown = 0;
            WorkTypeDef firstOpen = null, firstUnknown = null;
            TrackedProblem worst = null;
            int worstStall = -1;
            List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                WorkTypeDef w = all[i];
                if (p.workSettings == null || p.WorkTypeIsDisabled(w) || !p.workSettings.WorkIsActive(w)) continue;
                active++;
                switch (presence.Of(w))
                {
                    case Core.Presence.Open:
                        open++;
                        if (firstOpen == null) firstOpen = w;
                        break;
                    case Core.Presence.Blocked:
                        blocked++;
                        TrackedProblem b = presence.LongestBlocked(w);
                        if (b != null && presence.LongestStall(w) > worstStall)
                        {
                            worst = b;
                            worstStall = presence.LongestStall(w);
                        }
                        break;
                    case Core.Presence.Unknown:
                        unknown++;
                        if (firstUnknown == null && w.visible) firstUnknown = w;
                        break;
                }
            }
            Detail detail = IdleReason.Pick(active, open, blocked, unknown);
            var d = Core.Diagnosis.Of(Reason.Idle, detail);
            d.SubjectA = worst;
            d.SubjectB = firstOpen;
            d.SubjectC = firstUnknown;
            d.ValueA = open;
            d.ValueB = unknown;
            d.ValueC = blocked;
            return d;
        }
    }
}
