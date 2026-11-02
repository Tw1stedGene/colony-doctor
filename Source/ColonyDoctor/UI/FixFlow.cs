using ColonyDoctor.Fixes;
using ColonyDoctor.Sampling;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Confirm → re-validate → apply → toast with Undo → Monitoring (spec: Confirm and re-validate, Undo).</summary>
    public static class FixFlow
    {
        public static void Offer(TrackedProblem problem, Fix fix)
        {
            string text = "ColonyDoctor_FixConfirm".Translate(fix.Change.Named("CHANGE"), fix.SideEffect.Named("SIDE"));
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, () => Confirm(problem, fix), destructive: false,
                title: fix.Button));
        }

        private static void Confirm(TrackedProblem problem, Fix fix)
        {
            if (!fix.StillNeeded())
            {
                Messages.Message("ColonyDoctor_AlreadyResolved".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            System.Action undo = fix.Apply();
            problem.MarkFixed(Find.TickManager.TicksGame, fix.Change, undo);
            Find.WindowStack.Add(new UndoToast(problem, fix.Change, undo));
        }

        public static void Undo(TrackedProblem problem, System.Action undo)
        {
            if (undo == null) return;
            undo();
            if (problem.FixUndo == undo) problem.ClearFix();
            Messages.Message("ColonyDoctor_Undone".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }
    }

    /// <summary>Small non-modal toast with Undo, closes after 60 real seconds (spec: Undo).</summary>
    public class UndoToast : Window
    {
        private const float Lifetime = 60f;
        private readonly TrackedProblem problem;
        private readonly string change;
        private readonly System.Action undo;
        private readonly float openedAt;

        public UndoToast(TrackedProblem problem, string change, System.Action undo)
        {
            this.problem = problem;
            this.change = change;
            this.undo = undo;
            openedAt = Time.realtimeSinceStartup;
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            forcePause = false;
            draggable = true;
            layer = WindowLayer.SubSuper;
        }

        public override Vector2 InitialSize => new Vector2(420f, 110f);

        protected override void SetInitialSizeAndPosition()
            => windowRect = new Rect((Verse.UI.screenWidth - InitialSize.x) / 2f, 60f, InitialSize.x, InitialSize.y);

        public override void DoWindowContents(Rect inRect)
        {
            if (Time.realtimeSinceStartup - openedAt > Lifetime)
            {
                Close(false);
                return;
            }
            Widgets.Label(new Rect(0f, 0f, inRect.width - 24f, 44f), "ColonyDoctor_FixApplied".Translate(change.Named("CHANGE")));
            var btn = new Rect(inRect.width - 100f, inRect.height - 32f, 100f, 30f);
            if (Widgets.ButtonText(btn, "ColonyDoctor_Undo".Translate()))
            {
                FixFlow.Undo(problem, undo);
                Close();
            }
        }
    }
}
