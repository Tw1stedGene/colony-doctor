namespace ColonyDoctor.Core
{
    /// <summary>Whether work of one work type exists this cycle, as far as Colony Doctor can see (spec: Work presence).</summary>
    public enum Presence : byte
    {
        None = 0,
        Open = 1,
        Blocked = 2,
        Unknown = 3,
    }

    /// <summary>Spec: Idle detail. Pure rule over the presence of the colonist's active work types.</summary>
    public static class IdleReason
    {
        public static Detail Pick(int activeTypes, int open, int blocked, int unknown)
        {
            if (activeTypes == 0) return Detail.IdleNoWork;
            if (open == 0 && unknown == 0 && blocked > 0) return Detail.IdleWorkBlocked;
            if (open == 0 && unknown == 0 && blocked == 0) return Detail.IdleNothingToDo;
            return Detail.IdleWorkNotTaken;
        }
    }
}
