using System;
using System.Collections.Generic;
using System.Diagnostics;
using ColonyDoctor.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Sampling
{
    /// <summary>
    /// Asks modded work givers why they can't serve a bench whose bills bypass the vanilla bill work giver (spec: Fallback
    /// for non-vanilla benches, Performance budget). Runs per frame, not per tick, within about 0.3 ms, only for bills
    /// stalled at least one in-game hour, and re-probes each bill at most once per in-game hour.
    /// Disabled in Multiplayer: third-party HasJobOnThing implementations may touch state or Rand, and per-frame probes
    /// would run at different moments on each client.
    /// </summary>
    public static class ProbeScheduler
    {
        private const double BudgetMs = 0.3;
        private const double SlowProbeMs = 2.0;
        private const int MaxPawnsPerProbe = 3;
        private const int RefillEveryFrames = 60;

        private static readonly Stopwatch Frame = new Stopwatch();
        private static readonly Stopwatch One = new Stopwatch();
        private static readonly List<TrackedBill> Queue = new List<TrackedBill>(32);
        private static readonly HashSet<WorkGiverDef> Blacklist = new HashSet<WorkGiverDef>();
        private static int queueIndex;
        private static int framesSinceRefill = RefillEveryFrames;
        private static bool? multiplayerActive;

        /// <summary>Last frame's probe time in ms, for the dev overlay.</summary>
        public static double LastFrameMs { get; private set; }

        public static bool Enabled
        {
            get
            {
                if (multiplayerActive == null) multiplayerActive = ModsConfig.IsActive("rwmt.Multiplayer");
                return ColonyDoctorMod.Settings.enableProbes && !multiplayerActive.Value && JobFailReasonGuard.Available;
            }
        }

        public static void Update()
        {
            LastFrameMs = 0;
            if (Find.TickManager == null || Find.TickManager.Paused || !Enabled) return;

            if (++framesSinceRefill >= RefillEveryFrames || queueIndex >= Queue.Count) Refill();
            if (Queue.Count == 0) return;

            Frame.Restart();
            while (queueIndex < Queue.Count && Frame.Elapsed.TotalMilliseconds < BudgetMs)
            {
                TrackedBill t = Queue[queueIndex++];
                if (!t.IsGone) Probe(t);
            }
            LastFrameMs = Frame.Elapsed.TotalMilliseconds;
        }

        private static void Refill()
        {
            framesSinceRefill = 0;
            Queue.Clear();
            queueIndex = 0;
            int now = Find.TickManager.TicksGame;
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                MapTracker tracker = MapTracker.Existing(maps[m]);
                if (tracker == null) continue;
                IReadOnlyList<TrackedBill> bills = tracker.Tracked;
                for (int i = 0; i < bills.Count; i++)
                {
                    TrackedBill t = bills[i];
                    if (!t.UsesProbe || !t.History.IsCurrentlyStalled) continue;
                    if (t.History.ContinuousStallTicks(now) < GameTime.TicksPerHour) continue;
                    if (t.LastProbeTick >= 0 && now - t.LastProbeTick < GameTime.TicksPerHour) continue;
                    Queue.Add(t);
                }
            }
        }

        private static void Probe(TrackedBill t)
        {
            t.LastProbeTick = Find.TickManager.TicksGame;
            Thing bench = t.Bench;
            List<Pawn> pawns = bench.Map.mapPawns.FreeColonistsSpawned;
            string firstReason = null;
            int probed = 0;

            for (int p = 0; p < pawns.Count && probed < MaxPawnsPerProbe; p++)
            {
                Pawn pawn = pawns[p];
                if (pawn.Downed || pawn.workSettings == null) continue;
                probed++;
                List<WorkGiver> givers = pawn.workSettings.WorkGiversInOrderNormal;
                for (int g = 0; g < givers.Count; g++)
                {
                    if (!(givers[g] is WorkGiver_Scanner scanner) || Blacklist.Contains(scanner.def)) continue;
                    ThingRequest req = scanner.PotentialWorkThingRequest;
                    if (req.IsUndefined || !req.Accepts(bench)) continue;

                    bool hasJob = RunGuarded(scanner, pawn, bench, out string reason);
                    if (hasJob)
                    {
                        Store(t, Core.Diagnosis.Of(Reason.WaitingForWorker));
                        return;
                    }
                    if (firstReason == null && !string.IsNullOrEmpty(reason)) firstReason = reason;
                }
            }

            if (firstReason != null)
            {
                var d = Core.Diagnosis.Of(Reason.Unknown, Detail.FailReasonText);
                d.Text = firstReason;
                Store(t, d);
            }
            else
            {
                Store(t, Core.Diagnosis.Of(Reason.Unknown, Detail.Generic));
            }
        }

        private static bool RunGuarded(WorkGiver_Scanner scanner, Pawn pawn, Thing bench, out string reason)
        {
            reason = null;
            JobFailReasonGuard.Snapshot saved = JobFailReasonGuard.Save();
            One.Restart();
            try
            {
                JobFailReason.Clear();
                bool result = scanner.HasJobOnThing(pawn, bench, false);
                if (JobFailReason.HaveReason) reason = JobFailReason.Reason;
                return result;
            }
            catch (Exception e)
            {
                // Spec: Fault isolation. Never let a modded work giver break the frame; don't probe it again.
                Blacklist.Add(scanner.def);
                Log.ErrorOnce($"[Colony Doctor] Work giver {scanner.def.defName} threw while probing {bench}; " +
                              $"it won't be probed again this session. {e}", 0x0C0D0C72 ^ scanner.def.shortHash);
                return false;
            }
            finally
            {
                JobFailReasonGuard.Restore(saved);
                if (One.Elapsed.TotalMilliseconds > SlowProbeMs && Blacklist.Add(scanner.def))
                {
                    Log.Warning($"[Colony Doctor] Work giver {scanner.def.defName} took {One.Elapsed.TotalMilliseconds:F1} ms " +
                                "to probe; it won't be probed again this session.");
                }
            }
        }

        private static void Store(TrackedBill t, Core.Diagnosis d)
        {
            t.ProbeResult = d;
            t.HasProbeResult = true;
        }
    }
}
