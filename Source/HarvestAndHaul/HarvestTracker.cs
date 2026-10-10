using System.Collections.Generic;
using RimWorld;
using Verse;

namespace HarvestAndHaul
{
    /// <summary>
    /// What a single pawn has harvested into its inventory and still has to bring to storage.
    /// Tracked as counts per ThingDef rather than as Thing references, so it survives stacks
    /// merging with things the pawn already carried (e.g. a doctor's herbal medicine).
    /// </summary>
    public class HarvestLoad : IExposable
    {
        public Dictionary<ThingDef, int> owed = new Dictionary<ThingDef, int>();

        /// <summary>Tick at which the pawn started carrying this load (-1 when empty).</summary>
        public int startedTick = -1;

        /// <summary>Set when the next harvest would not fit anymore: stop harvesting and deliver.</summary>
        public bool full;

        /// <summary>Mass of the most recent harvest that was collected, used to predict the next one.</summary>
        public float lastYieldMass;

        public int lastDeliveryFailTick = -99999;

        // Not saved: guards against handing out a delivery job twice in the same tick.
        public int lastDeliveryJobTick = -1;

        private static readonly List<ThingDef> tmpDefs = new List<ThingDef>();

        public bool Empty => owed.Count == 0;

        public bool Owes(ThingDef def) => def != null && owed.ContainsKey(def);

        public int OwedCount(ThingDef def) => def != null && owed.TryGetValue(def, out int count) ? count : 0;

        public void Add(ThingDef def, int count)
        {
            if (def == null || count <= 0)
                return;
            if (owed.Count == 0)
                startedTick = Find.TickManager.TicksGame;
            owed[def] = OwedCount(def) + count;
        }

        public void Remove(ThingDef def, int count)
        {
            if (def == null || count <= 0 || !owed.TryGetValue(def, out int current))
                return;
            if (current <= count)
                owed.Remove(def);
            else
                owed[def] = current - count;
            if (owed.Count == 0)
                Reset();
        }

        /// <summary>Never owe more than the pawn actually still has (it may have eaten, dropped or traded some).</summary>
        public void Reconcile(Pawn pawn)
        {
            tmpDefs.Clear();
            tmpDefs.AddRange(owed.Keys);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                ThingDef def = tmpDefs[i];
                int actual = def == null || pawn.inventory == null ? 0 : pawn.inventory.innerContainer.TotalStackCountOfDef(def);
                if (actual <= 0)
                    owed.Remove(def);
                else if (actual < owed[def])
                    owed[def] = actual;
            }
            tmpDefs.Clear();
            if (owed.Count == 0)
                Reset();
        }

        private void Reset()
        {
            startedTick = -1;
            full = false;
            lastYieldMass = 0f;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref owed, "owed", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref startedTick, "startedTick", -1);
            Scribe_Values.Look(ref full, "full", false);
            Scribe_Values.Look(ref lastYieldMass, "lastYieldMass", 0f);
            Scribe_Values.Look(ref lastDeliveryFailTick, "lastDeliveryFailTick", -99999);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (owed == null)
                    owed = new Dictionary<ThingDef, int>();
                // Defs from removed mods load as null.
                owed.RemoveAll(kv => kv.Key == null || kv.Value <= 0);
                if (owed.Count == 0)
                    Reset();
            }
        }
    }

    /// <summary>
    /// Game-wide bookkeeping of which pawn carries which harvest. Saved with the game.
    /// </summary>
    public class HarvestTracker : GameComponent
    {
        private struct PendingSweep
        {
            public Pawn pawn;
            public ThingDef def;
            public IntVec3 cell;
            public Map map;
            public float expectedCount;
        }

        private static HarvestTracker instance;

        private Dictionary<Pawn, HarvestLoad> loads = new Dictionary<Pawn, HarvestLoad>();
        private readonly List<PendingSweep> pendingSweeps = new List<PendingSweep>();
        private readonly List<PendingSweep> processingSweeps = new List<PendingSweep>();

        private List<Pawn> tmpPawns;
        private List<HarvestLoad> tmpLoads;

        public static HarvestTracker Instance => Current.Game == null ? null : instance;

        public HarvestTracker(Game game)
        {
            instance = this;
        }

        public HarvestLoad GetLoad(Pawn pawn)
        {
            if (pawn == null)
                return null;
            return loads.TryGetValue(pawn, out HarvestLoad load) ? load : null;
        }

        public HarvestLoad GetOrCreateLoad(Pawn pawn)
        {
            if (!loads.TryGetValue(pawn, out HarvestLoad load))
            {
                load = new HarvestLoad();
                loads[pawn] = load;
            }
            return load;
        }

        public bool ShouldKeepInInventory(Pawn pawn, Thing thing)
        {
            HarvestLoad load = GetLoad(pawn);
            return load != null && thing != null && load.Owes(thing.def);
        }

        /// <summary>
        /// Look again around the harvest spot on the next tick. This catches yields that get
        /// placed after Plant.PlantCollected runs (other mods, or a different vanilla ordering).
        /// </summary>
        public void QueueSweep(Pawn pawn, ThingDef def, IntVec3 cell, Map map, float expectedCount)
        {
            for (int i = 0; i < pendingSweeps.Count; i++)
            {
                PendingSweep s = pendingSweeps[i];
                if (s.pawn == pawn && s.def == def && s.cell == cell && s.map == map)
                    return;
            }
            pendingSweeps.Add(new PendingSweep { pawn = pawn, def = def, cell = cell, map = map, expectedCount = expectedCount });
        }

        public override void GameComponentTick()
        {
            if (pendingSweeps.Count > 0)
            {
                processingSweeps.Clear();
                processingSweeps.AddRange(pendingSweeps);
                pendingSweeps.Clear();
                for (int i = 0; i < processingSweeps.Count; i++)
                {
                    PendingSweep s = processingSweeps[i];
                    if (s.pawn != null && s.pawn.Spawned && s.pawn.Map == s.map)
                        HarvestHaulUtility.CollectHarvest(s.pawn, s.def, s.cell, s.map, s.expectedCount);
                }
                processingSweeps.Clear();
            }

            if (Find.TickManager.TicksGame % GenDate.TicksPerHour == 0)
                Prune();
        }

        private void Prune()
        {
            loads.RemoveAll(kv => kv.Key == null || kv.Value == null || kv.Value.Empty
                                  || kv.Key.Destroyed || kv.Key.Discarded || kv.Key.Dead);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
                Prune();

            Scribe_Collections.Look(ref loads, "loads", LookMode.Reference, LookMode.Deep, ref tmpPawns, ref tmpLoads);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (loads == null)
                    loads = new Dictionary<Pawn, HarvestLoad>();
                loads.RemoveAll(kv => kv.Key == null || kv.Value == null);
            }
        }
    }
}
