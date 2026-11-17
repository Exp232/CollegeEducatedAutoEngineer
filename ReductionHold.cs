using System;
using HarmonyLib;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// No power into a speed reduction (0.6 rollout controller). The controller sometimes keeps the throttle open
    /// (even accelerating) while a lower limit is already in sight and the train is at or above that limit. This
    /// closes the throttle in that case, so the train coasts down before the brake curve takes over. Power returns
    /// when the train is more than 1 mph under the lower limit. Not applied on a steep climb (the train would
    /// stall) or below 5 mph.
    /// </summary>
    internal static class ReductionHold
    {
        private const float MpsToMph = 2.23694f;
        private const float SteepUphillPercent = 1.5f;

        /// <summary>Returns true when the command's throttle was closed.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            if (!Main.Settings.AvoidPowerIntoReductions || command.ThrottleNotch <= 0.001f) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null || StallGuard.IsLatched(ae)) return false;
            if (BrakeTaps.FContextualField == null || !BrakeFix.TargetsReadable) return false;

            float speedMph = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            if (speedMph < 5f) return false;
            float ctxMph = Mathf.Abs((float)BrakeTaps.FContextualField.GetValue(ae)) * MpsToMph;
            if (ctxMph <= 0f) return false;

            var targets = BrakeFix.TargetsOf(ae);
            if (targets != null && targets.AverageGradeUnder <= -SteepUphillPercent) return false;

            if (!DecelHold.LowerLimitAhead(ae, ctxMph, out float lowerMph) || speedMph <= lowerMph - 1f) return false;

            command = new RolloutCommand(command.Kind, 0f, command.IndependentBrake01, command.TrainBrakeSet, command.DrivesTrainBrake, command.BailOff, command.Cost, command.HoldCost);
            return true;
        }
    }
}
