using System;
using HarmonyLib;
using Model.AI;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Runs as each AutoEngineer is enabled: attaches the logger's sampler, the throttle glider (which also runs the per-frame
    /// guards) and the water stop, resets the stall guard and the direction-change pause, and dumps the live config once.
    /// </summary>
    [HarmonyPatch(typeof(AutoEngineer), "OnEnable")]
    internal static class AutoEngineer_OnEnable_Patch
    {
        private static void Postfix(AutoEngineer __instance)
        {
            Attach(__instance);
        }

        /// <summary>Also called for engineers that were already running when the mod was switched on.</summary>
        internal static void Attach(AutoEngineer __instance)
        {
            try
            {
                if (__instance.GetComponent<AutoEngineerSampler>() == null)
                {
                    __instance.gameObject.AddComponent<AutoEngineerSampler>();
                }

                if (__instance.GetComponent<ThrottleGlider>() == null)
                {
                    __instance.gameObject.AddComponent<ThrottleGlider>();
                }

                if (__instance.GetComponent<WaterStop>() == null)
                {
                    __instance.gameObject.AddComponent<WaterStop>();
                }

                BrakeFix.Register(__instance);
                if (Main.Settings.LogEnabled)
                {
                    ConfigDump.DumpOnce(__instance);
                    ConfigDump.DumpEngineOnce(__instance);
                    if (Main.Settings.LogCtc) CtcDump.DumpOnce();
                }
                // After the dump, so the dump shows the map as the game made it.
                try { CtcFix.Apply(); }
                catch (Exception e) { Main.Mod.Logger.Warning("CTC fix failed: " + e.Message); }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
