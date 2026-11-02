using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Sampling
{
    /// <summary>
    /// Saves and restores <see cref="JobFailReason"/>'s private static state around a probe, so probes never leak a
    /// failure reason into the vanilla float menu (spec: Read-only diagnosis, "Float menu unaffected").
    /// </summary>
    internal static class JobFailReasonGuard
    {
        private static readonly FieldInfo LastReason = AccessTools.Field(typeof(JobFailReason), "lastReason");
        private static readonly FieldInfo LastCustomJobString = AccessTools.Field(typeof(JobFailReason), "lastCustomJobString");
        private static readonly FieldInfo Silent = AccessTools.Field(typeof(JobFailReason), "silent");

        public static readonly bool Available = LastReason != null && LastCustomJobString != null && Silent != null;

        public struct Snapshot
        {
            internal object reason, custom, silent;
        }

        public static Snapshot Save() => new Snapshot
        {
            reason = LastReason.GetValue(null),
            custom = LastCustomJobString.GetValue(null),
            silent = Silent.GetValue(null),
        };

        public static void Restore(in Snapshot s)
        {
            LastReason.SetValue(null, s.reason);
            LastCustomJobString.SetValue(null, s.custom);
            Silent.SetValue(null, s.silent);
        }

        public static void WarnIfUnavailable()
        {
            if (!Available)
            {
                Log.WarningOnce("[Colony Doctor] JobFailReason internals not found; modded-bench diagnosis is disabled.",
                    0x0C0D0C71);
            }
        }
    }
}
