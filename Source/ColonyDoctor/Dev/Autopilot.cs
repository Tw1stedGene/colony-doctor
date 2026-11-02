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
    /// COLONYDOCTOR_AUTOPILOT_SETUP=outcompeted, hauling and/or fields (comma-separated) builds that test next to the first colonist after loading.
    /// With COLONYDOCTOR_AUTOPILOT_FIXTEST=1, every offered fix is applied and undone after each dump (Dev/FixTest).
    /// With COLONYDOCTOR_AUTOPILOT_QUIT=1, the game quits after COLONYDOCTOR_AUTOPILOT_DUMPS dumps (default 1).
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Autopilot
    {
        private const int DumpEvery = 2500;
        private static readonly string SaveName = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT");
        private static readonly string SaveAs = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_SAVEAS");
        private static readonly string Setup = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_SETUP");
        private static readonly bool Quit = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_QUIT") == "1";
        private static readonly int Dumps =
            int.TryParse(Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_DUMPS"), out int n) && n > 0 ? n : 1;
        private static int dumpsDone;
        private static int nextFixCheck;
        private static readonly bool FixTestOn = Environment.GetEnvironmentVariable("COLONYDOCTOR_AUTOPILOT_FIXTEST") == "1";
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
                string[] setups = (Setup ?? "").Split(',');
                if (Array.IndexOf(setups, "outcompeted") >= 0)
                {
                    Map map = Find.CurrentMap;
                    Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    IntVec3 origin = (anchor?.Position ?? map.Center) + new IntVec3(6, 0, 6);
                    TestScenarios.BuildOutcompetedTest(map, origin);
                }
                if (Array.IndexOf(setups, "fields") >= 0)
                {
                    Map map = Find.CurrentMap;
                    Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    TestScenarios.BuildFieldsTest(map, anchor?.Position ?? map.Center);
                }
                if (Array.IndexOf(setups, "hauling") >= 0)
                {
                    Map map = Find.CurrentMap;
                    Pawn anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                    TestScenarios.BuildHaulingTest(map, (anchor?.Position ?? map.Center) + new IntVec3(-10, 0, 6));
                }
            }
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Superfast)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            if (FixTestOn && now >= nextFixCheck)
            {
                nextFixCheck = now + Core.GameTime.TicksPerSample;
                FixTest.CheckOutcompeted(Find.CurrentMap);
            }
            if (now < nextDump) return;

            TestScenarios.LogDiagnoses();
            if (FixTestOn) FixTest.Run(Find.CurrentMap);
            nextDump = now + DumpEvery;

            if (!string.IsNullOrEmpty(SaveAs) && !saved)
            {
                saved = true;
                GameDataSaveLoader.SaveGame(SaveAs);
                Log.Message($"[Colony Doctor] Autopilot: saved as '{SaveAs}'.");
            }
            dumpsDone++;
            if (Quit && dumpsDone >= Dumps)
            {
                finished = true;
                Log.Message("[Colony Doctor] Autopilot: quitting.");
                Root.Shutdown();
            }
        }
    }
}
