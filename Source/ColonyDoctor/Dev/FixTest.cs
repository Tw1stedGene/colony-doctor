using System;
using System.Collections.Generic;
using System.Text;
using ColonyDoctor.Fixes;
using ColonyDoctor.Sampling;
using LudeonTK;
using Verse;

namespace ColonyDoctor.Dev
{
    /// <summary>
    /// Dev check for one-click fixes: for every fix currently offered, applies it, checks it is no longer needed,
    /// undoes it and checks it is needed again (StillNeeded reads live game state). Changes the map while it runs and
    /// restores it, so only use it on a test colony.
    /// </summary>
    public static class FixTest
    {
        [DebugAction("Colony Doctor", "Test all fixes (apply + undo)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunOnCurrentMap() => Run(Find.CurrentMap);

        public static void Run(Map map)
        {
            MapTracker tracker = MapTracker.Existing(map);
            if (tracker == null) return;
            var sb = new StringBuilder("[Colony Doctor] Fix test:\n");
            int pass = 0, fail = 0;
            var problems = new List<TrackedProblem>(tracker.Problems);
            foreach (TrackedProblem p in problems)
            {
                if (p.IsGone) continue;
                var fixes = new List<Fix>();
                Core.Diagnosis last = p.History.LastFor(p.History.LastReason);
                Fix main = FixCatalog.For(p, last);
                if (main != null) fixes.Add(main);
                if (p is TrackedBill tb) fixes.AddRange(FixCatalog.ForOutput(tb));
                foreach (Fix f in fixes)
                {
                    if (f == null) continue;
                    string title = Safe(() => p.Title);
                    try
                    {
                        bool before = f.StillNeeded();
                        Action undo = f.Apply();
                        bool afterApply = f.StillNeeded();
                        undo?.Invoke();
                        bool afterUndo = f.StillNeeded();
                        bool ok = before && !afterApply && afterUndo && undo != null;
                        if (ok) pass++; else fail++;
                        sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {p.Kind} '{title}' [{f.Button}] {f.Change}: needed {before} → apply {afterApply} → undo {afterUndo}");
                    }
                    catch (Exception e)
                    {
                        fail++;
                        sb.AppendLine($"  FAIL {p.Kind} '{title}' [{f.Button}] threw {e.GetType().Name}: {e.Message}");
                    }
                }
            }
            sb.AppendLine($"  {pass} passed, {fail} failed");
            Log.Message(sb.ToString());
        }

/// <summary>Applies the first Widen radius fix and keeps it (save/reload check). True when one was applied.</summary>
        public static bool ApplyOne(Map map)
        {
            MapTracker tracker = MapTracker.Existing(map);
            if (tracker == null) return false;
            foreach (TrackedProblem p in tracker.Problems)
            {
                if (p.IsGone) continue;
                Fix f = FixCatalog.For(p, p.History.LastFor(p.History.LastReason));
                if (f == null || !f.Change.Contains("radius") || !f.StillNeeded()) continue;
                f.Apply();
                Log.Message($"[Colony Doctor] Kept fix: {Safe(() => p.Title)}: {f.Change}");
                return true;
            }
            return false;
        }

        private static readonly HashSet<TrackedProblem> testedOutcompeted = new HashSet<TrackedProblem>();

        /// <summary>Called every sampling cycle by the autopilot: tests the fix of any problem that just became Outcompeted.</summary>
        public static void CheckOutcompeted(Map map)
        {
            MapTracker tracker = MapTracker.Existing(map);
            if (tracker == null) return;
            foreach (TrackedProblem p in tracker.Problems)
            {
                if (p.IsGone || p.History.LastReason != Core.Reason.Outcompeted || testedOutcompeted.Contains(p)) continue;
                testedOutcompeted.Add(p);
                Core.Diagnosis last = p.History.LastFor(Core.Reason.Outcompeted);
                Fix f = FixCatalog.For(p, last);
                string who = (last.SubjectA as Pawn)?.LabelShort ?? "?";
                if (f == null)
                {
                    Log.Message($"[Colony Doctor] Fix test: {p.Kind} '{Safe(() => p.Title)}' Outcompeted (by {who}) offers no fix");
                    continue;
                }
                bool before = f.StillNeeded();
                Action undo = f.Apply();
                bool afterApply = f.StillNeeded();
                undo?.Invoke();
                bool afterUndo = f.StillNeeded();
                bool ok = before && !afterApply && afterUndo && undo != null;
                Log.Message($"[Colony Doctor] Fix test: {(ok ? "PASS" : "FAIL")} {p.Kind} '{Safe(() => p.Title)}' [{f.Button}] {f.Change}: needed {before} → apply {afterApply} → undo {afterUndo}");
            }
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); }
            catch { return "?"; }
        }
    }
}
