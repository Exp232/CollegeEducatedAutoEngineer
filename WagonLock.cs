using System;
using System.Reflection;
using KeyValue.Runtime;
using HarmonyLib;
using Model;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Car wheel lock under Wheelslip Prototype. Its wagon brake law locks a car's wheels when the braking force (cylinder fraction times 0.7 of the
    /// empty weight times the game's brake-force curve, which rises as the train slows) is more than 1.02 times weight times the rail's friction
    /// coefficient, and the car then brakes at half of what the rail holds until the force falls under 0.8 of it. A train run down to a stop with a
    /// fixed application crosses that line on the way. The brake force of one car at 100 % is public (Car.CalculateBrakingForce), so the line is
    /// known per car: this filter keeps the train brake from being applied further once a car's cylinders are near it, and eases the brake off
    /// when a car is past it. Does nothing without Wheelslip Prototype, with its wagon brakes off, or with the setting off.
    /// </summary>
    internal static class WagonLock
    {
        private const float HoldAt = 0.7f;        // stop applying the train brake from this fraction of the lock line
        private const float EaseAt = 0.95f;      // ease it off above this
        private const float EaseStep = 0.04f;
        private const float LookaheadMps = 2.5f;  // the brake force curve is read at this much less than the current speed
        private const float EntryFloorMps = 0.5f;

        private static bool _tried;
        private static PropertyInfo _current;
        private static FieldInfo _blEnabled, _wagonEnabled, _wagonWeather, _wagonAdh, _wagonFactor, _dry, _mult, _entry, _threshold;
        private static MethodInfo _weather, _railGrip;
        private static bool _logged;

        internal static int LockedCars;                // cars Wheelslip has locked right now
        internal static float LastRatio = float.NaN;   // for the log: worst car, cylinders as a fraction of its lock line

        private static void Init()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "WheelslipPrototype") continue;
                    const BindingFlags st = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    _current = asm.GetType("WheelslipPrototype.SettingsRuntime")?.GetProperty("Current", st);
                    Type t = _current?.PropertyType;
                    if (t == null) return;
                    _blEnabled = t.GetField("BrakeLockEnabled", inst);
                    _wagonEnabled = t.GetField("WagonEnabled", inst);
                    _wagonWeather = t.GetField("WagonWeatherEnabled", inst);
                    _wagonAdh = t.GetField("WagonAdhesionFactor", inst);
                    _wagonFactor = t.GetField("WagonBrakeFactor", inst);
                    _dry = t.GetField("DryAdhesion", inst);
                    _mult = t.GetField("AdhesionMultiplier", inst);
                    _entry = t.GetField("WagonEntrySpeed", inst);
                    _threshold = t.GetField("WagonLockThreshold", inst);
                    _weather = asm.GetType("WheelslipPrototype.WeatherAdhesion")?.GetMethod("Coefficient", st);
                    _railGrip = asm.GetType("WheelslipPrototype.SandRuntime")?.GetMethod("RailGrip", st);
                    return;
                }
            }
            catch (Exception e)
            {
                _current = null;
                Main.Mod.Logger.Log("Wagon lock guard: Wheelslip Prototype link failed: " + e.Message);
            }
        }

        private static float F(object o, FieldInfo f, float fallback)
        {
            try { return f != null ? Convert.ToSingle(f.GetValue(o)) : fallback; }
            catch (Exception) { return fallback; }
        }

        private static bool B(object o, FieldInfo f, bool fallback)
        {
            try { return f != null ? (bool)f.GetValue(o) : fallback; }
            catch (Exception) { return fallback; }
        }

        private static bool LockedNow(Car car)
        {
            try { return car.KeyValueObject != null && car.KeyValueObject["wheelslip.wagonbrake.v1"].StringValueOrDefault((string)null) == "1"; }
            catch (Exception) { return false; }
        }

        /// <summary>The worst car's cylinder fraction of its lock line at the given speed (1 = the wagon brake law locks it), NaN when it does not apply.</summary>
        internal static float WorstRatio(BaseLocomotive loco, float speedMps)
        {
            Init();
            if (_current == null || loco == null) return float.NaN;
            try
            {
                object s = _current.GetValue(null, null);
                if (s == null || !B(s, _blEnabled, true) || !B(s, _wagonEnabled, true)) return float.NaN;

                float mu = B(s, _wagonWeather, true) && _weather != null
                    ? Convert.ToSingle(_weather.Invoke(null, new object[] { loco }))
                    : F(s, _dry, 0.22f) * F(s, _mult, 1f);
                mu *= F(s, _wagonAdh, 1f);
                float factor = F(s, _wagonFactor, 1f);
                float threshold = F(s, _threshold, 1.02f);
                float v = Mathf.Max(Mathf.Max(F(s, _entry, 0.5f), EntryFloorMps), speedMps - LookaheadMps);

                float worst = 0f;
                bool any = false;
                int locked = 0;
                foreach (Car car in loco.EnumerateCoupled())
                {
                    if (car == null || car is BaseLocomotive || car.IsLocomotive || car.IsDerailed || car.air == null) continue;
                    float m = mu;
                    if (_railGrip != null) m = Convert.ToSingle(_railGrip.Invoke(null, new object[] { car, mu }));
                    if (LockedNow(car)) { locked++; }
                    float full = car.CalculateBrakingForce(1f, v) * factor;
                    if (full <= 1f) continue;
                    float line = threshold * m * car.Weight * 4.44822f;
                    float r = car.air.brakePercent * full / line;
                    any = true;
                    if (r > worst) worst = r;
                }
                LockedCars = locked;
                if (locked > 0 && speedMps >= EntryFloorMps) worst = Mathf.Max(worst, EaseAt + 0.05f); // a car is locked now, whatever the model says
                return any ? worst : float.NaN;
            }
            catch (Exception)
            {
                return float.NaN;
            }
        }

        /// <summary>Returns true when the command was changed.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            LastRatio = float.NaN;
            if (!Main.Settings.WagonLockGuard) return false;
            if (!command.DrivesTrainBrake || BrakeTaps.PApplied == null) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;

            float speed = Mathf.Abs(ae.Locomotive.velocity);
            float ratio = WorstRatio(ae.Locomotive, speed);
            LastRatio = ratio;
            if (float.IsNaN(ratio)) return false;

            float applied = Convert.ToSingle(BrakeTaps.PApplied.GetValue(BrakeTaps.FTrainBrake.GetValue(loop), null));
            if (ratio >= EaseAt && speed > EntryFloorMps && command.TrainBrakeSet >= applied - 0.005f)
            {
                command = new RolloutCommand(command.Kind, command.ThrottleNotch, command.IndependentBrake01, Mathf.Max(0f, applied - EaseStep), true, command.BailOff, command.Cost, command.HoldCost);
                Note(ae, ratio, "eases the train brake off");
                return true;
            }
            if (ratio >= HoldAt && command.TrainBrakeSet > applied + 0.005f)
            {
                command = new RolloutCommand(RolloutActionKind.Hold, 0f, command.IndependentBrake01, applied, false, false, command.Cost, command.HoldCost);
                Note(ae, ratio, "holds the train brake where it is");
                return true;
            }
            return false;
        }

        private static void Note(AutoEngineer ae, float ratio, string what)
        {
            if (_logged) return;
            _logged = true;
            Main.Mod.Logger.Log("Wagon lock guard: " + ae.Locomotive.DisplayName + " " + what + " so a car's wheels do not lock (worst car at " + ratio.ToString("0.00") + " of its lock line; further ones are not logged)."); 
        }
    }
}
