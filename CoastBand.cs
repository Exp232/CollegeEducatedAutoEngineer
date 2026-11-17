using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Model;
using Model.AI;
using Model.AI.SpeedControl;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Coast band (CoastBandMph, off at 0). Stops the rollout controller from powering into a brake application:
    ///  - Power off: when coasting would not lose speed (gravity helping at least as much as the train's Davis
    ///    resistance, the game's own formula) and the train is within the band below its target, the throttle is
    ///    closed; it also stays closed whenever gravity alone would still pick the train up by about 0.1 mph a second,
    ///    however far below the target, since the downhill will bring the speed up without steam.
    ///  - Brake on: a brake application is held off until the train is more than the band over its target, unless a
    ///    lower limit or a stop ahead needs it (the same look-ahead windows as brake tap suppression).
    /// Road and Waypoint modes, above 5 mph. The controller runs as usual; only the command it sends is changed.
    /// </summary>
    internal static class CoastBand
    {
        private const float MpsToMph = 2.23694f;
        private const float LbfToN = 4.44822f;
        private const float MinSpeedMph = 5f;
        private const float LbPerTonToMphPerSec = 0.0161f / 1.46667f;   // lb/ton -> ft/s2 -> mph/s
        private const float AssistLbPerTon = 10f;     // net gravity help over resistance: about 0.05 m/s2, 0.1 mph a second
        private const float TonsRefreshSeconds = 2f;

        private sealed class Consist
        {
            public float NextRead;
            public float Tons;
            public int Cars;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, Consist> Consists = new ConditionalWeakTable<AutoEngineer, Consist>();
        private static bool _loggedPower, _loggedBrake;

        /// <summary>Returns true when the command was changed.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            float band = Main.Settings.CoastBandMph;
            if (band <= 0f) return false;

            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;
            var targets = BrakeFix.TargetsOf(ae);
            if (targets == null || targets.AllTargets == null) return false;
            string mode = targets.Mode.ToString();
            if (mode != "Road" && mode != "Waypoint") return false;

            float velocity = ae.Locomotive.velocity;
            float speedMph = Mathf.Abs(velocity) * MpsToMph;
            float targetMph = Mathf.Abs(Convert.ToSingle(BrakeTaps.FContextualField.GetValue(ae))) * MpsToMph;
            if (speedMph < MinSpeedMph || targetMph < MinSpeedMph) return false;

            bool changed = false;

            // ---- Power off ----
            if (command.ThrottleNotch > 0.001f)
            {
                Consist c = ConsistOf(ae);
                if (c.Tons > 0f)
                {
                    // SumGravityForce is signed toward the locomotive's F end; times the direction of travel it is the help.
                    float helpN = ae.SumGravityForce() * Mathf.Sign(velocity);
                    float resistN = TrainPlantModel.DavisResistanceLbs(c.Tons, c.Cars, speedMph) * LbfToN;
                    float netLbPerTon = (helpN - resistN) / LbfToN / c.Tons;
                    bool holdsSpeed = netLbPerTon >= 0f;
                    bool nearTarget = speedMph >= targetMph - band;
                    // Gravity alone only counts while it reaches the target soon; a long crawl up to speed is left to the controller.
                    float assistSeconds = Main.Settings.CoastBandAssistSeconds;
                    float accelMphPerSec = netLbPerTon * LbPerTonToMphPerSec;
                    bool gravityEnough = netLbPerTon >= AssistLbPerTon && (assistSeconds <= 0f || targetMph - speedMph <= accelMphPerSec * assistSeconds);
                    if (holdsSpeed && (nearTarget || gravityEnough))
                    {
                        command = new RolloutCommand(command.Kind, 0f, command.IndependentBrake01, command.TrainBrakeSet, command.DrivesTrainBrake, command.BailOff, command.Cost, command.HoldCost);
                        FuelSummary.CountPowerCut(ae);
                        changed = true;
                        if (!_loggedPower)
                        {
                            _loggedPower = true;
                            Main.Mod.Logger.Log($"Coast band: throttle closed for {ae.Locomotive.DisplayName} at {speedMph:0.0} mph, target {targetMph:0.0} mph, gravity net of resistance {netLbPerTon:+0.0;-0.0} lb/ton (further ones are counted in fuel_summary).");
                        }
                    }
                }
            }

            // ---- Brake on only above target + band ----
            if (command.DrivesTrainBrake && !command.BailOff && command.IndependentBrake01 <= 0.01f)
            {
                float applied = BrakeTaps.AppliedSet(loop);
                if (!float.IsNaN(applied) && command.TrainBrakeSet > applied + 0.005f && speedMph <= targetMph + band && !BrakeTaps.SlowingNeededAhead(targets, speedMph / MpsToMph))
                {
                    command = new RolloutCommand(RolloutActionKind.Hold, command.ThrottleNotch, command.IndependentBrake01, applied, false, false, command.Cost, command.HoldCost);
                    FuelSummary.CountBrakeHeld(ae);
                    changed = true;
                    if (!_loggedBrake)
                    {
                        _loggedBrake = true;
                        Main.Mod.Logger.Log($"Coast band: brake application held for {ae.Locomotive.DisplayName} at {speedMph:0.0} mph, target {targetMph:0.0} mph (further ones are counted in fuel_summary).");
                    }
                }
            }
            return changed;
        }

        private static Consist ConsistOf(AutoEngineer ae)
        {
            Consist c = Consists.GetOrCreateValue(ae);
            if (Time.time < c.NextRead) return c;
            c.NextRead = Time.time + TonsRefreshSeconds;
            float lbs = 0f;
            int cars = 0;
            foreach (Car car in ae.Locomotive.EnumerateCoupled())
            {
                lbs += car.Weight;
                cars++;
            }
            c.Tons = lbs / 2000f;
            c.Cars = cars;
            return c;
        }
    }
}
