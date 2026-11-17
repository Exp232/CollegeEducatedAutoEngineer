using System;
using System.Reflection;
using KeyValue.Runtime;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Physics;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Car wheel lock under Wheelslip Prototype. Its wagon brake law locks a car's wheels when the game's braking force for the car (times
    /// Wheelslip's wagon brake factor) is more than its lock threshold (1.02) times the car's weight times the rail's friction coefficient, and the
    /// car then brakes at half of what the rail holds until the force falls under 0.8 of it. The brake-force curve rises as a train slows, so a
    /// fixed application run down to a stop crosses that line on the way. The force of one car at a full application is the game's own formula
    /// (read here directly, not through the patched Car.CalculateBrakingForce), so the line is known per car: this filter caps the train brake
    /// handle where the worst car's cylinders would stay under the line, and eases the brake off when a car is past it. It also keeps Wheelslip's
    /// lock law to the physics (see EnsureIsolation). Does nothing without Wheelslip Prototype, with its wagon brakes off, or with the setting off.
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

        // The game's own brake force, read without calling Car.CalculateBrakingForce: Wheelslip patches that method and its postfix updates the
        // car's lock state from the demand it is given, so asking it about a full application locked every car (1.5.1 to 1.5.5).
        private static bool _nativeTried;
        private static FieldInfo _cfg, _nominal, _curveField;
        private static PropertyInfo _curveProp;

        private static bool InitNative()
        {
            if (!_nativeTried)
            {
                _nativeTried = true;
                try
                {
                    _cfg = AccessTools.Field(typeof(Car), "Config");
                    _nominal = AccessTools.Field(typeof(Car), "nominalBrakingForce");
                    if (_cfg != null)
                    {
                        _curveField = AccessTools.Field(_cfg.FieldType, "brakeForceCurve");
                        if (_curveField == null) _curveProp = AccessTools.Property(_cfg.FieldType, "brakeForceCurve");
                    }
                }
                catch (Exception e)
                {
                    Main.Mod.Logger.Log("Wagon lock guard: brake force members not found: " + e.Message);
                }
            }
            return _cfg != null && _nominal != null && (_curveField != null || _curveProp != null);
        }

        /// <summary>Car.CalculateBrakingForce(1, speed) as the game computes it, before Wheelslip's postfix. NaN when it cannot be read.</summary>
        private static float NativeFullForce(Car car, float absVelocity)
        {
            if (!InitNative()) return float.NaN;
            object cfg = _cfg.GetValue(car);
            if (cfg == null) return float.NaN;
            var curve = (_curveField != null ? _curveField.GetValue(cfg) : _curveProp.GetValue(cfg, null)) as AnimationCurve;
            if (curve == null) return float.NaN;
            float nominal = Convert.ToSingle(_nominal.GetValue(car));
            return Mathf.Lerp(0.8f, 1f, car.Condition) * nominal * Car.BrakeForceMultiplier * curve.Evaluate(absVelocity * 2.23694f) * 4.44822f;
        }

        // Wheelslip runs its lock law in a postfix on Car.CalculateBrakingForce, so every caller moves a car's lock state, not only the physics:
        // the auto engineer asks for each car at a full application (CalculateTotalAvailableBrakingAtSpeed, CalculateBrakingAtSpeed from 0.6.3) and the brake pressure simulator for
        // pressures that are only being tried, and a full application is past the lock line, so planning alone locked the cars. With the guard
        // on, the law is only let run inside IntegrationSet.CalculateRetardingForce (the physics); other callers get the game's own force.
        // Wheelslip loads after this mod, so the patch is put in on the first auto engineer command.
        private static int _inPhysics;
        private static bool _isolationTried;

        internal static void EnsureIsolation()
        {
            if (_isolationTried) return;
            _isolationTried = true;
            try
            {
                Assembly ws = null;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    if (asm.GetName().Name == "WheelslipPrototype") { ws = asm; break; }
                if (ws == null || Main.Harmony == null) return;
                const BindingFlags st = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo physics = AccessTools.Method(typeof(IntegrationSet), "CalculateRetardingForce");
                MethodInfo wagons = ws.GetType("WheelslipPrototype.WagonBrakes")?.GetMethod("Calculate", st);
                MethodInfo locos = ws.GetType("WheelslipPrototype.BrakeLock")?.GetMethod("Calculate", st);
                if (physics == null || wagons == null)
                {
                    Main.Mod.Logger.Log("Wagon lock guard: Wheelslip's brake law or the game's retarding force was not found; planning calls still reach the lock law.");
                    return;
                }
                Main.Harmony.Patch(physics, prefix: new HarmonyMethod(typeof(WagonLock), nameof(PhysicsEnter)), finalizer: new HarmonyMethod(typeof(WagonLock), nameof(PhysicsExit)));
                Main.Harmony.Patch(wagons, prefix: new HarmonyMethod(typeof(WagonLock), nameof(OnlyInPhysics)));
                if (locos != null) Main.Harmony.Patch(locos, prefix: new HarmonyMethod(typeof(WagonLock), nameof(OnlyInPhysics)));
                Main.Mod.Logger.Log("Wagon lock guard: Wheelslip's brake lock law now runs only for the physics, not for the auto engineer's planning.");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Log("Wagon lock guard: could not separate planning from Wheelslip's lock law: " + e.Message);
            }
        }

        private static void PhysicsEnter() { _inPhysics++; }

        private static void PhysicsExit() { if (_inPhysics > 0) _inPhysics--; }

        // Harmony prefix: false skips Wheelslip's law, so the caller gets the game's force and the lock state is left as it is.
        private static bool OnlyInPhysics()
        {
            return _inPhysics > 0 || Main.Settings == null || !Main.Settings.WagonLockGuard || !Main.Active;
        }

        /// <summary>True when the game's brake force members can be read (for the self-check).</summary>
        internal static bool NativeAvailable() { return InitNative(); }

        internal static int LockedCars;   // for the log: cars Wheelslip has locked right now (with their brakes on)

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
            return Measure(loco, speedMps, out float ratio, out _, out _) ? ratio : float.NaN;
        }

        /// <summary>
        /// ratio: the worst car's brake force over its lock line with the cylinders as they are; ratioAtFull: the same with the brake fully applied
        /// (so the cylinder fraction that reaches the line is 1 / ratioAtFull); cylinderFraction: the cylinders of the wagons, averaged (0 to 1).
        /// </summary>
        private static bool Measure(BaseLocomotive loco, float speedMps, out float ratio, out float ratioAtFull, out float cylinderFraction)
        {
            ratio = 0f;
            ratioAtFull = 0f;
            cylinderFraction = 0f;
            Init();
            if (_current == null || loco == null) return false;
            try
            {
                object s = _current.GetValue(null, null);
                if (s == null || !B(s, _blEnabled, true) || !B(s, _wagonEnabled, true)) return false;

                float mu = B(s, _wagonWeather, true) && _weather != null
                    ? Convert.ToSingle(_weather.Invoke(null, new object[] { loco }))
                    : F(s, _dry, 0.22f) * F(s, _mult, 1f);
                mu *= F(s, _wagonAdh, 1f);
                float factor = F(s, _wagonFactor, 1f);
                float threshold = F(s, _threshold, 1.02f);
                float v = Mathf.Max(Mathf.Max(F(s, _entry, 0.5f), EntryFloorMps), speedMps - LookaheadMps);

                int n = 0, locked = 0;
                float pct = 0f;
                foreach (Car car in loco.EnumerateCoupled())
                {
                    if (car == null || car is BaseLocomotive || car.IsLocomotive || car.IsDerailed || car.air == null) continue;
                    float m = mu;
                    if (_railGrip != null) m = Convert.ToSingle(_railGrip.Invoke(null, new object[] { car, mu }));
                    float full = NativeFullForce(car, v) * factor;
                    if (float.IsNaN(full)) return false;
                    if (full <= 1f) continue;
                    float line = threshold * m * car.Weight * 4.44822f;
                    // The cap must hold down to the slowest speed the law locks at, where the brake-force curve is highest.
                    float atFull = Mathf.Max(full, NativeFullForce(car, EntryFloorMps) * factor) / line;
                    float bp = car.air.brakePercent;
                    n++;
                    pct += bp;
                    if (bp > 0.15f && LockedNow(car)) locked++;
                    if (atFull > ratioAtFull) ratioAtFull = atFull;
                    if (bp * full / line > ratio) ratio = bp * full / line;
                }
                LockedCars = locked;
                if (n == 0) return false;
                cylinderFraction = pct / n;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Brake percent (cylinders) each unit of train brake handle ends at: the game gave 46 psi of 72 (0.645) for a handle of 0.15, about 4.3.
        private static float _pctPerHandle = 4.3f;

        /// <summary>Returns true when the command was changed.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            if (!Main.Settings.WagonLockGuard) return false;
            if (!command.DrivesTrainBrake || BrakeTaps.PApplied == null) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return false;

            float speed = Mathf.Abs(ae.Locomotive.velocity);
            if (!Measure(ae.Locomotive, speed, out float ratio, out float atFull, out float pct)) return false;

            float applied = Convert.ToSingle(BrakeTaps.PApplied.GetValue(BrakeTaps.FTrainBrake.GetValue(loop), null));
            // Learn the cylinders per handle once the cylinders have settled near the applied handle.
            if (applied > 0.05f && pct > 0.05f)
            {
                float k = pct / applied;
                if (k > 2f && k < 8f) _pctPerHandle = Mathf.Lerp(_pctPerHandle, k, 0.02f);
            }

            if (ratio >= EaseAt && speed > EntryFloorMps && command.TrainBrakeSet >= applied - 0.005f)
            {
                command = new RolloutCommand(command.Kind, command.ThrottleNotch, command.IndependentBrake01, Mathf.Max(0f, applied - EaseStep), true, command.BailOff, command.Cost, command.HoldCost);
                Note(ae, "eases the train brake off (the worst car is at " + ratio.ToString("0.00") + " of its lock line)");
                return true;
            }

            if (command.TrainBrakeSet > applied + 0.005f && atFull > 0.01f)
            {
                // A service application takes the cylinders most of the way in one step, so the handle itself is capped at the level whose
                // cylinders stay under the lock line (HoldAt of it), not just stopped once the cylinders have got there.
                float maxHandle = HoldAt / atFull / _pctPerHandle;
                float newSet = Mathf.Max(applied, Mathf.Min(command.TrainBrakeSet, maxHandle));
                if (newSet < command.TrainBrakeSet - 0.005f)
                {
                    command = newSet > applied + 0.005f
                        ? new RolloutCommand(command.Kind, command.ThrottleNotch, command.IndependentBrake01, newSet, true, command.BailOff, command.Cost, command.HoldCost)
                        : new RolloutCommand(RolloutActionKind.Hold, 0f, command.IndependentBrake01, applied, false, false, command.Cost, command.HoldCost);
                    Note(ae, "limits the train brake to a handle of " + maxHandle.ToString("0.00") + " (a full application would take the worst car to " + atFull.ToString("0.0") + " times its lock line)");
                    return true;
                }
            }
            return false;
        }

        private static void Note(AutoEngineer ae, string what)
        {
            if (_logged) return;
            _logged = true;
            Main.Mod.Logger.Log("Wagon lock guard: " + ae.Locomotive.DisplayName + " " + what + ", so the cars' wheels do not lock (logged once).");
        }
    }
}
