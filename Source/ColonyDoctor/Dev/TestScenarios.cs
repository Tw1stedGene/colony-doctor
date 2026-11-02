using System;
using System.Collections.Generic;
using System.Linq;
using ColonyDoctor.Diagnosis;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyDoctor.Dev
{
    /// <summary>
    /// Dev-mode action that builds one test station per diagnosis scenario (task 8.1) next to where you click.
    /// Each station is a bench with one bill, renamed "T1 …", "T2 …" with the reason it should show. It changes the
    /// map (and, for the work-type scenario, colonists' work settings), so only use it on a test colony.
    /// </summary>
    public static class TestScenarios
    {
        private const int Spacing = 12;
        private const int PerRow = 4;

        [DebugAction("Colony Doctor", "Build test stations", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void BuildTestStations()
        {
            DebugTools.curTool = null; // one click builds once; clicking again would stack a second set
            Map map = Find.CurrentMap;
            IntVec3 origin = Verse.UI.MouseCell();
            var built = new List<string>();
            var stations = Stations(map).ToList();

            var plot = CellRect.FromLimits(origin.x, origin.z,
                origin.x + PerRow * Spacing, origin.z + ((stations.Count + PerRow - 1) / PerRow) * Spacing).ClipInsideMap(map);
            ClearPlot(map, plot);

            for (int i = 0; i < stations.Count; i++)
            {
                IntVec3 at = origin + new IntVec3((i % PerRow) * Spacing + 2, 0, (i / PerRow) * Spacing + 3);
                if (!at.InBounds(map)) continue;
                try
                {
                    string label = stations[i].build(map, at);
                    if (label != null) built.Add($"T{i + 1} {stations[i].name}: expect {label}");
                }
                catch (Exception e)
                {
                    Log.Error($"[Colony Doctor] Test station {stations[i].name} failed: {e}");
                }
            }

            Log.Message("[Colony Doctor] Test stations built:\n  " + string.Join("\n  ", built));
            Messages.Message($"Colony Doctor: built {built.Count} test stations. Let ~1 in-game hour pass, then open Bottlenecks.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("Colony Doctor", "Log bill diagnoses", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogDiagnoses()
        {
            Sampling.MapTracker tracker = Sampling.MapTracker.Existing(Find.CurrentMap);
            if (tracker == null)
            {
                Log.Message("[Colony Doctor] No tracker for this map yet.");
                return;
            }
            var sb = new System.Text.StringBuilder("[Colony Doctor] Bill diagnoses:\n");
            int now = Find.TickManager.TicksGame;
            foreach (Sampling.TrackedBill t in tracker.Tracked)
            {
                var bp = t.Bill as Bill_Production;
                Core.Diagnosis last = t.History.LastFor(t.History.LastReason);
                sb.AppendLine($"  {t.Bill.LabelCap} @ {t.Bench.LabelCap} {t.Bench.Position}: last={t.History.LastReason}/{last.Detail} " +
                              $"A={last.SubjectA} B={last.SubjectB} vals={last.ValueA},{last.ValueB},{last.ValueC} " +
                              $"stall={t.History.ContinuousStallTicks(now)}t lastWorked={t.LastWorkedTick} probe={t.UsesProbe}");
                sb.AppendLine($"    store={bp?.GetStoreMode()?.defName} group={bp?.GetSlotGroup()?.GroupingLabel} " +
                              $"product={t.Bill.recipe.ProducedThingDef?.defName} outputNote={(t.HasOutputNote ? t.OutputNote.Detail.ToString() : "none")}");
                sb.AppendLine("    " + tracker.Diagnoser.DescribeGates(t));
                if (t.Competing.Total > 0 || t.History.LastReason == Core.Reason.Outcompeted) sb.AppendLine("    says: " + t.Explain(last) + Tally(t));
            }
            sb.AppendLine($"[Colony Doctor] Construction sites (sampling avg {tracker.AvgTickMs:F3} ms/tick, peak {tracker.PeakTickMs:F2} ms):");
            foreach (Construction.ConstructionSite site in tracker.Sites)
            {
                Core.Diagnosis last = site.History.LastFor(site.History.LastReason);
                sb.AppendLine($"  {site.Title} ({site.Location}), {site.PieceCount} pieces: last={site.History.LastReason}/{last.Detail} " +
                              $"pieces={last.PieceCount}/{last.PieceTotal} A={last.SubjectA} B={last.SubjectB} vals={last.ValueA},{last.ValueB} " +
                              $"stall={site.History.ContinuousStallTicks(now)}t lastWorked={site.LastWorkedTick}");
                sb.AppendLine($"    says: {site.Explain(last)}" + Tally(site) + tracker.ConstructionDiagnoser.Describe(site));
            }
            sb.AppendLine("[Colony Doctor] Idle colonists:");
            foreach (Sampling.TrackedProblem p in tracker.Problems)
            {
                if (!(p is Sampling.TrackedPawn tp)) continue;
                var agg = new Core.HistoryAggregate();
                p.History.Aggregate(now, Core.TimeRange.Day, agg);
                Core.Diagnosis last = p.History.LastFor(Core.Reason.Idle);
                sb.AppendLine($"  {tp.Title}: now={p.History.LastReason} idleDay={agg.StalledPercent}% lastIdle={last.Detail} " +
                              $"open={last.ValueA} unknown={last.ValueB} blocked={last.ValueC} work=[{tp.Location}]");
                if (last.Reason == Core.Reason.Idle) sb.AppendLine("    says: " + tp.Explain(last));
            }
            sb.AppendLine("[Colony Doctor] Hauling groups:");
            foreach (Sampling.TrackedHaulGroup g in tracker.HaulGroups)
            {
                var agg = new Core.HistoryAggregate();
                g.History.Aggregate(now, Core.TimeRange.Day, agg);
                Core.Diagnosis last = g.History.LastFor(g.History.LastReason);
                sb.AppendLine($"  {g.Title} ({g.Location}): items={g.Items.Count} waiting={g.WaitingStacks} rotting={g.Rotting} " +
                              $"now={g.History.LastReason}/{last.Detail} stacks={last.PieceCount}/{last.PieceTotal} stalledDay={agg.StalledPercent}% " +
                              $"oldest={g.Oldest?.Position}");
                if (g.History.LastReason != Core.Reason.Working) sb.AppendLine("    says: " + g.Explain(last) + Tally(g));
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("Colony Doctor", "Build hauling test", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void BuildHaulingTest()
        {
            DebugTools.curTool = null;
            BuildHaulingTest(Find.CurrentMap, Verse.UI.MouseCell());
        }

        /// <summary>
        /// Hauling scenarios: H1 steel inside a closed wooden wall ring (Unreachable), H2 raw meat in the open (rotting
        /// when warm). Items count as waiting after an in-game hour.
        /// </summary>
        internal static void BuildHaulingTest(Map map, IntVec3 origin)
        {
            var built = new List<string>();
            IntVec3 ring = origin;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    IntVec3 c = ring + new IntVec3(dx, 0, dz);
                    if (!c.InBounds(map)) continue;
                    bool edge = Math.Abs(dx) == 2 || Math.Abs(dz) == 2;
                    if (edge)
                    {
                        Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
                        wall.SetFaction(Faction.OfPlayer);
                        GenSpawn.Spawn(wall, c, map, WipeMode.Vanish);
                    }
                    else
                    {
                        foreach (Thing t in c.GetThingList(map).ToList())
                        {
                            if (t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Plant) t.Destroy();
                        }
                    }
                }
            }
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = 75;
            GenSpawn.Spawn(steel, ring, map);
            built.Add($"H1 steel walled off at {ring}");

            IntVec3 meatCell = origin + new IntVec3(6, 0, 0);
            ThingDef meatDef = DefDatabase<ThingDef>.GetNamedSilentFail("Meat_Muffalo") ?? ThingDefOf.Meat_Human;
            Thing meat = ThingMaker.MakeThing(meatDef);
            meat.stackCount = 40;
            GenPlace.TryPlaceThing(meat, meatCell, map, ThingPlaceMode.Near);
            built.Add($"H2 {meatDef.label} ×40 near {meatCell}");
            Log.Message("[Colony Doctor] Hauling test built:\n  " + string.Join("\n  ", built));
        }

        private static string Tally(Sampling.TrackedProblem p)
        {
            if (p.Competing.Total == 0) return "";
            var parts = DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(w => p.Competing.CountFor(w.index) > 0)
                .Select(w => $"{w.defName}={p.Competing.CountFor(w.index)}");
            return $" [tally {p.Competing.Total}: {string.Join(",", parts)}; top={(p.Competing.TopCompetitor as Pawn)?.LabelShort}]";
        }

        [DebugAction("Colony Doctor", "Build construction test sites", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void BuildConstructionSites()
        {
            DebugTools.curTool = null;
            Map map = Find.CurrentMap;
            IntVec3 origin = Verse.UI.MouseCell();
            ClearPlot(map, CellRect.FromLimits(origin.x, origin.z, origin.x + 4 * Spacing, origin.z + 2 * Spacing).ClipInsideMap(map));

            var absentBlocks = new[] { "Marble", "Slate", "Limestone", "Sandstone", "Granite" }
                .Select(r => DefDatabase<ThingDef>.GetNamedSilentFail("Blocks" + r))
                .Where(d => d != null && map.listerThings.ThingsOfDef(d).Count == 0).ToList();
            ThingDef wood = ThingDefOf.WoodLog;
            int maxSkill = map.mapPawns.FreeColonistsSpawned.Select(p => p.skills?.GetSkill(SkillDefOf.Construction)?.Level ?? 0)
                .DefaultIfEmpty(0).Max();
            var built = new List<string>();

            IntVec3 At(int i) => origin + new IntVec3((i % 4) * Spacing + 2, 0, (i / 4) * Spacing + 2);
            void WallLine(IntVec3 start, int count, ThingDef stuff)
            {
                for (int k = 0; k < count; k++)
                    GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, start + new IntVec3(k, 0, 0), map, Rot4.North, Faction.OfPlayer, stuff, null, null, false);
            }

            if (absentBlocks.Count > 0)
            {
                WallLine(At(0), 6, absentBlocks[0]);
                built.Add($"C1 {absentBlocks[0].label} walls ×6, none on map: expect Materials missing / not enough (30 short)");
            }

            IntVec3 tree = At(1);
            ThingDef treeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeOak") ?? DefDatabase<ThingDef>.GetNamed("Plant_TreePoplar");
            var plant = (Plant)ThingMaker.MakeThing(treeDef);
            plant.Growth = 1f;
            GenSpawn.Spawn(plant, tree, map);
            WallLine(tree, 3, wood);
            SpawnStack(map, wood, 30, tree + new IntVec3(0, 0, 2));
            built.Add("C2 wood walls ×3 on an oak: expect Blocked / plant (until someone cuts it)");

            ThingDef hard = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => d.category == ThingCategory.Building && d.BuildableByPlayer && d.constructionSkillPrerequisite > maxSkill
                            && d.Size.x <= 3 && d.Size.z <= 3 && !d.IsFrame && !d.IsBlueprint)
                .OrderBy(d => d.constructionSkillPrerequisite).FirstOrDefault();
            if (hard != null)
            {
                GenConstruct.PlaceBlueprintForBuild(hard, At(2), map, Rot4.North, Faction.OfPlayer,
                    hard.MadeFromStuff ? GenStuff.DefaultStuffFor(hard) : null, null, null, false);
                built.Add($"C3 {hard.label} (Construction {hard.constructionSkillPrerequisite}, best colonist {maxSkill}): expect No eligible worker / skill too low");
            }

            IntVec3 box = At(3);
            ThingDef wallStuff = GenStuff.DefaultStuffFor(ThingDefOf.Wall);
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (System.Math.Abs(dx) == 2 || System.Math.Abs(dz) == 2) Spawn(map, ThingDefOf.Wall, box + new IntVec3(dx, 0, dz), wallStuff);
            }
            GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, box, map, Rot4.North, Faction.OfPlayer, wood, null, null, false);
            built.Add("C4 wood wall inside a closed wall ring: expect No eligible worker / unreachable");

            if (absentBlocks.Count > 1)
            {
                Thing stack = ThingMaker.MakeThing(absentBlocks[1]);
                stack.stackCount = 50;
                GenPlace.TryPlaceThing(stack, At(4) + new IntVec3(0, 0, 2), map, ThingPlaceMode.Near, out Thing placed);
                placed?.SetForbidden(true, false);
                WallLine(At(4), 3, absentBlocks[1]);
                built.Add($"C5 {absentBlocks[1].label} walls ×3, only forbidden blocks: expect Materials missing / forbidden");
            }

            if (absentBlocks.Count > 2)
            {
                SpawnStack(map, absentBlocks[2], 10, At(5) + new IntVec3(0, 0, 2));
                WallLine(At(5), 6, absentBlocks[2]);
                built.Add($"C6 {absentBlocks[2].label} walls ×6, blocks for 2: expect Materials missing for 4 of 6 (20 short), or Working while 2 get built");
            }

            SpawnStack(map, wood, 50, At(6) + new IntVec3(0, 0, 2));
            WallLine(At(6), 3, wood);
            built.Add("C7 wood walls ×3 with wood next to them: expect Working / Waiting for a worker");

            Log.Message("[Colony Doctor] Construction test sites built:\n  " + string.Join("\n  ", built));
            Messages.Message($"Colony Doctor: built {built.Count} construction test sites. Let ~1 in-game hour pass, then open Bottlenecks → Construction.",
                MessageTypeDefOf.TaskCompletion, false);
        }

        // ---- Outcompeted (add-outcompeted) -----------------------------------------------------------------------

        private static readonly Dictionary<Pawn, (int hauling, int crafting)> savedPriorities = new Dictionary<Pawn, (int, int)>();
        private static bool savedManualPriorities;

        [DebugAction("Colony Doctor", "Build outcompeted test (changes priorities)", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void BuildOutcompetedTest()
        {
            DebugTools.curTool = null;
            BuildOutcompetedTest(Find.CurrentMap, Verse.UI.MouseCell());
        }

        internal static void BuildOutcompetedTest(Map map, IntVec3 origin)
        {
            ClearPlot(map, CellRect.FromLimits(origin.x, origin.z, origin.x + Spacing, origin.z + Spacing).ClipInsideMap(map));

            ThingDef chunk = ChunkFor("Granite") ?? ChunkFor("Sandstone");
            Thing bench = Bench(map, "TableStonecutter", origin + new IntVec3(2, 0, 3), BlocksRecipe(chunk), "O1 Outcompeted by hauling");
            for (int i = 0; i < 3; i++) GenSpawn.Spawn(ThingMaker.MakeThing(chunk), bench.InteractionCell + new IntVec3(i - 1, 0, -1), map);

            // Competing work: plenty to haul, and Hauling ranked above Crafting for everyone.
            for (int i = 0; i < 30; i++) SpawnStack(map, ThingDefOf.WoodLog, 25, origin + new IntVec3(i % 10, 0, 7 + i / 10));
            if (savedPriorities.Count == 0)
            {
                savedManualPriorities = Find.PlaySettings.useWorkPriorities;
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (p.workSettings == null) continue;
                    savedPriorities[p] = (p.workSettings.GetPriority(WorkTypeDefOf.Hauling), p.workSettings.GetPriority(WorkTypeDefOf.Crafting));
                }
            }
            Find.PlaySettings.useWorkPriorities = true;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.workSettings == null) continue;
                if (!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)) p.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
                if (!p.WorkTypeIsDisabled(WorkTypeDefOf.Crafting)) p.workSettings.SetPriority(WorkTypeDefOf.Crafting, 3);
            }
            Log.Message("[Colony Doctor] Outcompeted test built: O1 expect Outcompeted by Hauling while there is wood to haul, " +
                        "then Working. Use \"Restore work priorities\" afterwards.");
            Messages.Message("Colony Doctor: outcompeted test built; Hauling 1, Crafting 3 for everyone.", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("Colony Doctor", "Restore work priorities", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RestorePriorities()
        {
            foreach (var kv in savedPriorities)
            {
                if (kv.Key.Destroyed || kv.Key.workSettings == null) continue;
                if (!kv.Key.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)) kv.Key.workSettings.SetPriority(WorkTypeDefOf.Hauling, kv.Value.hauling);
                if (!kv.Key.WorkTypeIsDisabled(WorkTypeDefOf.Crafting)) kv.Key.workSettings.SetPriority(WorkTypeDefOf.Crafting, kv.Value.crafting);
            }
            if (savedPriorities.Count > 0) Find.PlaySettings.useWorkPriorities = savedManualPriorities;
            Log.Message($"[Colony Doctor] Restored work priorities for {savedPriorities.Count} colonists.");
            savedPriorities.Clear();
        }

        private static IEnumerable<(string name, Func<Map, IntVec3, string> build)> Stations(Map map)
        {
            ThingDef rockA = ChunkFor("Granite") ?? ChunkFor("Sandstone");
            ThingDef rockAbsent = new[] { "Marble", "Slate", "Limestone", "Sandstone", "Granite" }
                .Select(ChunkFor).FirstOrDefault(c => c != null && c != rockA && map.listerThings.ThingsOfDef(c).Count == 0);

            yield return ("Unpowered", (m, at) =>
            {
                Bench(m, "ElectricStove", at, "CookMealSimple", "T1 Unpowered");
                return "Bench problem / Unpowered";
            });
            yield return ("Unfueled", (m, at) =>
            {
                Thing stove = Bench(m, "FueledStove", at, "CookMealSimple", "T2 Unfueled");
                var fuel = stove.TryGetComp<CompRefuelable>();
                fuel?.ConsumeFuel(fuel.Fuel);
                return "Bench problem / Unfueled (until someone refuels it)";
            });
            yield return ("Interaction blocked", (m, at) =>
            {
                Thing bench = Bench(m, "TableStonecutter", at, BlocksRecipe(rockA), "T3 Blocked");
                Spawn(m, ThingDefOf.Wall, bench.InteractionCell, GenStuff.DefaultStuffFor(ThingDefOf.Wall));
                return "Bench problem / Blocked";
            });
            yield return ("Skill range", (m, at) =>
            {
                Thing bench = Bench(m, "HandTailoringBench", at, SimpleApparelRecipe(), "T4 Skill 20-20");
                ((Bill)((IBillGiver)bench).BillStack.Bills[0]).allowedSkillRange = new IntRange(20, 20);
                SpawnStack(m, ThingDefOf.Cloth, 200, at + new IntVec3(3, 0, 0));
                return "No eligible worker / Skill range";
            });
            yield return ("Work type disabled", (m, at) =>
            {
                Thing bench = Bench(m, "FueledSmithy", at, FirstProductRecipe("FueledSmithy"), "T5 Work disabled");
                var fuel = bench.TryGetComp<CompRefuelable>();
                fuel?.Refuel(fuel.Props.fuelCapacity);
                Bill bill = ((IBillGiver)bench).BillStack.Bills[0];
                WorkTypeDef work = BillWorkGivers.For(bench, bill)?.workType;
                if (work != null)
                {
                    foreach (Pawn p in m.mapPawns.FreeColonistsSpawned) p.workSettings?.Disable(work);
                }
                return $"No eligible worker / Work type disabled ({work?.labelShort}; disabled for all colonists)";
            });
            yield return ("Pawn restriction", (m, at) =>
            {
                Thing bench = Bench(m, "TableStonecutter", at, BlocksRecipe(rockA), "T6 Slaves only");
                ((IBillGiver)bench).BillStack.Bills[0].SetAnySlaveRestriction();
                return "No eligible worker / Pawn restriction (if you have no slaves)";
            });
            yield return ("Outside radius", (m, at) =>
            {
                Thing bench = Bench(m, "HandTailoringBench", at, SimpleApparelRecipe(), "T7 Radius 6");
                ((IBillGiver)bench).BillStack.Bills[0].ingredientSearchRadius = 6f;
                IntVec3 far = at + new IntVec3(0, 0, -20);
                if (!far.InBounds(m)) far = at + new IntVec3(0, 0, 20);
                SpawnStack(m, ThingDefOf.Cloth, 200, far);
                return "Materials missing / Outside radius (cloth ~20 tiles away, radius 6)";
            });
            yield return ("Forbidden", (m, at) =>
            {
                Bench(m, "TableButcher", at, "ButcherCorpseFlesh", "T8 Forbidden corpse");
                Pawn animal = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"));
                GenSpawn.Spawn(animal, at + new IntVec3(3, 0, 0), m);
                animal.Kill(null, null);
                animal.Corpse?.SetForbidden(true, false);
                return "Materials missing / Forbidden (if no other corpses on the map)";
            });
            if (rockAbsent != null)
            {
                yield return ("None on map", (m, at) =>
                {
                    Bench(m, "TableStonecutter", at, BlocksRecipe(rockAbsent), "T9 No " + rockAbsent.label);
                    return "Materials missing / None on map";
                });
            }
            yield return ("Output blocked", (m, at) =>
            {
                Thing bench = Bench(m, "TableStonecutter", at, BlocksRecipe(rockA), "T10 Output blocked");
                SpawnStack(m, rockA, 1, at + new IntVec3(3, 0, 0));
                SpawnStack(m, rockA, 1, at + new IntVec3(3, 0, 1));
                var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, m.zoneManager);
                m.zoneManager.RegisterZone(zone);
                zone.AddCell(at + new IntVec3(-3, 0, 0));
                zone.settings.filter.SetDisallowAll();
                ((Bill_Production)((IBillGiver)bench).BillStack.Bills[0]).SetStoreMode(BillStoreModeDefOf.SpecificStockpile, zone.GetSlotGroup());
                return "Working, with a \"Where products go\" note (stockpile rejects the blocks)";
            });
            yield return ("Everything available", (m, at) =>
            {
                Bench(m, "TableStonecutter", at, BlocksRecipe(rockA), "T11 Should work");
                SpawnStack(m, rockA, 1, at + new IntVec3(3, 0, 0));
                return "Working (or Waiting for a worker while colonists are busy)";
            });
        }

        // ---- Helpers ---------------------------------------------------------------------------------------------

        private static Thing Bench(Map map, string benchDef, IntVec3 at, string recipeDef, string billName)
            => Bench(map, benchDef, at, DefDatabase<RecipeDef>.GetNamed(recipeDef), billName);

        private static Thing Bench(Map map, string benchDef, IntVec3 at, RecipeDef recipe, string billName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(benchDef);
            Thing bench = Spawn(map, def, at, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            if (recipe == null) throw new InvalidOperationException($"No suitable recipe for {benchDef}");
            var bill = (Bill_Production)BillUtility.MakeNewBill(recipe, null);
            bill.RenamableLabel = billName;
            bill.repeatMode = BillRepeatModeDefOf.Forever;
            ((IBillGiver)bench).BillStack.AddBill(bill);
            return bench;
        }

        private static Thing Spawn(Map map, ThingDef def, IntVec3 at, ThingDef stuff)
        {
            Thing t = ThingMaker.MakeThing(def, stuff);
            t.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(t, at, map, Rot4.North, WipeMode.Vanish);
        }

        private static void SpawnStack(Map map, ThingDef def, int count, IntVec3 at)
        {
            Thing t = ThingMaker.MakeThing(def);
            t.stackCount = Math.Min(count, def.stackLimit);
            GenPlace.TryPlaceThing(t, at, map, ThingPlaceMode.Near);
        }

        private static void ClearPlot(Map map, CellRect plot)
        {
            foreach (IntVec3 c in plot)
            {
                List<Thing> things = c.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    bool natural = t.def.category == ThingCategory.Plant || t.def.category == ThingCategory.Filth
                                   || (t.def.building?.isNaturalRock ?? false) || (t.def.thingCategories?.Any(tc => tc.defName == "StoneChunks") ?? false);
                    if (natural) t.Destroy(DestroyMode.Vanish);
                }
                if (!map.terrainGrid.TerrainAt(c).affordances.Contains(TerrainAffordanceDefOf.Heavy))
                {
                    map.terrainGrid.SetTerrain(c, TerrainDefOf.Concrete);
                }
            }
        }

        private static ThingDef ChunkFor(string rock) => DefDatabase<ThingDef>.GetNamedSilentFail("Chunk" + rock);

        private static RecipeDef BlocksRecipe(ThingDef chunk)
            => chunk == null ? null : DefDatabase<RecipeDef>.GetNamedSilentFail("Make_StoneBlocks" + chunk.defName.Substring("Chunk".Length));

        private static RecipeDef SimpleApparelRecipe()
            => DefDatabase<ThingDef>.GetNamed("HandTailoringBench").AllRecipes.FirstOrDefault(r =>
                r.ProducedThingDef != null && r.ProducedThingDef.IsApparel && r.skillRequirements.NullOrEmpty()
                && r.ingredients.Count == 1 && r.ingredients[0].filter.Allows(ThingDefOf.Cloth)
                && r.fixedIngredientFilter.Allows(ThingDefOf.Cloth));

        private static RecipeDef FirstProductRecipe(string bench)
            => DefDatabase<ThingDef>.GetNamed(bench).AllRecipes.FirstOrDefault(r => r.ProducedThingDef != null);
    }
}
