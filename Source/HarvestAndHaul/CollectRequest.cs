using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace HarvestAndHaul
{
    /// <summary>
    /// "Items of these kinds were just dropped around here by this pawn's work; pick them up."
    /// </summary>
    public class CollectRequest
    {
        public Pawn pawn;
        public Map map;

        /// <summary>Cells to search (already including some margin around the drop spot).</summary>
        public CellRect area;

        /// <summary>The product, when there is exactly one kind.</summary>
        public ThingDef def;

        /// <summary>Possible products, when there are several kinds.</summary>
        public HashSet<ThingDef> defs;

        /// <summary>Anything a deep drill can produce.</summary>
        public bool anyDeepResource;

        /// <summary>Skill of the activity, used when the pawn's job does not say which work type it belongs to.</summary>
        public SkillDef fallbackSkill;

        /// <summary>For jobs that keep producing (deep drilling, fishing): end this job once the pawn is full.</summary>
        public Job jobToEndWhenFull;

        public bool Matches(ThingDef thingDef)
        {
            if (thingDef == null)
                return false;
            if (thingDef != def && (defs == null || !defs.Contains(thingDef)) && !(anyDeepResource && thingDef.deepCommonality > 0f))
                return false;
            return HarvestHaulUtility.IsCollectableDef(thingDef);
        }
    }
}
