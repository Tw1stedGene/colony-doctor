using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Diagnosis;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Construction
{
    /// <summary>
    /// Read-only diagnosis of a construction site (spec: construction-diagnosis). Mirrors the checks of vanilla
    /// <c>GenConstruct.CanConstruct</c> and the resource-delivery work givers without calling them (they write
    /// <c>JobFailReason</c>). One instance per map; collections are reused between samples.
    /// </summary>
    public sealed class ConstructionDiagnoser
    {
        public const int WorkingGraceTicks = 500;

        private const int GateIncapable = 0;
        private const int GateNotActive = 1;
        private const int GateSkill = 2;
        private const int GatePassed = 3;

        private const int StageOnMap = 0;
        private const int StageNotForbidden = 1;
        private const int StageReachable = 2;
        private const int StageNotReserved = 3;
        private const int StageCount = 4;

        private static readonly Detail[] StageDetail = { Detail.NoneOnMap, Detail.Forbidden, Detail.Unreachable, Detail.Reserved };

        private static HashSet<JobDef> constructionJobs;

        private sealed class GateInfo
        {
            public readonly List<Pawn> Passing = new List<Pawn>();
            public int BestGate;
            public Pawn Closest;
            public SkillDef FailedSkill;
            public int ClosestLevel;
            public int Required;
            public int Stamp;
        }

        private readonly Map map;
        private readonly ReachCache reach;
        private readonly List<Pawn> candidates = new List<Pawn>(32);
        private readonly Dictionary<int, GateInfo> gates = new Dictionary<int, GateInfo>();
        private readonly Dictionary<ThingDef, int[]> stages = new Dictionary<ThingDef, int[]>();
        private readonly Dictionary<ThingDef, int[]> consumed = new Dictionary<ThingDef, int[]>();
        private readonly Dictionary<ThingDef, int> shortfall = new Dictionary<ThingDef, int>();
        private readonly int[] pieceCounts = new int[Reasons.Count];
        // Per detail within a reason: piece count and the first piece's diagnosis (subjects for the explanation).
        private const int MaxDetails = 64;
        private readonly int[] detailCounts = new int[Reasons.Count * MaxDetails];
        private readonly Core.Diagnosis[] firstByDetail = new Core.Diagnosis[Reasons.Count * MaxDetails];
        private int stamp;
        private Pawn stockPawn;
        private IntVec3 stockRoot;

        public ConstructionDiagnoser(Map map, ReachCache reach)
        {
            this.map = map;
            this.reach = reach;
        }

        public Core.Diagnosis Diagnose(ConstructionSite site, int tick)
        {
            CollectCandidates();
            // Spec (stall-history): no colonists on the map → don't count the sample.
            if (candidates.Count == 0) return Core.Diagnosis.Of(Reason.Suspended);

            if (IsWorkedOn(site))
            {
                site.LastWorkedTick = tick;
                return Core.Diagnosis.Of(Reason.Working);
            }
            if (site.LastWorkedTick >= 0 && tick - site.LastWorkedTick <= WorkingGraceTicks) return Core.Diagnosis.Of(Reason.Working);

            stamp++;
            shortfall.Clear();
            for (int r = 0; r < Reasons.Count; r++) pieceCounts[r] = 0;
            System.Array.Clear(detailCounts, 0, detailCounts.Length);
            GateInfo any = Gate(0, 0);
            stockPawn = any.Passing.Count > 0 ? any.Passing[0] : null;
            stockRoot = site.Pieces[0].Position;

            List<Thing> pieces = site.Pieces;
            for (int i = 0; i < pieces.Count; i++)
            {
                Thing piece = pieces[i];
                if (!piece.Spawned) continue;
                Core.Diagnosis d = DiagnosePiece(piece, any);
                int r = (int)d.Reason;
                pieceCounts[r]++;
                int k = r * MaxDetails + (int)d.Detail;
                if (detailCounts[k]++ == 0) firstByDetail[k] = d;
            }

            Reason headline = SiteReason.Majority(pieceCounts);
            if (headline == Reason.Working) return Core.Diagnosis.Of(Reason.Suspended); // no spawned pieces left this sample
            // The headline's detail is the most common one within the winning reason (e.g. most pieces lack slate
            // entirely, a few could use the forbidden stack): count and subjects come from that detail.
            int baseK = (int)headline * MaxDetails, bestK = -1;
            for (int k = baseK; k < baseK + MaxDetails; k++)
            {
                if (detailCounts[k] > 0 && (bestK < 0 || detailCounts[k] > detailCounts[bestK])) bestK = k;
            }
            Core.Diagnosis result = firstByDetail[bestK];
            result.PieceCount = detailCounts[bestK];
            result.PieceTotal = pieces.Count;
            if (headline == Reason.MaterialsMissing && result.SubjectA is ThingDef mat)
            {
                shortfall.TryGetValue(mat, out int shortBy);
                result.ValueA = shortBy;
            }
            return result;
        }

        // ---- Candidates and "worked on" ----------------------------------------------------------------------------

        private void CollectCandidates()
        {
            candidates.Clear();
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++) candidates.Add(colonists[i]);
            List<Pawn> mechs = map.mapPawns.SpawnedColonyMechs;
            for (int i = 0; i < mechs.Count; i++)
            {
                if (!mechs[i].WorkTypeIsDisabled(WorkTypeDefOf.Construction)) candidates.Add(mechs[i]);
            }
        }

        private static bool IsConstructionJob(JobDef def)
        {
            if (constructionJobs == null)
            {
                constructionJobs = new HashSet<JobDef>
                {
                    JobDefOf.HaulToContainer, JobDefOf.FinishFrame, JobDefOf.PlaceNoCostFrame, JobDefOf.CutPlant,
                    JobDefOf.HaulToCell, JobDefOf.Deconstruct, JobDefOf.Mine,
                };
            }
            return constructionJobs.Contains(def);
        }

        /// <summary>Spec: Piece is being worked on. Delivery, finishing, or clearing a blocker on the site.</summary>
        private bool IsWorkedOn(ConstructionSite site)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Job job = candidates[i].CurJob;
                if (job == null || !IsConstructionJob(job.def)) continue;
                if (Targets(site, job.targetA) || Targets(site, job.targetB) || Targets(site, job.targetC)) return true;
            }
            return false;
        }

        private bool Targets(ConstructionSite site, LocalTargetInfo t)
        {
            if (!t.IsValid) return false;
            if (t.HasThing)
            {
                Thing thing = t.Thing;
                if (site.PieceSet.Contains(thing)) return true;
                return thing.Spawned && thing.Map == map && site.CellSet.Contains(map.cellIndices.CellToIndex(thing.Position));
            }
            return t.Cell.InBounds(map) && site.CellSet.Contains(map.cellIndices.CellToIndex(t.Cell));
        }

        // ---- Per piece ----------------------------------------------------------------------------------------------

        private Core.Diagnosis DiagnosePiece(Thing piece, GateInfo any)
        {
            if (!piece.Spawned || piece.Map != map) return Core.Diagnosis.Of(Reason.Suspended);
            BuildableDef built = ConstructionSite.BuiltDef(piece);

            // No eligible worker (spec: Worker eligibility).
            GateInfo g = Gate(built.constructionSkillPrerequisite, built.artisticSkillPrerequisite);
            if (g.Passing.Count == 0)
            {
                var d = Core.Diagnosis.Of(Reason.NoEligibleWorker,
                    g.BestGate == GateIncapable ? Detail.Incapable : g.BestGate == GateNotActive ? Detail.WorkTypeDisabled : Detail.SkillTooLow);
                d.SubjectA = g.Closest;
                d.SubjectB = g.BestGate == GateSkill ? (object)g.FailedSkill : WorkTypeDefOf.Construction;
                d.SubjectC = built;
                d.ValueA = g.ClosestLevel;
                d.ValueB = g.Required;
                return d;
            }
            Pawn reacher = null, available = null;
            for (int i = 0; i < g.Passing.Count; i++)
            {
                Pawn p = g.Passing[i];
                if (!reach.CanReach(p, p.Position, piece, PathEndMode.Touch)) continue;
                if (reacher == null) reacher = p;
                if (!p.Downed && !p.InMentalState && !p.Drafted)
                {
                    available = p;
                    break;
                }
            }
            if (reacher == null)
            {
                var d = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Unreachable);
                d.SubjectC = built;
                return d;
            }
            if (available == null)
            {
                var d = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.WorkersUnavailable);
                d.SubjectA = reacher;
                return d;
            }

            // Blocked (spec: Blocked).
            Thing blocker = GenConstruct.FirstBlockingThing(piece, null);
            if (blocker != null) return Blocked(blocker, any);

            // Materials (spec: Materials).
            if (TryMaterials(piece, out Core.Diagnosis m)) return m;

            var waiting = Core.Diagnosis.Of(Reason.WaitingForWorker);
            waiting.SubjectA = available;
            return waiting;
        }

        private Core.Diagnosis Blocked(Thing blocker, GateInfo any)
        {
            Core.Diagnosis d;
            if (blocker.def.category == ThingCategory.Plant)
            {
                d = Core.Diagnosis.Of(Reason.Blocked, Detail.BlockedPlant);
            }
            else if (blocker.def.mineable)
            {
                d = Core.Diagnosis.Of(Reason.Blocked, Detail.BlockedRock);
                d.ValueA = AnyCanMine(any) ? 0 : 1;
            }
            else if (blocker.def.category == ThingCategory.Building)
            {
                d = Core.Diagnosis.Of(Reason.Blocked, Detail.BlockedBuilding);
                d.ValueA = blocker is Building b && b.DeconstructibleBy(Faction.OfPlayer).Accepted ? 0 : 1;
            }
            else
            {
                d = Core.Diagnosis.Of(Reason.Blocked, Detail.BlockedItem);
            }
            d.SubjectA = blocker;
            return d;
        }

        private static bool AnyCanMine(GateInfo any)
        {
            for (int i = 0; i < any.Passing.Count; i++)
            {
                Pawn p = any.Passing[i];
                if (!p.WorkTypeIsDisabled(WorkTypeDefOf.Mining) && p.workSettings.WorkIsActive(WorkTypeDefOf.Mining)) return true;
            }
            return false;
        }

        private bool TryMaterials(Thing piece, out Core.Diagnosis d)
        {
            d = default;
            if (!(piece is IConstructible c)) return false;
            List<ThingDefCountClass> cost = c.TotalMaterialCost();
            bool missing = false;
            for (int i = 0; i < cost.Count; i++)
            {
                ThingDef mat = cost[i].thingDef;
                int need = c.ThingCountNeeded(mat);
                if (need <= 0) continue;
                int[] s = Stages(mat);
                int[] used = Used(mat);
                int fail = -1;
                for (int st = 0; st < StageCount; st++)
                {
                    if (used[st] + need > s[st])
                    {
                        fail = st;
                        break;
                    }
                }

                int usableBefore = used[StageNotReserved];
                // Stock is used up stage by stage (spec: allocation across pieces): a piece takes what is left at every
                // stage, so once the forbidden stack is "spent", later pieces report "not enough" rather than "forbidden".
                for (int st = 0; st < StageCount; st++) used[st] = System.Math.Min(s[st], used[st] + need);
                if (fail < 0) continue;

                shortfall.TryGetValue(mat, out int shortBy);
                shortfall[mat] = shortBy + need - System.Math.Max(0, s[StageNotReserved] - usableBefore);
                if (!missing)
                {
                    d = Core.Diagnosis.Of(Reason.MaterialsMissing, StageDetail[fail]);
                    d.SubjectA = mat;
                    d.ValueB = s[StageNotReserved];
                    missing = true;
                }
            }
            return missing;
        }

        /// <summary>Stock already allocated to earlier pieces, per stage, for this site sample. Reused arrays.</summary>
        private int[] Used(ThingDef mat)
        {
            if (!consumed.TryGetValue(mat, out int[] u)) consumed[mat] = u = new int[StageCount + 1];
            if (u[StageCount] != stamp)
            {
                for (int i = 0; i < StageCount; i++) u[i] = 0;
                u[StageCount] = stamp;
            }
            return u;
        }

        /// <summary>Usable stock of <paramref name="mat"/> at each stage, once per site sample.</summary>
        private int[] Stages(ThingDef mat)
        {
            // Arrays are kept across samples; slot [StageCount] holds the sample stamp they were filled for.
            if (!stages.TryGetValue(mat, out int[] s)) stages[mat] = s = new int[StageCount + 1];
            if (s[StageCount] == stamp) return s;
            for (int i = 0; i < StageCount; i++) s[i] = 0;
            s[StageCount] = stamp;
            List<Thing> things = map.listerThings.ThingsOfDef(mat);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (!t.Spawned) continue;
                int n = t.stackCount;
                s[StageOnMap] += n;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                s[StageNotForbidden] += n;
                if (stockPawn != null && !reach.CanReach(stockPawn, stockRoot, t, PathEndMode.ClosestTouch)) continue;
                s[StageReachable] += n;
                if (stockPawn != null && !stockPawn.CanReserve(t)) continue;
                s[StageNotReserved] += n;
            }
            return s;
        }

        // ---- Worker gates -------------------------------------------------------------------------------------------

        /// <summary>Who passes the incapable / not active / skill gates for a skill requirement, cached per sample.</summary>
        private GateInfo Gate(int constructionReq, int artisticReq)
        {
            int key = constructionReq * 64 + artisticReq;
            if (!gates.TryGetValue(key, out GateInfo g)) gates[key] = g = new GateInfo { Stamp = -1 };
            if (g.Stamp == stamp) return g;
            g.Stamp = stamp;
            g.Passing.Clear();
            g.BestGate = -1;
            g.Closest = null;
            g.FailedSkill = null;
            g.ClosestLevel = 0;
            g.Required = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                int gate;
                SkillDef failed = null;
                int level = 0, required = 0;
                if (p.WorkTypeIsDisabled(WorkTypeDefOf.Construction) || p.workSettings == null || !p.workSettings.EverWork)
                {
                    gate = GateIncapable;
                }
                else if (!p.workSettings.WorkIsActive(WorkTypeDefOf.Construction))
                {
                    gate = GateNotActive;
                }
                else
                {
                    int con = SkillLevel(p, SkillDefOf.Construction);
                    int art = SkillLevel(p, SkillDefOf.Artistic);
                    if (con < constructionReq)
                    {
                        gate = GateSkill;
                        failed = SkillDefOf.Construction;
                        level = con;
                        required = constructionReq;
                    }
                    else if (art < artisticReq)
                    {
                        gate = GateSkill;
                        failed = SkillDefOf.Artistic;
                        level = art;
                        required = artisticReq;
                    }
                    else
                    {
                        gate = GatePassed;
                    }
                }

                if (gate == GatePassed)
                {
                    g.Passing.Add(p);
                    continue;
                }
                if (gate > g.BestGate || (gate == g.BestGate && gate == GateSkill && level > g.ClosestLevel))
                {
                    g.BestGate = gate;
                    g.Closest = p;
                    g.FailedSkill = failed;
                    g.ClosestLevel = level;
                    g.Required = required;
                }
            }
            if (g.BestGate < 0) g.BestGate = GateIncapable;
            return g;
        }

        private static int SkillLevel(Pawn p, SkillDef skill)
        {
            if (p.IsColonyMech) return p.RaceProps.mechFixedSkillLevel;
            return p.skills?.GetSkill(skill)?.Level ?? 0;
        }

        /// <summary>Dev dump: per-piece reasons for the current state.</summary>
        public string Describe(ConstructionSite site)
        {
            CollectCandidates();
            stamp++;
            shortfall.Clear();
            GateInfo any = Gate(0, 0);
            stockPawn = any.Passing.Count > 0 ? any.Passing[0] : null;
            stockRoot = site.Pieces.Count > 0 ? site.Pieces[0].Position : IntVec3.Invalid;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < site.Pieces.Count && i < 40; i++)
            {
                Thing piece = site.Pieces[i];
                if (!piece.Spawned)
                {
                    sb.Append($"\n      {piece.LabelShort}: no longer spawned (built or removed since discovery)");
                    continue;
                }
                Core.Diagnosis d = DiagnosePiece(piece, any);
                sb.Append($"\n      {piece.LabelShort} {piece.Position}: {d.Reason}/{d.Detail} A={d.SubjectA} B={d.SubjectB} vals={d.ValueA},{d.ValueB}");
            }
            if (site.Pieces.Count > 40) sb.Append($"\n      … {site.Pieces.Count - 40} more");
            return sb.ToString();
        }
    }
}
