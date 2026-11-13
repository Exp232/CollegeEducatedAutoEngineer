using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Guidance for 0.6.0's rollout controller, which chooses throttle and brake actions by simulating the next
    /// few seconds and scoring them. Its cost function is compiled (Burst) code, so it cannot be changed; what
    /// can be changed is what goes into it: the brake limit it plans with and the score weights.
    /// </summary>
    internal static class BrakeGuidance
    {
        private const float MpsToMph = 2.23694f;

        /// <summary>Below this speed the direction-change limit lifts, so the train can still be held.</summary>
        internal const float FullBrakeBelowMph = 2f;

        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        internal static readonly Type TrainBrakeType = AccessTools.Inner(typeof(AutoEngineer), "TrainBrake");
        private static readonly FieldInfo FTrainBrakeAe = TrainBrakeType != null ? AccessTools.Field(TrainBrakeType, "_ae") : null;

        internal sealed class Record
        {
            public float Raw;
            public float Final;
            public bool Active;
        }

        /// <summary>What the last brake-limit query for each engineer returned (read by the logger).</summary>
        internal static readonly ConditionalWeakTable<AutoEngineer, Record> Records = new ConditionalWeakTable<AutoEngineer, Record>();

        // How the direction-change stop is doing, per engineer: it is given up on when the train is not slowing.
        private sealed class StopState
        {
            public bool Active;
            public bool Abandoned;
            public float RefSpeedMps;
            public float RefTime;
        }
        private static readonly ConditionalWeakTable<AutoEngineer, StopState> Stops = new ConditionalWeakTable<AutoEngineer, StopState>();
        private const float WatchSeconds = 15f;     // the stop must shed at least WatchMinMph within this long
        private const float WatchMinMph = 0.5f;
        private static bool _loggedLight;
        private static bool _loggedAbandon;

        /// <summary>
        /// The train brake alone can only stop a train whose weight is mostly in cars it acts on. A locomotive on its
        /// own (or with a tender and a couple of light cars) has almost nothing for it to hold, and the stop bails
        /// off the locomotive's own brake, so the train would roll on with no brake at all until it is under 2 mph.
        /// True when the cars behind the locomotives and tenders are at least DirectionStopMinTrailingPercent of the
        /// weight (an unreadable consist counts as fine, as before).
        /// </summary>
        internal static bool TrainBrakeHasCars(AutoEngineer ae)
        {
            try
            {
                float minShare = Mathf.Clamp(Main.Settings.DirectionStopMinTrailingPercent, 0f, 100f) / 100f;
                if (minShare <= 0f) return true;
                float total = 0f, trailing = 0f;
                foreach (var car in ae.Locomotive.EnumerateAirOpen())
                {
                    float w = car.Weight;
                    total += w;
                    if (!car.IsLocomotive && !car.CarType.IsTender) trailing += w;
                }
                if (total <= 0f) return true;
                bool ok = trailing >= total * minShare;
                if (!ok && !_loggedLight)
                {
                    _loggedLight = true;
                    Main.Mod.Logger.Log("Direction stop: " + ae.Locomotive.DisplayName + " has too little behind it for the train brake alone (" + (100f * trailing / total).ToString("0") + "% of its weight); the controller's own braking is used (logged once).");
                }
                return ok;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// True while the train is moving with no speed limit order and nothing ahead to stop for - what the
        /// engineer is left with when the direction is changed on a moving train. It ends by itself the moment a
        /// target or a speed limit appears again (a new order, a signal, a waypoint), or the train nearly stops.
        /// It is also false for a train with too little behind the locomotives to brake with the train brake alone,
        /// and it is given up for the rest of the stop when the train has not slowed by WatchMinMph in WatchSeconds.
        /// </summary>
        internal static bool StopWithoutTarget(AutoEngineer ae)
        {
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            if (targets == null || ae.Locomotive == null) return false;
            float speedMps = Mathf.Abs(ae.Locomotive.velocity);
            var list = targets.AllTargets as IList<AutoEngineer.Targets.Target>;
            bool stopping = speedMps * MpsToMph >= FullBrakeBelowMph
                && Mathf.Abs(targets.MaxSpeedMph) <= 0.5f
                && list != null && list.Count == 0;
            StopState st = Stops.GetOrCreateValue(ae);
            if (!stopping)
            {
                st.Active = false;
                st.Abandoned = false;
                return false;
            }
            if (!TrainBrakeHasCars(ae)) return false;
            float now = Time.time;
            if (!st.Active)
            {
                st.Active = true;
                st.Abandoned = false;
                st.RefSpeedMps = speedMps;
                st.RefTime = now;
            }
            if (st.Abandoned) return false;
            if (now - st.RefTime >= WatchSeconds)
            {
                if (speedMps > st.RefSpeedMps - WatchMinMph / MpsToMph)
                {
                    st.Abandoned = true;
                    if (!_loggedAbandon)
                    {
                        _loggedAbandon = true;
                        Main.Mod.Logger.Log("Direction stop: " + ae.Locomotive.DisplayName + " is not slowing with the train brake alone (" + (speedMps * MpsToMph).ToString("0.0") + " mph after " + WatchSeconds.ToString("0") + " s); handing the stop back to the controller (logged once).");
                    }
                    return false;
                }
                st.RefSpeedMps = speedMps;
                st.RefTime = now;
            }
            return true;
        }

        /// <summary>Postfix body for TrainBrake.CalculateMaxEffectiveApplication.</summary>
        internal static void LimitApplication(object trainBrake, ref float result)
        {
            try
            {
                var ae = FTrainBrakeAe?.GetValue(trainBrake) as AutoEngineer;
                if (ae == null) return;

                float raw = result;
                float limit = Main.Settings.DirectionStopBrakeLimit;
                bool active = Main.Active && limit > 0f && StopWithoutTarget(ae);
                if (active) result = Mathf.Min(result, limit);

                var record = Records.GetOrCreateValue(ae);
                record.Raw = raw;
                record.Final = result;
                record.Active = active;
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }

    /// <summary>
    /// Rollout-controller hook: while a moving train has no speed order and nothing ahead to stop for, the
    /// stop is made with the train brake alone. The independent brake is bailed off above 2 mph (it only holds
    /// the train once it is nearly stopped, and trims small errors); the train brake is driven toward a full
    /// application, which TrainBrake.DriveToward limits to the effective maximum (the direction-stop limit).
    /// </summary>
    internal static class DirectionStop
    {
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            if (!Main.Settings.DirectionStopTrainBrakeOnly) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null || !BrakeGuidance.StopWithoutTarget(ae)) return false;
            command = new RolloutCommand(command.Kind, 0f, 0f, 1f, true, true, command.Cost, command.HoldCost);
            RolloutThrottle.CloseFor(ae);
            return true;
        }
    }

    /// <summary>
    /// The planner reads this value as the deepest brake application it may use, and TrainBrake.DriveToward obeys
    /// it, so lowering it while a direction-change stop is under way makes the engineer plan a gentler stop
    /// instead of braking at the game's maximum.
    /// </summary>
    [HarmonyPatch]
    internal static class TrainBrake_CalculateMaxEffectiveApplication_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(BrakeGuidance.TrainBrakeType, "CalculateMaxEffectiveApplication");
        }

        private static void Postfix(object __instance, ref float __result)
        {
            BrakeGuidance.LimitApplication(__instance, ref __result);
        }
    }

    /// <summary>
    /// Scales the rollout controller's score weights (each setting is a multiplier, 1 = the game's value, so a
    /// game update that retunes the stock weights still carries through).
    /// </summary>
    [HarmonyPatch(typeof(RolloutController), "WeightsFor", new[] { typeof(AutoEngineer) })]
    internal static class RolloutController_WeightsFor_Patch
    {
        private static void Postfix(ref RolloutScoreWeights __result)
        {
            try
            {
                var s = Main.Settings;
                __result.Overspeed *= Mathf.Max(0f, s.PlanOverspeedScale);
                __result.Undershoot *= Mathf.Max(0f, s.PlanUndershootScale);
                __result.UndershootDeadbandMps *= Mathf.Max(0f, s.PlanUndershootDeadbandScale);
                __result.Ceiling *= Mathf.Max(0f, s.PlanCeilingScale);
                __result.IndependentUse *= Mathf.Max(0f, s.PlanIndependentUseScale);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
