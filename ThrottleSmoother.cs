using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Smooths the throttle the rollout controller asks for (RolloutThrottle) before it is applied: a low-pass filter plus a
    /// rate limit turns the controller's notch steps into a gentle ramp, like a hand on the regulator.
    ///
    /// Real overspeed (past OverspeedMarginMph) bypasses the filter so the throttle closes at once.
    /// </summary>
    internal static class ThrottleSmoother
    {
        private const float MaxGapSeconds = 3f;          // longer than this between calls: start over
        private const float ArrivalOvershoot = 0.06f;
        private const float SnapBand = 0.015f;
        private const float HoldMaxSeconds = 15f;   // give up holding the hand-over value after this long
        private const float MpsPerMph = 0.44703928f;
        private const float RiseLimitPerSecond = 0.2f;   // 0 -> full in 5 s
        private const float FallLimitPerSecond = 0.4f;   // full -> 0 in 2.5 s

        internal static void Apply(ThrottleState owner, float error, ref float result, bool slam = true)
        {
            float tau = Main.Settings.ThrottleSmoothingSeconds;
            float now = Time.time;

            if (!Main.Settings.SmoothThrottle || tau <= 0.01f)
            {
                owner.HasLast = false;
                owner.Gliding = false;
                return;
            }

            float dt = now - owner.LastTime;
            owner.LastTime = now;

            // First call, a pause, or a long gap (for example the AI's start-up routine, which holds the throttle at
            // 50% before handing over): start from where the lever actually is so the handover is continuous,
            // then filter toward the controller's value like any other tick.
            if (!owner.HasLast || dt <= 0f || dt > MaxGapSeconds)
            {
                owner.HasLast = true;
                owner.Last = Mathf.Clamp01(BrakeFix.ReadThrottle(owner.Engineer));
                owner.Gliding = false;
                dt = 0.5f; // one AI tick

                // The start-up routine leaves the throttle at 50% while the speed controller's first outputs are
                // lower (25%) and then climb. Rather than dip, hold the lever at its hand-over value until the
                // controller catches up.
                owner.Floor = (owner.Last > 0.05f && error > 0f && result < owner.Last) ? owner.Last : 0f;
                owner.FloorStart = now;
            }

            // Genuine overspeed: let the throttle close immediately.
            float bypass = Mathf.Max(0.05f, Main.Settings.OverspeedMarginMph) * MpsPerMph;
            if (error < -bypass && result < owner.Last)
            {
                owner.Last = result;
                owner.Gliding = false;
                owner.Floor = 0f;
                return;
            }

            // Falling is a little quicker than rising.
            float timeConstant = result > owner.Last ? tau : tau * 0.5f;
            float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, timeConstant));
            // Aim a little past the controller's value so the filter actually arrives (an exponential never quite reaches
            // its target, which showed up as a long crawl from 97% to 100%), then snap when close.
            float aim = Mathf.Clamp01(result + Mathf.Sign(result - owner.Last) * ArrivalOvershoot);
            float y = owner.Last + alpha * (aim - owner.Last);
            if ((result - y) * (result - owner.Last) <= 0f || Mathf.Abs(result - y) < SnapBand) y = result;

            y = Mathf.Clamp(y, owner.Last - FallLimitPerSecond * dt, owner.Last + RiseLimitPerSecond * dt);
            if (owner.Floor > 0f)
            {
                // Release the hold once the controller has reached it, is no longer asking for more speed,
                // or the hold has lasted long enough.
                if (result >= owner.Floor || error <= 0f || now - owner.FloorStart > HoldMaxSeconds) owner.Floor = 0f;
                else y = Mathf.Max(y, owner.Floor);
            }
            if (y < 0.01f && result <= 0.001f) y = 0f; // don't creep toward zero forever
            y = Mathf.Clamp01(y);

            owner.Last = y;

            // While the glider is running, hand the AI the value the lever is showing right now; the glider moves
            // it on to y between the AI's half-second ticks.
            result = (Main.Settings.GlideThrottle && owner.Gliding) ? owner.Shown : y;
        }
    }
}
