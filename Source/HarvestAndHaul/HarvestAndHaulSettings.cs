using UnityEngine;
using Verse;

namespace HarvestAndHaul
{
    public class HarvestAndHaulSettings : ModSettings
    {
        /// <summary>Harvesters put what they harvest into their inventory.</summary>
        public bool enabled = true;

        /// <summary>Fraction of the pawn's carrying capacity that may be filled with harvest.</summary>
        public float fillPercent = 1f;

        /// <summary>A pawn only starts collecting if it can carry at least this many kg more.</summary>
        public float minFreeMassToStart = 5f;

        /// <summary>Deliver before eating, sleeping, recreation or meditation (not when urgent).</summary>
        public bool deliverBeforeNeeds = true;

        /// <summary>Deliver after at most this many in-game hours, even while still doing field work.</summary>
        public float maxHoldHours = 8f;

        /// <summary>Colony mechanoids (e.g. agrihands) also deliver their harvest.</summary>
        public bool includeMechanoids = true;

        public bool verboseLogging;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref fillPercent, "fillPercent", 1f);
            Scribe_Values.Look(ref minFreeMassToStart, "minFreeMassToStart", 5f);
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
            listing.Gap();

            fillPercent = listing.SliderLabeled(
                "HarvestAndHaul_FillPercent".Translate(fillPercent.ToStringPercent()),
                fillPercent, 0.1f, 1f, 0.6f, "HarvestAndHaul_FillPercent_Desc".Translate());
            fillPercent = Mathf.Round(fillPercent * 20f) / 20f;

            minFreeMassToStart = listing.SliderLabeled(
                "HarvestAndHaul_MinFreeMass".Translate(minFreeMassToStart.ToString("0.#")),
                minFreeMassToStart, 0f, 30f, 0.6f, "HarvestAndHaul_MinFreeMass_Desc".Translate());
            minFreeMassToStart = Mathf.Round(minFreeMassToStart * 2f) / 2f;

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
            fillPercent = 1f;
            minFreeMassToStart = 5f;
            deliverBeforeNeeds = true;
            maxHoldHours = 8f;
            includeMechanoids = true;
            verboseLogging = false;
        }
    }
}
