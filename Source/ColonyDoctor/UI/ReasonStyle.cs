using ColonyDoctor.Core;
using UnityEngine;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Colours and labels per reason, matching the dark-theme tokens of docs/prototype.html.</summary>
    public static class ReasonStyle
    {
        private static readonly Color Working = Hex(0x8cc06a);
        private static readonly Color Materials = Hex(0xe59a3c);
        private static readonly Color Worker = Hex(0x5fa0de);
        private static readonly Color Waiting = Hex(0xa98be0);
        private static readonly Color Bench = Hex(0xe06650);
        private static readonly Color Outcompeted = Hex(0xd9b44a);
        private static readonly Color BlockedColor = Hex(0xb07a52);
        private static readonly Color IdleColor = Hex(0x6fb7b0);
        public static readonly Color Output = Hex(0x8f99a2);
        private static readonly Color Unknown = Hex(0x9aa1a7);
        private static readonly Color Suspended = new Color(0.3f, 0.3f, 0.3f);
        public static readonly Color NoData = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color SeverityHigh = Bench;
        public static readonly Color SeverityMid = Materials;
        public static readonly Color SeverityLow = new Color(0.4f, 0.42f, 0.45f);
        public static readonly Color Muted = new Color(0.6f, 0.63f, 0.65f);

        private static readonly string[] Labels = new string[Reasons.Count];

        public static Color ColorOf(Reason r)
        {
            switch (r)
            {
                case Reason.Working: return Working;
                case Reason.BenchProblem: return Bench;
                case Reason.NoEligibleWorker: return Worker;
                case Reason.MaterialsMissing: return Materials;
                case Reason.WaitingForWorker: return Waiting;
                case Reason.Outcompeted: return Outcompeted;
                case Reason.Blocked: return BlockedColor;
                case Reason.Idle: return IdleColor;
                case Reason.Suspended: return Suspended;
                default: return Unknown;
            }
        }

        public static Color ColorOfSlice(byte slice) => slice == Reasons.NoData ? NoData : ColorOf((Reason)slice);

        public static string LabelOf(Reason r)
        {
            int i = (int)r;
            return Labels[i] ?? (Labels[i] = ("ColonyDoctor_Reason_" + r).Translate().Resolve());
        }

        public static Color Severity(int stalledPercent)
            => stalledPercent >= 60 ? SeverityHigh : stalledPercent >= 30 ? SeverityMid : SeverityLow;

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);
    }
}
