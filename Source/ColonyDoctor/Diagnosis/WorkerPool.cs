using System.Collections.Generic;
using ColonyDoctor.Core;
using RimWorld;
using Verse;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Per map, per sampling cycle and per work type: the pawns available for that work (free colonists and colony
    /// mechs who can and may do it and are not downed, drafted or in a mental state), and the No eligible worker
    /// detail when there are none. Read-only.
    /// </summary>
    public sealed class WorkerPool
    {
        private sealed class Entry
        {
            public readonly List<Pawn> Available = new List<Pawn>(16);
            public Detail NoneDetail;
            public int Cycle = -1;
        }

        private readonly Map map;
        private readonly Dictionary<WorkTypeDef, Entry> entries = new Dictionary<WorkTypeDef, Entry>();

        public WorkerPool(Map map) => this.map = map;

        public List<Pawn> Available(WorkTypeDef work, int tick, out Detail noneDetail)
        {
            if (!entries.TryGetValue(work, out Entry e)) entries[work] = e = new Entry();
            int cycle = tick / GameTime.TicksPerSample;
            if (e.Cycle != cycle)
            {
                e.Cycle = cycle;
                e.Available.Clear();
                int pawns = 0, capable = 0, enabled = 0;
                Collect(map.mapPawns.FreeColonistsSpawned, work, e.Available, ref pawns, ref capable, ref enabled);
                Collect(map.mapPawns.SpawnedColonyMechs, work, e.Available, ref pawns, ref capable, ref enabled);
                e.NoneDetail = pawns == 0 ? Detail.NoColonists
                    : capable == 0 ? Detail.Incapable
                    : enabled == 0 ? Detail.WorkTypeDisabled
                    : Detail.WorkersUnavailable;
            }
            noneDetail = e.NoneDetail;
            return e.Available;
        }

        private static void Collect(List<Pawn> list, WorkTypeDef work, List<Pawn> into, ref int pawns, ref int capable, ref int enabled)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p.workSettings == null || !p.workSettings.EverWork) continue;
                pawns++;
                if (p.WorkTypeIsDisabled(work)) continue;
                capable++;
                if (!p.workSettings.WorkIsActive(work)) continue;
                enabled++;
                if (p.Downed || p.Drafted || p.InMentalState) continue;
                into.Add(p);
            }
        }

        public static int SkillLevel(Pawn p, SkillDef skill)
        {
            if (p.IsColonyMech) return p.RaceProps.mechFixedSkillLevel;
            return p.skills?.GetSkill(skill)?.Level ?? 0;
        }
    }
}
