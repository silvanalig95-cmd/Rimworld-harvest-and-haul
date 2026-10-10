using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HarvestAndHaul
{
    /// <summary>
    /// Brings everything the pawn harvested into its inventory to storage, one stack after the other.
    /// The pawn walks to the storage cell with the harvest still in its bag, takes the stack out
    /// there and places it using the vanilla hauling toils (so storage mods keep working).
    /// </summary>
    public class JobDriver_DeliverHarvest : JobDriver
    {
        private const TargetIndex ItemInd = TargetIndex.A;
        private const TargetIndex CellInd = TargetIndex.B;
        private const int TakeOutTicks = 10;

        private int countToDeliver = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref countToDeliver, "countToDeliver", -1);
        }

        // Storage cells are reserved one at a time while the job runs.
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction(condition =>
            {
                if (condition == JobCondition.Incompletable || condition == JobCondition.Errored)
                    HarvestHaulUtility.Notify_DeliveryFailed(pawn);
            });

            Toil findNext = ToilMaker.MakeToil("HarvestAndHaul_FindNext");
            findNext.initAction = () =>
            {
                if (!TrySetupNextDelivery())
                    EndJobWith(JobCondition.Succeeded);
            };
            findNext.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return findNext;

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
        }

        private bool ItemStillInInventory()
        {
            Thing thing = job.GetTarget(ItemInd).Thing;
            return thing != null && !thing.Destroyed && pawn.inventory != null && pawn.inventory.innerContainer.Contains(thing);
        }

        /// <summary>
        /// Picks the next owed stack and a storage cell for it. Stacks no storage accepts are
        /// dropped where the pawn stands so the normal haulers can take care of them later.
        /// </summary>
        private bool TrySetupNextDelivery()
        {
            HarvestLoad load = HarvestTracker.Instance?.GetLoad(pawn);
            Map map = pawn.Map;
            if (load == null || map == null || pawn.inventory == null)
                return false;

            map.reservationManager.ReleaseClaimedBy(pawn, job);
            ReturnCarriedThingToInventory(load);
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
                    return false;

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
                    return true;
                }

                HarvestAndHaulMod.Debug($"{pawn.LabelShort}: no storage for {thing.LabelCap}, dropping {count} here.");
                ThingDef def = thing.def;
                if (inventory.TryDrop(thing, pawn.Position, map, ThingPlaceMode.Near, count, out Thing dropped))
                    dropped?.SetForbidden(false, warnOnFail: false);
                load.Remove(def, count);
            }
            return false;
        }

        /// <summary>A remainder that did not fit anywhere (or a resumed job) goes back into the bag first.</summary>
        private void ReturnCarriedThingToInventory(HarvestLoad load)
        {
            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried == null || carried != job.GetTarget(ItemInd).Thing)
                return;
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
