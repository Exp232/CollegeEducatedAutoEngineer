using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Model.AI;

namespace CollegeEducatedAutoEngineer
{
    internal enum PidKind
    {
        Independent,
        TrainBrake,
        Throttle
    }

    internal sealed class PidInfo
    {
        public AutoEngineer Engineer;
        public PidKind Kind;

        // Throttle smoothing state (only used for PidKind.Throttle).
        public bool HasLast;
        public float Last;
        public float LastTime;

        // Hold-up after the start-up routine hands over (see ThrottleSmoother).
        public float Floor;
        public float FloorStart;

        // Gliding between AI ticks (see ThrottleGlider).
        public bool Gliding;
        public float Shown;

        // Diesel notch guard (see NotchGuard): what the filter last handed the controller, and whether another mod snaps the lever.
        public bool HasReturned;
        public float Returned;
        public float ReturnedTime;
        public int SnapCount;
        public bool NotchLocked;
    }

    /// <summary>
    /// Another mod (a diesel notch or throttle mod) can round the throttle lever to the nearest notch the moment it is written. The
    /// smoothing then writes 0.275, reads back 0.25, takes that as someone else's change and starts over each tick, so a diesel never
    /// climbs past the notch it is on. When a diesel's lever keeps coming back as an exact notch after the mod wrote a value between
    /// notches, smoothing is switched off for that engineer (the stock lever is continuous, so this never fires without such a mod).
    /// </summary>
    internal static class NotchGuard
    {
        private const float OffNotch = 0.1f;        // in notch units (0.125 of the lever): written value this far from a notch
        private const float OnNotch = 0.02f;        // read-back this close to a notch
        private const float SameValue = 0.004f;     // read-back equal to what was written
        private const int SnapsToLock = 4;

        /// <summary>True when smoothing is off for this engineer. Pass the value the mod wrote and the lever reading seen afterwards.</summary>
        internal static bool Observe(PidInfo info, float written, float read)
        {
            if (info == null || !Main.Settings.DieselNotchGuard) return false;
            if (info.NotchLocked) return true;
            if (!(info.Engineer != null && info.Engineer.Locomotive is Model.DieselLocomotive)) return false;

            float wn = written * 8f;
            float rn = read * 8f;
            float diff = Mathf.Abs(read - written);
            bool writtenOffNotch = Mathf.Abs(wn - Mathf.Round(wn)) > OffNotch;
            bool readOnNotch = Mathf.Abs(rn - Mathf.Round(rn)) < OnNotch;

            if (writtenOffNotch && readOnNotch && diff > SameValue && diff < 0.125f) info.SnapCount++;
            else if (writtenOffNotch && diff <= SameValue) info.SnapCount = 0;   // a value between notches read back intact: the lever is continuous

            if (info.SnapCount >= SnapsToLock)
            {
                info.NotchLocked = true;
                info.HasLast = false;
                info.Gliding = false;
                info.Floor = 0f;
                Main.Mod.Logger.Log("Diesel throttle: the lever of " + info.Engineer.Locomotive.DisplayName + " snaps to the nearest notch after each write (another mod rounds it), so throttle smoothing is off for this engineer (logged once per engineer).");
            }
            return info.NotchLocked;
        }
    }

    /// <summary>
    /// Access to the engineer's control state (throttle, brakes) by reflection, shared by the stall guard, the
    /// throttle smoother and the glider. The old brake-drag fix that lived here worked on the PID speed loop that
    /// 0.6.0 removed, and is gone.
    /// </summary>
    internal static class BrakeFix
    {
        private const float BrakeOnThreshold = 0.001f;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly FieldInfo FControl = AccessTools.Field(typeof(AutoEngineer), "_control");

        // The control helper's Throttle/TrainBrake/LocomotiveBrake may be properties or fields; resolve once per name.
        private sealed class Accessor
        {
            public PropertyInfo Property;
            public FieldInfo Field;
        }

        private static readonly Dictionary<string, Accessor> ControlAccessors = new Dictionary<string, Accessor>();

        /// <summary>Called from the AutoEngineer.OnEnable postfix, once per enable.</summary>
        internal static void Register(AutoEngineer ae)
        {
            StallGuard.Reset(ae);
            DirectionPause.Reset(ae);
        }

        internal static PidInfo ThrottleInfo(AutoEngineer ae)
        {
            return RolloutThrottle.Peek(ae);   // the rollout controller's smoothing state
        }

        /// <summary>True if the train brake or the locomotive brake is currently applied.</summary>
        internal static bool ReadBrakeRequested(AutoEngineer ae)
        {
            return ReadControl(ae, "TrainBrake") > BrakeOnThreshold || ReadControl(ae, "LocomotiveBrake") > BrakeOnThreshold;
        }

        internal static float ReadThrottle(AutoEngineer ae)
        {
            return ReadControl(ae, "Throttle");
        }

        internal static void WriteThrottle(AutoEngineer ae, float value)
        {
            object control = FControl?.GetValue(ae);
            if (control == null) return;
            ReadControl(ae, "Throttle"); // makes sure the accessor is cached
            if (!ControlAccessors.TryGetValue("Throttle", out Accessor accessor)) return;
            if (accessor.Property != null) accessor.Property.SetValue(control, value, null);
            else if (accessor.Field != null) accessor.Field.SetValue(control, value);
        }

        internal static void WriteControl(AutoEngineer ae, string name, float value)
        {
            object control = FControl?.GetValue(ae);
            if (control == null) return;
            Type type = control.GetType();
            var prop = type.GetProperty(name, Members);
            if (prop != null && prop.CanWrite) { prop.SetValue(control, value, null); return; }
            type.GetField(name, Members)?.SetValue(control, value);
        }

        internal static float ReadControl(AutoEngineer ae, string name)
        {
            object control = FControl?.GetValue(ae);
            if (control == null) return 0f;

            if (!ControlAccessors.TryGetValue(name, out Accessor accessor))
            {
                Type type = control.GetType();
                accessor = new Accessor { Property = type.GetProperty(name, Members) };
                if (accessor.Property == null)
                {
                    accessor.Field = type.GetField(name, Members);
                }
                ControlAccessors[name] = accessor;
            }

            if (accessor.Property != null) return Convert.ToSingle(accessor.Property.GetValue(control, null));
            if (accessor.Field != null) return Convert.ToSingle(accessor.Field.GetValue(control));
            return 0f;
        }
    }
}
