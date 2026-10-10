using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HarvestAndHaul
{
    /// <summary>
    /// Brings everything the pawn collected into its inventory to storage, one stack after the other.
    /// If a stack did not fit into the inventory, the pawn first picks that one up into its hands
    /// and carries it along. For inventory stacks the pawn walks to the storage cell with the items
    /// still in its bag, takes the stack out there and places it using the vanilla hauling toils
    /// (so storage mods keep working).
    /// </summary>
    public class JobDriver_DeliverHarvest : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex CellInd = TargetIndex.B;
        private const int TakeOutTicks = 10;
        private const float MaxLeftoverDistance = 15f;

        private int countToDeliver = -1;
        private bool carryingLeftover;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref countToDeliver, "countToDeliver", -1);
            Scribe_Values.Look(ref carryingLeftover, "carryingLeftover", false);
        }

        // Things and storage cells are reserved one at a time while the job runs.
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition =>
            {
                if (condition == JobCondition.Incompletable || condition == JobCondition.Errored)
                    HarvestHaulUtility.Notify_DeliveryFailed(pawn);
            });

            Toil findNext = ToilMaker.MakeToil("HarvestAndHaul_FindNext");
            Toil gotoLeftover = Toils_Goto.GotoThing(ItemInd, PathEndMode.ClosestTouch);

            findNext.initAction = () =>
            {
                switch (TrySetupNextDelivery())
                {
                    case NextStep.Done:
                        EndJobWith(JobCondition.Succeeded);
                        break;
                    case NextStep.Leftover:
                        JumpToToil(gotoLeftover);
                        break;
                }
            };
            findNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findNext;

            // --- A stack from the inventory ---
            Toil gotoStorage = Toils_Goto.GotoCell(CellInd, PathEndMode.ClosestTouch);
            gotoStorage.FailOn(() => !ItemStillInInventory());
            yield return gotoStorage;

            Toil takeOutDelay = Toils_General.Wait(TakeOutTicks, CellInd);
            takeOutDelay.FailOn(() => !ItemStillInInventory());
            yield return takeOutDelay;

            Toil takeOut = ToilMaker.MakeToil("HarvestAndHaul_TakeOut");
            takeOut.initAction = () =>
            {
                Thing thing = job.GetTarget(ItemInd).Thing;
                if (!ItemStillInInventory())
                {
                    JumpToToil(findNext);
                    return;
                }

                ThingDef def = thing.def;
                int count = Mathf.Min(countToDeliver, thing.stackCount, pawn.carryTracker.AvailableStackSpace(def));
                int moved = count > 0
                    ? pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, count, canMergeWithExistingStacks: false)
                    : 0;
                Thing carried = pawn.carryTracker.CarriedThing;
                if (moved <= 0 || carried == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }

                HarvestTracker.Instance?.GetLoad(pawn)?.Remove(def, moved);
                carried.SetForbidden(false, warnOnFail: false);
                job.SetTarget(ItemInd, carried);
                job.count = carried.stackCount;
            };
            takeOut.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return takeOut;

            Toil carryToCell = Toils_Haul.CarryHauledThingToCell(CellInd);
            yield return carryToCell;
            yield return Toils_Haul.PlaceHauledThingInCell(CellInd, carryToCell, storageMode: true);
            yield return Toils_Jump.Jump(findNext);

            // --- The stack that did not fit, carried in the hands ---
            gotoLeftover.JumpIfDespawnedOrNullOrForbidden(ItemInd, findNext);
            yield return gotoLeftover;
            yield return Toils_Haul.StartCarryThing(ItemInd, reserve: false);
            Toil carryLeftover = Toils_Haul.CarryHauledThingToCell(CellInd);
            yield return carryLeftover;
            yield return Toils_Haul.PlaceHauledThingInCell(CellInd, carryLeftover, storageMode: true);
            yield return Toils_Jump.Jump(findNext);
        }

        private enum NextStep
        {
            Done,
            Inventory,
            Leftover,
        }

        private bool ItemStillInInventory()
        {
            Thing thing = job.GetTarget(ItemInd).Thing;
            return thing != null && !thing.Destroyed && pawn.inventory != null && pawn.inventory.innerContainer.Contains(thing);
        }

        /// <summary>
        /// Picks what to deliver next. Stacks no storage accepts are dropped where the pawn stands
        /// so the normal haulers can take care of them later.
        /// </summary>
        private NextStep TrySetupNextDelivery()
        {
            HarvestLoad load = HarvestTracker.Instance?.GetLoad(pawn);
            Map map = pawn.Map;
            if (load == null || map == null || pawn.inventory == null)
                return NextStep.Done;

            map.reservationManager.ReleaseClaimedBy(pawn, job);
            HandleStillCarriedThing(load);

            Thing leftover = load.leftover;
            load.leftover = null;
            if (TrySetupLeftover(leftover, map))
                return NextStep.Leftover;

            load.Reconcile(pawn);
            ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
            // Every iteration either sets up a delivery or removes a stack from the load, so this ends.
            for (int guard = 0; guard < 200 && !load.Empty; guard++)
            {
                Thing thing = null;
                for (int i = 0; i < inventory.Count; i++)
                {
                    if (load.Owes(inventory[i].def))
                    {
                        thing = inventory[i];
                        break;
                    }
                }
                if (thing == null)
                    return NextStep.Done;

                int count = Mathf.Min(load.OwedCount(thing.def), thing.stackCount);
                int carryable = Mathf.Min(count, pawn.carryTracker.AvailableStackSpace(thing.def));
                if (carryable > 0
                    && StoreUtility.TryFindBestBetterStoreCellFor(thing, pawn, map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell)
                    && pawn.Reserve(cell, job, errorOnFailed: false))
                {
                    job.SetTarget(ItemInd, thing);
                    job.SetTarget(CellInd, cell);
                    job.count = carryable;
                    countToDeliver = carryable;
                    carryingLeftover = false;
                    return NextStep.Inventory;
                }

                HarvestAndHaulMod.Debug($"{pawn.LabelShort}: no storage for {thing.LabelCap}, dropping {count} here.");
                ThingDef def = thing.def;
                if (inventory.TryDrop(thing, pawn.Position, map, ThingPlaceMode.Near, count, out Thing dropped))
                    dropped?.SetForbidden(false, warnOnFail: false);
                load.Remove(def, count);
            }
            return NextStep.Done;
        }

        private bool TrySetupLeftover(Thing leftover, Map map)
        {
            if (leftover == null || leftover.Destroyed || !leftover.Spawned || leftover.Map != map)
                return false;
            if (pawn.carryTracker.CarriedThing != null || leftover.IsForbidden(pawn) || StoreUtility.IsInValidStorage(leftover))
                return false;
            if (!leftover.Position.InHorDistOf(pawn.Position, MaxLeftoverDistance))
                return false;
            if (!pawn.CanReserveAndReach(leftover, PathEndMode.ClosestTouch, Danger.Some))
                return false;
            int count = Mathf.Min(leftover.stackCount, pawn.carryTracker.AvailableStackSpace(leftover.def));
            if (count <= 0)
                return false;
            if (!StoreUtility.TryFindBestBetterStoreCellFor(leftover, pawn, map, StoragePriority.Unstored, pawn.Faction, out IntVec3 cell)
                || !pawn.Reserve(cell, job, errorOnFailed: false))
                return false;
            // Reserve the stack right away so no hauler takes it while the pawn walks over.
            if (!pawn.Reserve(leftover, job, errorOnFailed: false))
            {
                map.reservationManager.Release(cell, pawn, job);
                return false;
            }

            job.SetTarget(ItemInd, leftover);
            job.SetTarget(CellInd, cell);
            job.count = count;
            countToDeliver = count;
            carryingLeftover = true;
            HarvestAndHaulMod.Debug($"{pawn.LabelShort} also carries the {count}x {leftover.LabelNoCount} that did not fit.");
            return true;
        }

        /// <summary>
        /// Something still in the hands at this point did not fit into the storage cell (or the job was
        /// resumed). Inventory stacks go back into the bag; a carried leftover is set down here.
        /// </summary>
        private void HandleStillCarriedThing(HarvestLoad load)
        {
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried != job.GetTarget(ItemInd).Thing)
                return;
            if (carryingLeftover)
            {
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
                return;
            }
            ThingDef def = carried.def;
            int count = carried.stackCount;
            int moved = pawn.carryTracker.innerContainer.TryTransferToContainer(carried, pawn.inventory.innerContainer, count);
            if (moved > 0)
                load.Add(def, moved);
            if (moved < count)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
        }

        public override string GetReport()
        {
            Thing thing = job.GetTarget(ItemInd).Thing;
            if (thing == null)
                return "HarvestAndHaul_DeliveringHarvest".Translate();
            return "HarvestAndHaul_DeliveringThing".Translate(thing.LabelShort.Named("THING"));
        }
    }
}
