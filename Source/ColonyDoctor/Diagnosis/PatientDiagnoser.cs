using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>Spec: patient-diagnosis. One instance per map; read-only.</summary>
    public sealed class PatientDiagnoser
    {
        public const int NeedGrace = 1000;
        private const int WorkedGrace = 500;

        private readonly Map map;
        private readonly WorkerPool pool;
        private readonly ReachCache reach;
        private readonly HashSet<Pawn> beingTended = new HashSet<Pawn>();
        private readonly List<Pawn> doctors = new List<Pawn>(16);
        private int tendedCycle = -1;
        private static WorkGiverDef emergency, humanlike;

        public PatientDiagnoser(Map map, WorkerPool pool, ReachCache reach)
        {
            this.map = map;
            this.pool = pool;
            this.reach = reach;
        }

/// <summary>
        /// Vanilla's <see cref="HealthAIUtility.ShouldBeTendedNowByPlayer"/>, minus its "medical care is No care" check:
        /// those patients are tracked so the row can say that care is switched off (detail MedicalCareOff).
        /// Prisoners set for execution and animals marked for slaughter are left out, as in vanilla.
        /// </summary>
        public static bool NeedsTending(Pawn p)
        {
            if (p.playerSettings == null || p.health == null) return false;
            if (p.guest != null && p.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Execution)) return false;
            if (PawnUtility.ShouldBeSlaughtered(p)) return false;
            return p.health.HasHediffsNeedingTendByPlayer(false);
        }

        public Core.Diagnosis Diagnose(TrackedPatient t, int tick)
        {
            Pawn p = t.Pawn;
            if (!NeedsTending(p))
            {
                t.NeedSince = -1;
                if (t.FineSince < 0) t.FineSince = tick;
                return Core.Diagnosis.Of(Reason.Working);
            }
            t.FineSince = -1;
            if (t.NeedSince < 0) t.NeedSince = tick;

            EnsureTended(tick);
            if (beingTended.Contains(p)) t.LastWorkedTick = tick;
            if ((t.LastWorkedTick >= 0 && tick - t.LastWorkedTick < WorkedGrace) || tick - t.NeedSince < NeedGrace)
                return Core.Diagnosis.Of(Reason.Working);

            if (p.playerSettings != null && p.playerSettings.medCare == MedicalCareCategory.NoCare)
                return Core.Diagnosis.Of(Reason.Blocked, Detail.MedicalCareOff);
            if (!p.InBed()) return Core.Diagnosis.Of(Reason.Blocked, Detail.PatientNotInBed);

            WorkTypeDef doctor = WorkTypeDefOf.Doctor;
            List<Pawn> available = pool.Available(doctor, tick, out Detail none);
            bool selfTend = p.playerSettings != null && p.playerSettings.selfTend;
            doctors.Clear();
            for (int i = 0; i < available.Count; i++)
            {
                if (available[i] != p || selfTend) doctors.Add(available[i]);
            }
            if (doctors.Count == 0)
            {
                var n = Core.Diagnosis.Of(Reason.NoEligibleWorker, available.Count > 0 ? Detail.WorkersUnavailable : none);
                n.SubjectC = doctor;
                if (available.Count > 0) n.SubjectA = p; // only the patient could tend, and self-tend is off
                return n;
            }
            bool reached = false;
            for (int i = 0; i < doctors.Count && !reached; i++)
            {
                Pawn d = doctors[i];
                reached = d == p || reach.CanReach(d, d.Position, p, PathEndMode.Touch);
            }
            if (!reached) return Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Unreachable);

            if (emergency == null)
            {
                emergency = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendEmergency");
                humanlike = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DoctorTendToHumanlikes");
            }
            WorkGiverDef giver = HealthAIUtility.ShouldBeTendedNowByPlayerUrgent(p) && emergency != null ? emergency : humanlike;
            return giver != null ? ActivityClassifier.Decide(doctors, giver) : Core.Diagnosis.Of(Reason.WaitingForWorker);
        }

        private void EnsureTended(int tick)
        {
            int cycle = tick / GameTime.TicksPerSample;
            if (cycle == tendedCycle) return;
            tendedCycle = cycle;
            beingTended.Clear();
            Add(map.mapPawns.FreeColonistsSpawned);
            Add(map.mapPawns.SpawnedColonyMechs);
        }

        private void Add(List<Pawn> pawns)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Job job = pawns[i].CurJob;
                if (job == null || job.def != JobDefOf.TendPatient) continue;
                if (job.targetA.Thing is Pawn patient) beingTended.Add(patient);
            }
        }
    }
}
