using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Game.Messages;
using HarmonyLib;
using Model;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// A train that cannot climb a grade ends up in a loop: full throttle, speed falls to nothing, the train
    /// rolls back, the planner loses its targets, the AI brakes lightly, sees targets again, opens the
    /// throttle, and so on, until someone stops it. Neither the game nor the planner has a stall check.
    ///
    /// This one declares a stall when the throttle has been fully open for a while on an uphill grade without
    /// the train gaining speed, and it is under 4 mph. From then on the throttle stays shut, the brakes stay
    /// applied, the start-up routine is skipped and the engineer says so. It lasts until the engineer is
    /// switched off and on again.
    /// </summary>
    internal static class StallGuard
    {
        private const float MpsToMph = 2.23694f;
        private const float MaxSpeedMph = 4f;
        private const float MinUphillPercent = 1f;
        private const float StandMaxMph = 0.5f;
        private const float StandMinThrottle = 0.5f;
        private const float StandGainMph = 0.15f;

        private sealed class State
        {
            public bool Latched;
            public float Timer;
            public float LastTime;
            public float PrevSpeed;
            public float PrevSpeedTime = -100f;
            public float Trend;
            public float LatchTime;
            public float StandTimer;
            public float StandSmooth;
            public float StandRef;
            public string LatchMode = "";
            public int LatchSign;
            public float StillSince = -1f;
            public bool Settled;
            // Waypoint mode: rollback detection and the hold after the speed order was set to 0.
            public bool Armed;
            public int SeenForward;
            public OrderWaypoint? SeenWaypoint;
            public bool WpHold;
            public float NextHold;
            public OrderWaypoint? LatchWaypoint;
        }

        private const float RollbackMph = 0.5f;
        private const float ArmMph = 1f;

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly MethodInfo MSay = AccessTools.Method(SignalPlanning.PlannerType, "Say", new[] { typeof(string) });
        private static readonly MethodInfo MNotice = AccessTools.Method(typeof(BaseLocomotive), "PostNotice", new[] { typeof(string), typeof(string) });

        internal static bool IsLatched(AutoEngineer ae) => Main.Settings.StallGuard && States.TryGetValue(ae, out State st) && st.Latched;

        /// <summary>
        /// Train brake level while latched: a firm 60% (with the locomotive brake full) while the train is still
        /// moving, so it stops without a jolt, then full once it has been still for a second or 8 s have passed.
        /// </summary>
        internal static float BrakeLevel(AutoEngineer ae)
        {
            if (!States.TryGetValue(ae, out State st) || !st.Latched) return 1f;
            if (st.Settled) return 1f;
            float now = Time.time;
            float speed = ae.Locomotive != null ? Mathf.Abs(ae.Locomotive.velocity) * MpsToMph : 0f;
            if (speed < 0.3f)
            {
                if (st.StillSince < 0f) st.StillSince = now;
            }
            else st.StillSince = -1f;
            if ((st.StillSince >= 0f && now - st.StillSince >= 1f) || now - st.LatchTime >= 8f) st.Settled = true;
            return st.Settled ? 1f : 0.6f;
        }

        /// <summary>
        /// The independent brake only holds the train once it is nearly stopped (or the hold has settled); while the
        /// train is still moving the train brake does the stopping and the independent is bailed off.
        /// </summary>
        internal static bool IndependentOn(AutoEngineer ae)
        {
            if (!States.TryGetValue(ae, out State st) || !st.Latched || st.Settled) return true;
            return ae.Locomotive == null || Mathf.Abs(ae.Locomotive.velocity) * MpsToMph < BrakeGuidance.FullBrakeBelowMph;
        }

        /// <summary>Writes the brake lever for the hold: train brake level, independent only when IndependentOn.</summary>
        internal static void ApplyHoldBrakes(AutoEngineer ae)
        {
            float train = BrakeLevel(ae);
            BrakeFix.WriteControl(ae, "TrainBrake", train);
            BrakeFix.WriteControl(ae, "LocomotiveBrake", IndependentOn(ae) ? 1f : 0f);
        }

        private static bool TryOrders(AutoEngineer ae, out Orders orders)
        {
            orders = default;
            try
            {
                if (ae == null || ae.Locomotive == null) return false;
                orders = new AutoEngineerPersistence(ae.Locomotive.KeyValueObject).Orders;
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static void Reset(AutoEngineer ae)
        {
            if (States.TryGetValue(ae, out State st) && st.Latched)
            {
                Main.Mod.Logger.Log("Stall guard cleared.");
            }
            if (States.TryGetValue(ae, out State s2)) { s2.Latched = false; s2.Timer = 0f; s2.Settled = false; s2.StandTimer = 0f; s2.StillSince = -1f; s2.Armed = false; s2.WpHold = false; }
        }



        /// <summary>The engineer's mode and direction of travel (the sign of the speed limit, which the planner signs by direction).</summary>
        private static void ReadOrders(AutoEngineer ae, out string mode, out int sign)
        {
            // The orders themselves, not the planner's targets: the planner swaps in empty targets (and a different
            // mode) while it stops a train, which would look like the player changing the orders.
            if (TryOrders(ae, out Orders o))
            {
                mode = o.Mode.ToString();
                sign = o.Forward ? 1 : -1;
                return;
            }
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            mode = targets != null ? targets.Mode.ToString() : "";
            float m = targets != null ? targets.MaxSpeedMph : 0f;
            sign = m > 0.5f ? 1 : (m < -0.5f ? -1 : 0);
        }

        /// <summary>While latched: switching the engineer's direction or mode clears the stall.</summary>
        private static void CheckOrdersChanged(AutoEngineer ae, State st)
        {
            if (st.WpHold && TryOrders(ae, out Orders o))
            {
                if (o.MaxSpeedMph > 0)
                {
                    Main.Mod.Logger.Log("Stall guard cleared: a speed order was set again.");
                    Reset(ae);
                    return;
                }
                if (!Nullable.Equals(o.Waypoint, st.LatchWaypoint))
                {
                    Main.Mod.Logger.Log("Stall guard cleared: the waypoint changed.");
                    Reset(ae);
                    return;
                }
            }
            ReadOrders(ae, out string mode, out int sign);
            if (st.LatchSign == 0 && sign != 0) st.LatchSign = sign; // direction was not known yet
            bool modeChanged = !string.IsNullOrEmpty(st.LatchMode) && mode != st.LatchMode;
            bool dirChanged = st.LatchSign != 0 && sign != 0 && sign != st.LatchSign;
            if (!modeChanged && !dirChanged) return;
            Main.Mod.Logger.Log(dirChanged ? "Stall guard cleared: direction changed." : $"Stall guard cleared: mode changed to {mode}.");
            Reset(ae);
        }

        private static void Latch(AutoEngineer ae, State st, string logLine, string say, string notice)
        {
            float now = Time.time;
            st.Latched = true;
            st.LatchTime = now;
            st.Settled = false;
            st.StillSince = -1f;
            st.StandTimer = 0f;
            ReadOrders(ae, out st.LatchMode, out st.LatchSign);
            st.WpHold = false;
            if (TryOrders(ae, out Orders orders) && (orders.Mode == AutoEngineerMode.Waypoint || orders.Mode == AutoEngineerMode.Road))
            {
                // Waypoint and Road modes: the planner would keep re-planning and driving at the grade, so set the speed order
                // to 0 (as the game does for an empty tender) and tell the player how to try again.
                st.WpHold = true;
                st.LatchWaypoint = orders.Waypoint;
                st.NextHold = now + 0.25f;
                try
                {
                    var persistence = new AutoEngineerPersistence(ae.Locomotive.KeyValueObject);
                    persistence.Orders = orders.WithMaxSpeedMph(0);
                    logLine += " Speed order set to 0 mph.";
                    say += " Speed set to 0 mph - set a speed again to retry.";
                }
                catch (Exception e)
                {
                    Main.Mod.Logger.Warning("Stall guard: could not set the speed order to 0: " + e.Message);
                }
            }
            Main.Mod.Logger.Warning(logLine);
            try
            {
                BrakeFix.WriteControl(ae, "Throttle", 0f);
                ApplyHoldBrakes(ae);
                MSay?.Invoke(ae.GetComponent(SignalPlanning.PlannerType), new object[] { say });
                MNotice?.Invoke(ae.Locomotive, new object[] { "ai-stop", notice });
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Stall guard: could not announce: " + e.Message);
            }
        }

        /// <summary>
        /// Waypoint and Road modes: the train rolls back against its order. That is a grade the train cannot climb, so the
        /// stall is declared at once (the speed order goes to 0, the brakes hold, the player is told). The detector is
        /// armed once the train has moved the ordered way or the engineer has applied power to a standing train; a new
        /// direction or waypoint disarms it, so the run-out of the old direction after a reversal is not a rollback.
        /// </summary>
        private static bool CheckRollback(AutoEngineer ae, State st)
        {
            if (!TryOrders(ae, out Orders o) || (o.Mode != AutoEngineerMode.Waypoint && o.Mode != AutoEngineerMode.Road) || o.MaxSpeedMph <= 0) { st.Armed = false; return false; }
            int fwd = o.Forward ? 1 : -1;
            if (fwd != st.SeenForward || !Nullable.Equals(o.Waypoint, st.SeenWaypoint))
            {
                st.SeenForward = fwd;
                st.SeenWaypoint = o.Waypoint;
                st.Armed = false;
            }
            float along = ae.Locomotive.velocity * fwd * MpsToMph; // positive = moving the way the engineer is ordered
            if (!st.Armed)
            {
                if (along >= ArmMph || (Mathf.Abs(along) < StandMaxMph && BrakeFix.ReadThrottle(ae) >= StandMinThrottle)) st.Armed = true;
                else return false;
            }
            if (along > -RollbackMph) return false;
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            float grade = targets != null ? targets.AverageGradeUnder : 0f;
            Latch(ae, st, $"Stall guard: rolling back at {-along:0.0} mph against the order{(Mathf.Abs(grade) > 0.01f ? $" on a {-grade:0.0}% grade" : "")}.",
                "We can't climb this grade - the train is rolling back. Holding with the brakes; we need help or a lighter train.", "Can't climb grade");
            return true;
        }

        /// <summary>
        /// Called every frame while the engineer is on. Catches a train that cannot get moving at all: throttle at
        /// half or more, brakes off, still under 0.5 mph, on any grade, for the stall time. Runs outside the throttle
        /// PID because the PID does not run while the engineer is in its start-up state.
        /// </summary>
        internal static void Tick(AutoEngineer ae)
        {
            if (!Main.Settings.StallGuard || ae == null || ae.Locomotive == null) return;
            State st = States.GetOrCreateValue(ae);
            if (st.Latched)
            {
                CheckOrdersChanged(ae, st);
                if (st.Latched && st.WpHold && Time.time >= st.NextHold)
                {
                    st.NextHold = Time.time + 0.25f;
                    BrakeFix.WriteControl(ae, "Throttle", 0f);
                    ApplyHoldBrakes(ae);
                }
                return;
            }

            float speed = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            float frameDt = Mathf.Clamp(Time.deltaTime, 0f, 0.25f);
            if (CheckRollback(ae, st)) return;
            st.StandSmooth = st.StandTimer <= 0f ? speed : Mathf.Lerp(st.StandSmooth, speed, 1f - Mathf.Exp(-frameDt / 0.5f));
            bool standing = speed < StandMaxMph
                && BrakeFix.ReadThrottle(ae) >= StandMinThrottle
                && !BrakeFix.ReadBrakeRequested(ae);
            if (!standing)
            {
                st.StandTimer = 0f;
            }
            else
            {
                // A train that is still gaining speed, however slowly, is starting, not stuck: gaining more than
                // StandGainMph from its lowest point restarts the clock.
                if (st.StandTimer <= 0f || st.StandSmooth > st.StandRef + StandGainMph)
                {
                    st.StandRef = st.StandSmooth;
                    st.StandTimer = 0f;
                }
                else
                {
                    st.StandRef = Mathf.Min(st.StandRef, st.StandSmooth);
                }
                st.StandTimer += frameDt;
            }

            if (st.StandTimer < Mathf.Max(3f, Main.Settings.StallSeconds)) return;

            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            float grade = targets != null ? targets.AverageGradeUnder : 0f;
            Latch(ae, st, $"Stall guard: could not get moving ({speed:0.00} mph with the throttle at {BrakeFix.ReadThrottle(ae):0.00}, grade {-grade:0.00}% up). Holding with the brakes.",
                "We can't get this train moving. Holding with the brakes - we need help or a lighter train.", "Can't start the train");
        }


        /// <summary>
        /// Rollout-controller hook (0.6): runs on every command before the other filters. Uses the command's throttle
        /// notch as the throttle the engineer is asking for; once the stall is declared (or while latched) the command
        /// becomes throttle 0, locomotive brake full, train brake at BrakeLevel.
        /// </summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            if (!Main.Settings.StallGuard) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;
            float throttle = command.ThrottleNotch;
            if (!Check(ae, ref throttle)) return false;
            float train = BrakeLevel(ae);
            bool independent = IndependentOn(ae);
            command = new RolloutCommand(command.Kind, 0f, independent ? 1f : 0f, train, true, !independent, command.Cost, command.HoldCost);
            return true;
        }

        /// <summary>
        /// Called from the throttle PID postfix with its output. Returns true when the stall guard has taken
        /// over (the throttle is forced shut).
        /// </summary>
        internal static bool Check(AutoEngineer ae, ref float throttle)
        {
            if (!Main.Settings.StallGuard || ae.Locomotive == null) return false;
            State st = States.GetOrCreateValue(ae);
            if (st.Latched) { throttle = 0f; return true; }

            float now = Time.time;
            float speed = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            if (now - st.PrevSpeedTime > 3f) { st.Trend = 0f; st.PrevSpeed = speed; st.PrevSpeedTime = now; }
            else if (now - st.PrevSpeedTime >= 0.4f)
            {
                st.Trend = Mathf.Lerp(st.Trend, (speed - st.PrevSpeed) / (now - st.PrevSpeedTime), 0.5f);
                st.PrevSpeed = speed; st.PrevSpeedTime = now;
            }

            float dt = st.LastTime > 0f ? Mathf.Clamp(now - st.LastTime, 0f, 1.5f) : 0f;
            st.LastTime = now;

            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            float grade = targets != null ? targets.AverageGradeUnder : 0f; // percent, negative = uphill in the direction of travel
            bool stalling = throttle >= 0.95f && speed < MaxSpeedMph && grade <= -MinUphillPercent && st.Trend <= 0.05f;
            st.Timer = stalling ? st.Timer + dt : Mathf.Max(0f, st.Timer - 2f * dt);

            if (st.Timer < Mathf.Max(3f, Main.Settings.StallSeconds)) return false;

            Latch(ae, st, $"Stall guard: stalled at {speed:0.0} mph on a {-grade:0.0}% grade. Holding with the brakes.",
                "We're stalled on the grade. Holding with the brakes - we need help or a lighter train.", "Stalled on grade");
            throttle = 0f;
            return true;
        }
    }

    /// <summary>While the stall guard holds, the start-up routine (reverser, throttle 25-50%) does nothing but wait.</summary>
    [HarmonyPatch(typeof(AutoEngineer), "StartMovement")]
    internal static class AutoEngineer_StartMovement_Patch
    {
        private static bool Prefix(AutoEngineer __instance, ref IEnumerator __result)
        {
            if (!Main.Active || !StallGuard.IsLatched(__instance)) return true;
            __result = Hold(__instance);
            return false;
        }

        /// <summary>Direction-change pause (DirectionPause.cs) wraps the start-up routine; one patch class for the method keeps the self-check's patch count right.</summary>
        private static void Postfix(AutoEngineer __instance, ref IEnumerator __result)
        {
            if (!Main.Active || StallGuard.IsLatched(__instance)) return;
            __result = DirectionPause.Wrap(__instance, __result);
        }

        private static IEnumerator Hold(AutoEngineer ae)
        {
            BrakeFix.WriteControl(ae, "Throttle", 0f);
            StallGuard.ApplyHoldBrakes(ae);
            yield return DirectionPause.WaitFixed(1f);   // the scheduler does not accept WaitForSeconds
        }
    }
}
