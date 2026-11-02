namespace ColonyDoctor.Core
{
    /// <summary>Top-level outcome of one bill sample. Values are stored as bytes in history.</summary>
    public enum Reason : byte
    {
        Working = 0,
        BenchProblem = 1,
        NoEligibleWorker = 2,
        MaterialsMissing = 3,
        WaitingForWorker = 4,
        Unknown = 5,
        Suspended = 6,
        Blocked = 7,
        Outcompeted = 8,
        Idle = 9,
        NoStorage = 10,
    }

    public enum Detail : byte
    {
        None = 0,

        // Bench problem
        Unpowered,
        Unfueled,
        BrokenDown,
        InteractionBlocked,
        BenchUnreachable,

        // No eligible worker
        WorkTypeDisabled,
        Incapable,
        PawnRestriction,
        SkillRange,
        AreaRestriction,
        WorkersUnavailable,
        NoColonists,

        // Materials missing
        NoneOnMap,
        ExcludedByBillFilter,
        Forbidden,
        OutsideRadius,
        Reserved,
        Unreachable,

        // Output note (not a stall reason: vanilla drops the product next to the bench and carries on)
        NoStockpileAccepts,
        SpecificStockpileRejects,

        // Waiting for a worker
        BenchBusy,
        OffDuty,
        SameOrLowerRank,

        // Construction: blocked footprint (what clears it)
        BlockedPlant,
        BlockedItem,
        BlockedBuilding,
        BlockedRock,

        // Construction: worker
        SkillTooLow,

        // Idle colonists
        IdleNoWork,
        IdleWorkBlocked,
        IdleNothingToDo,
        IdleWorkNotTaken,

        // Hauling: no storage
        StorageFull,

        // Patients
        MedicalCareOff,
        PatientNotInBed,

        // Unknown
        FailReasonText,
        Generic,
    }

    public static class Reasons
    {
        public const int Count = 11;
        public const byte NoData = 255;

        /// <summary>Not working and not suspended: counts towards "stalled".</summary>
        public static bool IsStalled(Reason r) => r != Reason.Working && r != Reason.Suspended;

        /// <summary>
        /// Precedence when several blockers apply (spec: Reason precedence): the one the player must fix first.
        /// </summary>
        public static Reason Pick(bool benchProblem, bool noEligibleWorker, bool materialsMissing)
        {
            if (benchProblem) return Reason.BenchProblem;
            if (noEligibleWorker) return Reason.NoEligibleWorker;
            if (materialsMissing) return Reason.MaterialsMissing;
            return Reason.WaitingForWorker;
        }
    }
}
