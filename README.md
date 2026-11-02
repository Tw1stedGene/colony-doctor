# Colony Doctor

![Colony Doctor: the Bottlenecks tab explaining why a bill is stalled](.github/preview.png)

**Why isn't work getting done? Colony Doctor tells you.** A RimWorld mod.

You queued the bill three days ago. The room is still a blueprint. Colony Doctor watches your bills
and construction sites over time and tells you, in plain language, what has been holding each one
up: a bench without power, nobody allowed to do the work, materials that are missing or out of
reach, a tree in the way, or simply no one getting round to it.

RimWorld already knows many of these reasons, but it only shows them for one colonist at one moment,
when you right-click. Colony Doctor keeps track of them, so you see "stalled 70% of the last day,
mostly because of missing steel", not just "blocked right now".

## Features

### The Bottlenecks tab
A new main tab lists everything that's stalled, worst first. Switch between **Bills** and
**Construction**, and between the **last hour**, **last day** and **last quadrum**. Each row shows
what's stalled, where it is, the main reason and how much of that time it was stalled.

![The Bottlenecks tab: stalled bills ranked worst first](.github/screenshots/bottlenecks-tab.jpg)

### Why, in plain language
Select a row to see a timeline coloured by reason, a breakdown of what blocked it, and an explanation
for each reason, such as "The electric smithy has no power.", "Nobody has Crafting enabled in the
Work tab." or "12 steel is available, but the nearest is 48 tiles from the bench. The bill only
searches 30." **Jump to** takes the camera straight to the bench or site.

![A stalled bill's timeline, reason breakdown and explanations](.github/screenshots/bill-detail.jpg)

The reasons, in the order you'd fix them:
- **Bench problem:** no power, out of fuel, broken down, the work spot is blocked, or no one can
  reach the bench.
- **No eligible worker:** nobody has the work type enabled or is capable of it, the bill's worker
  restriction or skill range excludes everyone, an allowed area keeps them away, or everyone who
  could do it is downed, drafted or having a mental break.
- **Blocked** (construction): a plant, item, building or rock is in the way and has to be cleared
  first.
- **Materials missing:** none on the map, excluded by the bill's filter, forbidden, outside the
  ingredient radius, reserved by someone else, or unreachable.
- **Waiting for a worker:** nothing blocks it, but nobody has started yet.

Bills also get a note when finished products have nowhere to go, for example when no stockpile
accepts them.

### Construction sites
Blueprints and frames that touch are grouped into one site, so a planned room shows up as one row
("Granite wall ×14, wooden door ×1"), with the same timeline, reasons and Jump to as bills.

![A stalled construction site blocked by a tree](.github/screenshots/construction.jpg)

### Alerts
**Bills stalled (N)** and **Construction stalled (N)** appear on the right when something has been
stalled for at least half of the last day and still is. Click one to open the tab on that category.

## Install

You need **RimWorld 1.6** and **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)**.
No DLCs are needed.

1. **Get the mod.** Subscribe on the Steam Workshop, or download the mod zip from
   [Releases](https://github.com/Tw1stedGene/colony-doctor/releases) and unzip it into your RimWorld
   `Mods` folder.
2. **Enable it.** In RimWorld, open **Mods**, turn on **Harmony** and **Colony Doctor** (Colony
   Doctor below Harmony), and restart when asked.

## Getting started

1. **Load any colony.** New or existing, it doesn't matter.
2. **Let the game run for a while.** Colony Doctor starts watching as soon as the map loads. History
   is kept in memory only, so after loading a save it builds up again from that point.
3. **Open the Bottlenecks tab** in the bar at the bottom of the screen, or click a stalled alert.
4. **Pick a row, read why, and Jump to it.** Fix the cause the way you normally would.

**Settings** (Options → Mod settings → Colony Doctor):
- **Show "Bills stalled" alert:** turns the stalled alerts on or off.
- **Diagnose modded benches:** asks modded benches' own code why a bill can't be done. Without it,
  bills on those benches show as **Unknown**. Always off while the Multiplayer mod is active.

## Safe for your saves

- **Read-only.** Colony Doctor only looks. It never changes bills, work priorities, stockpiles or
  anything else in your colony.
- **Nothing is written to your save.** You can add or remove it in the middle of a game.
- **Light on performance.** It samples every 250 ticks and keeps a fixed amount of history per item.

## Troubleshooting

- **The tab says "No data yet".** Let the game run unpaused for a little while; the history fills up
  as time passes.
- **History is gone after loading.** That's expected: history isn't saved, and it builds up again
  after a load.
- **A bill shows "Unknown".** The bench uses a modded work giver. Turn on **Diagnose modded benches**
  in the settings (not available with Multiplayer). Some modded benches still can't be explained.
- **"Waiting for a worker" for a long time.** Nothing blocks the work, but your colonists keep
  choosing other jobs. Check their work priorities. Telling you which work wins is planned (see
  below).

## Compatibility

- Works with RimWorld 1.6, with or without DLCs. Needs Harmony.
- Safe to add to or remove from an existing save.
- Designed to be Multiplayer-safe: diagnosis never changes game state or uses randomness, and
  **Diagnose modded benches** is switched off while Multiplayer is active.
- Benches and bills added by other mods are covered when they use the game's normal bill system.
  Others fall back to **Unknown**, or to what the bench's own code reports when **Diagnose modded
  benches** is on.

## Coming next

Planned, not in the mod yet:
- **Outcompeted:** when eligible colonists are always busy with work they rank higher, say who and
  with what.
- **One-click fixes with undo** for common causes, always confirmed by you first.
- **Idle colonists:** why a colonist is standing around.
- **Hauling backlog:** what's waiting to be hauled and why.

## Support

Found a bug or have an idea? [Open an issue](https://github.com/Tw1stedGene/colony-doctor/issues).

Colony Doctor is free. If it helps your colony, you can
[buy me a coffee](https://buymeacoffee.com/tw1stedgene).

Want to help build it? See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT, see [LICENSE](LICENSE).

Colony Doctor is a fan project. It is not affiliated with, endorsed by or sponsored by Ludeon Studios.
