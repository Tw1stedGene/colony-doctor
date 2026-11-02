using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.UI
{
    /// <summary>Text for No eligible worker / Area restriction outside bills (spec: Area restriction detail). UI only.</summary>
    public static class AreaText
    {
        public static string For(in Core.Diagnosis d)
        {
            string pawn = (d.SubjectA as Pawn)?.LabelShort ?? "?";
            string area = (d.SubjectB as Area)?.Label ?? "?";
            string work = (d.SubjectC as WorkTypeDef)?.labelShort ?? "?";
            return d.ValueC > 1
                ? "ColonyDoctor_Area_Many".Translate(pawn.Named("PAWN"), area.Named("AREA"), work.Named("WORK"), (d.ValueC - 1).Named("OTHERS"))
                : "ColonyDoctor_Area_One".Translate(pawn.Named("PAWN"), area.Named("AREA"), work.Named("WORK"));
        }
    }
}
