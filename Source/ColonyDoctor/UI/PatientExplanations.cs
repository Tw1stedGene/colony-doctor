using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Plain-language text for patient diagnoses (spec: Patient reason). UI only.</summary>
    public static class PatientExplanations
    {
        public static string For(TrackedPatient t, in Core.Diagnosis d)
        {
            string name = t.Pawn.LabelShort;
            switch (d.Reason)
            {
                case Reason.Working: return "ColonyDoctor_Patient_Working".Translate();
                case Reason.Blocked:
                    if (d.Detail == Detail.MedicalCareOff) return "ColonyDoctor_Patient_NoCare".Translate(name.Named("PAWN"));
                    return (t.Pawn.Downed ? "ColonyDoctor_Patient_DownedNotInBed" : "ColonyDoctor_Patient_UpNotInBed").Translate(name.Named("PAWN"));
                case Reason.NoEligibleWorker:
                    switch (d.Detail)
                    {
                        case Detail.Unreachable: return "ColonyDoctor_Patient_Unreachable".Translate(name.Named("PAWN"));
                        case Detail.NoColonists: return "ColonyDoctor_Haul_NoColonists".Translate();
                        case Detail.Incapable: return "ColonyDoctor_Patient_Incapable".Translate();
                        case Detail.WorkTypeDisabled: return "ColonyDoctor_Patient_Disabled".Translate();
                        default:
                            return d.SubjectA != null ? "ColonyDoctor_Patient_OnlySelf".Translate(name.Named("PAWN"))
                                : "ColonyDoctor_Patient_Unavailable".Translate();
                    }
                case Reason.Outcompeted: return OutcompetedText.For(t, in d);
                case Reason.WaitingForWorker:
                    return (d.Detail == Detail.OffDuty ? "ColonyDoctor_Why_OffDuty" : "ColonyDoctor_Why_SameOrLowerRank").Translate();
            }
            return "";
        }
    }
}
