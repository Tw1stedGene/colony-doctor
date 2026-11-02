using System;
using System.Linq;
using RimWorld;
using Verse;

namespace ColonyDoctor.Dev
{
    /// <summary>
    /// Unattended test runs, for checking the mod without anyone at the keyboard.
    /// Off unless the COLONYDOCTOR_AUTOPILOT environment variable names a save (file name without .rws).
    /// The save is loaded at startup, the game runs at superfast speed, and "Log bill diagnoses" is written
    /// to the log after COLONYDOCTOR_AUTOPILOT_TICKS ticks (default 2500) and every 2500 ticks after.
    /// With COLONYDOCTOR_AUTOPILOT_SAVEAS set, the game is saved under that name after the first dump.
    /// COLONYDOCTOR_AUTOPILOT_SETUP=outcompeted, hauling or fields builds that test next to the first colonist after loading.
    /// With COLONYDOCTOR_AUTOPILOT_QUIT=1, the game quits after the first dump (and save).
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Autopilot
    {
        private const int DumpEvery = 2500;
        private static readonly string SaveName = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT");
        private static readonly string SaveAs = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_SAVEAS");
        private static readonly string Setup = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_SETUP");
        private static readonly bool Quit = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_QUIT") == "1";
        private static readonly int FirstDumpAfter =
            int.TryParse(Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_TICKS"), out int t) ? t : DumpEvery;

        private static int startTick = -1;
        private static int nextDump = -1;
        private static bool finished;
        private static bool saved;

        public static bool Active => !string.IsNullOrEmpty(SaveName);

        static Autopilot()
        {
            if (!Active) return;
            Log.Message($"[Colony Doctor] Autopilot: loading save '{SaveName}'.");
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                if (Current.ProgramState == ProgramState.Entry)
                    GameDataSaveLoader.LoadGame(SaveName);
            });
        }

        /// <summary>Called every frame while playing.</summary>
        public static void Update()
        {
            if (!Active || finished || Find.CurrentMap == null) return;
            int now = Find.TickManager.TicksGame;
            if (startTick < 0)
            {
                startTick = now;
                nextDump = now + FirstDumpAfter;
                Log.Message($"[Colony Doctor] Autopilot: game loaded at tick {now}, first dump at {nextDump}.");
                if (Setup == "outcompeted")
                {
                    Map map = Find.CurrentMap;
                    Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    IntVec3 origin = (anchor?.Position ?? map.Center) + new IntVec3(6, 0, 6);
                    TestScenarios.BuildOutcompetedTest(map, origin);
                }
                else if (Setup == "fields")
                {
                    Map map = Find.CurrentMap;
                    Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    TestScenarios.BuildFieldsTest(map, anchor?.Position ?? map.Center);
                }
                else if (Setup == "hauling")
                {
                    Map map = Find.CurrentMap;
                    Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    TestScenarios.BuildHaulingTest(map, (anchor?.Position ?? map.Center) + new IntVec3(-10, 0, 6));
                }
            }
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Superfast)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            if (now < nextDump) return;

            TestScenarios.LogDiagnoses();
            nextDump = now + DumpEvery;

            if (!string.IsNullOrEmpty(SaveAs) && !saved)
            {
                saved = true;
                GameDataSaveLoader.SaveGame(SaveAs);
                Log.Message($"[Colony Doctor] Autopilot: saved as '{SaveAs}'.");
            }
            if (Quit)
            {
                finished = true;
                Log.Message("[Colony Doctor] Autopilot: quitting.");
                Root.Shutdown();
            }
        }
    }
}
