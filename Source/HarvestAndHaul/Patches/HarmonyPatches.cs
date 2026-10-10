using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HarvestAndHaul
{
    /// <summary>
    /// All patches are plain prefixes/postfixes (no transpilers) and are applied one by one, so a
    /// missing target only disables the part of the mod that depends on it.
    /// </summary>
    public static class HarmonyPatches
    {
        public static void Apply(Harmony harmony)
        {
            bool decisionPatched =
                TryPatch(harmony, AccessTools.Method(typeof(Pawn_JobTracker), "TryFindAndStartJob"),
                    prefix: nameof(Patch_TryFindAndStartJob.Prefix), finalizer: nameof(Patch_TryFindAndStartJob.Finalizer), patchClass: typeof(Patch_TryFindAndStartJob))
                && TryPatch(harmony, AccessTools.Method(typeof(Pawn_JobTracker), "DetermineNextJob"),
                    postfix: nameof(Patch_DetermineNextJob.Postfix), patchClass: typeof(Patch_DetermineNextJob));

            // Without the delivery decision pawns would hoard harvest, so collecting is switched off.
            HarvestHaulUtility.DeliveryAvailable = decisionPatched;
            if (!decisionPatched)
                Log.Error(HarvestAndHaulMod.LogPrefix + "Could not hook into job selection; harvest collecting is disabled.");

            TryPatch(harmony, AccessTools.Method(typeof(Plant), nameof(Plant.PlantCollected)),
                postfix: nameof(Patch_Plant_PlantCollected.Postfix), patchClass: typeof(Patch_Plant_PlantCollected));

            TryPatch(harmony, AccessTools.Method(typeof(JobGiver_DropUnusedInventory), "Drop"),
                prefix: nameof(Patch_JobGiver_DropUnusedInventory_Drop.Prefix), patchClass: typeof(Patch_JobGiver_DropUnusedInventory_Drop));
        }

        private static bool TryPatch(Harmony harmony, MethodBase target, Type patchClass,
            string prefix = null, string postfix = null, string finalizer = null)
        {
            if (target == null)
            {
                Log.Error(HarvestAndHaulMod.LogPrefix + "Patch target for " + patchClass.Name + " not found.");
                return false;
            }
            try
            {
                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(patchClass, prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(patchClass, postfix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(patchClass, finalizer));
                return true;
            }
            catch (Exception e)
            {
                Log.Error(HarvestAndHaulMod.LogPrefix + "Failed to patch " + target.DeclaringType?.Name + "." + target.Name + ": " + e);
                return false;
            }
        }
    }

    /// <summary>After a plant was harvested or cut, put the yield into the harvester's inventory.</summary>
    public static class Patch_Plant_PlantCollected
    {
        public static void Postfix(Plant __instance, Pawn by)
        {
            if (by == null)
                return;
            try
            {
                HarvestHaulUtility.Notify_PlantCollected(by, __instance);
            }
            catch (Exception e)
            {
                Log.ErrorOnce(HarvestAndHaulMod.LogPrefix + "Error while collecting harvest: " + e, 0x48A1);
            }
        }
    }

    /// <summary>
    /// Marks the window in which a pawn is choosing a new job because its previous one ended.
    /// Periodic re-evaluations of a running job (CheckForJobOverride) are left untouched.
    /// </summary>
    public static class Patch_TryFindAndStartJob
    {
        public static Pawn choosingPawn;

        public static void Prefix(Pawn ___pawn, out Pawn __state)
        {
            __state = choosingPawn;
            choosingPawn = ___pawn;
        }

        public static void Finalizer(Pawn __state)
        {
            choosingPawn = __state;
        }
    }

    /// <summary>Swap the chosen job for "deliver harvest" when the pawn should bring its harvest to storage first.</summary>
    public static class Patch_DetermineNextJob
    {
        public static void Postfix(Pawn ___pawn, ref ThinkResult __result)
        {
            if (___pawn == null || Patch_TryFindAndStartJob.choosingPawn != ___pawn)
                return;
            try
            {
                HarvestHaulUtility.TryReplaceNextJob(___pawn, ref __result);
            }
            catch (Exception e)
            {
                Log.ErrorOnce(HarvestAndHaulMod.LogPrefix + "Error while choosing a delivery job: " + e, 0x48A2);
            }
        }
    }

    /// <summary>
    /// Vanilla makes pawns in the home area drop raw food from their inventory. Keep harvest that
    /// is on its way to storage.
    /// </summary>
    public static class Patch_JobGiver_DropUnusedInventory_Drop
    {
        public static bool Prefix(Pawn pawn, Thing thing)
        {
            HarvestTracker tracker = HarvestTracker.Instance;
            return tracker == null || !tracker.ShouldKeepInInventory(pawn, thing);
        }
    }
}
