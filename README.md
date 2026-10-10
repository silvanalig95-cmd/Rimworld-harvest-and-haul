# Harvest and Haul (RimWorld 1.6)

Pawns bring the items their own work produces to storage.

In vanilla, a pawn harvests a field, chops trees, mines ore or deconstructs a wall and leaves
everything on the ground for somebody else to haul later, often while walking back to base
anyway. With this mod the pawn puts those items into its inventory while it works and delivers
them itself.

## Where items get picked up

| Source | What gets collected |
|---|---|
| Plants | Harvesting crops and berries, chopping trees, cutting plants. **This includes cutting as part of another job**, e.g. clearing a cell for sowing or for a building. |
| Mining | Steel, components, silver, gold, plasteel, uranium, jade and other mined resources. |
| Deep drilling | Every portion the drill produces. When full, the driller stops, delivers and can come back. |
| Animals | Wool, milk and other products from shearing and milking. |
| Deconstruction | The materials a building or floor gives back when it is taken apart. |
| Fishing (Odyssey) | Fish and other catches. |
| Stone / slag chunks | Off by default. A single chunk fills most of an inventory. |

Each source can be switched off in the settings.

## How it behaves

- **Always fills up the inventory.** Whatever a pawn's work drops goes into its inventory, up
  to its carrying capacity. This happens even when the drop is only a side effect of another
  job.
- **Keeps working while there is room.** The pawn carries on with the same kind of work:
  harvesting then sowing, mining cell after cell, milking one cow after another,
  deconstructing then building. "Same kind" means the next job uses the same skill (Plants,
  Mining, Animals, Construction...), so mods that split up work types are covered too.
- **When something no longer fits, it delivers.** The pawn finishes what it is doing, picks up
  the stack that didn't fit into its hands, brings everything to storage, and can come back
  for more afterwards.
- **When done with that kind of work, it delivers before doing anything else.** If the next
  job is different work, eating, sleeping, recreation, meditation or idling, it drops off its
  items first.
- **Delivering is part of the work.** Pawns who are incapable of hauling or dumb labor still
  bring in what they produced. It does not depend on the Hauling work type.
- **What comes first:** drafting, mental states, fleeing and fighting, emergency work
  (firefighting, emergency tending), medical rest, urgent hunger or exhaustion, rituals and
  caravans, and anything the player ordered or queued.
- **Pawns that can barely carry anything more** (e.g. in heavy armor) still fill their
  inventory. They only stop work for a delivery trip once they carry a minimum weight, so they
  don't walk to storage after every plant.
- If no storage accepts an item, the pawn drops it, and the normal haulers take over.
- Colony mechanoids (agrihands etc.) also collect and deliver. You can turn this off.

## Settings

Options → Mod settings → Harvest and Haul

| Setting | Default | What it does |
|---|---|---|
| Pawns collect what their work drops | on | Master switch. Items already being carried are still delivered when it's turned off. |
| Collect items from … | all on, chunks off | One switch per source (see the table above). |
| Fill inventory up to | 100 % | Share of the pawn's carrying capacity that may be filled. |
| Stop working to deliver only with at least X kg collected | 5 kg | Below this, a full pawn keeps working and delivers when it is done with that kind of work. |
| Deliver after at most X hours of the same kind of work | 8 h | A pawn that keeps working delivers after this long anyway. |
| Deliver before eating, sleeping or recreation | on | Drop off items before non-urgent needs. |
| Colony mechanoids collect and deliver too | on | Applies to agrihands and similar mechs. |
| Verbose logging | off | Logs decisions, useful for bug reports. |

## Compatibility

Compatibility was a design goal:

- **No XML patches** to existing defs, think trees or races. The mod adds one JobDef.
- **Only plain Harmony prefixes/postfixes, no transpilers.** Each is applied separately and
  fails safely. If a game update moves one of the methods, only that one source stops being
  collected.
  - Item sources: `Plant.PlantCollected`, `Mineable.DestroyMined`, `CompDeepDrill.TryProducePortion`,
    `CompHasGatherableBodyResource.Gathered`, `JobDriver_Deconstruct.FinishedRemoving`,
    `JobDriver_RemoveFloor.DoEffect`, `FishingUtility.GetCatchesFor`. These only observe what
    was dropped and never change what the game does.
  - `Pawn_JobTracker.TryFindAndStartJob` (prefix/finalizer) and `Pawn_JobTracker.DetermineNextJob`
    (postfix) can swap the chosen job for "deliver items". This only happens for pawns carrying
    collected items, and only for job sources known to be safe to delay: normal work, idling,
    and (optionally) food, rest, joy and meditation.
  - `JobGiver_DropUnusedInventory.Drop` (prefix) stops vanilla from dumping raw food out of the
    inventory while it's on its way to storage.
- If the job-selection patch can't be applied (e.g. a future game update renames something),
  collecting switches itself off, so pawns never hoard items.
- Uses the vanilla storage search and hauling toils, so stockpiles, shelves, fridges and storage
  mods such as LWM's Deep Storage or Adaptive Storage Framework work normally.
- Things are tracked as "this pawn owes N of item X", so harvest merging with a stack the pawn
  already had (a doctor's herbal medicine, say) is handled correctly.
- **Pick Up And Haul**: compatible. Both mods track their own items separately.
- **Combat Extended**: if CE is loaded, its bulk/weight limits are respected (reflection only,
  with no hard dependency). CE's loadout system may still drop items that aren't in a pawn's
  loadout. Those then lie on the ground like in vanilla.
- **Safe to add to an existing save.** If you remove it later, you may get a one-time error about
  a missing class or job. Items still in pawns' inventories stay there as normal inventory.

## Installing

1. Subscribe to / install [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077).
2. Copy this whole folder (with `About`, `1.6`, `Defs`, `Languages`, `LoadFolders.xml`) into
   `RimWorld/Mods/HarvestAndHaul`.
3. Enable **Harvest and Haul** below Harmony in the mod list.

The compiled DLL is included in `1.6/Assemblies`, so no build step is needed.

## Building from source

Requires the .NET SDK (6 or newer). The RimWorld reference assemblies and Harmony are pulled
from NuGet, so the game doesn't need to be installed to compile.

```sh
cd Source/HarvestAndHaul
dotnet build -c Release
```

The output goes to `1.6/Assemblies/HarvestAndHaul.dll`.

## Layout

```
About/About.xml                     mod metadata (packageId silvanalig95.HarvestAndHaul)
LoadFolders.xml                     loads / and 1.6/ on RimWorld 1.6
1.6/Assemblies/HarvestAndHaul.dll   compiled mod
Defs/JobDefs/                       the "deliver harvest" job
Languages/English/Keyed/            UI strings
Source/HarvestAndHaul/              C# source
```
