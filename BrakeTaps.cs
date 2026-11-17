using System;
using System.Reflection;
using HarmonyLib;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Brake tap limit (BrakeTapSuppressMph, 2 mph by default, 0 = off). The rollout controller sometimes applies the train
    /// brake to shed a mph or two, which spends fuel to gain the speed and then throws it away as heat. When the
    /// train is only a little over its target, is not gaining speed, and nothing ahead needs it to slow, this turns
    /// that first application into a coast; if the speed then rises past the margin or a lower limit comes into
    /// range, the controller's brake application goes through as usual. Road and Waypoint modes (a stop within about
    /// 1500 m or 80 s always stays with the controller). This class also holds the rollout loop's reflected members
    /// that the other command filters share.
    /// </summary>
    internal static class BrakeTaps
    {
        private const float MpsToMph = 2.23694f;
        private const float MaxAccelMps2 = 0.03f;       // measured acceleration at or below this: not gaining speed
        private const float AppliedSetMax = 0.12f;      // handle at most a 10 psi reduction (the engineer parks it at 5 psi with empty cylinders)
        private const float CylinderPsiMax = 1f;        // average car cylinder pressure at or below this: brakes not really applied
        private static bool _logged;

        internal static readonly Type LoopType = AccessTools.Inner(typeof(AutoEngineer), "SpeedControlLoop");
        internal static readonly FieldInfo FAe = LoopType != null ? AccessTools.Field(LoopType, "_ae") : null;
        internal static readonly FieldInfo FTrainBrake = LoopType != null ? AccessTools.Field(LoopType, "_trainBrake") : null;
        internal static readonly FieldInfo FMeasuredAccel = LoopType != null ? AccessTools.Field(LoopType, "_measuredAccel") : null;
        internal static readonly PropertyInfo PApplied = BrakeGuidance.TrainBrakeType != null ? AccessTools.Property(BrakeGuidance.TrainBrakeType, "AppliedBrakeSet") : null;
        private static readonly FieldInfo FContextual = AccessTools.Field(typeof(AutoEngineer), "_debugContextualTargetVelocity");
        internal static FieldInfo FContextualField => FContextual;

        /// <summary>The train brake set the loop has applied now (0 = released), or NaN.</summary>
        internal static float AppliedSet(object loop)
        {
            if (PApplied == null || FTrainBrake == null) return float.NaN;
            object tb = FTrainBrake.GetValue(loop);
            return tb == null ? float.NaN : Convert.ToSingle(PApplied.GetValue(tb, null));
        }

        /// <summary>True when a lower limit or a stop ahead is close enough that the controller must be left to brake.</summary>
        internal static bool SlowingNeededAhead(AutoEngineer.Targets targets, float speedMps)
        {
            return SlowingNeededAhead(targets, speedMps, out _);
        }

        /// <summary>The same, saying whether it is a stop ("stopAhead") or a lower limit ("limitAhead").</summary>
        private static bool SlowingNeededAhead(AutoEngineer.Targets targets, float speedMps, out string why)
        {
            why = null;
            foreach (var t in targets.AllTargets)
            {
                if (t.Distance <= 0f) continue;
                float tv = Mathf.Abs(t.SpeedMph) / MpsToMph;
                if (tv >= speedMps - 0.9f) continue;
                float window = tv < 0.5f ? Mathf.Max(1500f, speedMps * 80f) : Mathf.Max(600f, speedMps * 40f);
                if (t.Distance < window)
                {
                    why = tv < 0.5f ? "stopAhead" : "limitAhead";
                    return true;
                }
            }
            return false;
        }

        private static readonly MethodInfo MCylinder = AccessTools.Method(typeof(AutoEngineer), "AverageTrainBrakeCylinder");

        internal static bool Available => MCylinder != null && LoopType != null && FAe != null && FTrainBrake != null && FMeasuredAccel != null && PApplied != null && BrakeFix.TargetsReadable && FContextual != null;

        private static readonly System.Collections.Generic.SortedDictionary<string, int> Rejects = new System.Collections.Generic.SortedDictionary<string, int>();

        private static bool Reject(string why)
        {
            lock (Rejects)
            {
                Rejects.TryGetValue(why, out int n);
                Rejects[why] = n + 1;
            }
            return false;
        }

        /// <summary>Why first applications were left to the controller so far, as "reason:count|reason:count" (for the fuel summary).</summary>
        internal static string RejectSummary()
        {
            lock (Rejects)
            {
                return string.Join("|", System.Linq.Enumerable.Select(Rejects, kv => kv.Key + ":" + kv.Value));
            }
        }

        /// <summary>Returns true when the command was a brake tap and has been replaced by a coast.</summary>
        internal static bool Filter(object loop, ref RolloutCommand command)
        {
            float limitMph = Main.Settings.BrakeTapSuppressMph;
            if (limitMph <= 0f) return false;
            if (!command.DrivesTrainBrake) return false;
            if (command.BailOff || command.IndependentBrake01 > 0.01f) return Reject("locoBrake");

            var ae = FAe.GetValue(loop) as AutoEngineer;
            if (ae == null || ae.Locomotive == null) return Reject("noLoco");
            float applied = Convert.ToSingle(PApplied.GetValue(FTrainBrake.GetValue(loop), null));
            if (command.TrainBrakeSet <= applied + 0.005f) return false;
            if (applied > AppliedSetMax) return Reject("handleDown");
            // Only the first real application: the cylinders must still be (nearly) empty.
            if (Convert.ToSingle(MCylinder.Invoke(ae, null)) > CylinderPsiMax) return Reject("cylinders");

            var targets = BrakeFix.TargetsOf(ae);
            if (targets == null || targets.AllTargets == null) return Reject("noTargets");
            // Road and Waypoint only. In Waypoint mode the stop is protected by the distance check below, not by the mode.
            string mode = targets.Mode.ToString();
            if (mode != "Road" && mode != "Waypoint") return Reject("mode");

            float speed = Mathf.Abs(ae.Locomotive.velocity);
            float target = Mathf.Abs(Convert.ToSingle(FContextual.GetValue(ae)));
            if (target < 3f || speed < 3f) return Reject("slow");
            if (speed - target > limitMph / MpsToMph) return Reject("overLimit");                      // really over: brake
            if (Convert.ToSingle(FMeasuredAccel.GetValue(loop)) > MaxAccelMps2) return Reject("gaining");         // still gaining speed: brake

            // Anything ahead that needs the train to slow (a lower limit or a stop) keeps the controller in charge.
            if (SlowingNeededAhead(targets, speed, out string why)) return Reject(why);

            command = new RolloutCommand(RolloutActionKind.Hold, 0f, command.IndependentBrake01, applied, false, false, command.Cost, command.HoldCost);
            FuelSummary.CountSuppressed(ae);
            if (!_logged)
            {
                _logged = true;
                Main.Mod.Logger.Log("Brake tap suppressed for " + ae.Locomotive.DisplayName + " at " + (speed * MpsToMph).ToString("0.0") + " mph, target " + (target * MpsToMph).ToString("0.0") + " mph (further ones are only counted in fuel_summary).");
            }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class SpeedControlLoop_ApplyRolloutCommand_Patch
    {
        private static MethodBase TargetMethod()
        {
            return BrakeTaps.LoopType != null ? AccessTools.Method(BrakeTaps.LoopType, "ApplyRolloutCommand") : null;
        }

        // ApplyRolloutCommand(in RolloutCommand command): the first argument is patched by reference.
        private static void Prefix(object __instance, ref RolloutCommand __0)
        {
            if (!Main.Active) return;
            try
            {
                RolloutCommand before = __0;
                WagonLock.EnsureIsolation();
                if (StallGuard.Filter(__instance, ref __0))
                {
                    CommandLog.Note(BrakeTaps.FAe?.GetValue(__instance) as AutoEngineer, before, __0, false, false, false);
                    return;
                }
                if (DirectionPause.Filter(__instance, ref __0))
                {
                    CommandLog.Note(BrakeTaps.FAe?.GetValue(__instance) as AutoEngineer, before, __0, false, false, false);
                    return;
                }
                if (DirectionStop.Filter(__instance, ref __0))
                {
                    WagonLock.Filter(__instance, ref __0);
                    CommandLog.Note(BrakeTaps.FAe?.GetValue(__instance) as AutoEngineer, before, __0, false, false, false);
                    return;
                }
                bool hold = ReductionHold.Filter(__instance, ref __0);
                bool tap = BrakeTaps.Filter(__instance, ref __0);
                bool band = CoastBand.Filter(__instance, ref __0);
                bool smooth = RolloutThrottle.Filter(__instance, ref __0);
                smooth |= SlipGuard.Filter(__instance, ref __0);
                smooth |= WagonLock.Filter(__instance, ref __0);
                CommandLog.Note(BrakeTaps.FAe?.GetValue(__instance) as AutoEngineer, before, __0, tap || hold, band, smooth);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }
    }
}
