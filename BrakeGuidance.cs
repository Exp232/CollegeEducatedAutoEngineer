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
            var targets = BrakeFix.TargetsOf(ae);
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
    /// Game 0.6.3 leaves the speed loop as soon as a moving train's command is zero or the other way
    /// (IsMovingAgainstCommand) and goes to StopAndHold, which writes the train brake handle to 1 (a 90 psi
    /// reduction: an emergency application) and the locomotive brake to 1 every half second until the train
    /// stands. It was added for a train creeping past its stop mark with the brakes off, but it also fires for a
    /// speed order of 0 or a change of direction on a train at speed: in a 1.7.1 log, 0 mph ordered at 36 mph
    /// put the train in emergency at once. Here a moving train is only left to StopAndHold for that stop-mark
    /// case (a stop target within the game's 1 m, or passed); otherwise the speed loop stops it with a service
    /// application, as in 0.6.2, and the direction stop above, the car wheel lock cap and the direction-change
    /// pause all keep working. The game's own emergency stop for a stop target overrun by 3 m is untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class AutoEngineer_IsMovingAgainstCommand_Patch
    {
        private static bool _logged;

        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(AutoEngineer), "IsMovingAgainstCommand");
        }

        private static void Postfix(AutoEngineer __instance, ref bool __result)
        {
            if (!__result || !Main.Active) return;
            try
            {
                var targets = BrakeFix.TargetsOf(__instance);
                if (targets == null || __instance.Locomotive == null) return;
                var list = targets.AllTargets;
                if (list != null)
                {
                    foreach (var t in list)
                    {
                        // The game's TargetSpeedMph: the first target within 1 m. A stop there is the stop-mark case: leave it to the game.
                        if (t.Distance < 1f)
                        {
                            if (Mathf.Abs(t.SpeedMph) < 0.01f) return;
                            break;
                        }
                    }
                }
                __result = false;
                if (!_logged)
                {
                    _logged = true;
                    Main.Mod.Logger.Log("Stop order: " + __instance.Locomotive.DisplayName + " is stopped by the speed loop with a service application at "
                        + (Mathf.Abs(__instance.Locomotive.velocity) * 2.23694f).ToString("0.0") + " mph, not by the game's StopAndHold emergency application (logged once).");
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }

    /// <summary>
    /// Scales the rollout controller's score weights (each setting is a multiplier, 1 = the game's value, so a
    /// game update that retunes the stock weights still carries through).
    /// </summary>
    [HarmonyPatch(typeof(RolloutController), "WeightsFor", new[] { typeof(AutoEngineer) })]
    internal static class RolloutController_WeightsFor_Patch
    {
        private static readonly FieldInfo FFraction = AccessTools.Field(typeof(RolloutScoreWeights), "UndershootDeadbandFraction");

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
                // 0.6.3: the undershoot deadband is min(UndershootDeadbandMps, UndershootDeadbandFraction x target),
                // so the same margin scale goes on the fraction. Reflection keeps 0.6.2 (no such field) working.
                if (FFraction != null && s.PlanUndershootDeadbandScale != 1f)
                {
                    object boxed = __result;
                    FFraction.SetValue(boxed, (float)FFraction.GetValue(boxed) * Mathf.Max(0f, s.PlanUndershootDeadbandScale));
                    __result = (RolloutScoreWeights)boxed;
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
