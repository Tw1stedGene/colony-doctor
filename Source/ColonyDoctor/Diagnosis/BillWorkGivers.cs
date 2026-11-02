using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Which vanilla <see cref="WorkGiver_DoBill"/> serves a bench and bill. Built once from defs, mirroring
    /// <c>WorkGiver_DoBill.ThingIsUsableBillGiver</c> (fixedBillGiverDefs) and the <c>requiredGiverWorkType</c> check in
    /// <c>StartOrResumeBillJob</c>. Benches with no entry here bypass the vanilla bill work giver and use the probe path.
    /// </summary>
    public static class BillWorkGivers
    {
        private static Dictionary<ThingDef, List<WorkGiverDef>> byBench;

        private static void Build()
        {
            byBench = new Dictionary<ThingDef, List<WorkGiverDef>>();
            foreach (WorkGiverDef wg in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (wg.giverClass == null || !typeof(WorkGiver_DoBill).IsAssignableFrom(wg.giverClass)) continue;
                if (wg.fixedBillGiverDefs == null) continue;
                foreach (ThingDef bench in wg.fixedBillGiverDefs)
                {
                    if (!byBench.TryGetValue(bench, out var list)) byBench[bench] = list = new List<WorkGiverDef>();
                    list.Add(wg);
                }
            }
        }

        /// <summary>The DoBill work giver that would pick up <paramref name="bill"/> on <paramref name="bench"/>, or null.</summary>
        public static WorkGiverDef For(Thing bench, Bill bill)
        {
            if (byBench == null) Build();
            if (!byBench.TryGetValue(bench.def, out var list)) return null;
            WorkTypeDef required = bill.recipe.requiredGiverWorkType;
            for (int i = 0; i < list.Count; i++)
            {
                if (required == null || list[i].workType == required) return list[i];
            }
            return null;
        }

        /// <summary>True when the bench has any vanilla DoBill work giver (so structured checks apply).</summary>
        public static bool IsVanillaBench(Thing bench)
        {
            if (byBench == null) Build();
            return byBench.ContainsKey(bench.def);
        }
    }
}
