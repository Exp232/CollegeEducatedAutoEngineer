using System;
using Model.AI;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// The auto-engineer only touches the throttle once every 0.5 s, so the lever visibly steps. This component
    /// moves the lever smoothly toward the smoothed target (ThrottleSmoother) between those ticks, about 20
    /// times a second. It stands down if anything else changes the throttle or the AI stops updating it
    /// (stopping, emergency, startup), so it never fights the game.
    /// </summary>
    public class ThrottleGlider : MonoBehaviour
    {
        private const float WriteInterval = 0.05f;   // 20 Hz
        private const float GlideTimeConstant = 0.35f;
        private const float StaleSeconds = 1.5f;     // no AI update for this long: stop
        private const float ExternalChange = 0.004f; // lever moved by someone else

        private AutoEngineer _ae;
        private float _nextWrite;
        private bool _loggedError;
        private bool _loggedPrepError;
        private bool _loggedLightsError;

        private void Awake()
        {
            _ae = GetComponent<AutoEngineer>();
        }

        private void Update()
        {
            if (_ae != null && Main.Active && _ae.isActiveAndEnabled)
            {
                try { StallGuard.Tick(_ae); } catch (Exception) { /* never let the guard break the frame */ }
                try { TrainLights.Tick(_ae); } catch (Exception e) { if (!_loggedLightsError) { _loggedLightsError = true; Main.Mod.Logger.Log("Doors and lamps error: " + e); } }
                try { HillStart.Tick(_ae); } catch (Exception) { /* never let it break the frame */ }
                try { DirectionPause.Tick(_ae); } catch (Exception) { /* never let it break the frame */ }
                try { TrainPrep.Tick(_ae); } catch (Exception e) { if (!_loggedPrepError) { _loggedPrepError = true; Main.Mod.Logger.Log("Train prep error: " + e.Message); } }
            }
            if (_ae == null || !Main.Active || StallGuard.IsLatched(_ae) || !Main.Settings.SmoothThrottle || !Main.Settings.GlideThrottle) return;
            if (Time.time < _nextWrite) return;

            try
            {
                Step();
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Main.Mod.Logger.Log("Glider error (disabled for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
                enabled = false;
            }
        }

        private void Step()
        {
            PidInfo info = BrakeFix.ThrottleInfo(_ae);
            if (info == null || !info.HasLast) return;

            if (Time.time - info.LastTime > StaleSeconds)
            {
                info.Gliding = false;
                return;
            }

            float current = BrakeFix.ReadThrottle(_ae);
            if (!info.Gliding)
            {
                info.Shown = current;
                info.Gliding = true;
            }
            else if (Mathf.Abs(current - info.Shown) > ExternalChange)
            {
                // Someone else (the AI, the player, a diesel notch mod rounding the lever) set the throttle: follow it and start over.
                if (NotchGuard.Observe(info, info.Shown, current)) return;
                info.Shown = current;
                return;
            }

            float dt = Mathf.Max(WriteInterval, Time.time - (_nextWrite - WriteInterval));
            _nextWrite = Time.time + WriteInterval;

            float target = info.Last;
            float alpha = 1f - Mathf.Exp(-dt / GlideTimeConstant);
            float aim = Mathf.Clamp01(target + Mathf.Sign(target - info.Shown) * 0.02f);
            float y = info.Shown + alpha * (aim - info.Shown);
            if ((target - y) * (target - info.Shown) <= 0f || Mathf.Abs(target - y) < 0.002f) y = target;
            y = Mathf.Clamp01(y);

            if (Mathf.Abs(y - info.Shown) < 0.0005f) return;
            BrakeFix.WriteThrottle(_ae, y);
            info.Shown = y;
        }
    }
}
