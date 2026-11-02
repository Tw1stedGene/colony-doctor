using ColonyDoctor.Sampling;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ColonyDoctor
{
    public class ColonyDoctorMod : Mod
    {
        public static ColonyDoctorSettings Settings { get; private set; }

        public ColonyDoctorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ColonyDoctorSettings>();
            new Harmony(content.PackageIdPlayerFacing).PatchAll(typeof(ColonyDoctorMod).Assembly);
            JobFailReasonGuard.WarnIfUnavailable();
            // Spec (problem-fixes): fixes and undo run as Multiplayer sync methods when the Multiplayer mod is active.
            if (Multiplayer.API.MP.enabled) Multiplayer.API.MP.RegisterAll();
            Log.Message($"[Colony Doctor] Loaded {typeof(ColonyDoctorMod).Assembly.GetName().Version} (probes {(Sampling.ProbeScheduler.Enabled ? "on" : "off")}).");
        }

        public override string SettingsCategory() => "ColonyDoctor_ModName".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.CheckboxLabeled("ColonyDoctor_SettingAlert".Translate(), ref Settings.showStalledBillsAlert,
                "ColonyDoctor_SettingAlertDesc".Translate());
            list.CheckboxLabeled("ColonyDoctor_SettingProbes".Translate(), ref Settings.enableProbes,
                "ColonyDoctor_SettingProbesDesc".Translate());
            list.End();
        }
    }

    public class ColonyDoctorSettings : ModSettings
    {
        public bool showStalledBillsAlert = true;
        public bool enableProbes = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref showStalledBillsAlert, "showStalledBillsAlert", true);
            Scribe_Values.Look(ref enableProbes, "enableProbes", true);
        }
    }
}
