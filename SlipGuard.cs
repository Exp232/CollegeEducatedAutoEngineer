using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using Model.Physics;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Slip guard. A driver wheelset slips when its tractive effort is more than its weight on the driving wheels times the friction
    /// coefficient (DriverWheelState.UpdateWheelslip); once it slips the game gives it only a tenth of that until the effort falls under
    /// it, so a lever left where it was keeps spinning. The limit is known in advance, so the guard works it out instead of waiting for a slip:
    ///
    ///   limit = sum over the wheelsets of  AdhesiveWeight * mu
    ///   mu    = the game's curve 7.5/(km/h + 44) + 0.161, then, with Wheelslip Prototype, the lower of that and its weather adhesion,
    ///           plus its sand bonus while sanding, times its rail wear and burn-mark grip.
    ///
    /// The tractive effort follows the throttle (effort = k * lever, with k measured as the train runs and falling with speed), so the lever may
    /// not go above  margin * limit / k.  That ceiling rises by itself as the train speeds up, needs no slip to learn it, and is lower on a wet
    /// rail. If the wheels do slip anyway, the throttle comes down to a tenth of what the wheels can carry, which is where the game lets them grip
    /// again, and then climbs back to the ceiling. The sanding decision uses the same numbers (see SlipSand).
    /// </summary>
    internal static class SlipGuard
    {
        private sealed class State
        {
            public float Next;
            public float LastTick;
            public float Ceiling = 1f;       // the throttle may not be above this (1 = no limit)
            public float LastSlip = -100f;
            public bool WasSlipping;
            public float SlipSince = -1f;    // when the wheels started slipping (-1 = not slipping)
            public int Slips;
            public float K;                  // tractive effort per unit of throttle (lbf) at the current speed, measured while the wheels grip
            public float[] KBand = new float[4];   // the same per speed band: effort per throttle falls steeply with speed
            public float Demand;             // what the controller last asked for before the ceiling was applied
            public float DemandTime = -100f;
            public bool SandNeed;            // the train asks for more effort than dry rail carries
            public float SandLowSince = -1f;
            public float LimitNoSand;        // lbf, for the log
            public float Effort;             // lbf, for the log
            public bool LoggedLimit;
            public float Required = float.NaN;   // lbf the train needs to move (grade, rolling and starting resistance, a little acceleration)
            public bool Wet;                 // the weather has cut the rail's grip below the dry figure
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FStates = AccessTools.Field(typeof(BaseLocomotive), "DriverWheelStates");
        private static readonly FieldInfo FTargets = AccessTools.Field(typeof(AutoEngineer), "_targets");

        private const float TickInterval = 0.05f;
        private const float ClearHoldSeconds = 0.8f;    // after the slip stops, stay closed this long
        private const float ClimbPerSecond = 0.15f;     // then open this fast up to the ceiling
        private const float SlipRecoverFraction = 0.09f; // the game lets a slipping wheelset grip again under a tenth of its limit
        private const float SandTailSeconds = 15f;      // keep sanding this long after the last slip (at a crawl)
        private const float SandProactiveMph = 8f;      // below this the sand goes down as soon as the request passes the dry limit
        private const float SandRollingFactor = 1.15f;  // above it, only when the request is this far past the dry limit (or after a slip)
        private static float SandTail(float mph) { return Mathf.Lerp(SandTailSeconds, 6f, Mathf.Clamp01((mph - SandProactiveMph) / 8f)); }
        private const float SandOffSeconds = 4f;        // no reason to sand for this long: sand off
        private const int LoggedSlips = 12;

        /// <summary>0 at 3 mph and under, 1 from 15 mph: how much of a high-speed treatment a slip gets (a slip at speed is eased, not slammed).</summary>
        private static float SpeedFactor(float mph) { return Mathf.Clamp01((mph - 3f) / 12f); }

        /// <summary>Speed bands for the effort per throttle: a figure measured at 35 mph (about a third of the standing one) says nothing at a standstill.</summary>
        private static int Band(float mph) { return mph < 2.5f ? 0 : (mph < 8f ? 1 : (mph < 16f ? 2 : 3)); }

        /// <summary>The effort per throttle for this speed: its own band, else the nearest slower one (larger, so the ceiling errs low), else 0 (unknown).</summary>
        private static float KFor(State st, float mph)
        {
            for (int b = Band(mph); b >= 0; b--)
            {
                if (st.KBand[b] > 0f) return st.KBand[b];
            }
            return 0f;
        }

        internal static bool Available { get { return FStates != null; } }

        /// <summary>For the log: whether a driver wheelset slips now, the throttle ceiling (1 = none), the adhesion limit without sand and the tractive effort (lbf).</summary>
        internal static void Status(AutoEngineer ae, out bool slipping, out float ceiling, out float limitLbf, out float effortLbf)
        {
            slipping = false;
            ceiling = 1f;
            limitLbf = float.NaN;
            effortLbf = float.NaN;
            if (ae == null || !States.TryGetValue(ae, out State st)) return;
            slipping = st.WasSlipping;
            ceiling = st.Ceiling;
            limitLbf = st.LimitNoSand;
            effortLbf = st.Effort;
        }

        /// <summary>For the log: the effort the train needs to move (lbf, NaN when unknown) and whether the rail is wet.</summary>
        internal static void SandInfo(AutoEngineer ae, out float requiredLbf, out bool wet)
        {
            requiredLbf = float.NaN;
            wet = false;
            if (ae == null || !States.TryGetValue(ae, out State st)) return;
            requiredLbf = st.Required;
            wet = st.Wet;
        }

        /// <summary>
        /// A slipping wheelset's effort is held at exactly a tenth of its adhesive weight. The wheels' own slip flag can read false while that is
        /// so (in the 1.5.7 log a diesel stood at 19600 lbf, a tenth of its 196000 lb on the drivers, for 2 to 3 s at every start with the flag
        /// mostly off), so an effort at that figure counts as a slip.
        /// </summary>
        private static bool AtSlipCap(BaseLocomotive loco, float effort, float lever)
        {
            if (lever < 0.05f || effort < 100f) return false;
            DriverWheelState[] ws = Wheels(loco);
            if (ws == null) return false;
            float aw = 0f;
            for (int i = 0; i < ws.Length; i++) aw += ws[i].AdhesiveWeight;
            if (aw < 1f) return false;
            float cap = 0.1f * aw;
            return Mathf.Abs(effort - cap) <= 0.003f * cap;
        }

        /// <summary>
        /// The effort the train needs to keep moving, or to get moving, in lbf: the uphill grade under it, the game's rolling resistance (Davis) at
        /// this speed, this mod's starting resistance while standing, and enough for a slow gain of speed (0.05 mph a second). NaN when unknown.
        /// </summary>
        private static float RequiredEffort(AutoEngineer ae, BaseLocomotive loco, float mph)
        {
            try
            {
                float tons = 0f, davis = 0f;
                foreach (Car car in loco.EnumerateCoupled())
                {
                    if (car == null) continue;
                    float t = car.Weight / 2000f;
                    if (t <= 0f) continue;
                    tons += t;
                    davis += (1.3f + 29f / t + 0.045f * mph + 0.063f * mph * mph / t) * t;
                }
                if (tons <= 0f) return float.NaN;
                float grade = 0f;
                var targets = FTargets?.GetValue(ae) as AutoEngineer.Targets;
                if (targets != null && !float.IsNaN(targets.AverageGradeUnder)) grade = Mathf.Max(0f, -targets.AverageGradeUnder);
                float gradeLbf = 20f * grade * tons;   // 20 lb per ton per percent
                float start = 0f;
                if (Main.Settings.StartingResistance && mph < 5f)
                    start = Main.Settings.StartingResistanceLbPerTon * tons * Mathf.Exp(-mph / Mathf.Max(0.5f, Main.Settings.StartingResistanceFadeMph));
                float accel = tons * 2000f * 0.05f * 0.44704f / 9.80665f;   // weight in lb times a / g
                return gradeLbf + davis + start + accel;
            }
            catch (Exception)
            {
                return float.NaN;
            }
        }

        /// <summary>The wheels slipped within the last <paramref name="seconds"/> seconds.</summary>
        internal static bool SlippedRecently(AutoEngineer ae, float seconds)
        {
            return ae != null && States.TryGetValue(ae, out State st) && Time.time - st.LastSlip < seconds;
        }

        /// <summary>The throttle ceiling for this engineer, 1 when there is none.</summary>
        internal static float Ceiling(AutoEngineer ae)
        {
            if (ae == null || !Main.Settings.SlipGuard || !States.TryGetValue(ae, out State st)) return 1f;
            return st.Ceiling;
        }

        internal static DriverWheelState[] Wheels(BaseLocomotive loco)
        {
            return FStates?.GetValue(loco) as DriverWheelState[];
        }

        internal static bool IsSlipping(BaseLocomotive loco)
        {
            var wheels = Wheels(loco);
            if (wheels == null) return false;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i].IsSlipping) return true;
            }
            return false;
        }

        /// <summary>Every frame while the engineer runs.</summary>
        internal static void Tick(AutoEngineer ae)
        {
            if (!Main.Settings.SlipGuard && !Main.Settings.SlipSand)
            {
                Release(ae);
                return;
            }
            var loco = ae.Locomotive as BaseLocomotive;
            if (loco == null || loco.set == null || !Available) return;

            State st = States.GetOrCreateValue(ae);
            float now = Time.time;
            if (now < st.Next) return;
            float dt = Mathf.Clamp(now - st.LastTick, TickInterval, 0.5f);
            st.LastTick = now;
            st.Next = now + TickInterval;

            float mph = loco.VelocityMphAbs;
            if (StallGuard.IsLatched(ae))
            {
                // Held with the brakes: no sanding, no limit.
                SlipSand.Release(ae, loco);
                st.Ceiling = 1f;
                st.SandNeed = false;
                st.SlipSince = -1f;
                st.WasSlipping = false;
                return;
            }

            // A short slip at speed is let go: the longer the train is moving, the longer the slip must last before the throttle is touched.
            float lever = BrakeFix.ReadThrottle(ae);
            float effort = Mathf.Abs(loco.TractiveEffort);
            st.Effort = effort;

            bool raw = IsSlipping(loco) || AtSlipCap(loco, effort, lever);
            if (!raw) st.SlipSince = -1f;
            else if (st.SlipSince < 0f) st.SlipSince = now;
            bool slipping = raw && now - st.SlipSince >= Mathf.Lerp(0.1f, 0.8f, SpeedFactor(mph));

            // Effort per unit of throttle, measured while the wheels grip (a slipping wheelset's effort is cut, so it says nothing), and not
            // in the second after a slip. A diesel's effort does not follow the notch evenly (a standing EMR 1200 gave 63k to 89k lbf per unit),
            // so a band takes a higher figure at once and lets it down slowly: the ceiling errs low.
            if (!raw && now - st.LastSlip > 1f && lever > 0.05f && effort > 100f)
            {
                float k = effort / lever;
                int b = Band(mph);
                if (st.KBand[b] <= 0f || k > st.KBand[b]) st.KBand[b] = k;
                else st.KBand[b] = Mathf.Lerp(st.KBand[b], k, 1f - Mathf.Exp(-dt / 10f));
            }
            st.K = KFor(st, mph);

            float limitNo, limitSand;
            bool haveLimit = SlipSand.Limits(loco, out limitNo, out limitSand);
            st.LimitNoSand = haveLimit ? limitNo : float.NaN;

            // What the train is asking for, in lbf: the controller's own request when it is fresh, else the lever.
            float demand = now - st.DemandTime < 1.5f ? Mathf.Max(lever, st.Demand) : lever;
            float asked = st.K > 0f ? st.K * demand : effort;

            // Sand ahead of a slip only when it is needed: when the train itself needs more effort than the rail carries without sand (its tonnage on
            // this grade, from a stand or at speed), or on a rail the weather has made slippery when the engineer asks for more than it carries.
            // On a dry rail a request alone is no reason: the slip guard holds the throttle to what the wheels carry. After a slip the tail below
            // sands as before.
            bool crawl = mph < SandProactiveMph;
            bool recent = now - st.LastSlip < SandTail(mph);
            float needAt = crawl ? Main.Settings.SlipMargin : SandRollingFactor;
            st.Wet = SlipSand.Inclement(loco);
            st.Required = haveLimit ? RequiredEffort(ae, loco, mph) : float.NaN;
            bool tonnage = haveLimit && !float.IsNaN(st.Required) && st.Required > Main.Settings.SlipMargin * limitNo;
            bool wetRequest = st.Wet && haveLimit && asked > needAt * limitNo && (crawl || recent || asked > SandRollingFactor * limitNo);
            if (haveLimit && limitSand > limitNo * 1.02f && (tonnage || wetRequest))
            {
                st.SandNeed = true;
                st.SandLowSince = -1f;
            }
            else if (st.SandNeed)
            {
                if (st.SandLowSince < 0f) st.SandLowSince = now;
                if (now - st.SandLowSince > SandOffSeconds) st.SandNeed = false;
            }
            else
            {
                st.SandLowSince = -1f;
            }

            if (Main.Settings.SlipGuard)
            {
                SlipSand.Status(ae, loco, out bool sandOn, out _);
                Guard(ae, loco, st, slipping, mph, now, dt, lever, haveLimit ? (sandOn ? limitSand : limitNo) : float.NaN);
            }
            else
            {
                st.Ceiling = 1f;
            }

            // Sand while the wheels slip, for a while after, and while the train asks for more than dry rail carries. With the throttle shut
            // (coasting or braking) there is nothing for sand to do: it goes off at once rather than after the off delay and the hold.
            bool powerOff = lever < 0.02f && (now - st.DemandTime >= 1.5f || st.Demand < 0.02f);
            if (powerOff) st.SandNeed = false;
            bool wantSand = !powerOff && (raw || st.SandNeed || (now - st.LastSlip < SandTail(mph)));
            if (Main.Settings.SlipSand && !powerOff) SlipSand.Update(ae, loco, wantSand, mph, now);
            else SlipSand.Release(ae, loco);

            st.WasSlipping = slipping;
        }

        private static void Guard(AutoEngineer ae, BaseLocomotive loco, State st, bool slipping, float mph, float now, float dt, float lever, float limit)
        {
            if (mph > Main.Settings.SlipGuardMaxMph)
            {
                st.Ceiling = 1f;
                return;
            }

            float sf = SpeedFactor(mph);
            float target = 1f;
            if (st.K > 0f && !float.IsNaN(limit))
            {
                float wanted = Main.Settings.SlipMargin * limit;
                if (slipping)
                {
                    // A slipping wheelset gets a friction cap of 0.1 (absolute, not a tenth of its limit) until its effort is under it.
                    DriverWheelState[] ws = Wheels(loco);
                    float aw = 0f;
                    if (ws != null) for (int i = 0; i < ws.Length; i++) aw += ws[i].AdhesiveWeight;
                    wanted = aw > 1f ? SlipRecoverFraction * aw : SlipRecoverFraction * limit;
                    // The limit itself can sit lower than that (burn marks on the rail, a wet rail): never aim above a level the wheels can carry.
                    wanted = Mathf.Min(wanted, 0.85f * Main.Settings.SlipMargin * limit);
                }
                target = Mathf.Clamp01(wanted / st.K);
                if (target > 0.999f) target = 1f;
            }
            else if (slipping)
            {
                target = 0f;
            }

            if (slipping)
            {
                if (!st.WasSlipping)
                {
                    st.Slips++;
                    if (st.Slips <= LoggedSlips)
                    {
                        Main.Mod.Logger.Log("Slip guard: " + loco.DisplayName + " slip " + st.Slips + " at throttle " + lever.ToString("0.00") + ", " + mph.ToString("0.0") + " mph; effort "
                            + st.Effort.ToString("0") + " lbf" + (float.IsNaN(limit) ? "" : " of " + limit.ToString("0") + " lbf")
                            + (st.Slips == LoggedSlips ? " (no more slips are logged for this engineer)" : "") + ".");
                    }
                }
                st.LastSlip = now;
                // Closing is quick at a crawl and gentle at speed: first a cut of a tenth at speed (all of it under 3 mph), then down at 4 per
                // second at a crawl or 0.25 per second at 15 mph and over until the wheels grip again.
                float from = Mathf.Min(st.Ceiling, lever);
                if (!st.WasSlipping) from = Mathf.Min(from, lever * 0.9f * sf);
                st.Ceiling = Mathf.Max(target, from - Mathf.Lerp(4f, 0.25f, sf) * dt);
            }
            else if (target < st.Ceiling)
            {
                // The limit is lower than the ceiling (a wet rail, a hill start): come down to it, quickly at a crawl and gently at speed.
                st.Ceiling = Mathf.Max(target, st.Ceiling - Mathf.Lerp(2f, 0.5f, sf) * dt);
                if (!st.LoggedLimit && target < 0.98f && lever > target + 0.02f)
                {
                    st.LoggedLimit = true;
                    Main.Mod.Logger.Log("Slip guard: " + loco.DisplayName + " is held to throttle " + target.ToString("0.00") + " (the wheels carry " + limit.ToString("0") + " lbf, the locomotive makes "
                        + st.K.ToString("0") + " lbf per unit of throttle; logged once per engineer).");
                }
            }
            else if (target > st.Ceiling && now - st.LastSlip >= Mathf.Lerp(ClearHoldSeconds, 2f, sf))
            {
                st.Ceiling = Mathf.Min(target, st.Ceiling + ClimbPerSecond * dt);
            }

            if (st.Ceiling < 1f && lever > st.Ceiling + 0.004f)
            {
                BrakeFix.WriteThrottle(ae, st.Ceiling);
            }
        }

        /// <summary>Stand down (the engineer was switched off, or the feature was).</summary>
        internal static void Release(AutoEngineer ae)
        {
            if (ae == null) return;
            if (States.TryGetValue(ae, out State st))
            {
                st.Ceiling = 1f;
                st.SandNeed = false;
                st.WasSlipping = false;
            }
            var loco = ae.Locomotive as BaseLocomotive;
            if (loco != null) SlipSand.Release(ae, loco);
        }

        /// <summary>Rollout command filter: the controller's notch never goes over the ceiling.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            if (!Main.Settings.SlipGuard) return false;
            var ae = BrakeTaps.FAe?.GetValue(loop) as AutoEngineer;
            if (ae == null) return false;
            if (States.TryGetValue(ae, out State st))
            {
                st.Demand = command.ThrottleNotch;     // what the controller wants, before the ceiling
                st.DemandTime = Time.time;
            }
            float ceiling = Ceiling(ae);
            if (ceiling >= 1f || command.ThrottleNotch <= ceiling + 0.0005f) return false;
            command = new RolloutCommand(command.Kind, ceiling, command.IndependentBrake01, command.TrainBrakeSet, command.DrivesTrainBrake, command.BailOff, command.Cost, command.HoldCost);
            return true;
        }
    }

    /// <summary>
    /// Sanding for the engineer, through Wheelslip Prototype's own sand request (the key the sanding button writes). While a driver wheelset
    /// slips at low speed and the locomotive has sand, the request is switched on; it is switched off a few seconds after the slip has
    /// stopped, when the sand runs out or the train is going too fast. Locomotives with Wheelslip's automatic sanding (GP9, SD7) are left to it.
    /// Wheelslip only applies sand to engineer-driven locomotives when its "Include AutoEngineer" option is on.
    /// </summary>
    internal static class SlipSand
    {
        private sealed class State
        {
            public bool Owned;
            public float Until;
            public bool LoggedOn;
            public bool LoggedEmpty;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();

        private const float HoldSeconds = 6f;    // sand on this long after the slip guard stopped asking for it

        private static bool _tried;
        private static MethodInfo _profile, _amount, _requestKey, _supportsAuto, _autoOn;
        private static PropertyInfo _settingsCurrent;
        private static FieldInfo _autoEngineerOption, _adhesionMultiplier;
        private static MethodInfo _weather, _railGrip, _burnGrip;

        internal static bool Present
        {
            get
            {
                Init();
                return _profile != null && _amount != null && _requestKey != null;
            }
        }

        private static void Init()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "WheelslipPrototype") continue;
                    const BindingFlags f = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    Type sand = asm.GetType("WheelslipPrototype.SandRuntime");
                    if (sand != null)
                    {
                        _profile = sand.GetMethod("Profile", f);
                        _amount = sand.GetMethod("Amount", f);
                        _requestKey = sand.GetMethod("RequestControl", f);
                        _supportsAuto = sand.GetMethod("SupportsAutomaticSand", f);
                        _autoOn = sand.GetMethod("AutomaticSandEnabled", f);
                    }
                    Type settings = asm.GetType("WheelslipPrototype.SettingsRuntime");
                    _settingsCurrent = settings?.GetProperty("Current", f);
                    _autoEngineerOption = _settingsCurrent?.PropertyType.GetField("AutoEngineerEnabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _adhesionMultiplier = _settingsCurrent?.PropertyType.GetField("AdhesionMultiplier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    _weather = asm.GetType("WheelslipPrototype.WeatherAdhesion")?.GetMethod("Coefficient", f);
                    _railGrip = sand?.GetMethod("RailGrip", f);
                    _burnGrip = asm.GetType("WheelslipPrototype.WheelBurns")?.GetMethod("Grip", f);
                    Main.Mod.Logger.Log("Wheelslip Prototype found: " + (Present ? "the engineer can sand when the wheels slip." : "its sanding members were not found, so the engineer will not sand."));
                    return;
                }
            }
            catch (Exception e)
            {
                _profile = null;
                Main.Mod.Logger.Log("Wheelslip Prototype link failed, the engineer will not sand: " + e.Message);
            }
        }

        private static bool WheelslipSandsEngineers()
        {
            try
            {
                object current = _settingsCurrent?.GetValue(null, null);
                if (current == null || _autoEngineerOption == null) return true;
                return (bool)_autoEngineerOption.GetValue(current);
            }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// The most tractive effort (lbf) the driver wheels carry before they slip, without sand and with sand: for each wheelset its weight on the
        /// drivers times the friction coefficient Wheelslip Prototype's wheel patch hands to DriverWheelState.UpdateWheelslip (or the game's own curve when
        /// Wheelslip is not there or does not cover engineers).
        /// </summary>
        /// <summary>
        /// True when the weather has cut the rail's grip: Wheelslip's weather coefficient is more than 5 % under the game's dry curve at the
        /// wheels' speed. False without Wheelslip (the game has no wet rail of its own) or when it does not cover engineers.
        /// </summary>
        internal static bool Inclement(BaseLocomotive loco)
        {
            try
            {
                if (!Present || _weather == null || !WheelslipSandsEngineers()) return false;
                DriverWheelState[] wheels = SlipGuard.Wheels(loco);
                if (wheels == null || wheels.Length == 0) return false;
                float weather = Convert.ToSingle(_weather.Invoke(null, new object[] { loco }));
                if (float.IsNaN(weather)) return false;
                float dry = TrainMath.TrackCoefficientOfFriction(TrainMath.TrackCondition.Dry, wheels[0].WheelVelocity);
                return weather < 0.95f * dry;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool Limits(BaseLocomotive loco, out float noSand, out float withSand)
        {
            noSand = 0f;
            withSand = 0f;
            try
            {
                DriverWheelState[] wheels = SlipGuard.Wheels(loco);
                if (wheels == null || wheels.Length == 0) return false;

                bool ws = Present && WheelslipSandsEngineers();
                float weather = float.NaN, bonus = 0f, mult = 1f, c = 1f;
                if (ws)
                {
                    if (_weather != null) weather = Convert.ToSingle(_weather.Invoke(null, new object[] { loco }));
                    object profile = _profile.Invoke(null, new object[] { loco });
                    if (profile != null)
                    {
                        FieldInfo fb = profile.GetType().GetField("adhesionBonus");
                        if (fb != null) bonus = Convert.ToSingle(fb.GetValue(profile));
                    }
                    object current = _settingsCurrent?.GetValue(null, null);
                    if (current != null && _adhesionMultiplier != null) mult = Convert.ToSingle(_adhesionMultiplier.GetValue(current));
                    // Rail wear and burn marks scale the coefficient; the same factor holds for any coefficient.
                    float scaled = 1f;
                    if (_railGrip != null) scaled = Convert.ToSingle(_railGrip.Invoke(null, new object[] { loco, 1f }));
                    if (_burnGrip != null) scaled = Convert.ToSingle(_burnGrip.Invoke(null, new object[] { loco, scaled }));
                    c = scaled;
                }

                for (int i = 0; i < wheels.Length; i++)
                {
                    float mu = TrainMath.TrackCoefficientOfFriction(TrainMath.TrackCondition.Dry, wheels[i].WheelVelocity);
                    if (ws && !float.IsNaN(weather)) mu = Mathf.Min(mu, weather);
                    float muSand = ws && bonus > 0f ? Mathf.Min(0.35f * mult, mu + bonus * mult) : mu;
                    noSand += wheels[i].AdhesiveWeight * mu * c;
                    withSand += wheels[i].AdhesiveWeight * muSand * c;
                }
                return noSand > 1f;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool Requested(BaseLocomotive loco, string key)
        {
            try { return loco.KeyValueObject[key].BoolValue; }
            catch (Exception) { return false; }
        }

        private static void Request(BaseLocomotive loco, string key, bool on)
        {
            loco.KeyValueObject[key] = KeyValue.Runtime.Value.Bool(on);
        }

        internal static void Update(AutoEngineer ae, BaseLocomotive loco, bool wanted, float mph, float now)
        {
            if (!Present || loco.KeyValueObject == null) return;
            State st = States.GetOrCreateValue(ae);
            try
            {
                object profile = _profile.Invoke(null, new object[] { loco });
                if (profile == null) return;
                bool auto = _supportsAuto != null && _autoOn != null
                    && (bool)_supportsAuto.Invoke(null, new object[] { loco }) && (bool)_autoOn.Invoke(null, new object[] { loco });
                if (auto) return;   // GP9 and SD7 sand by themselves

                string key = (string)_requestKey.Invoke(null, new object[] { profile });
                float pounds = Convert.ToSingle(_amount.Invoke(null, new object[] { loco, profile }));
                float maxMph = Main.Settings.SlipSandMaxMph;

                if (wanted && mph <= maxMph && WheelslipSandsEngineers())
                {
                    if (pounds < 1f)
                    {
                        if (!st.LoggedEmpty)
                        {
                            st.LoggedEmpty = true;
                            Main.Mod.Logger.Log("Slip sanding: " + loco.DisplayName + " slips but has no sand left (logged once per engineer).");
                        }
                    }
                    else
                    {
                        st.Until = now + HoldSeconds;
                        if (!st.Owned && !Requested(loco, key))
                        {
                            Request(loco, key, true);
                            st.Owned = true;
                            if (!st.LoggedOn)
                            {
                                st.LoggedOn = true;
                                Main.Mod.Logger.Log("Slip sanding: " + loco.DisplayName + " sands while the wheels slip (" + pounds.ToString("0") + " lb of sand, logged once per engineer).");
                            }
                        }
                    }
                }

                if (st.Owned && (now > st.Until || mph > maxMph + 3f || pounds < 0.5f))
                {
                    Request(loco, key, false);
                    st.Owned = false;
                }
            }
            catch (Exception e)
            {
                _profile = null;   // give up for the rest of the session rather than fail every frame
                Main.Mod.Logger.Log("Slip sanding stopped: " + e.Message);
            }
        }

        /// <summary>Testing: fills the sand tank of the selected locomotive to its capacity (Wheelslip Prototype's stored amount).</summary>
        internal static void FillSelected()
        {
            try
            {
                if (!Present) { Main.Mod.Logger.Log("Fill sand: Wheelslip Prototype was not found."); return; }
                var loco = TrainController.Shared != null ? TrainController.Shared.SelectedLocomotive : null;
                if (loco == null || loco.KeyValueObject == null) { Main.Mod.Logger.Log("Fill sand: no locomotive is selected."); return; }
                object profile = _profile.Invoke(null, new object[] { loco });
                if (profile == null) { Main.Mod.Logger.Log("Fill sand: " + loco.DisplayName + " has no sand profile in Wheelslip."); return; }
                float capacity = Convert.ToSingle(profile.GetType().GetField("capacityPounds").GetValue(profile));
                loco.KeyValueObject["wheelslip.sand.pounds.v1"] = KeyValue.Runtime.Value.Float(capacity);
                Main.Mod.Logger.Log("Fill sand: " + loco.DisplayName + " now has " + capacity.ToString("0") + " lb of sand.");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Log("Fill sand failed: " + e.Message);
            }
        }

        /// <summary>For the log: whether this engineer has the sand request on, and the sand left (lb, NaN when unknown).</summary>
        internal static void Status(AutoEngineer ae, BaseLocomotive loco, out bool on, out float pounds)
        {
            on = false;
            pounds = float.NaN;
            try
            {
                if (!Present || loco == null || loco.KeyValueObject == null) return;
                object profile = _profile.Invoke(null, new object[] { loco });
                if (profile == null) return;
                pounds = Convert.ToSingle(_amount.Invoke(null, new object[] { loco, profile }));
                on = Requested(loco, (string)_requestKey.Invoke(null, new object[] { profile }));
            }
            catch (Exception) { /* leave the defaults */ }
        }

        /// <summary>Switches the sand off again if this engineer switched it on.</summary>
        internal static void Release(AutoEngineer ae, BaseLocomotive loco)
        {
            if (ae == null || loco == null || !_tried || _requestKey == null || _profile == null) return;
            if (!States.TryGetValue(ae, out State st) || !st.Owned) return;
            try
            {
                object profile = _profile.Invoke(null, new object[] { loco });
                if (profile != null && loco.KeyValueObject != null) Request(loco, (string)_requestKey.Invoke(null, new object[] { profile }), false);
            }
            catch (Exception) { /* nothing more to do */ }
            st.Owned = false;
        }
    }
}
