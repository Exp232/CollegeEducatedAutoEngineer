using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model.AI;
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
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FLastState = AccessTools.Field(typeof(AutoEngineer), "_lastState");
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");

        private const float MovingMph = 0.5f;
        private const float StandingMph = 0.1f;
        private const float MpsToMph = 2.23694f;
        private const float BlastSeconds = 0.4f;
        private static bool _logged;

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
            var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
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
