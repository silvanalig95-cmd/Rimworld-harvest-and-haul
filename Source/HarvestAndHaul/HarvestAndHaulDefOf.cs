using RimWorld;
using Verse;

namespace HarvestAndHaul
{
    [DefOf]
    public static class HarvestAndHaulDefOf
    {
        public static JobDef HarvestAndHaul_DeliverHarvest;

        static HarvestAndHaulDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HarvestAndHaulDefOf));
        }
    }
}
