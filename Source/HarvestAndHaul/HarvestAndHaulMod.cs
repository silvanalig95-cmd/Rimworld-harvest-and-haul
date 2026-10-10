using HarmonyLib;
using UnityEngine;
using Verse;

namespace HarvestAndHaul
{
    public class HarvestAndHaulMod : Mod
    {
        public const string HarmonyId = "silvanalig95.HarvestAndHaul";
        public const string LogPrefix = "[Harvest & Haul] ";

        public static HarvestAndHaulSettings Settings { get; private set; }

        public HarvestAndHaulMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<HarvestAndHaulSettings>();
            HarmonyPatches.Apply(new Harmony(HarmonyId));
        }

        public override string SettingsCategory() => "HarvestAndHaul_SettingsCategory".Translate();

        public override void DoSettingsWindowContents(Rect inRect) => Settings.DoWindowContents(inRect);

        public static void Debug(string message)
        {
            if (Settings != null && Settings.verboseLogging)
                Log.Message(LogPrefix + message);
        }
    }
}
