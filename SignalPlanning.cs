using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Model;
using Model.AI;
using Track;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Signal handling in AutoEngineerPlanner.UpdateTargets has two quirks that make the AI brake hard for an
    /// Approach signal:
    ///
    ///  1. A signal's aspect is only acted on once the signal is within 200 m (three "distance > 200f" tests).
    ///  2. The Approach restriction is then added as "25 mph at distance 0" (right now) instead of at the
    ///     signal, so the AI sees a target it has already violated and brakes at full effort.
    ///
    /// This mod (a) replaces the 200 m constants with a setting, and (b) moves the "Approach Signal" target to
    /// the distance of the signal it belongs to.
    /// </summary>
    internal static class SignalPlanning
    {
        internal const float StockSightMeters = 200f;

        /// <summary>True when DistanceToNextSignalAfter ran during the current UpdateTargets (an Approach aspect was handled).</summary>
        internal static bool Fresh;
        /// <summary>Distance from the Approach signal to the next signal, 0 when there is none within range.</summary>
        internal static float Gap;
        private static bool _warned;

        internal static Type PlannerType => AccessTools.TypeByName("Model.AI.AutoEngineerPlanner");

        /// <summary>Called from the patched UpdateTargets in place of the literal 200f.</summary>
        public static float SignalSightMeters()
        {
            if (!Main.Active) return StockSightMeters;
            return Mathf.Clamp(Main.Settings.SignalSightMeters, StockSightMeters, 900f);
        }

        private static bool _wqChecked, _wq;

        /// <summary>True when the Waypoint Queue mod is loaded. It rewrites the waypoint target's speed in AutoEngineer.SetRouteProfile and finds that target by its reason and by its distance (within 10 m).</summary>
        internal static bool WaypointQueueLoaded
        {
            get
            {
                if (!_wqChecked)
                {
                    _wqChecked = true;
                    try
                    {
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            if (assembly.GetName().Name == "WaypointQueue") { _wq = true; break; }
                        }
                    }
                    catch (Exception) { }
                }
                return _wq;
            }
        }

        internal static bool IsWaypointTarget(string reason)
        {
            return reason == "Running to waypoint" || reason == "At waypoint";
        }

        internal static void Warn(string message)
        {
            if (_warned) return;
            _warned = true;
            Main.Mod?.Logger.Warning(message);
        }
    }

    [HarmonyPatch]
    internal static class Planner_UpdateTargets_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(SignalPlanning.PlannerType, "UpdateTargets");
        }

        private static void Prefix()
        {
            SignalPlanning.Fresh = false;
            SignalPlanning.Gap = 0f;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.Method(typeof(SignalPlanning), nameof(SignalPlanning.SignalSightMeters));
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && Math.Abs(f - 200f) < 0.001f)
                {
                    ins.opcode = OpCodes.Call;   // keeps labels and exception blocks on the instruction
                    ins.operand = getter;
                    replaced++;
                }
                yield return ins;
            }
            Main.Mod?.Logger.Log("Signal sight: replaced " + replaced + " distance constant(s) (expected 3).");
        }
    }

    /// <summary>Remembers the gap between the Approach signal and the one after it.</summary>
    [HarmonyPatch]
    internal static class Planner_DistanceToNextSignalAfter_Patch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(SignalPlanning.PlannerType, "DistanceToNextSignalAfter");
        }

        private static void Postfix(float? __result)
        {
            SignalPlanning.Fresh = true;
            SignalPlanning.Gap = __result ?? 0f;
        }
    }

    /// <summary>
    /// Post-processes the targets the planner hands to the engineer:
    ///  - moves the "Approach Signal" target from distance 0 to the Approach signal's own distance;
    ///  - after that signal is passed, keeps its speed limit (as an "Approach Hold" target) until the next
    ///    signal, because the game drops the restriction the moment the signal is behind the train, before
    ///    the next one is registered, and the AI accelerates in that gap.
    /// </summary>
    [HarmonyPatch(typeof(AutoEngineer), "SetTargets")]
    internal static class AutoEngineer_SetTargets_Patch
    {
        private sealed class HoldState
        {
            public bool Active;
            public float Speed;
            public float Near;      // metres to the Approach signal (negative once passed)
            public float Far;       // metres to the signal after it
            public float LastTime;
            public float Started;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AutoEngineer, HoldState> Holds =
            new System.Runtime.CompilerServices.ConditionalWeakTable<AutoEngineer, HoldState>();

        private const float PassedWithinMeters = 15f;
        private const float MaxHoldSeconds = 120f;

        private static void Prefix(AutoEngineer __instance, AutoEngineer.Targets __0)
        {
            bool fresh = SignalPlanning.Fresh;
            SignalPlanning.Fresh = false;
            if (!Main.Active || __0 == null) return;
            try
            {
                var list = __0.AllTargets as IList<AutoEngineer.Targets.Target>;
                if (list == null || list.IsReadOnly)
                {
                    SignalPlanning.Warn("SetTargets: AllTargets is not an editable list; signal fixes disabled.");
                    return;
                }
                if (Main.Settings.LogEnabled && Main.Settings.LogSignals) SignalLog.CaptureRaw(__instance, list);   // before any edit below
                if (fresh && SignalPlanning.Gap <= 0f) LimitApproachWithoutNext(list);
                if (fresh && Main.Settings.ApproachTargetAtSignal) MoveApproachTarget(list);
                if (Main.Settings.HoldApproachSpeed) HoldApproach(__instance, list);
                if (Main.Settings.SpeedReductionLeadSeconds > 0f) ShiftReductions(__instance, __0, list);
                if (__0.Mode.ToString() == "Off") StallGuard.Reset(__instance);
            }
            catch (Exception e)
            {
                SignalPlanning.Warn("SetTargets signal fix failed: " + e.Message);
            }
        }


        private const float FullRouteMeters = 1500f;

        /// <summary>
        /// Walks 1.5 km of route ahead the way the planner does (for the diagnostic log only) and finds the slowest limit on it, where the
        /// first drop below the starting limit is, and why that slowest piece is slow. False when there is no drop.
        /// </summary>
        internal static bool ScanRoute(AutoEngineer ae, bool forward, out float slowest, out float firstDrop, out string cause)
        {
            slowest = 0f;
            firstDrop = float.MaxValue;
            cause = "";
            BaseLocomotive loco = ae.Locomotive;
            Graph graph = Graph.Shared;
            if (loco == null || graph == null) return false;
            float equip = float.MaxValue;
            foreach (Car car in loco.EnumerateCoupled())
            {
                if (car != null) equip = Mathf.Min(equip, car.MaximumTrackCurvature);
            }
            Location head = DetailedStatus.HeadOf(loco, forward);
            if (!head.IsValid) return false;

            bool first = true;
            TrackSegment slowSegment = null;
            RouteWalker walker = graph.WalkRoute(head, FullRouteMeters);
            while (walker.TryNext(out WalkSegment ws))
            {
                float v = DetailedStatus.SegmentSpeed(graph, ws.Segment, equip);
                if (first) { slowest = v; slowSegment = ws.Segment; first = false; continue; }
                if (v < slowest)
                {
                    slowest = v;
                    slowSegment = ws.Segment;
                    firstDrop = Mathf.Min(firstDrop, ws.EntryDistance);
                }
            }
            if (first || firstDrop >= float.MaxValue) return false;
            cause = DetailedStatus.ShortCause(graph, slowSegment, equip);
            return true;
        }

        private static int Find(IList<AutoEngineer.Targets.Target> list, string reason)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Reason == reason) return i;
            }
            return -1;
        }

        /// <summary>
        /// The game sets 25 mph at an Approach signal when it finds no signal after it. With ApproachNoNextSignalMph
        /// below 25 this lowers that limit (both the "Approach Signal" and "Signal" targets). At the default of 25
        /// nothing changes. Speeds are signed by direction of travel, so only the magnitude is compared.
        /// </summary>
        private static void LimitApproachWithoutNext(IList<AutoEngineer.Targets.Target> list)
        {
            float limit = Main.Settings.ApproachNoNextSignalMph;
            if (limit < 5f || limit >= 25f) return;
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t.Reason != "Approach Signal" && t.Reason != "Signal") continue;
                if (Mathf.Abs(t.SpeedMph) <= limit) continue;
                list[i] = new AutoEngineer.Targets.Target(Mathf.Sign(t.SpeedMph) * limit, t.Distance, t.Reason);
                changed = true;
            }
            if (changed) SortByDistance(list);
        }

        private static void MoveApproachTarget(IList<AutoEngineer.Targets.Target> list)
        {
            int approach = Find(list, "Approach Signal");
            int signal = Find(list, "Signal");
            if (approach < 0 || signal < 0) return;

            float near = Mathf.Max(0f, list[signal].Distance - SignalPlanning.Gap);
            var a = list[approach];
            if (near <= a.Distance) return;
            list[approach] = new AutoEngineer.Targets.Target(a.SpeedMph, near, a.Reason);
            SortByDistance(list);
        }

        private static void HoldApproach(AutoEngineer ae, IList<AutoEngineer.Targets.Target> list)
        {
            var st = Holds.GetOrCreateValue(ae);
            float now = Time.time;
            float dt = st.LastTime > 0f ? Mathf.Clamp(now - st.LastTime, 0f, 3f) : 0f;
            st.LastTime = now;
            float speed = ae.Locomotive != null ? Mathf.Abs(ae.Locomotive.velocity) : 0f;
            float travelled = speed * dt;

            int approach = Find(list, "Approach Signal");
            if (approach >= 0)
            {
                int far = Find(list, "Signal");
                st.Active = true;
                st.Speed = list[approach].SpeedMph;
                st.Near = list[approach].Distance;
                st.Far = far >= 0 ? list[far].Distance : st.Near;
                st.Started = now;
                return;
            }
            if (!st.Active) return;

            st.Near -= travelled;
            st.Far -= travelled;
            // Only hold once the Approach signal has been passed. If it was still ahead when the target
            // vanished, its aspect changed and the limit no longer applies.
            if (st.Near > PassedWithinMeters || st.Far <= 0f || speed < 0.3f || now - st.Started > MaxHoldSeconds)
            {
                st.Active = false;
                return;
            }
            list.Add(new AutoEngineer.Targets.Target(st.Speed, 0f, "Approach Hold"));
            SortByDistance(list);
        }

        /// <summary>
        /// The AI waits until the last moment and then brakes hard for a lower limit, and the brakes keep
        /// bleeding speed for seconds after the release, so the train ends up well under the limit. Treat each
        /// lower limit as starting a few seconds of travel sooner: braking begins earlier and is lighter.
        /// Stops are left alone (their accuracy matters), and so are limits the train is already under.
        /// </summary>
        private static void ShiftReductions(AutoEngineer ae, AutoEngineer.Targets targets, IList<AutoEngineer.Targets.Target> list)
        {
            if (ae.Locomotive == null) return;
            float speedMps = Mathf.Abs(ae.Locomotive.velocity);
            float lead = Mathf.Min(300f, speedMps * Mathf.Clamp(Main.Settings.SpeedReductionLeadSeconds, 0f, 15f));
            if (lead < 1f) return;
            float upper = Mathf.Abs(targets.MaxSpeedMph);
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                float v = Mathf.Abs(t.SpeedMph);
                if (v < 5f || t.Distance <= 0f || v >= upper - 0.5f || t.Reason == "Approach Hold") continue;
                // Waypoint Queue finds the waypoint target again by its distance; moving it would stop its passing speed from applying.
                if (Main.Settings.WaypointQueueCompat && SignalPlanning.IsWaypointTarget(t.Reason) && SignalPlanning.WaypointQueueLoaded) continue;
                list[i] = new AutoEngineer.Targets.Target(t.SpeedMph, Mathf.Max(0f, t.Distance - lead), t.Reason);
                changed = true;
            }
            if (changed) SortByDistance(list);
        }

        // Stable insertion sort by distance, like the planner's OrderBy (the list is tiny).
        private static void SortByDistance(IList<AutoEngineer.Targets.Target> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                var t = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].Distance > t.Distance) { list[j + 1] = list[j]; j--; }
                list[j + 1] = t;
            }
        }
    }
}
