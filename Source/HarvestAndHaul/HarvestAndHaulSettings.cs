using UnityEngine;
using Verse;

namespace HarvestAndHaul
{
    public class HarvestAndHaulSettings : ModSettings
    {
        /// <summary>Pawns put items produced by their work into their inventory.</summary>
        public bool enabled = true;

        // Item sources
        public bool collectPlants = true;
        public bool collectMining = true;
        public bool collectDeepDrill = true;
        public bool collectAnimalProducts = true;
        public bool collectDeconstruction = true;
        public bool haulDeconstructionLeftovers = true;
        public bool collectFishing = true;
        public bool collectChunks;

        /// <summary>Fraction of the pawn's carrying capacity that may be filled.</summary>
        public float fillPercent = 1f;

        /// <summary>A full pawn only stops working to deliver once it carries at least this many kg.</summary>
        public float minTripMass = 5f;

        /// <summary>Deliver before eating, sleeping, recreation or meditation (not when urgent).</summary>
        public bool deliverBeforeNeeds = true;

        /// <summary>Deliver after at most this many in-game hours, even while still doing the same kind of work.</summary>
        public float maxHoldHours = 8f;

        /// <summary>Colony mechanoids (e.g. agrihands) also collect and deliver.</summary>
        public bool includeMechanoids = true;

        public bool verboseLogging;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref collectPlants, "collectPlants", true);
            Scribe_Values.Look(ref collectMining, "collectMining", true);
            Scribe_Values.Look(ref collectDeepDrill, "collectDeepDrill", true);
            Scribe_Values.Look(ref collectAnimalProducts, "collectAnimalProducts", true);
            Scribe_Values.Look(ref collectDeconstruction, "collectDeconstruction", true);
            Scribe_Values.Look(ref haulDeconstructionLeftovers, "haulDeconstructionLeftovers", true);
            Scribe_Values.Look(ref collectFishing, "collectFishing", true);
            Scribe_Values.Look(ref collectChunks, "collectChunks", false);
            Scribe_Values.Look(ref fillPercent, "fillPercent", 1f);
            Scribe_Values.Look(ref minTripMass, "minTripMass", 5f);
            Scribe_Values.Look(ref deliverBeforeNeeds, "deliverBeforeNeeds", true);
            Scribe_Values.Look(ref maxHoldHours, "maxHoldHours", 8f);
            Scribe_Values.Look(ref includeMechanoids, "includeMechanoids", true);
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
        }

        public void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("HarvestAndHaul_Enabled".Translate(), ref enabled, "HarvestAndHaul_Enabled_Desc".Translate());
            listing.GapLine();

            listing.Label("HarvestAndHaul_SourcesHeader".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectPlants".Translate(), ref collectPlants, "HarvestAndHaul_CollectPlants_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectMining".Translate(), ref collectMining, "HarvestAndHaul_CollectMining_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectDeepDrill".Translate(), ref collectDeepDrill, "HarvestAndHaul_CollectDeepDrill_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectAnimalProducts".Translate(), ref collectAnimalProducts, "HarvestAndHaul_CollectAnimalProducts_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectDeconstruction".Translate(), ref collectDeconstruction, "HarvestAndHaul_CollectDeconstruction_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_HaulDeconstructionLeftovers".Translate(), ref haulDeconstructionLeftovers, "HarvestAndHaul_HaulDeconstructionLeftovers_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectFishing".Translate(), ref collectFishing, "HarvestAndHaul_CollectFishing_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_CollectChunks".Translate(), ref collectChunks, "HarvestAndHaul_CollectChunks_Desc".Translate());
            listing.GapLine();

            fillPercent = listing.SliderLabeled(
                "HarvestAndHaul_FillPercent".Translate(fillPercent.ToStringPercent()),
                fillPercent, 0.1f, 1f, 0.6f, "HarvestAndHaul_FillPercent_Desc".Translate());
            fillPercent = Mathf.Round(fillPercent * 20f) / 20f;

            minTripMass = listing.SliderLabeled(
                "HarvestAndHaul_MinTripMass".Translate(minTripMass.ToString("0.#")),
                minTripMass, 0f, 30f, 0.6f, "HarvestAndHaul_MinTripMass_Desc".Translate());
            minTripMass = Mathf.Round(minTripMass * 2f) / 2f;

            maxHoldHours = listing.SliderLabeled(
                "HarvestAndHaul_MaxHoldHours".Translate(maxHoldHours.ToString("0")),
                maxHoldHours, 1f, 24f, 0.6f, "HarvestAndHaul_MaxHoldHours_Desc".Translate());
            maxHoldHours = Mathf.Round(maxHoldHours);

            listing.Gap();
            listing.CheckboxLabeled("HarvestAndHaul_DeliverBeforeNeeds".Translate(), ref deliverBeforeNeeds, "HarvestAndHaul_DeliverBeforeNeeds_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_IncludeMechanoids".Translate(), ref includeMechanoids, "HarvestAndHaul_IncludeMechanoids_Desc".Translate());
            listing.CheckboxLabeled("HarvestAndHaul_VerboseLogging".Translate(), ref verboseLogging);

            listing.Gap();
            if (listing.ButtonText("HarvestAndHaul_ResetDefaults".Translate(), widthPct: 0.4f))
                ResetToDefaults();

            listing.End();
        }

        private void ResetToDefaults()
        {
            enabled = true;
            collectPlants = collectMining = collectDeepDrill = collectAnimalProducts = collectDeconstruction = collectFishing = true;
            haulDeconstructionLeftovers = true;
            collectChunks = false;
            fillPercent = 1f;
            minTripMass = 5f;
            deliverBeforeNeeds = true;
            maxHoldHours = 8f;
            includeMechanoids = true;
            verboseLogging = false;
        }
    }
}
