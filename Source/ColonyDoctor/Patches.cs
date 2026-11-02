using System;
using ColonyDoctor.Sampling;
using HarmonyLib;
using Verse;

namespace ColonyDoctor
{
    /// <summary>
    /// Hooks that drive sampling and probes. Postfixes only; they never change what the patched methods do.
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.MapPostTick))]
    internal static class Patch_Map_MapPostTick
    {
        private static void Postfix(Map __instance)
        {
            try
            {
                MapTracker.For(__instance).Tick();
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Sampling failed: {e}", 0x0C0D0C73);
            }
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class Patch_Game_UpdatePlay
    {
        private static void Postfix()
        {
            try
            {
                ProbeScheduler.Update();
                Dev.Autopilot.Update();
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[Colony Doctor] Probing failed: {e}", 0x0C0D0C74);
            }
        }
    }
}
