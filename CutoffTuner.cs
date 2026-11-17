using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Physics;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// The stock cutoff table picks, for each speed, the longest cutoff that reaches the peak of
    /// TrainMath.ReverserPowerMultiplier. Steam (and so coal) use grows with cutoff while tractive effort only
    /// follows the multiplier, so a shorter cutoff that keeps nearly the same multiplier gives the same pull for
    /// less steam. This picks the shortest cutoff within a tolerance of the peak. It can only shorten the
    /// stock cutoff, never lengthen it.
    /// </summary>
    internal static class CutoffTuner
    {
        private const float MpsToMph = 2.23694f;
        private const float BlendStartMph = 5f;   // at or below this speed the stock value is used
        private const float BlendEndMph = 10f;    // from here up the tuned value is used in full
        private const float SpeedGuardMph = 2f;   // must also hold this far below the current speed
        private const float MinCutoff = 0.15f;
        private const float Step = 0.05f;

        private const float GradeBlendBandPercent = 0.5f; // eases back to stock over this much grade below the limit

        private static readonly ConditionalWeakTable<SteamLocomotive, AutoEngineer> Engineers =
            new ConditionalWeakTable<SteamLocomotive, AutoEngineer>();

        /// <summary>
        /// Steepest uphill grade (percent, positive = uphill) under the train or just ahead of it.
        /// Returns 0 if it cannot be determined, which keeps the tuned cutoff (the speed guard still applies).
        /// </summary>
        private static float UphillGradePercent(SteamLocomotive loco)
        {
            AutoEngineer ae;
            if (!Engineers.TryGetValue(loco, out ae) || ae == null)
            {
                ae = loco.GetComponent<AutoEngineer>();
                if (ae == null)
                {
                    foreach (var candidate in UnityEngine.Object.FindObjectsOfType<AutoEngineer>())
                    {
                        if (candidate.Locomotive == loco) { ae = candidate; break; }
                    }
                }
                if (ae == null) return 0f;
                Engineers.Remove(loco);
                Engineers.Add(loco, ae);
            }

            var targets = BrakeFix.TargetsOf(ae);
            if (targets == null) return 0f;
            // The logs show uphill as a negative grade.
            float under = -targets.AverageGradeUnder;
            float ahead = -targets.AverageGradeAhead;
            float uphill = Mathf.Max(under, ahead);
            return float.IsNaN(uphill) ? 0f : uphill;
        }

        internal static float Adjust(SteamLocomotive loco, float velocityMps, float stock)
        {
            float mph = Mathf.Abs(velocityMps) * MpsToMph;
            if (mph <= BlendStartMph) return stock;

            // Steep climbs: hand back to the stock cutoff (full blend back by the configured grade).
            float limit = Mathf.Max(0.5f, Main.Settings.CutoffRevertGradePercent);
            float grade = UphillGradePercent(loco);
            float keepTuned = 1f - Mathf.InverseLerp(limit - GradeBlendBandPercent, limit, grade);
            if (keepTuned <= 0f) return stock;

            float maxSpeed = loco.engine.MaximumSpeedMph;
            float tolerance = Mathf.Clamp(Main.Settings.CutoffTolerancePercent, 0f, 10f) / 100f;

            float tuned = Shortest(mph, maxSpeed, tolerance);
            float w = Mathf.InverseLerp(BlendStartMph, BlendEndMph, mph);
            float result = Mathf.Lerp(stock, Mathf.Min(stock, tuned), w * keepTuned);
            return Mathf.Min(stock, result);
        }

        private static float Shortest(float mph, float maxSpeed, float tolerance)
        {
            float peakHere = Peak(mph, maxSpeed);
            float below = Mathf.Max(0f, mph - SpeedGuardMph);
            float peakBelow = Peak(below, maxSpeed);

            for (float c = MinCutoff; c <= 1.0001f; c += Step)
            {
                if (TrainMath.ReverserPowerMultiplier(c, mph, maxSpeed) >= (1f - tolerance) * peakHere &&
                    TrainMath.ReverserPowerMultiplier(c, below, maxSpeed) >= (1f - tolerance) * peakBelow)
                {
                    return c;
                }
            }
            return 1f;
        }

        private static float Peak(float mph, float maxSpeed)
        {
            float peak = 0f;
            for (float c = 0.1f; c <= 1.0001f; c += Step)
            {
                peak = Mathf.Max(peak, TrainMath.ReverserPowerMultiplier(c, mph, maxSpeed));
            }
            return peak;
        }
    }

    [HarmonyPatch(typeof(SteamLocomotive), nameof(SteamLocomotive.CutoffSettingForVelocity))]
    internal static class SteamLocomotive_CutoffSettingForVelocity_Patch
    {
        private static bool _failed;

        private static void Postfix(SteamLocomotive __instance, float velocityMps, ref float __result)
        {
            if (_failed || !Main.Active || !Main.Settings.CutoffEnabled) return;
            try
            {
                __result = CutoffTuner.Adjust(__instance, velocityMps, __result);
            }
            catch (Exception e)
            {
                _failed = true; // log once and fall back to stock behaviour
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
