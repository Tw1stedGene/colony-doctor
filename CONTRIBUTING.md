# Contributing to Colony Doctor

This repo is the RimWorld mod itself: the repo root is the mod folder (`About/`, `1.6/`, `Defs/`,
`Languages/`), and the C# source and tests live under `Source/`.
Players don't need any of this; see the [README](README.md).

## Architecture

```
 every 250 ticks, per home map                         on demand
┌────────────────────────────────────────┐            ┌──────────────────────────────┐
│ MapTracker: discover, then sample      │            │ Bottlenecks tab, alerts      │
│   bills, sites, idle colonists,        │  history   │   ranked list, timeline,     │
│   hauling, growers, orders, patients,  │ ─────────► │   breakdown, explanations,   │
│   research  -> one diagnoser per kind  │ ring buffer│   fixes with undo, Jump to   │
└────────────────────────────────────────┘            └──────────────────────────────┘
```

- **`Sampling/`** finds what to watch on each home map (`MapTracker`) and samples each tracked
  problem once per 250-tick cycle, spread across ticks. One class per kind (`TrackedBill`,
  `ConstructionSite`, `TrackedPawn`, `TrackedHaulGroup`, `TrackedGrower`, `TrackedOrderGroup`,
  `TrackedPatient`, `TrackedResearch`) holds its title, location, Jump to and history. `ProbeScheduler`
  runs rate-limited probes for benches that bypass the vanilla bill work giver, with
  `JobFailReasonGuard` saving and restoring vanilla's static fail reason. Probes are off under the
  Multiplayer mod.
- **`Diagnosis/`** decides, for one problem at one moment, whether it is being worked on and, if not,
  the reason and detail. It mirrors vanilla's checks (for example the ingredient and worker checks in
  `WorkGiver_DoBill`) rather than parsing localized `JobFailReason` text. Shared pieces:
  `WorkerPool` (who is available for a work type, per cycle), `AreaGate` (allowed areas),
  `ActivityClassifier` (Outcompeted versus Waiting for a worker), `ReachCache`.
- **`Construction/`** groups touching blueprints and frames into sites and diagnoses them.
- **`Fixes/`** builds the one-click fixes for a stored diagnosis (`FixCatalog`). Every change the mod
  makes goes through `FixActions`, whose methods are `[SyncMethod]` for the Multiplayer mod.
- **`Core/`** holds the pure, RimWorld-free logic: reasons and their precedence, the per-item history
  ring buffer, aggregation over the last hour, day and quadrum, and fix maths. The tests compile these
  files directly.
- **`UI/`** is the Bottlenecks main tab, the alerts, reason colours, explanations and the fix flow
  (confirm, re-check, apply, Undo, Monitoring).
- **`Dev/`** holds the debug actions, test scenarios, fix test and the autopilot.

Diagnosis is read-only: it must not change game state, create reservations or call `Rand`. Only a fix
the player confirms changes anything. Nothing is saved; history rebuilds after a load.

## Building and testing

Only needed if you want to change the mod. Players never build anything.

You need Mono (its `msbuild` and `mono`). The RimWorld and Harmony reference assemblies come from
NuGet (`Krafs.Rimworld.Ref`, `Lib.Harmony`), so the game install isn't needed to compile.

### 1. Point the build at your game

```sh
cp Directory.Build.props.user.example Directory.Build.props.user
```

Edit `RimWorldModsDir` in `Directory.Build.props.user` (git-ignored) to your RimWorld `Mods` folder.
When it is set, every build copies `About/`, `1.6/`, `Defs/` and `Languages/` to
`<Mods>/ColonyDoctor`. Leave it out to build without deploying.

### 2. Build

```sh
msbuild -restore Source/ColonyDoctor/ColonyDoctor.csproj
# -> 1.6/Assemblies/ColonyDoctor.dll, then deployed to <Mods>/ColonyDoctor
```

Enable **Harmony** and **Colony Doctor** in the game's mod list and load a colony.

### 3. Try it in the game

Turn on Development mode, open **Debug actions** and look under **Colony Doctor**:

| Action | Effect |
|---|---|
| Build test stations | Click a spot: builds one bench and bill per bill scenario, named "T1 …", "T2 …" after the reason it should show. |
| Build construction test sites | Click a spot: builds construction sites for the construction scenarios. |
| Build hauling test | Click a spot: steel and meat walled off where nobody can reach them. |
| Build fields, orders and research test | Click a spot: an allowed area for everyone, a devilstrand field, a field outside the area, mining outside it, an unarmed hunt, a thrumbo to tame and a research project that needs a hi-tech bench. Removes colonists' ranged weapons. |
| Build outcompeted test (changes priorities) | A ready stonecutter's bill while everyone ranks Hauling above Crafting and has wood to haul. |
| Restore work priorities | Undoes the priority changes of the outcompeted test. |
| Log bill diagnoses | Writes every tracked problem of every kind, its last reason, detail and explanation, and the sampling cost to the log. |
| Test all fixes (apply + undo) | Applies every offered fix, checks it is no longer needed, undoes it and checks it is needed again. |

