# Harvest and Haul (RimWorld 1.6)

Pawns bring their own harvest to storage.

In vanilla, a pawn harvests a field, chops trees or picks berries and leaves everything on the
ground for somebody else to haul later, often while walking back to base anyway. With this mod
the harvester puts the yield into its inventory while it works and delivers it itself.

## How it behaves

- **Harvest goes into the pawn's inventory.** Each time a plant is harvested or cut (crops, berry
  bushes, trees for wood, healroot, mushrooms, modded plants), its yield goes straight into the
  harvester's inventory.
- **It keeps harvesting while there is room.** The pawn does a whole run of harvests in a row,
  as in vanilla.
- **When full, it stops and delivers.** Once the next harvest would no longer fit within its
  carrying capacity, the pawn finishes the current plant, delivers everything to storage, and
  can come back for more afterwards.
- **When done with field work, it delivers before doing anything else.** If the pawn's next job
  is something other than field work (another job, eating, sleeping, recreation, meditation or
  idling), it drops off the harvest first.
- **Sowing counts as field work**, so "harvest, then re-sow the field" is not interrupted by a
  trip to storage. The pawn sows first and delivers afterwards. Field work here means any job
  from a work type that uses the Plants skill, so mods that split up Growing work types are
  covered too.
- **Delivering is part of harvesting.** Pawns who are incapable of hauling or dumb labor still
  bring in their own harvest. It does not depend on the Hauling work type.
- **What comes first:** drafting, mental states, fleeing and fighting, emergency work
  (firefighting, emergency tending), medical rest, urgent hunger or exhaustion, rituals and
  caravans, and anything the player ordered or queued.
- Pawns that can barely carry anything more (e.g. in heavy armor) leave the harvest on the
  ground like vanilla, so they don't make a trip after every plant.
- If no storage accepts an item, the pawn drops it, and the normal haulers take over.
- Colony mechanoids that harvest (agrihands) also deliver their harvest. You can turn this off.

## Settings

Options → Mod settings → Harvest and Haul

| Setting | Default | What it does |
|---|---|---|
| Harvesters collect what they harvest | on | Master switch. Harvest already being carried is still delivered when it's turned off. |
| Fill inventory up to | 100 % | Share of the pawn's carrying capacity that may be filled with harvest. Lower means more, shorter trips. |
| Only start collecting with at least X kg free | 5 kg | Below this free capacity, behave like vanilla. |
| Deliver after at most X hours of field work | 8 h | A pawn that keeps sowing or harvesting delivers after this long anyway. |
| Deliver before eating, sleeping or recreation | on | Drop off harvest before non-urgent needs. |
| Colony mechanoids deliver their harvest too | on | Applies to agrihands and similar mechs. |
| Verbose logging | off | Logs decisions, useful for bug reports. |

## Compatibility

Compatibility was a design goal:

- **No XML patches** to existing defs, think trees or races. The mod adds one JobDef.
- **Four small Harmony patches, no transpilers.** Each is applied separately and fails safely:
  - `Plant.PlantCollected` (postfix) puts the yield into the harvester's inventory.
  - `Pawn_JobTracker.TryFindAndStartJob` (prefix/finalizer) marks when a pawn is choosing a new job.
  - `Pawn_JobTracker.DetermineNextJob` (postfix) can swap the chosen job for "deliver harvest".
    It only does this for pawns carrying harvest, and only for job sources it knows are safe to
    delay: normal work, idling, and (optionally) food, rest, joy and meditation.
  - `JobGiver_DropUnusedInventory.Drop` (prefix) stops vanilla from dumping raw food out of the
    inventory while it's on its way to storage.
- If the job-selection patch can't be applied (e.g. a future game update renames something),
  collecting switches itself off, so pawns never hoard harvest.
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
