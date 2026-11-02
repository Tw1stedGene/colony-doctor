# Contributing to Colony Doctor

This repo is the RimWorld mod itself: the repo root is the mod folder (`About/`, `1.6/`, `Defs/`,
`Languages/`), and the C# source and tests live under `Source/`.
Players don't need any of this; see the [README](README.md).

## Architecture

```
 every 250 ticks, per home map                     on demand
┌───────────────────────────────────┐            ┌──────────────────────────────┐
│ MapTracker                        │            │ Bottlenecks tab, alerts      │
│   bills      -> BillDiagnoser     │  history   │   ranked list, timeline,     │
│   sites      -> ConstructionDiag. │ ─────────► │   breakdown, explanations,   │
│   modded     -> ProbeScheduler    │ ring buffer│   Jump to                    │
└───────────────────────────────────┘            └──────────────────────────────┘
```

- **`Sampling/`** finds the problems to watch on each home map (`MapTracker`), samples them on a
  fixed interval, and runs rate-limited probes for benches that bypass the vanilla bill work giver
  (`ProbeScheduler`, with `JobFailReasonGuard` saving and restoring vanilla's static fail reason).
  Probes are off under the Multiplayer mod.
- **`Diagnosis/`** decides, for one bill at one moment, whether it is being worked on and, if not,
  the reason and detail (`BillDiagnoser`). It mirrors the ingredient and worker checks in
  `WorkGiver_DoBill` rather than parsing localized `JobFailReason` text.
- **`Construction/`** groups touching blueprints and frames into sites and diagnoses them the same
  way.
- **`Core/`** holds the pure, RimWorld-free logic: reasons and their precedence, the per-item history
  ring buffer, and aggregation over the last hour, day and quadrum. The tests compile these files
  directly.
- **`UI/`** is the Bottlenecks main tab, the stalled alerts, reason colours and explanations.

Diagnosis is read-only: it must not change game state, create reservations or call `Rand`. Nothing is
saved; history rebuilds after a load.

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
| Build test stations | Click a spot on the map: builds one bench and bill per diagnosis scenario, named "T1 …", "T2 …" after the reason it should show. Let about an in-game hour pass, then open Bottlenecks. |
| Build construction test sites | Click a spot: builds construction sites for the construction scenarios. |
| Log bill diagnoses | Writes every tracked bill and construction site, its last reason and detail, and the sampling cost to the log. |

The test actions change the map (and, for the work-type scenario, colonists' work settings), so only
use them on a test colony. In Development mode the Bottlenecks tab also shows the sampling and probe
cost per map.

### 4. Unattended runs (autopilot)

`Dev/Autopilot.cs` runs the game without anyone at the keyboard. It's off unless
`COLONYDOCTOR_AUTOPILOT` names a save:

| Variable | Effect |
|---|---|
| `COLONYDOCTOR_AUTOPILOT=<save>` | Load that save (file name without `.rws`) at startup and run at superfast speed. |
| `COLONYDOCTOR_AUTOPILOT_TICKS=<n>` | Write "Log bill diagnoses" to the log after `n` ticks (default 2500), then every 2500 ticks. |
| `COLONYDOCTOR_AUTOPILOT_SAVEAS=<name>` | Save the game under that name after the first dump. |
| `COLONYDOCTOR_AUTOPILOT_QUIT=1` | Quit after the first dump (and save). |

```sh
COLONYDOCTOR_AUTOPILOT="Test colony" COLONYDOCTOR_AUTOPILOT_QUIT=1 \
  "/path/to/RimWorld/RimWorldMac.app/Contents/MacOS/RimWorld by Ludeon Studios"
```

Then read the dump in the game's `Player.log`.

### Tests

```sh
msbuild -restore Source/ColonyDoctor.Tests/ColonyDoctor.Tests.csproj
mono Source/ColonyDoctor.Tests/bin/ColonyDoctor.Tests.exe
```

The tests cover the pure logic in `Source/ColonyDoctor/Core/` (history, aggregation, reason
precedence, site matching). They need no RimWorld references and run as a self-executing NUnitLite
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
Source/ColonyDoctor/     the mod (Core/, Sampling/, Diagnosis/, Construction/, UI/, Dev/)
Source/ColonyDoctor.Tests/  NUnitLite tests for Core/
```
