using System;
using System.Collections.Generic;
using ColonyDoctor.Core;
using ColonyDoctor.Sampling;
using RimWorld;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Structured, read-only diagnosis of one production bill (spec: bill-diagnosis). One instance per map; reuses its
    /// lists so diagnosing does not allocate. Checks run in precedence order and return at the first hit.
    /// Never calls vanilla methods that write state (ShouldDoNow, PawnAllowedToStartAnew, JobOnThing) or use Rand.
    /// </summary>
    public sealed class BillDiagnoser
    {
        // Worker gates, in the order a pawn is tested. A pawn that fails a later gate got "closer" to doing the bill.
        private const int GateIncapable = 0;
        private const int GateWorkTypeDisabled = 1;
        private const int GatePawnRestriction = 2;
        private const int GateSkill = 3;
        private const int GateArea = 4;
        private const int GateUnavailable = 5;
        private const int GateEligible = 6;

        // Ingredient filter stages (spec: Materials detection), most general first.
        private const int StageRecipe = 0;
        private const int StageBillFilter = 1;
        private const int StageForbidden = 2;
        private const int StageRadius = 3;
        private const int StageReserved = 4;
        private const int StageReachable = 5;
        private const int StageCount = 6;

        private static readonly Detail[] StageDetail =
        {
            Detail.NoneOnMap, Detail.ExcludedByBillFilter, Detail.Forbidden, Detail.OutsideRadius, Detail.Reserved,
            Detail.Unreachable,
        };

        private static readonly Detail[] GateDetail =
        {
            Detail.Incapable, Detail.WorkTypeDisabled, Detail.PawnRestriction, Detail.SkillRange, Detail.AreaRestriction,
            Detail.WorkersUnavailable,
        };

        private readonly Map map;
        private readonly ReachCache reach;
        private readonly List<Pawn> candidates = new List<Pawn>(32);
        private readonly List<Pawn> eligible = new List<Pawn>(32);
        private readonly float[] stageValue = new float[StageCount];
        private readonly Thing[] stageExample = new Thing[StageCount];
        private readonly int[] stageExampleDistSq = new int[StageCount];

        public BillDiagnoser(Map map)
        {
            this.map = map;
            reach = new ReachCache(map);
        }

        public ReachCache Reach => reach;

        /// <summary>Called once per sampling cycle.</summary>
        public void BeginCycle() => reach.Clear();

        /// <summary>
        /// A pawn finishing one iteration hauls the product away as a separate job, so a bill being worked repeatedly
        /// has short gaps where nobody is "doing" it. Samples this soon after real work still count as Working.
        /// </summary>
        public const int WorkingGraceTicks = 500;

        public Core.Diagnosis Diagnose(TrackedBill tracked, int tick)
        {
            Bill bill = tracked.Bill;
            Thing bench = tracked.Bench;
            if (!BillActivity.IsActive(bill))
            {
                tracked.HasOutputNote = false;
                return Core.Diagnosis.Of(Reason.Suspended);
            }

            tracked.HasOutputNote = TryOutputNote(bill, out tracked.OutputNote);

            CollectCandidates();
            // No colonists on the map (drop pods not open yet, everyone away on a caravan): nobody could stall the
            // bill, so don't count the sample at all.
            if (candidates.Count == 0) return Core.Diagnosis.Of(Reason.Suspended);

            if (IsAnyoneDoing(bill))
            {
                tracked.LastWorkedTick = tick;
                return Core.Diagnosis.Of(Reason.Working);
            }
            if (tracked.LastWorkedTick >= 0 && tick - tracked.LastWorkedTick <= WorkingGraceTicks)
            {
                return Core.Diagnosis.Of(Reason.Working);
            }

            // Spec: Fallback for non-vanilla benches. Structured checks only apply to the vanilla bill work giver.
            WorkGiverDef workGiver = tracked.UsesProbe ? null : BillWorkGivers.For(bench, bill);
            if (workGiver == null)
            {
                return tracked.HasProbeResult ? tracked.ProbeResult : Core.Diagnosis.Of(Reason.Unknown, Detail.Generic);
            }

            if (TryBenchProblem(bench, out var d)) return d;
            if (TryNoEligibleWorker(bill, bench, workGiver, out d)) return d;
            if (TryMaterialsMissing(bill, bench, out d)) return d;
            return Waiting(bill, bench, workGiver);
        }

        // ---- Working ----------------------------------------------------------------------------------------------

        private void CollectCandidates()
        {
            candidates.Clear();
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++) candidates.Add(colonists[i]);
            List<Pawn> mechs = map.mapPawns.SpawnedColonyMechs;
            for (int i = 0; i < mechs.Count; i++) candidates.Add(mechs[i]);
        }

        private bool IsAnyoneDoing(Bill bill)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].CurJob?.bill == bill) return true;
            }
            return false;
        }

        // ---- Bench problem ----------------------------------------------------------------------------------------

        private bool TryBenchProblem(Thing bench, out Core.Diagnosis d)
        {
            d = default;
            var giver = (IBillGiver)bench;

            if (!giver.UsableForBillsAfterFueling())
            {
                var breakdown = bench.TryGetComp<CompBreakdownable>();
                d = Core.Diagnosis.Of(Reason.BenchProblem, breakdown != null && breakdown.BrokenDown ? Detail.BrokenDown : Detail.Unpowered);
                return true;
            }
            if (!giver.CurrentlyUsableForBills())
            {
                d = Core.Diagnosis.Of(Reason.BenchProblem, Detail.Unfueled);
                return true;
            }

            if (bench.def.hasInteractionCell)
            {
                IntVec3 cell = bench.InteractionCell;
                if (!cell.InBounds(map) || !cell.Standable(map))
                {
                    d = Core.Diagnosis.Of(Reason.BenchProblem, Detail.InteractionBlocked);
                    d.SubjectA = cell.InBounds(map) ? cell.GetEdifice(map) : null;
                    return true;
                }
            }

            // Unreachable: no colonist who could move at all can get to the bench.
            bool anyMobile = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                if (p.Downed) continue;
                anyMobile = true;
                if (reach.CanReach(p, p.Position, bench, PathEndMode.InteractionCell)) return false;
            }
            if (anyMobile)
            {
                d = Core.Diagnosis.Of(Reason.BenchProblem, Detail.BenchUnreachable);
                return true;
            }
            return false;
        }

        // ---- No eligible worker -----------------------------------------------------------------------------------

        private bool TryNoEligibleWorker(Bill bill, Thing bench, WorkGiverDef workGiver, out Core.Diagnosis d)
        {
            d = default;
            eligible.Clear();
            if (candidates.Count == 0)
            {
                d = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.NoColonists);
                return true;
            }

            WorkTypeDef workType = workGiver.workType;
            int bestGate = -1;
            int bestSkill = int.MinValue;
            Pawn closest = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                int gate = WorkerGate(p, bill, bench, workGiver, workType);
                if (gate == GateEligible)
                {
                    eligible.Add(p);
                    continue;
                }
                // Mechs that can never do this work type are noise, not near-misses.
                if (gate == GateIncapable && p.IsColonyMech) continue;

                int skill = SkillLevel(p, bill.recipe.workSkill);
                if (gate > bestGate || (gate == bestGate && skill > bestSkill))
                {
                    bestGate = gate;
                    bestSkill = skill;
                    closest = p;
                }
            }

            if (eligible.Count > 0) return false;

            if (bestGate < 0)
            {
                d = Core.Diagnosis.Of(Reason.NoEligibleWorker, Detail.Incapable);
                d.SubjectB = workType;
                return true;
            }

            d = Core.Diagnosis.Of(Reason.NoEligibleWorker, GateDetail[bestGate]);
            d.SubjectA = closest;
            if (bestGate == GateSkill)
            {
                SkillRequirement req = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(closest);
                if (req != null)
                {
                    d.SubjectB = req.skill;
                    d.ValueA = SkillLevel(closest, req.skill);
                    d.ValueB = req.minLevel;
                    d.ValueC = 20;
                }
                else
                {
                    d.SubjectB = bill.recipe.workSkill;
                    d.ValueA = bestSkill;
                    d.ValueB = bill.allowedSkillRange.min;
                    d.ValueC = bill.allowedSkillRange.max;
                }
            }
            else if (bestGate == GateArea)
            {
                d.SubjectB = closest.playerSettings?.EffectiveAreaRestrictionInPawnCurrentMap;
            }
            else if (bestGate == GateWorkTypeDisabled || bestGate == GateIncapable)
            {
                d.SubjectB = workType;
            }
            return true;
        }

        /// <summary>Dev dump: which worker gate each candidate stops at (6 = eligible).</summary>
        public string DescribeGates(TrackedBill t)
        {
            WorkGiverDef wg = BillWorkGivers.For(t.Bench, t.Bill);
            if (wg == null) return "no vanilla DoBill work giver";
            CollectCandidates();
            var sb = new System.Text.StringBuilder($"giver {wg.defName} ({wg.workType.defName}):");
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn p = candidates[i];
                sb.Append($" {p.LabelShort}=gate{WorkerGate(p, t.Bill, t.Bench, wg, wg.workType)}" +
                          $"[prio {p.workSettings?.GetPriority(wg.workType)}, disabled {p.WorkTypeIsDisabled(wg.workType)}]");
            }
            return sb.ToString();
        }

        private int WorkerGate(Pawn p, Bill bill, Thing bench, WorkGiverDef workGiver, WorkTypeDef workType)
        {
            if (p.WorkTypeIsDisabled(workType) || p.workSettings == null || !p.workSettings.EverWork) return GateIncapable;
            if (workGiver.Worker.MissingRequiredCapacity(p) != null) return GateIncapable;
            if (!p.workSettings.WorkIsActive(workType)) return GateWorkTypeDisabled;
            if (!PawnRestrictionAllows(bill, p)) return GatePawnRestriction;
            if (!SkillAllows(bill, p)) return GateSkill;
            if (bench.IsForbidden(p)) return GateArea;
            if (p.Downed || p.InMentalState || p.Drafted) return GateUnavailable;
            return GateEligible;
        }

        /// <summary>Restriction part of <c>Bill.PawnAllowedToStartAnew</c>, without its JobFailReason side effect.</summary>
        private static bool PawnRestrictionAllows(Bill bill, Pawn p)
        {
            if (bill.PawnRestriction != null) return bill.PawnRestriction == p;
            if (bill.SlavesOnly && !p.IsSlave) return false;
            if (bill.MechsOnly && !p.IsColonyMechPlayerControlled) return false;
            if (bill.NonMechsOnly && p.IsColonyMechPlayerControlled) return false;
            if (ModsConfig.BiotechActive && bill.recipe.mechanitorOnlyRecipe && !MechanitorUtility.IsMechanitor(p)) return false;
            return true;
        }

        /// <summary>Skill part of <c>PawnAllowedToStartAnew</c> plus the recipe's own skill requirements.</summary>
        private static bool SkillAllows(Bill bill, Pawn p)
        {
            SkillDef workSkill = bill.recipe.workSkill;
            if (workSkill != null && (p.skills != null || p.IsColonyMech))
            {
                int level = SkillLevel(p, workSkill);
                if (level < bill.allowedSkillRange.min || level > bill.allowedSkillRange.max) return false;
            }
            return bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(p) == null;
        }

        private static int SkillLevel(Pawn p, SkillDef skill)
        {
            if (skill == null) return 0;
            if (p.IsColonyMech) return p.RaceProps.mechFixedSkillLevel;
            return p.skills?.GetSkill(skill)?.Level ?? 0;
        }

        // ---- Materials missing ------------------------------------------------------------------------------------

        private bool TryMaterialsMissing(Bill bill, Thing bench, out Core.Diagnosis d)
        {
            d = default;
            // Work already started on an unfinished thing: ingredients are consumed.
            if (bill is Bill_ProductionWithUft uftBill && uftBill.BoundUft != null) return false;

            Pawn pawn = eligible[0];
            RecipeDef recipe = bill.recipe;
            IngredientValueGetter valueGetter = recipe.IngredientValueGetter;
            float radius = bill.ingredientSearchRadius;
            float radiusSq = radius * radius;
            IntVec3 root = bench.def.hasInteractionCell ? bench.InteractionCell : bench.Position;

            for (int ing = 0; ing < recipe.ingredients.Count; ing++)
            {
                IngredientCount ic = recipe.ingredients[ing];
                float need = ic.GetBaseCount();
                if (need <= 0f) continue;

                for (int s = 0; s < StageCount; s++)
                {
                    stageValue[s] = 0f;
                    stageExample[s] = null;
                    stageExampleDistSq[s] = int.MaxValue;
                }

                List<Thing> things = map.listerThings.ThingsMatching(ic.filter.BestThingRequest);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (!t.Spawned || !ic.filter.Allows(t)) continue;
                    float value = valueGetter.ValuePerUnitOf(t.def) * t.stackCount;
                    if (value <= 0f) continue;
                    int distSq = (t.Position - bench.Position).LengthHorizontalSquared;

                    // Walk the stages; the first stage a thing fails is where it is "stuck".
                    int passed = StageRecipe;
                    if (bill.IsFixedOrAllowedIngredient(t))
                    {
                        passed = StageBillFilter;
                        if (!t.IsForbidden(pawn))
                        {
                            passed = StageForbidden;
                            if (distSq <= radiusSq)
                            {
                                passed = StageRadius;
                                if (pawn.CanReserve(t))
                                {
                                    passed = StageReserved;
                                    if (reach.CanReach(pawn, root, t, PathEndMode.ClosestTouch)) passed = StageReachable;
                                }
                            }
                        }
                    }

                    for (int s = 0; s <= passed; s++) stageValue[s] += value;
                    // Remember the nearest thing that got stuck right after each stage, for the explanation.
                    if (passed < StageReachable && distSq < stageExampleDistSq[passed + 1])
                    {
                        stageExampleDistSq[passed + 1] = distSq;
                        stageExample[passed + 1] = t;
                    }
                }

                for (int s = 0; s < StageCount; s++)
                {
                    if (stageValue[s] >= need) continue;
                    d = Core.Diagnosis.Of(Reason.MaterialsMissing, StageDetail[s]);
                    d.SubjectA = ic;
                    d.SubjectB = stageExample[s];
                    d.ValueC = (int)Math.Floor(s > 0 ? stageValue[s - 1] : 0f);
                    if (s == StageRadius)
                    {
                        d.ValueA = (int)Math.Round(Math.Sqrt(stageExampleDistSq[s]));
                        d.ValueB = (int)radius;
                    }
                    else if (s == StageReserved && stageExample[s] != null)
                    {
                        d.SubjectB = map.reservationManager.FirstRespectedReserver(stageExample[s], pawn, null) ?? (object)stageExample[s];
                    }
                    return true;
                }
            }
            return false;
        }

        // ---- Output note -------------------------------------------------------------------------------------------

        /// <summary>
        /// Products with nowhere to go. Not a stall reason: vanilla drops the product next to the bench and the bill
        /// keeps running (spec: Output note). Reason is left as Working; only Detail and subjects matter.
        /// </summary>
        private bool TryOutputNote(Bill bill, out Core.Diagnosis d)
        {
            d = default;
            ThingDef product = bill.recipe.ProducedThingDef;
            if (product == null) return false;

            BillStoreModeDef mode = bill.GetStoreMode();
            if (mode == BillStoreModeDefOf.BestStockpile)
            {
                List<SlotGroup> groups = map.haulDestinationManager.AllGroupsListForReading;
                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i].Settings.AllowedToAccept(product)) return false;
                }
                d = Core.Diagnosis.Of(Reason.Working, Detail.NoStockpileAccepts);
                d.SubjectA = product;
                return true;
            }
            if (mode == BillStoreModeDefOf.SpecificStockpile)
            {
                ISlotGroup group = bill.GetSlotGroup();
                if (group != null && group.Settings.AllowedToAccept(product)) return false;
                d = Core.Diagnosis.Of(Reason.Working, Detail.SpecificStockpileRejects);
                d.SubjectA = product;
                d.SubjectB = group;
                return true;
            }
            return false;
        }

        // ---- Waiting for a worker ---------------------------------------------------------------------------------

        private Core.Diagnosis Waiting(Bill bill, Thing bench, WorkGiverDef workGiver)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Job job = candidates[i].CurJob;
                if (job != null && job.bill != null && job.bill != bill && job.targetA.Thing == bench)
                {
                    var busy = Core.Diagnosis.Of(Reason.WaitingForWorker, Detail.BenchBusy);
                    busy.SubjectA = candidates[i];
                    busy.SubjectB = job.bill;
                    return busy;
                }
            }
            // Spec (outcompeted-diagnosis): what were the eligible workers doing instead?
            return ActivityClassifier.Decide(eligible, workGiver);
        }
    }
}
