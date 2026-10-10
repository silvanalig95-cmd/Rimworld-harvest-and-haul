using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace HarvestAndHaul
{
    public static class HarvestHaulUtility
    {
        /// <summary>Harvest products land at or right next to the harvester; look a bit wider to be safe.</summary>
        private const float PickupRadius = 2.9f;

        private const int DeliveryFailCooldownTicks = 600;

        /// <summary>
        /// False if the patch that makes pawns deliver could not be applied. Collecting is then
        /// switched off, so pawns never end up hoarding harvest in their inventory.
        /// </summary>
        public static bool DeliveryAvailable = true;

        private static HarvestAndHaulSettings Settings => HarvestAndHaulMod.Settings;

        private static readonly List<Thing> tmpCandidates = new List<Thing>();

        // ------------------------------------------------------------------
        // Who takes part
        // ------------------------------------------------------------------

        public static bool PawnCanCollect(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.Map == null)
                return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
                return false;
            if (pawn.inventory == null || pawn.carryTracker == null)
                return false;
            if (pawn.RaceProps.IsMechanoid)
            {
                if (!Settings.includeMechanoids)
                    return false;
            }
            else if (!pawn.RaceProps.Humanlike)
            {
                return false;
            }
            return MassUtility.CanEverCarryAnything(pawn) && MassUtility.Capacity(pawn) > 0f;
        }

        /// <summary>Free mass within the configured fill limit.</summary>
        public static float FreeMass(Pawn pawn)
        {
            return MassUtility.Capacity(pawn) * Settings.fillPercent - MassUtility.GearAndInventoryMass(pawn);
        }

        private static int CountThatFits(Pawn pawn, Thing thing, float unitMass)
        {
            int count;
            if (unitMass <= 0.0001f)
                count = thing.stackCount;
            else
                count = Mathf.Min(thing.stackCount, Mathf.FloorToInt(FreeMass(pawn) / unitMass + 0.0001f));
            if (count <= 0)
                return 0;
            return CombatExtendedCompat.LimitCount(pawn, thing, count);
        }

        // ------------------------------------------------------------------
        // Collecting
        // ------------------------------------------------------------------

        /// <summary>Called from the Plant.PlantCollected postfix.</summary>
        public static void Notify_PlantCollected(Pawn pawn, Plant plant)
        {
            ThingDef harvestedDef = plant.def?.plant?.harvestedThingDef;
            if (!Settings.enabled || !DeliveryAvailable || harvestedDef == null)
                return;
            if (!harvestedDef.EverStorable(false) || !PawnCanCollect(pawn))
                return;
            HarvestTracker tracker = HarvestTracker.Instance;
            if (tracker == null)
                return;

            Map map = pawn.Map;
            IntVec3 cell = pawn.Position;
            float expectedCount = plant.def.plant.harvestYield;
            CollectHarvest(pawn, harvestedDef, cell, map, expectedCount);
            tracker.QueueSweep(pawn, harvestedDef, cell, map, expectedCount);
        }

        /// <summary>
        /// Moves loose items of <paramref name="def"/> around <paramref name="center"/> into the
        /// pawn's inventory, as far as its carrying capacity allows.
        /// <paramref name="expectedCount"/> is the plant's nominal yield, used to judge whether
        /// another harvest of this kind would still fit.
        /// </summary>
        public static void CollectHarvest(Pawn pawn, ThingDef def, IntVec3 center, Map map, float expectedCount)
        {
            if (!Settings.enabled || !DeliveryAvailable || !PawnCanCollect(pawn) || pawn.Map != map)
                return;
            HarvestTracker tracker = HarvestTracker.Instance;
            if (tracker == null)
                return;

            tmpCandidates.Clear();
            int numCells = GenRadial.NumCellsInRadius(PickupRadius);
            for (int i = 0; i < numCells; i++)
            {
                IntVec3 c = center + GenRadial.RadialPattern[i];
                if (!c.InBounds(map))
                    continue;
                List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (t.def == def && CanCollect(pawn, t))
                    {
                        tmpCandidates.Add(t);
                    }
                }
            }
            if (tmpCandidates.Count == 0)
                return;

            float unitMass = tmpCandidates[0].GetStatValue(StatDefOf.Mass);
            float expectedMass = expectedCount * unitMass;
            HarvestLoad load = tracker.GetLoad(pawn);
            bool alreadyCarrying = load != null && !load.Empty;

            // Only start a new load if a full harvest fits and there is meaningful room left.
            // Otherwise behave like vanilla and leave it for the haulers.
            if (!alreadyCarrying && FreeMass(pawn) < Mathf.Max(expectedMass, Settings.minFreeMassToStart))
            {
                tmpCandidates.Clear();
                return;
            }

            int picked = 0;
            bool leftSomething = false;
            for (int i = 0; i < tmpCandidates.Count; i++)
            {
                Thing t = tmpCandidates[i];
                int count = CountThatFits(pawn, t, unitMass);
                if (count <= 0)
                {
                    leftSomething = true;
                    break;
                }
                if (count < t.stackCount)
                    leftSomething = true;

                IntVec3 pos = t.Position;
                Thing taken = t.SplitOff(count);
                int takenCount = taken.stackCount;
                if (pawn.inventory.innerContainer.TryAdd(taken, canMergeWithExistingStacks: true))
                {
                    picked += takenCount;
                }
                else
                {
                    // Should not happen with an inventory, but never lose items.
                    if (!taken.Destroyed && !taken.Spawned)
                        GenPlace.TryPlaceThing(taken, pos, map, ThingPlaceMode.Near);
                    leftSomething = true;
                    break;
                }
            }
            tmpCandidates.Clear();

            if (picked > 0)
            {
                load = tracker.GetOrCreateLoad(pawn);
                load.Add(def, picked);
                // Largest single harvest seen in this load: a conservative guess for the next one.
                load.lastYieldMass = Mathf.Max(load.lastYieldMass, picked * unitMass);
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} collected {picked}x {def.label} (free mass now {FreeMass(pawn):0.##} kg).");
            }

            if (load == null || load.Empty)
                return;

            // Stop harvesting if something did not fit, or the next harvest of this size would not fit.
            if (leftSomething || (picked > 0 && FreeMass(pawn) < Mathf.Max(load.lastYieldMass, expectedMass)))
                MarkFull(pawn, load);
        }

        private static bool CanCollect(Pawn pawn, Thing t)
        {
            if (!t.Spawned || t.Destroyed || t.stackCount <= 0)
                return false;
            if (t.IsForbidden(pawn) || t.IsBurning())
                return false;
            if (StoreUtility.IsInValidStorage(t))
                return false;
            // Someone else is already on their way to haul it.
            return pawn.CanReserve(t);
        }

        private static void MarkFull(Pawn pawn, HarvestLoad load)
        {
            if (!load.full)
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} is full and will deliver after this plant.");
            load.full = true;

            // Finish the plant currently being worked on, then end the job by dropping the
            // remaining queued plants. The job then succeeds through its normal vanilla flow.
            if (pawn.jobs?.curDriver is JobDriver_PlantWork)
                pawn.CurJob?.targetQueueA?.Clear();
        }

        // ------------------------------------------------------------------
        // Deciding when to deliver
        // ------------------------------------------------------------------

        /// <summary>
        /// Called when the pawn has just picked its next job (after finishing or ending its previous one).
        /// Replaces that job with a delivery job when the harvest should be brought to storage first.
        /// </summary>
        public static void TryReplaceNextJob(Pawn pawn, ref ThinkResult result)
        {
            HarvestTracker tracker = HarvestTracker.Instance;
            HarvestLoad load = tracker?.GetLoad(pawn);
            if (load == null || load.Empty || pawn.inventory == null)
                return;

            load.Reconcile(pawn);
            if (load.Empty)
                return;

            if (!ShouldDeliverInsteadOf(pawn, load, result))
                return;

            Job job = JobMaker.MakeJob(HarvestAndHaulDefOf.HarvestAndHaul_DeliverHarvest);
            load.lastDeliveryJobTick = Find.TickManager.TicksGame;
            HarvestAndHaulMod.Debug($"{pawn.LabelShort} delivers harvest instead of {(result.IsValid ? result.Job.def.defName : "idling")}.");
            result = new ThinkResult(job, result.IsValid ? result.SourceNode : null, JobTag.MiscWork, false);
        }

        private static bool ShouldDeliverInsteadOf(Pawn pawn, HarvestLoad load, ThinkResult next)
        {
            if (!CanDeliverNow(pawn, load))
                return false;

            if (!next.IsValid)
                return true;

            Job job = next.Job;
            // Never override what the player explicitly ordered.
            if (next.FromQueue || job.playerForced)
                return false;

            int ticks = Find.TickManager.TicksGame;
            bool holdExpired = load.startedTick >= 0 && ticks - load.startedTick > Settings.maxHoldHours * GenDate.TicksPerHour;

            // Keep harvesting / sowing while there is room; when full (or held too long) go deliver.
            if (IsFieldWork(job))
                return load.full || holdExpired;

            return IsPostponableSource(next.SourceNode);
        }

        private static bool CanDeliverNow(Pawn pawn, HarvestLoad load)
        {
            if (!pawn.Spawned || pawn.Map == null || pawn.Downed || pawn.Drafted || pawn.InMentalState)
                return false;
            if (pawn.carryTracker == null || pawn.carryTracker.CarriedThing != null)
                return false;
            if (pawn.IsBurning() || pawn.GetLord() != null)
                return false;
            if (pawn.mindState != null && pawn.mindState.anyCloseHostilesRecently)
                return false;
            if (HealthAIUtility.ShouldSeekMedicalRest(pawn))
                return false;

            Need_Food food = pawn.needs?.food;
            if (food != null && food.CurCategory >= HungerCategory.UrgentlyHungry)
                return false;
            Need_Rest rest = pawn.needs?.rest;
            if (rest != null && rest.CurCategory >= RestCategory.VeryTired)
                return false;

            int ticks = Find.TickManager.TicksGame;
            if (load.lastDeliveryJobTick == ticks)
                return false;
            if (ticks - load.lastDeliveryFailTick < DeliveryFailCooldownTicks)
                return false;
            return true;
        }

        /// <summary>Harvesting, cutting, sowing and anything else from a work type that uses the Plants skill.</summary>
        public static bool IsFieldWork(Job job)
        {
            Type driver = job.def.driverClass;
            if (driver != null && (typeof(JobDriver_PlantWork).IsAssignableFrom(driver) || typeof(JobDriver_PlantSow).IsAssignableFrom(driver)))
                return true;
            WorkTypeDef workType = job.workGiverDef?.workType;
            return workType?.relevantSkills != null && workType.relevantSkills.Contains(SkillDefOf.Plants);
        }

        /// <summary>
        /// Jobs that may wait until the harvest is delivered. Anything not listed here (emergencies,
        /// medical care, fleeing, rituals, jobs from other mods...) is left alone.
        /// </summary>
        private static bool IsPostponableSource(ThinkNode node)
        {
            switch (node)
            {
                case null:
                    return false;
                case JobGiver_Work work:
                    return !work.emergency;
                case JobGiver_Wander _:
                case JobGiver_Idle _:
                case JobGiver_SelfShutdown _:
                    return true;
            }

            if (!Settings.deliverBeforeNeeds)
                return false;
            return node is JobGiver_GetFood || node is JobGiver_GetRest || node is JobGiver_GetJoy || node is JobGiver_Meditate;
        }

        public static void Notify_DeliveryFailed(Pawn pawn)
        {
            HarvestLoad load = HarvestTracker.Instance?.GetLoad(pawn);
            if (load != null)
                load.lastDeliveryFailTick = Find.TickManager.TicksGame;
        }
    }
}
