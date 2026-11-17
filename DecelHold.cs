using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Reads of the planner's target list that other features share: distance to a stop, station stop ahead,
    /// Road mode, and the nearest lower speed limit; and the "Stay stopped at stop targets" patch below.
    /// </summary>
    internal static class DecelHold
    {

        /// <summary>Metres to the nearest stop (0 mph) target still ahead, or a large number.</summary>
        internal static float DistanceToStopTarget(AutoEngineer ae)
        {
            var targets = BrakeFix.TargetsOf(ae);
            var list = targets?.AllTargets as IList<AutoEngineer.Targets.Target>;
            float best = 1e9f;
            if (list == null) return best;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (Mathf.Abs(t.SpeedMph) < 0.1f && t.Distance > -5f && t.Distance < best) best = t.Distance;
            }
            return best;
        }

        /// <summary>True when a station stop is listed within this many metres ahead (or being served at the platform).</summary>
        internal static bool StationStopWithin(AutoEngineer ae, float meters)
        {
            var targets = BrakeFix.TargetsOf(ae);
            var list = targets?.AllTargets as IList<AutoEngineer.Targets.Target>;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t.Reason != null && t.Reason.StartsWith("Station Stop", System.StringComparison.Ordinal) && t.Distance > -5f && t.Distance < meters) return true;
            }
            return false;
        }

        /// <summary>True in Road mode (signals, track speed). Waypoint and Yard keep the game's own stopping accuracy.</summary>
        internal static bool IsRoad(AutoEngineer ae)
        {
            var targets = BrakeFix.TargetsOf(ae);
            return targets != null && targets.Mode.ToString() == "Road";
        }

        /// <summary>
        /// The nearest speed limit ahead (5 mph or more, so not a stop) that is lower than where the train is
        /// headed. The planner only lists targets within its lookahead, so "ahead" is whatever the AI can see.
        /// Speeds are signed by direction of travel, so compare magnitudes.
        /// </summary>
        internal static bool LowerLimitAhead(AutoEngineer ae, float ctxMph, out float limitMph)
        {
            limitMph = 0f;
            var targets = BrakeFix.TargetsOf(ae);
            var list = targets?.AllTargets as IList<AutoEngineer.Targets.Target>;
            if (list == null) return false;
            float bestDistance = 1e9f;
            bool found = false;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                float v = Mathf.Abs(t.SpeedMph);
                if (v < 5f || t.Distance <= 0f || v >= ctxMph - 0.5f) continue;
                if (t.Distance < bestDistance) { bestDistance = t.Distance; limitMph = v; found = true; }
            }
            return found;
        }
    }

    /// <summary>
    /// The planner's speed curve allows about 0.5 m/s (1.1 mph) even at 1 m from a stop target, and AutoEngineer
    /// only treats a target as "the" target once it is within 1 m (TargetSpeedMph). A train that stops 1-3 m
    /// short of its target therefore counts as "stopped but should be moving": the start-up routine runs
    /// (reverser to full, throttle 25-50%) and the train creeps past the signal until the brake catches it.
    /// Reporting a contextual target of 0 within a couple of metres of a stop target makes the AI hold the stop.
    /// </summary>
    [HarmonyPatch(typeof(AutoEngineer), "ContextualTargetVelocity")]
    internal static class AutoEngineer_ContextualTargetVelocity_Patch
    {
        private const float StopWithinMeters = 2.5f;
        private const float WaypointHoldMeters = 1.5f;
        private const float StandingMps = 0.09f;   // 0.2 mph
        private const float GameArrivalMeters = 1f;   // the game's own "at the target" distance
        private const float NewTargetJumpMeters = 0.3f;

        private sealed class Arrival
        {
            public bool Arrived;
            public float LastDistance = 1e9f;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, Arrival> Arrivals = new ConditionalWeakTable<AutoEngineer, Arrival>();

        private static void Postfix(AutoEngineer __instance, ref float __result)
        {
            if (!Main.Active || __result == 0f || !Main.Settings.HoldStopAtStopTargets) return;
            if (!DecelHold.IsRoad(__instance))
            {
                // Waypoint/Yard keep the game's own stopping accuracy (it stops once the target is under 1 m away). But a train that stopped at
                // 0.99 m can be eased back past 1 m by its coupler slack, and the game then starts it again for the last few centimetres; with
                // the slip guard's starting throttle it only creeps back to 0.99 m and the cycle repeats (an EMR 1000 did that for nearly two
                // minutes in the 1.6.1 log). Standing still, a stop target within 1.5 m counts as reached.
                // Only for a stop the train has arrived at (been within the game's 1 m of): a waypoint newly set a few feet away, as for spotting a
                // car, is driven to as before (1.6.2 to 1.6.5 held a standing train at any stop target within 1.5 m, so a waypoint set 4 ft ahead
                // was never run to). A new target shows as a jump in distance; slack only moves a standing train a few centimetres.
                var loco = __instance.Locomotive;
                float dist = DecelHold.DistanceToStopTarget(__instance);
                Arrival a = Arrivals.GetOrCreateValue(__instance);
                if (dist >= WaypointHoldMeters || Mathf.Abs(dist - a.LastDistance) > NewTargetJumpMeters) a.Arrived = false;
                if (dist < GameArrivalMeters) a.Arrived = true;
                a.LastDistance = dist;
                // Only between 1 m and 1.5 m: inside 1 m the game's own rules decide, exactly as stock, so the hold can never keep a train that
                // the game would move (a user on 1.6.4 had trains stopping and holding about a foot from the waypoint).
                if (a.Arrived && loco != null && Mathf.Abs(loco.velocity) < StandingMps && dist >= GameArrivalMeters && dist < WaypointHoldMeters) __result = 0f;
                return;
            }
            if (DecelHold.DistanceToStopTarget(__instance) < StopWithinMeters) __result = 0f;
        }
    }
}
