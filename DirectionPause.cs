using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// A train that changes direction stops, blows one short blast, waits, and only then starts (with the game's own
    /// release-the-brakes wait and the forward or reverse whistle). On a heavy train the engineer could go from the
    /// stop straight into the start (the planner already has the new direction's targets when the train is under about
    /// 1 mph), so the stop and its blast were skipped and the train reversed at once.
    ///
    /// This wraps AutoEngineer.StartMovement: when the direction of the new order differs from the direction the
    /// locomotive was last moving in, the brakes are held with the throttle closed until the train has stood for
    /// DirectionChangePauseSeconds (time already stood counts), with the stop blast first when the stock engineer
    /// did not blow it. Then the game's own start-up runs unchanged.
    /// </summary>
    internal static class DirectionPause
    {
        private sealed class State
        {
            public float LastSign;      // direction of the last real movement (+1 or -1), 0 = unknown
            public float StoppedSince = -1f;
            public float HornOff = -1f;     // the stop blast of a running-state pause ends at this time
            public bool Holding;            // a running-state pause holds the train now
            public bool Blown;              // its stop blast has been blown
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FLastState = AccessTools.Field(typeof(AutoEngineer), "_lastState");

        private const float MovingMph = 0.5f;
        private const float StandingMph = 0.1f;
        private const float MpsToMph = 2.23694f;
        private const float BlastSeconds = 0.4f;
        private const float RunningPauseMph = 2f;   // the rollout path only pauses a train already below this
        private static bool _logged, _loggedRunning;

        /// <summary>Called when the engineer is enabled: the first start after that never pauses.</summary>
        internal static void Reset(AutoEngineer ae)
        {
            State st = States.GetOrCreateValue(ae);
            st.LastSign = 0f;
            st.StoppedSince = -1f;
        }

        /// <summary>Every frame while the engineer runs: remember which way the train last moved and since when it has stood still.</summary>
        internal static void Tick(AutoEngineer ae)
        {
            if (ae.Locomotive == null || ae.Locomotive.set == null) return;
            State st = States.GetOrCreateValue(ae);
            if (st.HornOff > 0f && Time.time >= st.HornOff)
            {
                st.HornOff = -1f;
                BrakeFix.WriteControl(ae, "Horn", 0f);
            }
            float v = ae.Locomotive.velocity;
            float mph = Mathf.Abs(v) * MpsToMph;
            if (mph >= MovingMph)
            {
                st.LastSign = Mathf.Sign(v);
                st.StoppedSince = -1f;
            }
            else if (mph < StandingMph && st.StoppedSince < 0f)
            {
                st.StoppedSince = Time.time;
            }
            else if (mph >= StandingMph)
            {
                st.StoppedSince = -1f;
            }
        }

        /// <summary>Sign of the speed the engineer is being ordered to run at, 0 when there is none.</summary>
        private static float OrderedSign(AutoEngineer ae)
        {
            var targets = BrakeFix.TargetsOf(ae);
            if (targets == null) return 0f;
            float speed = targets.MaxSpeedMph;
            var list = targets.AllTargets;
            if (list != null)
            {
                foreach (var t in list)
                {
                    if (t.Distance < 1f) { speed = t.SpeedMph; break; }
                }
            }
            if (Mathf.Abs(speed) < 0.01f) return 0f;
            return Mathf.Sign(speed);
        }

        private static bool LastStateWasStopped(AutoEngineer ae)
        {
            try
            {
                object last = FLastState?.GetValue(ae);
                return last != null && last.ToString() == "Stopped";
            }
            catch (Exception)
            {
                return true; // unknown: assume the stock engineer blew its own blast
            }
        }

        /// <summary>
        /// Rollout-controller hook. The wrap of StartMovement only runs when the engineer goes through its Stopped and Starting states; with the
        /// 0.6 controller a train ordered the other way while still rolling stays in Running, and the controller flips the reverser and opens the
        /// throttle as the speed passes through zero (in the 1.5.6 log it went from 0.45 mph forward straight to throttle in reverse, with the car
        /// brakes still at 15 psi). So here too: below 2 mph, while the order runs against the way the train last moved, the throttle is closed
        /// and the locomotive brake held until the train has stood for the pause, with the stop blast once it stands. Returns true while holding.
        /// </summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            float seconds = Main.Settings.DirectionChangePauseSeconds;
            if (seconds < 0.5f) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;
            State st = States.GetOrCreateValue(ae);
            float mph = Mathf.Abs(ae.Locomotive.velocity) * MpsToMph;
            float want = OrderedSign(ae);
            if (want == 0f || st.LastSign == 0f || want == st.LastSign || mph >= RunningPauseMph)
            {
                st.Holding = false;
                return false;
            }

            float now = Time.time;
            if (st.StoppedSince >= 0f && now - st.StoppedSince >= seconds)
            {
                // Stood long enough: this change of direction is done, the controller starts the train.
                st.LastSign = want;
                st.Holding = false;
                return false;
            }

            if (!st.Holding)
            {
                st.Holding = true;
                st.Blown = false;
                if (!_loggedRunning)
                {
                    _loggedRunning = true;
                    Main.Mod.Logger.Log("Direction change: " + ae.Locomotive.DisplayName + " stops and stands " + seconds.ToString("0.0") + " s before moving the other way (rollout path, logged once).");
                }
            }
            if (st.StoppedSince >= 0f && !st.Blown && Main.Settings.DirectionChangeHorn)
            {
                st.Blown = true;
                BrakeFix.WriteControl(ae, "Horn", 1f);
                st.HornOff = now + BlastSeconds;
            }

            float applied = command.TrainBrakeSet;
            try
            {
                if (BrakeTaps.PApplied != null && BrakeTaps.FTrainBrake != null)
                    applied = Convert.ToSingle(BrakeTaps.PApplied.GetValue(BrakeTaps.FTrainBrake.GetValue(loop), null));
            }
            catch (Exception) { }
            command = new RolloutCommand(RolloutActionKind.Hold, 0f, 1f, applied, false, false, command.Cost, command.HoldCost);
            RolloutThrottle.CloseFor(ae);
            return true;
        }

        /// <summary>Postfix body for AutoEngineer.StartMovement: returns the enumerator to run instead.</summary>
        internal static IEnumerator Wrap(AutoEngineer ae, IEnumerator original)
        {
            try
            {
                float seconds = Main.Settings.DirectionChangePauseSeconds;
                if (!Main.Active || seconds < 0.5f || ae == null || ae.Locomotive == null) return original;
                State st = States.GetOrCreateValue(ae);
                float want = OrderedSign(ae);
                if (want == 0f || st.LastSign == 0f || want == st.LastSign) return original;

                float stood = st.StoppedSince >= 0f ? Time.time - st.StoppedSince : 0f;
                float remaining = seconds - stood;
                st.LastSign = want; // one pause per change of direction
                bool blast = Main.Settings.DirectionChangeHorn && !LastStateWasStopped(ae);
                if (remaining <= 0.05f && !blast) return original;
                if (!_logged)
                {
                    _logged = true;
                    Main.Mod.Logger.Log("Direction change: " + ae.Locomotive.DisplayName + " holds " + Mathf.Max(0f, remaining).ToString("0.0") + " s before moving the other way" + (blast ? " (with a stop blast)" : "") + " (logged once).");
                }
                return Pause(ae, original, Mathf.Max(0f, remaining), blast);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
                return original;
            }
        }

        /// <summary>
        /// The engineer runs inside AutoEngineerScheduler, which only accepts WaitForFixedUpdate, null, a nested enumerator or a
        /// BudgetRequest (WaitForSeconds throws and the engineer is dropped). This is a wait in fixed-time seconds made of those.
        /// </summary>
        internal static IEnumerator WaitFixed(float seconds)
        {
            float end = Time.fixedTime + seconds;
            while (Time.fixedTime < end) yield return new WaitForFixedUpdate();
        }

        private static IEnumerator Pause(AutoEngineer ae, IEnumerator original, float seconds, bool blast)
        {
            float end = Time.fixedTime + seconds;
            Hold(ae);
            if (blast)
            {
                BrakeFix.WriteControl(ae, "Horn", 1f);
                yield return WaitFixed(BlastSeconds);
                BrakeFix.WriteControl(ae, "Horn", 0f);
            }
            while (Time.fixedTime < end)
            {
                Hold(ae);
                yield return WaitFixed(0.25f);
            }
            while (original.MoveNext()) yield return original.Current;
        }

        private static void Hold(AutoEngineer ae)
        {
            BrakeFix.WriteControl(ae, "Throttle", 0f);
            BrakeFix.WriteControl(ae, "LocomotiveBrake", 1f);
        }
    }
}
