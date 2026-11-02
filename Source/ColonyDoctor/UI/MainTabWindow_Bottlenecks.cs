using System;
using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>
    /// The Bottlenecks main tab (spec: bottlenecks-ui). Left: stalled problems of the selected category (bills,
    /// construction sites), worst first. Right: timeline, reason breakdown with explanations, and Jump to.
    /// Layout follows docs/prototype.html.
    /// </summary>
    public class MainTabWindow_Bottlenecks : MainTabWindow
    {
        private const float TopBarHeight = 40f;
        private const float ListWidth = 380f;
        private const float RowHeight = 58f;
        private const int RefreshEveryFrames = 60;

        private static readonly TimeRange[] Ranges = { TimeRange.Hour, TimeRange.Day, TimeRange.Quadrum };
        private static readonly ProblemKind[] Categories = { ProblemKind.Bill, ProblemKind.Construction };

        /// <summary>Selected category; static so it is remembered while the game runs (spec: Construction category).</summary>
        public static ProblemKind Category = ProblemKind.Bill;
        private static readonly Comparison<Row> ByWorst = CompareRows;

        private sealed class Row
        {
            public TrackedProblem Problem;
            public readonly HistoryAggregate Agg = new HistoryAggregate();
            public int ContinuousStall;
            public string Title, Location, Extra;
        }

        private readonly Dictionary<TrackedProblem, Row> rowCache = new Dictionary<TrackedProblem, Row>();
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Reason> detailOrder = new List<Reason>(Reasons.Count);
        private readonly string[] detailText = new string[Reasons.Count];
        private readonly Fixes.Fix[] detailFix = new Fixes.Fix[Reasons.Count];
        private List<Fixes.Fix> outputFixes = new List<Fixes.Fix>();

        private TimeRange range = TimeRange.Day;
        private TrackedProblem selected;
        private ProblemKind shownCategory;
        private Map shownMap;
        private int shownVersion = -1;
        private TimeRange shownRange;
        private int framesSinceRefresh = RefreshEveryFrames;
        private Vector2 listScroll, detailScroll;
        private float detailHeight = 600f;

        public override Vector2 RequestedTabSize => new Vector2(1010f, 560f);

        public override void PreOpen()
        {
            base.PreOpen();
            range = TimeRange.Day; // spec: opens on Last day
            framesSinceRefresh = RefreshEveryFrames;
        }

        public override void DoWindowContents(Rect inRect)
        {
            MaybeRefresh();

            var top = new Rect(inRect.x, inRect.y, inRect.width, TopBarHeight);
            DrawTopBar(top);

            var body = new Rect(inRect.x, top.yMax + 4f, inRect.width, inRect.height - top.height - 4f);
            var list = new Rect(body.x, body.y, ListWidth, body.height);
            var detail = new Rect(list.xMax + 12f, body.y, body.width - ListWidth - 12f, body.height);

            DrawList(list);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Widgets.DrawLineVertical(list.xMax + 6f, body.y, body.height);
            GUI.color = Color.white;
            DrawDetail(detail);
            if (Prefs.DevMode) DrawDevStats(inRect);
        }

        /// <summary>Dev mode: per-map sampling cost and probe cost (task 7.1).</summary>
        private void DrawDevStats(Rect inRect)
        {
            MapTracker tracker = MapTracker.Existing(shownMap);
            if (tracker == null) return;
            string s = $"dev: {tracker.Tracked.Count} bills, {tracker.Sites.Count} sites, sampling avg {tracker.AvgTickMs:F3} ms/tick, peak {tracker.PeakTickMs:F2} ms, " +
                       $"probes {Sampling.ProbeScheduler.LastFrameMs:F2} ms/frame{(Sampling.ProbeScheduler.Enabled ? "" : " (off)")}";
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            Widgets.Label(new Rect(inRect.x + 400f, inRect.y + 26f, inRect.width - 760f, 16f), s);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        // ---- Data ----------------------------------------------------------------------------------------------------

        private void MaybeRefresh()
        {
            Map map = Find.CurrentMap;
            MapTracker tracker = MapTracker.Existing(map);
            int version = tracker?.Version ?? -1;
            bool force = map != shownMap || version != shownVersion || range != shownRange || Category != shownCategory;
            if (!force && ++framesSinceRefresh < RefreshEveryFrames) return;

            framesSinceRefresh = 0;
            shownMap = map;
            shownVersion = version;
            shownRange = range;
            shownCategory = Category;
            Rebuild(tracker);
        }

        private void Rebuild(MapTracker tracker)
        {
            rows.Clear();
            if (tracker == null)
            {
                selected = null;
                return;
            }

            int now = Find.TickManager.TicksGame;
            IReadOnlyList<TrackedProblem> problems = tracker.Problems;
            for (int i = 0; i < problems.Count; i++)
            {
                TrackedProblem t = problems[i];
                if (t.Kind != Category || t.IsGone) continue;
                if (!rowCache.TryGetValue(t, out Row row)) rowCache[t] = row = new Row { Problem = t };
                t.History.Aggregate(now, range, row.Agg);
                if (row.Agg.StalledPercent <= 0) continue; // spec: list problems stalled above zero
                row.ContinuousStall = t.History.ContinuousStallTicks(now);
                row.Title = t.Title;
                row.Location = t.Location;
                row.Extra = t.Extra;
                rows.Add(row);
            }
            rows.Sort(ByWorst);

            // spec: Live refresh. Keep the selection, or fall back to the top row.
            bool keep = false;
            for (int i = 0; i < rows.Count; i++) keep |= rows[i].Problem == selected;
            if (!keep) selected = rows.Count > 0 ? rows[0].Problem : null;
            BuildDetailText();
        }

        private static int CompareRows(Row a, Row b)
        {
            int c = b.Agg.StalledPercent.CompareTo(a.Agg.StalledPercent);
            return c != 0 ? c : b.ContinuousStall.CompareTo(a.ContinuousStall);
        }

        private void BuildDetailText()
        {
            detailOrder.Clear();
            outputFixes.Clear();
            for (int r = 0; r < Reasons.Count; r++) detailFix[r] = null;
            Row row = SelectedRow();
            if (row == null) return;
            if (row.Problem is TrackedBill tb) outputFixes = Fixes.FixCatalog.ForOutput(tb);

            for (int r = 0; r < Reasons.Count; r++)
            {
                var reason = (Reason)r;
                if (reason == Reason.Suspended || row.Agg.Count(reason) == 0) continue;
                detailOrder.Add(reason);
                detailText[r] = row.Problem.Explain(in row.Problem.History.LastFor(reason));
                if (Reasons.IsStalled(reason)) detailFix[r] = Fixes.FixCatalog.For(row.Problem, in row.Problem.History.LastFor(reason));
            }
            // Stalled reasons by share, Working last (as in the prototype).
            detailOrder.Sort((x, y) =>
            {
                if (x == Reason.Working) return 1;
                if (y == Reason.Working) return -1;
                return row.Agg.Count(y).CompareTo(row.Agg.Count(x));
            });
        }

        private Row SelectedRow()
        {
            if (selected == null) return null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Problem == selected) return rows[i];
            }
            return null;
        }

        // ---- Top bar -------------------------------------------------------------------------------------------------

        private void DrawTopBar(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, 200f, rect.height), "ColonyDoctor_TabTitle".Translate());
            Text.Font = GameFont.Small;

            // spec: Construction category. Bills | Construction.
            for (int i = 0; i < Categories.Length; i++)
            {
                var cat = new Rect(rect.x + 170f + i * 110f, rect.y + 6f, 110f, 28f);
                if (Segment(cat, ("ColonyDoctor_Category_" + Categories[i]).Translate(), Category == Categories[i]))
                {
                    Category = Categories[i];
                    detailScroll = Vector2.zero;
                }
            }

            float w = 120f;
            float x = rect.xMax - w * Ranges.Length;
            for (int i = 0; i < Ranges.Length; i++)
            {
                var r = new Rect(x + i * w, rect.y + 6f, w, 28f);
                if (Segment(r, RangeLabel(Ranges[i]), range == Ranges[i])) range = Ranges[i];
            }
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static bool Segment(Rect r, string label, bool on)
        {
            Widgets.DrawBoxSolid(r, on ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.03f));
            Widgets.DrawBox(r);
            Text.Anchor = TextAnchor.MiddleCenter;
            if (on) GUI.color = new Color(0.91f, 0.77f, 0.28f);
            Widgets.Label(r, label);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.DrawHighlightIfMouseover(r);
            return Widgets.ButtonInvisible(r);
        }

        private static string RangeLabel(TimeRange r) => ("ColonyDoctor_Range_" + r).Translate();

        // ---- List ----------------------------------------------------------------------------------------------------

        private void DrawList(Rect rect)
        {
            if (rows.Count == 0)
            {
                GUI.color = ReasonStyle.Muted;
                Widgets.Label(rect.ContractedBy(8f), shownMap == null ? "ColonyDoctor_NoMap".Translate() : "ColonyDoctor_NothingStalled".Translate());
                GUI.color = Color.white;
                return;
            }

            var view = new Rect(0f, 0f, rect.width - 16f, rows.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref listScroll, view);
            for (int i = 0; i < rows.Count; i++) DrawRow(new Rect(0f, i * RowHeight, view.width, RowHeight), rows[i]);
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect rect, Row row)
        {
            bool isSelected = row.Problem == selected;
            if (isSelected) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.08f));
            Widgets.DrawHighlightIfMouseover(rect);

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + 2f, 5f, rect.height - 4f), ReasonStyle.Severity(row.Agg.StalledPercent));

            var pct = new Rect(rect.xMax - 64f, rect.y, 60f, rect.height);
            var text = new Rect(rect.x + 14f, rect.y + 4f, pct.x - rect.x - 18f, rect.height - 8f);

            if (isSelected) GUI.color = new Color(0.91f, 0.77f, 0.28f);
            Widgets.Label(new Rect(text.x, text.y, text.width, 22f), row.Title.Truncate(text.width));
            GUI.color = ReasonStyle.Muted;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(text.x, text.y + 20f, text.width, 16f), row.Location.Truncate(text.width));
            var fixState = row.Problem.StateAt(Find.TickManager.TicksGame);
            if (fixState != FixState.None)
            {
                GUI.color = fixState == FixState.Fixed ? new Color(0.55f, 0.75f, 0.42f) : new Color(0.85f, 0.7f, 0.3f);
                Widgets.Label(new Rect(text.x, text.y + 34f, text.width, 16f),
                    (fixState == FixState.Fixed ? "ColonyDoctor_RowFixed" : "ColonyDoctor_RowMonitoring").Translate());
                GUI.color = ReasonStyle.Muted;
            }
            else if (row.Agg.Dominant.HasValue)
            {
                Reason dom = row.Agg.Dominant.Value;
                Widgets.DrawBoxSolid(new Rect(text.x, text.y + 39f, 8f, 8f), ReasonStyle.ColorOf(dom));
                Widgets.Label(new Rect(text.x + 13f, text.y + 34f, text.width - 13f, 16f), ReasonStyle.LabelOf(dom));
            }
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.UpperRight;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(pct.x, rect.y + 6f, pct.width, 30f), row.Agg.StalledPercent + "%");
            Text.Font = GameFont.Tiny;
            GUI.color = ReasonStyle.Muted;
            Widgets.Label(new Rect(pct.x, rect.y + 34f, pct.width, 16f), "ColonyDoctor_Stalled".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonInvisible(rect))
            {
                selected = row.Problem;
                detailScroll = Vector2.zero;
                BuildDetailText();
            }
        }

        // ---- Detail --------------------------------------------------------------------------------------------------

        private void DrawDetail(Rect rect)
        {
            Row row = SelectedRow();
            if (row == null) return;

            var view = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(detailHeight, rect.height));
            Widgets.BeginScrollView(rect, ref detailScroll, view);
            float y = 0f;
            float w = view.width;

            // Header with Jump to.
            var jump = new Rect(w - 110f, 2f, 110f, 30f);
            Text.Font = GameFont.Medium;
            float titleH = Text.CalcHeight(row.Title, jump.x - 8f);
            Widgets.Label(new Rect(0f, y, jump.x - 8f, titleH), row.Title);
            Text.Font = GameFont.Small;
            y += titleH;
            GUI.color = ReasonStyle.Muted;
            Widgets.Label(new Rect(0f, y, w, 22f), row.Location);
            y += 22f;
            if (row.Extra != null)
            {
                Widgets.Label(new Rect(0f, y, w, 22f), row.Extra);
                y += 22f;
            }
            GUI.color = Color.white;
            y += 8f;
            if (Widgets.ButtonText(jump, "ColonyDoctor_JumpTo".Translate())) row.Problem.JumpTo();

            // Range and stalled percent.
            GUI.color = ReasonStyle.Muted;
            Widgets.Label(new Rect(0f, y, w, 22f), "ColonyDoctor_RangeStalled".Translate(RangeLabel(range).Named("RANGE"), row.Agg.StalledPercent.Named("PCT")));
            GUI.color = Color.white;
            y += 24f;

            // Timeline strip (oldest left).
            int n = row.Agg.SliceCount;
            float gap = 2f;
            float sliceW = (w - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var s = new Rect(i * (sliceW + gap), y, sliceW, 32f);
                byte slice = row.Agg.Slice(i);
                Widgets.DrawBoxSolid(s, ReasonStyle.ColorOfSlice(slice));
                if (Mouse.IsOver(s))
                {
                    TooltipHandler.TipRegion(s, slice == Reasons.NoData ? "ColonyDoctor_NoData".Translate().Resolve() : ReasonStyle.LabelOf((Reason)slice));
                }
            }
            y += 34f;
            Text.Font = GameFont.Tiny;
            GUI.color = ReasonStyle.Muted;
            Widgets.Label(new Rect(0f, y, w / 2f, 16f), ("ColonyDoctor_AxisStart_" + range).Translate());
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(w / 2f, y, w / 2f, 16f), ("ColonyDoctor_AxisEnd_" + range).Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 26f;

            // Reason breakdown.
            y = Heading(y, w, "ColonyDoctor_WhatBlocked".Translate());
            for (int i = 0; i < detailOrder.Count; i++)
            {
                Reason reason = detailOrder[i];
                Widgets.DrawBoxSolid(new Rect(0f, y + 6f, 10f, 10f), ReasonStyle.ColorOf(reason));
                Widgets.Label(new Rect(18f, y, w - 70f, 22f), ReasonStyle.LabelOf(reason));
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(w - 60f, y, 60f, 22f), row.Agg.Percent(reason) + "%");
                Text.Anchor = TextAnchor.UpperLeft;
                y += 22f;

                string why = detailText[(int)reason];
                if (!why.NullOrEmpty())
                {
                    GUI.color = ReasonStyle.Muted;
                    float h = Text.CalcHeight(why, w - 18f);
                    Widgets.Label(new Rect(18f, y, w - 18f, h), why);
                    GUI.color = Color.white;
                    y += h;
                }
                y = FixButton(y + 4f, w, row, detailFix[(int)reason]);
                y += 6f;
                GUI.color = new Color(1f, 1f, 1f, 0.1f);
                Widgets.DrawLineHorizontal(0f, y, w);
                GUI.color = Color.white;
                y += 6f;
            }

            // Output side note (spec: Output note): not a stall reason, shown separately.
            if (row.Problem is TrackedBill bill && bill.HasOutputNote)
            {
                y = Heading(y + 8f, w, "ColonyDoctor_OutputHeading".Translate());
                Widgets.DrawBoxSolid(new Rect(0f, y + 6f, 10f, 10f), ReasonStyle.Output);
                string note0 = bill.Explain(in bill.OutputNote);
                float oh = Text.CalcHeight(note0, w - 18f);
                Widgets.Label(new Rect(18f, y, w - 18f, oh), note0);
                y += oh + 6f;
                for (int i = 0; i < outputFixes.Count; i++) y = FixButton(y, w, row, outputFixes[i]);
            }

            // Fixes (spec: Detail panel, Monitoring).
            y = Heading(y + 8f, w, "ColonyDoctor_Fixes".Translate());
            var state = row.Problem.StateAt(Find.TickManager.TicksGame);
            if (state != FixState.None)
            {
                string label = (state == FixState.Fixed ? "ColonyDoctor_FixState_Fixed" : "ColonyDoctor_FixState_Monitoring")
                    .Translate(row.Problem.FixLabel.Named("CHANGE"));
                float mh = Text.CalcHeight(label, w - 110f);
                Widgets.Label(new Rect(0f, y, w - 110f, mh), label);
                if (state == FixState.Monitoring && row.Problem.FixUndo != null
                    && Widgets.ButtonText(new Rect(w - 100f, y, 100f, 28f), "ColonyDoctor_Undo".Translate()))
                {
                    FixFlow.Undo(row.Problem, row.Problem.FixUndo);
                    BuildDetailText();
                }
                y += Mathf.Max(mh, 30f) + 6f;
            }
            if (!AnyFix())
            {
                GUI.color = ReasonStyle.Muted;
                string note = "ColonyDoctor_NoFix".Translate();
                float nh = Text.CalcHeight(note, w);
                Widgets.Label(new Rect(0f, y, w, nh), note);
                GUI.color = Color.white;
                y += nh + 8f;
            }

            detailHeight = y;
            Widgets.EndScrollView();
        }

        private bool AnyFix()
        {
            if (outputFixes.Count > 0) return true;
            for (int i = 0; i < detailFix.Length; i++) if (detailFix[i] != null) return true;
            return false;
        }

        private float FixButton(float y, float w, Row row, Fixes.Fix fix)
        {
            if (fix == null) return y;
            Text.Font = GameFont.Small;
            float bw = Mathf.Min(w - 18f, Text.CalcSize(fix.Button).x + 24f);
            var btn = new Rect(18f, y, bw, 26f);
            TooltipHandler.TipRegion(btn, fix.Change + "\n\n" + fix.SideEffect);
            if (Widgets.ButtonText(btn, fix.Button)) FixFlow.Offer(row.Problem, fix);
            return y + 30f;
        }

        private static float Heading(float y, float w, string label)
        {
            GUI.color = ReasonStyle.Muted;
            Widgets.Label(new Rect(0f, y, w, 22f), label);
            GUI.color = Color.white;
            return y + 26f;
        }
    }
}
