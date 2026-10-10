using System;
using System.Reflection;
using Verse;

namespace HarvestAndHaul
{
    /// <summary>
    /// Optional, reflection-only support for Combat Extended's bulk/weight inventory limits.
    /// Does nothing when CE is not loaded; disables itself if CE's API ever changes.
    /// </summary>
    public static class CombatExtendedCompat
    {
        private static bool initialized;
        private static Type compInventoryType;
        private static MethodInfo canFitInInventory;

        public static int LimitCount(Pawn pawn, Thing thing, int count)
        {
            if (!initialized)
                Initialize();
            if (canFitInInventory == null || count <= 0)
                return count;

            try
            {
                ThingComp comp = null;
                var comps = pawn.AllComps;
                for (int i = 0; i < comps.Count; i++)
                {
                    if (compInventoryType.IsInstanceOfType(comps[i]))
                    {
                        comp = comps[i];
                        break;
                    }
                }
                if (comp == null)
                    return count;

                ParameterInfo[] parameters = canFitInInventory.GetParameters();
                object[] args = new object[parameters.Length];
                args[0] = thing;
                args[1] = 0;
                for (int i = 2; i < parameters.Length; i++)
                    args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;

                bool fits = (bool)canFitInInventory.Invoke(comp, args);
                return fits ? Math.Min(count, (int)args[1]) : 0;
            }
            catch (Exception e)
            {
                canFitInInventory = null;
                Log.Warning(HarvestAndHaulMod.LogPrefix + "Combat Extended inventory check failed, falling back to vanilla mass limits: " + e.Message);
                return count;
            }
        }

        private static void Initialize()
        {
            initialized = true;
            try
            {
                compInventoryType = GenTypes.GetTypeInAnyAssembly("CombatExtended.CompInventory");
                if (compInventoryType == null)
                    return;
                foreach (MethodInfo method in compInventoryType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name != "CanFitInInventory" || method.ReturnType != typeof(bool))
                        continue;
                    ParameterInfo[] p = method.GetParameters();
                    bool restOptional = true;
                    for (int i = 2; i < p.Length; i++)
                        restOptional &= p[i].HasDefaultValue;
                    if (p.Length >= 2 && typeof(Thing).IsAssignableFrom(p[0].ParameterType)
                        && p[1].IsOut && p[1].ParameterType.GetElementType() == typeof(int) && restOptional)
                    {
                        canFitInInventory = method;
                        Log.Message(HarvestAndHaulMod.LogPrefix + "Combat Extended detected, using its inventory limits.");
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                canFitInInventory = null;
                Log.Warning(HarvestAndHaulMod.LogPrefix + "Could not set up Combat Extended support: " + e.Message);
            }
        }
    }
}
