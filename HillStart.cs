using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Hill-start spool-up. Starting a train, the stock engineer waits for the train brake to release with the
    /// locomotive brake applied and the throttle at only what holds the train against gravity, then releases the
    /// locomotive brake and only then sets the throttle it needs to start. A locomotive that is barely strong enough
    /// is still spooling up (diesel notch response, steam cylinder fill) while the train rolls back.
    ///
    /// While the engineer is waiting for the brakes (WaitingForBrakes), the locomotive brake is applied, the reverser is
    /// set and the train is on an upgrade, this raises the throttle to the value the stock start-up will use the moment
    /// the brake lets go (its own CalculateThrottleForTargetVelocityStarting, same arguments), so the power is already
    /// there. Nothing else changes: the stock start-up writes the throttle itself every half second once it is moving.
    /// </summary>
    internal static class HillStart
    {
        private sealed class State
        {
            public float NextCheck;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly MethodInfo MContextual = AccessTools.Method(typeof(AutoEngineer), "ContextualTargetVelocity", Type.EmptyTypes);
        private static readonly MethodInfo MStarting = AccessTools.Method(typeof(AutoEngineer), "CalculateThrottleForTargetVelocityStarting", new[] { typeof(float), typeof(float), typeof(float) });

        private const float CheckInterval = 0.25f;
        private const float StartingAggressiveness = 0.5f;   // the stock start-up's own value
        private static bool _logged;

        internal static void Tick(AutoEngineer ae)
        {
            if (!Main.Settings.HillStartSpoolUp || !ae.WaitingForBrakes) return;
            var loco = ae.Locomotive;
            if (loco == null || loco.set == null || StallGuard.IsLatched(ae)) return;

            State st = States.GetOrCreateValue(ae);
            if (Time.time < st.NextCheck) return;
            st.NextCheck = Time.time + CheckInterval;

            if (loco.VelocityMphAbs >= 1f) return;
            if (MContextual == null || MStarting == null) return;

            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
            if (targets == null || targets.AverageGradeUnder > -Mathf.Max(0f, Main.Settings.HillStartMinGradePercent)) return;   // negative = uphill in the direction of travel
            if (Mathf.Abs(BrakeFix.ReadControl(ae, "Reverser")) < 0.5f) return;                  // the reverser is set after the first wait
            if (BrakeFix.ReadControl(ae, "LocomotiveBrake") < 0.9f) return;                       // only while the locomotive brake holds the train

            float velocity = Convert.ToSingle(MContextual.Invoke(ae, null));
            if (Mathf.Abs(velocity) < 0.1f) return;
            float want = Mathf.Clamp01(Convert.ToSingle(MStarting.Invoke(ae, new object[] { velocity, 0f, StartingAggressiveness })));
            float current = BrakeFix.ReadControl(ae, "Throttle");
            if (want <= current + 0.01f) return;

            BrakeFix.WriteControl(ae, "Throttle", want);
            if (!_logged)
            {
                _logged = true;
                Main.Mod.Logger.Log("Hill start: " + loco.DisplayName + " spools to " + want.ToString("0.000") + " throttle on a " + (-targets.AverageGradeUnder).ToString("0.0") + "% grade while the brakes release (logged once).");
            }
        }
    }
}