Let about an in-game hour pass after building, then open Bottlenecks. The test actions change the map,
work settings, allowed areas and the research project, so only use them on a test colony. In
Development mode the Bottlenecks tab also shows the sampling and probe cost per map.

### 4. Unattended runs (autopilot)

`Dev/Autopilot.cs` runs the game without anyone at the keyboard. It's off unless
`COLONYDOCTOR_AUTOPILOT` names a save:

| Variable | Effect |
|---|---|
| `COLONYDOCTOR_AUTOPILOT=<save>` | Load that save (file name without `.rws`) at startup and run at superfast speed. |
| `COLONYDOCTOR_AUTOPILOT_SETUP=<list>` | Build test scenarios after loading, comma-separated: `outcompeted`, `hauling`, `fields`, `extra` (unpowered hydroponics, forbidden targets, an injured patient on No care), `big` (about 200 bills and 300 loose stacks, with allocation measuring). |
| `COLONYDOCTOR_AUTOPILOT_TICKS=<n>` | Write the diagnosis dump after `n` ticks (default 2500), then every 2500 ticks. Each dump also logs active alerts and the sampling cost. |
| `COLONYDOCTOR_AUTOPILOT_FIXTEST=1` | Apply and undo every offered fix after each dump, and test the priority fix as soon as something becomes Outcompeted. |
| `COLONYDOCTOR_AUTOPILOT_KEEPFIX=1` | Apply one Widen radius fix and keep it (for save and reload checks). |
| `COLONYDOCTOR_AUTOPILOT_SAVEAS=<name>` | Save the game under that name after the first dump. |
| `COLONYDOCTOR_AUTOPILOT_QUIT=1` | Quit after `COLONYDOCTOR_AUTOPILOT_DUMPS` dumps (default 1). |

On macOS, start it through `open` so Steam and Workshop mods load (launching the binary directly
stalls):

```sh
open -n --env SteamAppId=294100 --env COLONYDOCTOR_AUTOPILOT="Test colony" \
  --env COLONYDOCTOR_AUTOPILOT_SETUP=hauling,fields --env COLONYDOCTOR_AUTOPILOT_FIXTEST=1 \
  --env COLONYDOCTOR_AUTOPILOT_QUIT=1 -a "/path/to/RimWorld/RimWorldMac.app"
```

Then read the dump in `Player.log`, which the game writes in full when it quits.

### Multiplayer check

Enable Prepatcher, Multiplayer and Colony Doctor. Host a save from the Multiplayer menu with Direct
on, then start a second copy with its own data folder and connect it:

```sh
open -n --env SteamAppId=294100 -a "/path/to/RimWorld/RimWorldMac.app" \
  --args -savedatafolder=/tmp/mpclient -connect=127.0.0.1:30502
```

Apply and undo a fix on each side and check the other side changes too, with no desync.

### Tests

```sh
msbuild -restore Source/ColonyDoctor.Tests/ColonyDoctor.Tests.csproj
mono Source/ColonyDoctor.Tests/bin/ColonyDoctor.Tests.exe
```

The tests cover the pure logic in `Source/ColonyDoctor/Core/` (history, aggregation, reason
precedence, site matching, Outcompeted, idle and hauling rules, fix maths). They need no RimWorld references and run as a self-executing NUnitLite
console app, so no .NET SDK is needed.

## Releasing

1. Set `<modVersion>` in `About/About.xml`.
2. Build, then zip a `ColonyDoctor/` folder holding `About/`, `1.6/`, `Defs/`, `Languages/` and
   `LICENSE` as `colony-doctor-mod-vX.Y.Z.zip`.
3. Tag `vX.Y.Z` and publish a GitHub release named "Colony Doctor X.Y.Z" with the zip attached and
   notes under **New**, **Improved** and **Update**.
4. Upload to the Steam Workshop from the game (Development mode, Mods → Upload).

## Repo layout

```
About/                   About.xml, Preview.png, ModIcon.png
1.6/Assemblies/          build output (ColonyDoctor.dll, git-ignored)
Defs/                    the Bottlenecks main tab
Languages/English/       all player-facing text
Source/ColonyDoctor/     the mod (Core/, Sampling/, Diagnosis/, Construction/, Fixes/, UI/, Dev/)
Source/ColonyDoctor.Tests/  NUnitLite tests for Core/
```
