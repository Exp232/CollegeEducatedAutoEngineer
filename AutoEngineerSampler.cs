using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Model;
using Model.AI;
using Model.Physics;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Samples one AutoEngineer's controls and PID state into the CSV log.
    /// Runs in FixedUpdate so timestamps line up with the game's own Time.fixedTime-based control loop.
    /// </summary>
    public class AutoEngineerSampler : MonoBehaviour
    {
        private const float MpsToMph = 2.23694f;

        // Private members of AutoEngineer, cached once.
        private static readonly FieldInfo FControl = AccessTools.Field(typeof(AutoEngineer), "_control");
        private static readonly FieldInfo FRouteProfile = AccessTools.Field(typeof(AutoEngineer), "_routeProfile");
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");
        private static readonly FieldInfo FContextualTarget = AccessTools.Field(typeof(AutoEngineer), "_debugContextualTargetVelocity");
        private static readonly MethodInfo MShouldUseLocoBrake = AccessTools.Method(typeof(AutoEngineer), "ShouldUseLocomotiveBrake");
        private static readonly MethodInfo MLookahead = AccessTools.Method(typeof(AutoEngineer), "CalculateLookaheadDistance", Type.EmptyTypes);
        private static readonly MethodInfo MAvgTrainCylinder = AccessTools.Method(typeof(AutoEngineer), "AverageTrainBrakeCylinder");

        // The rollout controller (0.6.0) lives inside AutoEngineer's private speed-control loop.
        private static readonly FieldInfo FSpeedLoop = AccessTools.Field(typeof(AutoEngineer), "_speedControlLoop");
        private static readonly FieldInfo FRollout = AccessTools.Inner(typeof(AutoEngineer), "SpeedControlLoop") != null
            ? AccessTools.Field(AccessTools.Inner(typeof(AutoEngineer), "SpeedControlLoop"), "_rolloutController")
            : null;

        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private AutoEngineer _ae;
        private float _nextSampleTime;
        private bool _loggedError;

        // Fuel and water change slowly, so they are re-read about once a second and reused in between.
        private FuelReader.Reading _fuel = FuelReader.Reading.Empty();
        private float _nextFuelTime;
        private bool _loggedFuelError;

        // The controller's result is cleared while it searches, so the last valid decision is kept and its age logged.
        private string _planKind = "";
        private float _planCost = float.NaN;
        private float _planHoldCost = float.NaN;
        private float _planTime = float.NaN;
        private string _planRoots = "";

        private void Awake()
        {
            _ae = GetComponent<AutoEngineer>();
        }

        private void FixedUpdate()
        {
            if (!Main.Active || !Main.Settings.LogEnabled || _ae == null) return;
            if (!_ae.isActiveAndEnabled || !_ae.Run) return;

            // Signal changes are watched every tick, standing still or not (the main sample below is skipped at rest).
            if (Main.Settings.LogSignals)
            {
                if (_ae.Locomotive != null) SignalLog.AddTravel(_ae, Mathf.Abs(_ae.Locomotive.velocity) * Time.fixedDeltaTime);
                ObserveSignal();
            }

            if (Main.Settings.LogFuelSummary) TickFuelSummary();

            // G forces need every physics tick: acceleration is the change in velocity from one tick to the next.
            if (Main.Settings.LogGForces) TickGForces();

            if (Time.fixedTime < _nextSampleTime) return;

            _nextSampleTime = Time.fixedTime + Mathf.Max(0.05f, Main.Settings.SampleIntervalSeconds);

            try
            {
                Sample();
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Main.Mod.Logger.Log("Sampler error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }
        }

        private bool _loggedFuelSummaryError;
        private float _nextCylinderRead;
        private float _nextTonsRead;
        private float _grossTons = float.NaN;
        private float _trainCylPsi, _locoCylPsi;

        private void TickFuelSummary()
        {
            try
            {
                var loco = _ae.Locomotive;
                if (loco == null) return;
                object control = FControl?.GetValue(_ae);
                // The cylinder average walks every car, so it is refreshed a few times a second, not every step.
                if (Time.fixedTime >= _nextCylinderRead)
                {
                    _nextCylinderRead = Time.fixedTime + 0.25f;
                    float c = SafeCarCylinderAverage();
                    _trainCylPsi = float.IsNaN(c) ? 0f : c;
                    float l = SafeLocoCylinder(loco);
                    _locoCylPsi = float.IsNaN(l) ? 0f : l;
                }
                FuelSummary.Tick(_ae, Time.fixedDeltaTime, loco.velocity, ReadFloat(control, "Throttle"), _trainCylPsi, _locoCylPsi,
                    SteamValue(loco, 2), SteamValue(loco, 1), GrossTons(loco), FuelReader.DieselRate(loco));
            }
            catch (Exception e)
            {
                if (!_loggedFuelSummaryError)
                {
                    _loggedFuelSummaryError = true;
                    Main.Mod.Logger.Log("Fuel summary error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }
        }

        private readonly GForceMeter _gMeter = new GForceMeter();
        private bool _loggedGForceError;

        private void TickGForces()
        {
            try
            {
                _gMeter.Tick(_ae.Locomotive, Time.fixedTime, Main.Settings.GForceSmoothingSeconds);
            }
            catch (Exception e)
            {
                _gMeter.Reset();
                if (!_loggedGForceError)
                {
                    _loggedGForceError = true;
                    Main.Mod.Logger.Log("G-force meter error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }
        }

        /// <summary>
        /// Where the locomotive is and the G forces on it (LogGForces): posX/posY/posZ in game coordinates (m, posY =
        /// elevation), trackLoc (the front's track location, as the /location command writes it), area (the nearest
        /// ops area), and gX/gY/gZ (forward, right, up, in g; see GForceMeter).
        /// </summary>
        private void AddGForces(RowBuilder row)
        {
            if (!Main.Settings.LogGForces) return;
            bool pos = _gMeter.HasPosition;
            Vector3 p = _gMeter.Position;
            Vector3 g = _gMeter.Reading;
            row.Add("posX", pos ? p.x : float.NaN)
               .Add("posY", pos ? p.y : float.NaN)
               .Add("posZ", pos ? p.z : float.NaN)
               .Add("trackLoc", pos ? _gMeter.TrackLocation : "")
               .Add("area", pos ? _gMeter.Area : "")
               .Add("gX", g.x)
               .Add("gY", g.y)
               .Add("gZ", g.z);
        }

        private bool _loggedSignalError;

        private void ObserveSignal()
        {
            try
            {
                var targets = FTargets?.GetValue(_ae) as AutoEngineer.Targets;
                float mph = _ae.Locomotive != null ? _ae.Locomotive.velocity * MpsToMph : 0f;
                SignalLog.Observe(_ae, targets, mph);
            }
            catch (Exception e)
            {
                if (!_loggedSignalError)
                {
                    _loggedSignalError = true;
                    Main.Mod.Logger.Log("Signal log error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }
        }

        /// <summary>The signal the AI is tracking: its id, the aspect it shows, the distance to it in the target list the engineer sees (sigDistM), the same distance as the game built it before this mod's edits (sigRawM), and the metres travelled since logging began (travelM).</summary>
        private void AddSignal(RowBuilder row, AutoEngineer.Targets targets)
        {
            if (!Main.Settings.LogSignals) return;
            SignalLog.Read(targets, out string id, out string name, out string aspect);
            row.Add("sigId", id)
               .Add("sigAspect", aspect)
               .Add("sigDistM", id == "" ? float.NaN : SignalLog.DistanceToSignal(targets))
               .Add("sigRawM", id == "" ? float.NaN : SignalLog.RawDistance(_ae))
               .Add("travelM", SignalLog.Travelled(_ae));
        }

        private float LookaheadMeters()
        {
            try { return MLookahead != null ? Convert.ToSingle(MLookahead.Invoke(_ae, null)) : float.NaN; }
            catch { return float.NaN; }
        }

        /// <summary>Every target the AI currently knows about, as "speed@distance:reason" joined with ';'.</summary>
        internal static string TargetList(AutoEngineer.Targets targets)
        {
            if (targets == null || targets.AllTargets == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var t in targets.AllTargets)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(t.SpeedMph.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('@')
                  .Append(t.Distance.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(t.Reason);
            }
            return sb.ToString();
        }

        private void Sample()
        {
            var loco = _ae.Locomotive;
            if (loco == null) return;
            if (Mathf.Abs(loco.velocity) < 0.01f) return; // don't fill the log while standing still

            object control = FControl?.GetValue(_ae);
            var targets = FTargets?.GetValue(_ae) as AutoEngineer.Targets;

            float nextMph = float.NaN, nextDist = float.NaN;
            string nextReason = "";
            if (targets != null && targets.AllTargets.Count > 0)
            {
                var t = targets.AllTargets[0];
                nextMph = t.SpeedMph;
                nextDist = t.Distance;
                nextReason = t.Reason;
            }

            float throttle = ReadFloat(control, "Throttle");
            float trainBrake = ReadFloat(control, "TrainBrake");
            float locoBrake = ReadFloat(control, "LocomotiveBrake");
            bool overlap = throttle > 0.01f && (trainBrake > 0.01f || locoBrake > 0.01f);
            FuelReader.Reading fuel = ReadFuel(loco);

            var row = new RowBuilder()
                .Add("t", Time.fixedTime)
                .Add("loco", loco.DisplayName)
                .Add("cars", SafeCount(loco))
                .Add("tons", GrossTons(loco))
                .Add("mph", loco.velocity * MpsToMph)
                .Add("ctxTargetMph", ReadFloat(_ae, FContextualTarget) * MpsToMph)
                .Add("maxMph", targets != null ? targets.MaxSpeedMph : float.NaN)
                .Add("nextTargetMph", nextMph)
                .Add("nextTargetDistM", nextDist)
                .Add("nextTargetReason", nextReason)
                .Add("lookaheadM", LookaheadMeters())
                .Add("targets", TargetList(targets))
                .Add("gradeUnderPct", targets != null ? targets.AverageGradeUnder : float.NaN)
                .Add("gradeAheadPct", targets != null ? targets.AverageGradeAhead : float.NaN)
                .Add("gravityKN", _ae.SumGravityForce() / 1000f)
                .Add("throttle", throttle)
                .Add("reverser", ReadFloat(control, "Reverser"))
                .Add("trainBrake", trainBrake)
                .Add("trainBrakePsi", ReadFloat(control, "TrainBrakeSet"))
                .Add("locoBrake", locoBrake)
                .Add("brakePath", BrakePath())
                .Add("locoCylPsi", SafeLocoCylinder(loco))
                .Add("carCylAvgPsi", SafeCarCylinderAverage())
                .Add("overlap", overlap ? 1 : 0)
                .Add("fuelId", fuel.FuelId)
                .Add("fuelQty", fuel.Fuel)
                .Add("waterQty", fuel.Water)
                .Add("tenders", fuel.Tenders)
                .Add("teLbf", SteamValue(loco, 0))
                .Add("waterGps", SteamValue(loco, 1))
                .Add("coalLbs", SteamValue(loco, 2))
                .Add("dieselGps", FuelReader.DieselRate(loco));

            AddGForces(row);
            AddSignal(row, targets);
            AddGuidance(row);
            AddCommand(row);
            AddRoute(row, targets);

            LogWriter.Write(row);
        }

        /// <summary>
        /// The rollout command last handed to the locomotive: cmd* as the game issued it, out* as it left this mod's filters,
        /// cmdMod which filter changed it (tap, band), cmdAgeS seconds since it was applied.
        /// </summary>
        private void AddCommand(RowBuilder row)
        {
            CommandLog.Record c = CommandLog.Get(_ae);
            bool has = c != null && !float.IsNaN(c.Time);
            row.Add("cmdKind", has ? c.Kind : "")
               .Add("cmdThr", has ? c.Thr : float.NaN)
               .Add("cmdInd", has ? c.Ind : float.NaN)
               .Add("cmdTb", has ? c.Tb : float.NaN)
               .Add("cmdBail", has ? (c.Bail ? 1 : 0) : 0)
               .Add("outThr", has ? c.OutThr : float.NaN)
               .Add("outInd", has ? c.OutInd : float.NaN)
               .Add("outTb", has ? c.OutTb : float.NaN)
               .Add("cmdMod", has ? c.Mod : "")
               .Add("cmdAgeS", has ? Time.fixedTime - c.Time : float.NaN);
        }


        /// <summary>
        /// routeMph/routeDropM/routeCause: the slowest limit in 1.5 km of route as this mod walks it, where it first drops
        /// below the starting limit, and why. zones: the first speed zones of the route profile the game hands the
        /// rollout controller, "mph@startM" joined with ';'. Compare with targets to see whether a limit was known earlier.
        /// </summary>
        private void AddRoute(RowBuilder row, AutoEngineer.Targets targets)
        {
            float mph = float.NaN, drop = float.NaN;
            string cause = "";
            try
            {
                if (targets != null && Mathf.Abs(targets.MaxSpeedMph) > 0.1f
                    && SignalPlanning_Scan(targets.MaxSpeedMph > 0f, out float slowest, out float firstDrop, out string why))
                {
                    mph = slowest;
                    drop = firstDrop;
                    cause = why;
                }
            }
            catch (Exception) { }
            row.Add("routeMph", mph).Add("routeDropM", drop).Add("routeCause", cause).Add("zones", ZoneList());
        }

        private bool SignalPlanning_Scan(bool forward, out float slowest, out float firstDrop, out string cause)
        {
            return AutoEngineer_SetTargets_Patch.ScanRoute(_ae, forward, out slowest, out firstDrop, out cause);
        }

        private string ZoneList()
        {
            try
            {
                object profile = FRouteProfile?.GetValue(_ae);
                if (profile == null) return "";
                object limits = profile.GetType().GetProperty("Limits")?.GetValue(profile, null);
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (object zone in (System.Collections.IEnumerable)limits)
                {
                    if (n++ >= 8) break;
                    Type zt = zone.GetType();
                    float start = Convert.ToSingle(Member(zt, zone, "StartM"));
                    float speed = ZoneSpeedMps(zt, zone) * MpsToMph;
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append(speed.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append('@')
                      .Append(start.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
            catch (Exception) { return ""; }
        }

        // The zone's speed member is not known by name: take the float member called ...Speed/Mps/Limit, else the last float.
        private static float ZoneSpeedMps(Type t, object o)
        {
            float last = float.NaN;
            foreach (MemberInfo m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = null;
                if (m is PropertyInfo pi && pi.GetIndexParameters().Length == 0 && pi.PropertyType == typeof(float)) v = pi.GetValue(o, null);
                else if (m is FieldInfo fi && fi.FieldType == typeof(float)) v = fi.GetValue(o);
                if (v == null) continue;
                last = (float)v;
                string n = m.Name;
                if (n.IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Mps", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) >= 0) return last;
            }
            return last;
        }

        private static object Member(Type t, object o, string name)
        {
            PropertyInfo p = t.GetProperty(name);
            if (p != null) return p.GetValue(o, null);
            return t.GetField(name).GetValue(o);
        }

        /// <summary>Brake limit state and the rollout controller's latest decision.</summary>
        private void AddGuidance(RowBuilder row)
        {
            BrakeGuidance.Records.TryGetValue(_ae, out BrakeGuidance.Record cap);
            row.Add("maxApp", cap != null ? cap.Final : float.NaN)
               .Add("maxAppRaw", cap != null ? cap.Raw : float.NaN)
               .Add("capActive", cap != null && cap.Active ? 1 : 0);

            if (Main.Settings.LogDecisions) ReadPlan();
            row.Add("plan", _planKind)
               .Add("planCost", _planCost)
               .Add("planHoldCost", _planHoldCost)
               .Add("planAgeS", float.IsNaN(_planTime) ? float.NaN : Time.fixedTime - _planTime)
               .Add("planRoots", _planRoots);
        }

        private void ReadPlan()
        {
            try
            {
                object loop = FSpeedLoop?.GetValue(_ae);
                var rollout = loop != null ? FRollout?.GetValue(loop) as Model.AI.SpeedControl.Rollout.RolloutController : null;
                if (rollout == null) return;

                var result = rollout.Result;
                if (result.Valid)
                {
                    _planKind = result.Kind.ToString();
                    _planCost = result.Cost;
                    _planHoldCost = result.HoldCost;
                    _planTime = Time.fixedTime;
                    string roots = rollout.DescribeRootCosts();
                    if (!string.IsNullOrEmpty(roots)) _planRoots = roots;
                }
            }
            catch (Exception)
            {
                // Logging only; leave the last values in place.
            }
        }

        /// <summary>Steam locomotives only: 0 = tractive effort (lbf), 1 = water use (gal/s), 2 = coal use (lb/s). NaN otherwise.</summary>
        private static float SteamValue(BaseLocomotive loco, int which)
        {
            var steam = loco as SteamLocomotive;
            if (steam == null || steam.engine == null) return float.NaN;
            switch (which)
            {
                case 0: return steam.engine.tractiveEffort;
                case 1: return steam.engine.WaterConsumptionRate;
                default: return steam.engine.CoalConsumptionRate;
            }
        }

        private FuelReader.Reading ReadFuel(Model.BaseLocomotive loco)
        {
            if (!Main.Settings.LogFuel || Time.fixedTime < _nextFuelTime) return _fuel;
            _nextFuelTime = Time.fixedTime + 1f;

            try
            {
                _fuel = FuelReader.Read(loco);
            }
            catch (Exception e)
            {
                if (!_loggedFuelError)
                {
                    _loggedFuelError = true;
                    Main.Mod.Logger.Log("Fuel read error (further errors suppressed for this engineer):");
                    Main.Mod.Logger.LogException(e);
                }
            }

            return _fuel;
        }

        private string BrakePath()
        {
            try
            {
                return MShouldUseLocoBrake != null && (bool)MShouldUseLocoBrake.Invoke(_ae, null) ? "loco" : "train";
            }
            catch
            {
                return "?";
            }
        }

        private float SafeCarCylinderAverage()
        {
            try
            {
                return MAvgTrainCylinder != null ? (float)MAvgTrainCylinder.Invoke(_ae, null) : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }

        private static float SafeLocoCylinder(Model.BaseLocomotive loco)
        {
            try
            {
                return loco.air.BrakeCylinder.Pressure;
            }
            catch
            {
                return float.NaN;
            }
        }

        /// <summary>
        /// Gross weight in short tons of everything coupled to the locomotive (the locomotive, its tender and every car,
        /// loaded weight included). A car's weight only changes when it is loaded or unloaded, so the walk is repeated every
        /// couple of seconds and the last value is returned in between. NaN when it cannot be read.
        /// </summary>
        private float GrossTons(Model.BaseLocomotive loco)
        {
            if (Time.fixedTime < _nextTonsRead) return _grossTons;
            _nextTonsRead = Time.fixedTime + 2f;
            try
            {
                float pounds = 0f;
                foreach (var car in loco.EnumerateCoupled()) pounds += car.Weight;
                _grossTons = pounds / 2000f;
            }
            catch
            {
                _grossTons = float.NaN;
            }
            return _grossTons;
        }

        private static int SafeCount(Model.BaseLocomotive loco)
        {
            try
            {
                return loco.EnumerateCoupled().Count();
            }
            catch
            {
                return -1;
            }
        }

        private static float ReadFloat(object target, FieldInfo field)
        {
            if (target == null || field == null) return float.NaN;
            object value = field.GetValue(target);
            return value == null ? float.NaN : Convert.ToSingle(value);
        }

        /// <summary>Reads a float property or field by name, so we don't depend on the helper's namespace.</summary>
        private static float ReadFloat(object target, string name)
        {
            if (target == null) return float.NaN;
            Type type = target.GetType();

            PropertyInfo property = type.GetProperty(name, Members);
            if (property != null)
            {
                return Convert.ToSingle(property.GetValue(target, null));
            }

            FieldInfo field = type.GetField(name, Members);
            if (field != null)
            {
                return Convert.ToSingle(field.GetValue(target));
            }

            return float.NaN;
        }
    }
}
