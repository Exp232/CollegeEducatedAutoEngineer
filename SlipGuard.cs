using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Model;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Slip guard. A driver wheelset slips when the tractive effort is more than the adhesion allows (DriverWheelState.IsSlipping), and
    /// once it slips the game only gives it a tenth of the adhesion until the effort falls below that, so an engineer that keeps the lever
    /// where it was keeps spinning. This closes the throttle while any driver wheelset of the engineer's locomotive slips, then lets it open
    /// again slowly to a level just under the one that slipped, and releases the limit once the train has run a while without slipping.
    /// It uses the game's own slip state, so it works with or without Wheelslip Prototype.
    /// </summary>
    internal static class SlipGuard
    {
        private sealed class State
        {
            public float Next;
            public float LastTick;
            public float Ceiling = 1f;       // the throttle may not be above this (1 = no limit)
            public float Limit = 1f;         // where the ceiling climbs to after a slip
            public float LastSlip = -100f;
            public bool WasSlipping;
            public bool LoggedSlip;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly FieldInfo FStates = AccessTools.Field(typeof(BaseLocomotive), "DriverWheelStates");

        private const float TickInterval = 0.05f;
        private const float ClearHoldSeconds = 0.8f;     // after the slip stops, stay closed this long
        private const float ClimbPerSecond = 0.15f;      // then open this fast up to the limit
        private const float LimitFactor = 0.85f;         // the limit is this fraction of the level that slipped
        private const float MinLimit = 0.1f;
        private const float MemorySeconds = 20f;         // slip-free time before the limit is lifted
        private const float ReleasePerSecond = 0.1f;

        internal static bool Available { get { return FStates != null; } }

        /// <summary>The throttle ceiling for this engineer, 1 when there is none.</summary>
        internal static float Ceiling(AutoEngineer ae)
        {
            if (ae == null || !Main.Settings.SlipGuard || !States.TryGetValue(ae, out State st)) return 1f;
            return st.Ceiling;
        }

        internal static bool IsSlipping(BaseLocomotive loco)
        {
            var wheels = FStates?.GetValue(loco) as DriverWheelState[];
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
            bool slipping = IsSlipping(loco) && !StallGuard.IsLatched(ae);

            if (Main.Settings.SlipGuard)
            {
                Guard(ae, loco, st, slipping, mph, now, dt);
            }
            else
            {
                st.Ceiling = 1f;
                st.Limit = 1f;
            }

            if (Main.Settings.SlipSand) SlipSand.Update(ae, loco, slipping, mph, now);
            else SlipSand.Release(ae, loco);

            st.WasSlipping = slipping;
        }

        private static void Guard(AutoEngineer ae, BaseLocomotive loco, State st, bool slipping, float mph, float now, float dt)
        {
            if (mph > Main.Settings.SlipGuardMaxMph || StallGuard.IsLatched(ae))
            {
                st.Ceiling = 1f;
                st.Limit = 1f;
                return;
            }

            float lever = BrakeFix.ReadThrottle(ae);
            if (slipping)
            {
                if (!st.WasSlipping)
                {
                    float basis = st.Ceiling < 1f ? Mathf.Min(lever, Mathf.Max(st.Ceiling, MinLimit)) : lever;
                    st.Limit = Mathf.Min(st.Limit, Mathf.Max(MinLimit, basis * LimitFactor));
                    if (!st.LoggedSlip)
                    {
                        st.LoggedSlip = true;
                        Main.Mod.Logger.Log("Slip guard: " + loco.DisplayName + " slipped at throttle " + lever.ToString("0.00") + "; the throttle is closed and then limited to " + st.Limit.ToString("0.00") + " until it has run clear for a while (logged once per engineer).");
                    }
                }
                st.LastSlip = now;
                st.Ceiling = 0f;
            }
            else if (st.Ceiling < 1f)
            {
                if (now - st.LastSlip >= ClearHoldSeconds)
                {
                    if (st.Ceiling < st.Limit)
                    {
                        st.Ceiling = Mathf.Min(st.Limit, st.Ceiling + ClimbPerSecond * dt);
                    }
                    else if (now - st.LastSlip > MemorySeconds)
                    {
                        st.Ceiling = Mathf.Min(1f, st.Ceiling + ReleasePerSecond * dt);
                        if (st.Ceiling >= 1f) st.Limit = 1f;
                    }
                }
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
                st.Limit = 1f;
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

        private const float HoldSeconds = 4f;

        private static bool _tried;
        private static MethodInfo _profile, _amount, _requestKey, _supportsAuto, _autoOn;
        private static PropertyInfo _settingsCurrent;
        private static FieldInfo _autoEngineerOption;

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

        private static bool Requested(BaseLocomotive loco, string key)
        {
            try { return loco.KeyValueObject[key].BoolValue; }
            catch (Exception) { return false; }
        }

        private static void Request(BaseLocomotive loco, string key, bool on)
        {
            loco.KeyValueObject[key] = KeyValue.Runtime.Value.Bool(on);
        }

        internal static void Update(AutoEngineer ae, BaseLocomotive loco, bool slipping, float mph, float now)
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

                if (slipping && mph <= maxMph && WheelslipSandsEngineers())
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
