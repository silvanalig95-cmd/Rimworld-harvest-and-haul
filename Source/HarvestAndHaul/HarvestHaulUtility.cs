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
        /// <summary>Products land at or right next to the spot; look a bit wider to be safe.</summary>
        private const int SearchMargin = 2;

        private const int DeliveryFailCooldownTicks = 600;

        /// <summary>Things left behind are only hauled "right away" within this window and distance.</summary>
        private const int HaulNowWindowTicks = GenDate.TicksPerHour;
        private const float HaulNowMaxDistance = 25f;

        /// <summary>
        /// False if the patch that makes pawns deliver could not be applied. Collecting is then
        /// switched off, so pawns never end up hoarding items in their inventory.
        /// </summary>
        public static bool DeliveryAvailable = true;

        private static HarvestAndHaulSettings Settings => HarvestAndHaulMod.Settings;

        private static readonly List<Thing> tmpCandidates = new List<Thing>();

        // ------------------------------------------------------------------
        // Who and what takes part
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

        public static bool IsCollectableDef(ThingDef def)
        {
            if (def.category != ThingCategory.Item || !def.EverStorable(false))
                return false;
            if (def.IsCorpse || def == ThingDefOf.MinifiedThing)
                return false;
            return Settings.collectChunks || !IsChunk(def);
        }

        public static bool IsChunk(ThingDef def)
        {
            List<ThingCategoryDef> categories = def.thingCategories;
            return categories != null && (categories.Contains(ThingCategoryDefOf.Chunks) || categories.Contains(ThingCategoryDefOf.StoneChunks));
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

        /// <summary>Stopping work for a delivery trip is only worth it once the load has some weight.</summary>
        public static bool WorthATrip(HarvestLoad load) => load.Mass >= Settings.minTripMass;

        // ------------------------------------------------------------------
        // Item sources (called from the Harmony patches)
        // ------------------------------------------------------------------

        /// <summary>A plant was harvested or cut, for any reason (harvesting, chopping, clearing for sowing or building...).</summary>
        public static void Notify_PlantCollected(Pawn pawn, Plant plant)
        {
            ThingDef def = plant.def?.plant?.harvestedThingDef;
            if (!Settings.collectPlants || def == null)
                return;
            Submit(new CollectRequest
            {
                pawn = pawn,
                map = pawn.Map,
                area = CellRect.CenteredOn(pawn.Position, SearchMargin),
                def = def,
                fallbackSkill = SkillDefOf.Plants,
            });
        }

        /// <summary>A rock, ore or other mineable was mined out.</summary>
        public static void Notify_Mined(Pawn pawn, Map map, CellRect rect, ThingDef mineableThing)
        {
            if (!Settings.collectMining || mineableThing == null || pawn.Map != map)
                return;
            Submit(new CollectRequest
            {
                pawn = pawn,
                map = map,
                area = rect.ExpandedBy(SearchMargin),
                def = mineableThing,
                fallbackSkill = SkillDefOf.Mining,
            });
        }

        /// <summary>A deep drill produced a portion of resources.</summary>
        public static void Notify_DeepDrillPortion(Pawn driller, Thing drill)
        {
            if (!Settings.collectDeepDrill || drill == null || !drill.Spawned || driller.Map != drill.Map)
                return;
            Submit(new CollectRequest
            {
                pawn = driller,
                map = drill.Map,
                area = drill.OccupiedRect().ExpandedBy(SearchMargin + 1),
                anyDeepResource = true,
                fallbackSkill = SkillDefOf.Mining,
                jobToEndWhenFull = driller.CurJob,
            });
        }

        /// <summary>Wool, milk or another body resource was gathered from an animal.</summary>
        public static void Notify_AnimalProductGathered(Pawn doer, ThingDef product)
        {
            if (!Settings.collectAnimalProducts || product == null)
                return;
            Submit(new CollectRequest
            {
                pawn = doer,
                map = doer.Map,
                area = CellRect.CenteredOn(doer.Position, SearchMargin),
                def = product,
                fallbackSkill = SkillDefOf.Animals,
            });
        }

        /// <summary>A building or floor was removed and refunded some of its materials.</summary>
        public static void Notify_Deconstructed(Pawn pawn, Map map, CellRect rect, HashSet<ThingDef> refundDefs)
        {
            if (!Settings.collectDeconstruction || refundDefs == null || refundDefs.Count == 0 || pawn.Map != map)
                return;
            Submit(new CollectRequest
            {
                pawn = pawn,
                map = map,
                area = rect.ExpandedBy(SearchMargin),
                defs = refundDefs,
                fallbackSkill = SkillDefOf.Construction,
            });
        }

        /// <summary>Ids of the items lying around a building before it is deconstructed.</summary>
        public static HashSet<int> SnapshotLeftoverArea(Map map, CellRect rect)
        {
            if (!Settings.haulDeconstructionLeftovers)
                return null;
            var ids = new HashSet<int>();
            foreach (IntVec3 c in rect.ExpandedBy(SearchMargin).ClipInsideMap(map))
            {
                List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i].def.category == ThingCategory.Item)
                        ids.Add(things[i].thingIDNumber);
                }
            }
            return ids;
        }

        /// <summary>
        /// Whatever a deconstruction left lying around that did not go into the inventory (slag or
        /// stone chunks, fuel, contents...) is hauled by the same pawn right after its job.
        /// </summary>
        public static void Notify_DeconstructionLeftovers(Pawn pawn, Map map, CellRect rect, HashSet<int> itemsBefore)
        {
            if (!Settings.haulDeconstructionLeftovers || !PawnCanCollect(pawn) || pawn.Map != map)
                return;
            HarvestTracker tracker = HarvestTracker.Instance;
            if (tracker == null)
                return;

            HarvestLoad load = null;
            foreach (IntVec3 c in rect.ExpandedBy(SearchMargin).ClipInsideMap(map))
            {
                List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.def.category != ThingCategory.Item || !t.def.EverHaulable || itemsBefore.Contains(t.thingIDNumber))
                        continue;
                    if (t.IsForbidden(pawn) || StoreUtility.IsInValidStorage(t))
                        continue;
                    if (load == null)
                        load = tracker.GetOrCreateLoad(pawn);
                    if (!load.haulNow.Contains(t))
                        load.haulNow.Add(t);
                }
            }
            if (load != null)
            {
                load.haulNowTick = Find.TickManager.TicksGame;
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} will haul {load.haulNow.Count} deconstruction leftover(s) right away.");
            }
        }

        /// <summary>Fish were caught. They are placed after this is called, so only the next-tick sweep looks for them.</summary>
        public static void Notify_FishCaught(Pawn pawn, IntVec3 spot, List<Thing> catches)
        {
            if (!Settings.collectFishing || catches == null || catches.Count == 0)
                return;
            var defs = new HashSet<ThingDef>();
            for (int i = 0; i < catches.Count; i++)
            {
                if (catches[i]?.def != null)
                    defs.Add(catches[i].def);
            }
            CellRect area = CellRect.CenteredOn(pawn.Position, SearchMargin);
            if (spot.IsValid)
                area = area.Encapsulate(CellRect.CenteredOn(spot, 1));
            Submit(new CollectRequest
            {
                pawn = pawn,
                map = pawn.Map,
                area = area,
                defs = defs,
                fallbackSkill = SkillDefOf.Animals,
                jobToEndWhenFull = pawn.CurJob,
            }, collectNow: false);
        }

        private static void Submit(CollectRequest request, bool collectNow = true)
        {
            if (!Settings.enabled || !DeliveryAvailable || !PawnCanCollect(request.pawn) || request.map == null)
                return;
            HarvestTracker tracker = HarvestTracker.Instance;
            if (tracker == null)
                return;
            if (collectNow)
                CollectItems(request);
            tracker.QueueSweep(request);
        }

        /// <summary>Runs one tick after the drop: collect anything that appeared late, and end continuous jobs once full.</summary>
        public static void ProcessSweep(CollectRequest request)
        {
            Pawn pawn = request.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map != request.map)
                return;
            CollectItems(request);

            Job job = request.jobToEndWhenFull;
            if (job == null || pawn.CurJob != job)
                return;
            HarvestLoad load = HarvestTracker.Instance?.GetLoad(pawn);
            if (load != null && load.full && WorthATrip(load))
            {
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} is full, stopping {job.def.defName} to deliver.");
                pawn.jobs.EndCurrentJob(JobCondition.Succeeded);
            }
        }

        // ------------------------------------------------------------------
        // Collecting
        // ------------------------------------------------------------------

        /// <summary>
        /// Moves matching loose items in the request's area into the pawn's inventory, filling it
        /// up as far as its carrying capacity allows.
        /// </summary>
        public static void CollectItems(CollectRequest request)
        {
            Pawn pawn = request.pawn;
            Map map = request.map;
            if (!Settings.enabled || !DeliveryAvailable || !PawnCanCollect(pawn) || pawn.Map != map)
                return;
            HarvestTracker tracker = HarvestTracker.Instance;
            if (tracker == null)
                return;

            tmpCandidates.Clear();
            foreach (IntVec3 c in request.area.ClipInsideMap(map))
            {
                List<Thing> things = map.thingGrid.ThingsListAtFast(c);
                for (int j = 0; j < things.Count; j++)
                {
                    Thing t = things[j];
                    if (request.Matches(t.def) && CanCollect(pawn, t))
                        tmpCandidates.Add(t);
                }
            }
            if (tmpCandidates.Count == 0)
                return;
            IntVec3 pawnPos = pawn.Position;
            tmpCandidates.SortBy(t => t.Position.DistanceToSquared(pawnPos));

            HarvestLoad load = tracker.GetLoad(pawn);
            int picked = 0;
            float smallestUnitMass = float.MaxValue;
            Thing leftover = null;
            for (int i = 0; i < tmpCandidates.Count; i++)
            {
                Thing t = tmpCandidates[i];
                if (t.Destroyed || !t.Spawned)
                    continue;
                float unitMass = t.GetStatValue(StatDefOf.Mass);
                int count = CountThatFits(pawn, t, unitMass);
                if (count < t.stackCount && leftover == null)
                    leftover = t; // whatever stays behind of this stack
                if (count <= 0)
                    continue;

                IntVec3 pos = t.Position;
                ThingDef def = t.def;
                Thing taken = t.SplitOff(count);
                int takenCount = taken.stackCount;
                if (!pawn.inventory.innerContainer.TryAdd(taken, canMergeWithExistingStacks: true))
                {
                    // Should not happen with an inventory, but never lose items.
                    if (!taken.Destroyed && !taken.Spawned)
                        GenPlace.TryPlaceThing(taken, pos, map, ThingPlaceMode.Near);
                    continue;
                }

                if (load == null)
                    load = tracker.GetOrCreateLoad(pawn);
                load.Add(def, takenCount);
                picked += takenCount;
                smallestUnitMass = Mathf.Min(smallestUnitMass, unitMass);
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} collected {takenCount}x {def.label} (free {FreeMass(pawn):0.##} kg).");
            }
            tmpCandidates.Clear();

            if (load == null || load.Empty)
                return;
            if (picked > 0)
                AddWorkSkills(pawn, load, request.fallbackSkill);

            // Full once something did not fit, or there is no room left for even one more unit.
            if (leftover != null || (picked > 0 && FreeMass(pawn) < smallestUnitMass))
                MarkFull(pawn, load, leftover);
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

        private static void AddWorkSkills(Pawn pawn, HarvestLoad load, SkillDef fallback)
        {
            List<SkillDef> jobSkills = pawn.CurJob?.workGiverDef?.workType?.relevantSkills;
            if (jobSkills != null && jobSkills.Count > 0)
            {
                for (int i = 0; i < jobSkills.Count; i++)
                    load.AddSkill(jobSkills[i]);
            }
            else
            {
                load.AddSkill(fallback);
            }
        }

        private static void MarkFull(Pawn pawn, HarvestLoad load, Thing leftover)
        {
            if (leftover != null)
                load.leftover = leftover;
            if (!load.full)
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} is full ({load.Mass:0.#} kg collected).");
            load.full = true;
            if (!WorthATrip(load))
                return;

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
        /// Replaces that job with a delivery job when the load should be brought to storage first.
        /// </summary>
        public static void TryReplaceNextJob(Pawn pawn, ref ThinkResult result)
        {
            HarvestTracker tracker = HarvestTracker.Instance;
            HarvestLoad load = tracker?.GetLoad(pawn);
            if (load == null)
                return;
            if (TryHaulLeftBehind(pawn, load, ref result))
                return;
            if (load.Empty || pawn.inventory == null)
                return;

            load.Reconcile(pawn);
            if (load.Empty)
                return;

            if (!ShouldDeliverInsteadOf(pawn, load, result))
                return;

            Job job = JobMaker.MakeJob(HarvestAndHaulDefOf.HarvestAndHaul_DeliverHarvest);
            load.lastDeliveryJobTick = Find.TickManager.TicksGame;
            HarvestAndHaulMod.Debug($"{pawn.LabelShort} delivers its load instead of {(result.IsValid ? result.Job.def.defName : "idling")}.");
            result = new ThinkResult(job, result.IsValid ? result.SourceNode : null, JobTag.MiscWork, false);
        }

        /// <summary>
        /// Hauls the next thing the pawn's work left behind, using the vanilla haul job, before
        /// anything that can wait (including more of the same work).
        /// </summary>
        private static bool TryHaulLeftBehind(Pawn pawn, HarvestLoad load, ref ThinkResult result)
        {
            List<Thing> list = load.haulNow;
            if (list.Count == 0)
                return false;
            if (Find.TickManager.TicksGame - load.haulNowTick > HaulNowWindowTicks || pawn.Map == null)
            {
                list.Clear();
                return false;
            }
            if (!CanDeliverNow(pawn, load))
                return false;
            if (result.IsValid && (result.FromQueue || result.Job.playerForced || !IsPostponableSource(result.SourceNode)))
                return false;

            while (list.Count > 0)
            {
                // Closest first.
                int best = 0;
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i] != null && (list[best] == null || list[i].PositionHeld.DistanceToSquared(pawn.Position) < list[best].PositionHeld.DistanceToSquared(pawn.Position)))
                        best = i;
                }
                Thing thing = list[best];
                list.RemoveAt(best);

                if (thing == null || !thing.Spawned || thing.Map != pawn.Map || thing.IsForbidden(pawn) || StoreUtility.IsInValidStorage(thing))
                    continue;
                if (!thing.Position.InHorDistOf(pawn.Position, HaulNowMaxDistance) || !HaulAIUtility.PawnCanAutomaticallyHaul(pawn, thing, false))
                    continue;
                Job haul = HaulAIUtility.HaulToStorageJob(pawn, thing, false);
                if (haul == null)
                    continue; // no storage accepts it; leave it for later

                load.lastDeliveryJobTick = Find.TickManager.TicksGame;
                HarvestAndHaulMod.Debug($"{pawn.LabelShort} hauls {thing.LabelCap} right away instead of {(result.IsValid ? result.Job.def.defName : "idling")}.");
                result = new ThinkResult(haul, result.IsValid ? result.SourceNode : null, JobTag.MiscWork, false);
                return true;
            }
            return false;
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

            // Keep doing the same kind of work while there is room; when full (or held too long) deliver.
            if (IsSameKindOfWork(job, load))
                return (load.full && WorthATrip(load)) || holdExpired;

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

        /// <summary>
        /// True if the next job uses one of the skills of the work that produced the load: harvesting
        /// then sowing (Plants), mining one cell after another (Mining), milking one cow after another
        /// (Animals), deconstructing then building (Construction)...
        /// </summary>
        public static bool IsSameKindOfWork(Job job, HarvestLoad load)
        {
            List<SkillDef> jobSkills = job.workGiverDef?.workType?.relevantSkills;
            if (jobSkills != null)
            {
                for (int i = 0; i < jobSkills.Count; i++)
                {
                    if (load.skills.Contains(jobSkills[i]))
                        return true;
                }
            }

            // Jobs that were not handed out by a work giver.
            Type driver = job.def.driverClass;
            if (driver == null)
                return false;
            if (load.skills.Contains(SkillDefOf.Plants)
                && (typeof(JobDriver_PlantWork).IsAssignableFrom(driver) || typeof(JobDriver_PlantSow).IsAssignableFrom(driver)))
                return true;
            if (load.skills.Contains(SkillDefOf.Mining)
                && (typeof(JobDriver_Mine).IsAssignableFrom(driver) || typeof(JobDriver_OperateDeepDrill).IsAssignableFrom(driver)))
                return true;
            return false;
        }

        /// <summary>
        /// Jobs that may wait until the load is delivered. Anything not listed here (emergencies,
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
