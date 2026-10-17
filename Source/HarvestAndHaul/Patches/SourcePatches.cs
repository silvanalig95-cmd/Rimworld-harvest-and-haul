using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HarvestAndHaul
{
    // Hooks for the places where a pawn's work drops items on the ground. All of them only
    // observe (prefixes return void) and swallow their own errors, so they never change what the
    // game itself does.

    internal static class SourcePatchUtility
    {
        public static void Run(Action action, string what, int key)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.ErrorOnce(HarvestAndHaulMod.LogPrefix + "Error while collecting " + what + ": " + e, key);
            }
        }

        public static HashSet<ThingDef> DefsOf(List<ThingDefCountClass> costs)
        {
            if (costs == null || costs.Count == 0)
                return null;
            var defs = new HashSet<ThingDef>();
            for (int i = 0; i < costs.Count; i++)
            {
                if (costs[i]?.thingDef != null)
                    defs.Add(costs[i].thingDef);
            }
            return defs;
        }
    }

    /// <summary>
    /// A plant was harvested or cut: harvesting crops, picking berries, chopping trees, and also
    /// cutting as part of another job (clearing a cell for sowing or for a building).
    /// </summary>
    public static class Patch_Plant_PlantCollected
    {
        public static void Postfix(Plant __instance, Pawn by)
        {
            if (by == null)
                return;
            SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_PlantCollected(by, __instance), "harvest", 0x48A1);
        }
    }

    /// <summary>A cell was mined out (ore, steel, components, ...).</summary>
    public static class Patch_Mineable_DestroyMined
    {
        public struct State
        {
            public Map map;
            public CellRect rect;
            public ThingDef product;
        }

        public static void Prefix(Mineable __instance, out State __state)
        {
            __state = default;
            if (__instance.Spawned)
            {
                __state.map = __instance.Map;
                __state.rect = __instance.OccupiedRect();
                __state.product = __instance.def?.building?.mineableThing;
            }
        }

        public static void Postfix(Pawn pawn, State __state)
        {
            if (pawn == null || __state.map == null || __state.product == null)
                return;
            SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_Mined(pawn, __state.map, __state.rect, __state.product), "mined resources", 0x48A3);
        }
    }

    /// <summary>A deep drill produced a portion.</summary>
    public static class Patch_CompDeepDrill_TryProducePortion
    {
        public static void Postfix(CompDeepDrill __instance, Pawn driller)
        {
            if (driller == null)
                return;
            SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_DeepDrillPortion(driller, __instance.parent), "deep drill output", 0x48A4);
        }
    }

    /// <summary>An animal was shorn or milked (or any other gatherable body resource).</summary>
    public static class Patch_CompHasGatherableBodyResource_Gathered
    {
        private static Func<CompHasGatherableBodyResource, ThingDef> resourceDefGetter;
        private static bool getterFailed;

        public static void Postfix(CompHasGatherableBodyResource __instance, Pawn doer)
        {
            if (doer == null || !EnsureGetter())
                return;
            SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_AnimalProductGathered(doer, resourceDefGetter(__instance)), "animal products", 0x48A5);
        }

        // ResourceDef is a protected property; build a fast accessor once.
        private static bool EnsureGetter()
        {
            if (resourceDefGetter != null)
                return true;
            if (getterFailed)
                return false;
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(CompHasGatherableBodyResource), "ResourceDef");
                if (getter != null)
                    resourceDefGetter = AccessTools.MethodDelegate<Func<CompHasGatherableBodyResource, ThingDef>>(getter);
            }
            catch (Exception e)
            {
                Log.Warning(HarvestAndHaulMod.LogPrefix + "Animal products will not be collected: " + e.Message);
            }
            getterFailed = resourceDefGetter == null;
            return !getterFailed;
        }
    }

    /// <summary>
    /// A building was deconstructed. Refunded materials go into the inventory; anything else it
    /// leaves behind (slag or stone chunks, fuel, contents...) is hauled right away by hand.
    /// </summary>
    public static class Patch_JobDriver_Deconstruct_FinishedRemoving
    {
        public struct State
        {
            public Map map;
            public CellRect rect;
            public HashSet<ThingDef> refunds;
            public HashSet<int> itemsBefore;
        }

        public static void Prefix(JobDriver_Deconstruct __instance, out State __state)
        {
            __state = default;
            try
            {
                Thing target = __instance.job?.targetA.Thing;
                if (target == null || !target.Spawned)
                    return;
                __state.map = target.Map;
                __state.rect = target.OccupiedRect();
                __state.refunds = SourcePatchUtility.DefsOf(target.CostListAdjusted());
                __state.itemsBefore = HarvestHaulUtility.SnapshotLeftoverArea(__state.map, __state.rect);
            }
            catch (Exception e)
            {
                __state = default;
                Log.ErrorOnce(HarvestAndHaulMod.LogPrefix + "Error while reading deconstruction refunds: " + e, 0x48A6);
            }
        }

        public static void Postfix(JobDriver_Deconstruct __instance, State __state)
        {
            Pawn pawn = __instance.pawn;
            if (pawn == null || __state.map == null)
                return;
            if (__state.refunds != null)
                SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_Deconstructed(pawn, __state.map, __state.rect, __state.refunds), "deconstruction refunds", 0x48A7);
            if (__state.itemsBefore != null)
                SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_DeconstructionLeftovers(pawn, __state.map, __state.rect, __state.itemsBefore), "deconstruction leftovers", 0x48AA);
        }
    }

    /// <summary>A floor was removed and refunded part of its materials.</summary>
    public static class Patch_JobDriver_RemoveFloor_DoEffect
    {
        public static void Prefix(JobDriver_RemoveFloor __instance, IntVec3 c, out HashSet<ThingDef> __state)
        {
            __state = null;
            Map map = __instance.pawn?.Map;
            if (map != null && c.InBounds(map))
                __state = SourcePatchUtility.DefsOf(c.GetTerrain(map)?.CostList);
        }

        public static void Postfix(JobDriver_RemoveFloor __instance, IntVec3 c, HashSet<ThingDef> __state)
        {
            Pawn pawn = __instance.pawn;
            if (pawn?.Map == null || __state == null)
                return;
            SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_Deconstructed(pawn, pawn.Map, CellRect.SingleCell(c), __state), "floor refunds", 0x48A8);
        }
    }

    /// <summary>Fish were caught (Odyssey). The catches are placed right after this returns.</summary>
    public static class Patch_FishingUtility_GetCatchesFor
    {
        public static void Postfix(Pawn pawn, IntVec3 cell, List<Thing> __result)
        {
            if (pawn == null || __result == null || !(pawn.jobs?.curDriver is JobDriver_Fish))
                return;
            SourcePatchUtility.Run(() => HarvestHaulUtility.Notify_FishCaught(pawn, cell, __result), "fish", 0x48A9);
        }
    }
}
