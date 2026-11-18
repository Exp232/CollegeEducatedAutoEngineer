using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Model;
using Model.AI;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// The engineer's StopAndHold state writes the train brake handle to 1, which is a 90 psi
    /// reduction: an emergency application. 0.6.3 sends every train that reaches its stop mark still moving there
    /// (in the 1.7.3 log at 0.7 to 0.9 mph, 0.8 m short of a waypoint), so each waypoint stop ended in emergency.
    /// With StopHoldFullService on, those writes from the engineer's main loop are capped at a full service
    /// application (the train brake's own maximum effective set), which holds a standing train just as well.
    /// Only the main loop's writes are touched (a transpiler on its iterator); the player's brake handle and the
    /// speed loop are not, nor is EmergencyStop (a separate method, for a stop target overrun by 3 m).
    /// </summary>
    internal static class StopHold
    {
        private const float FallbackFullService = 26f / 90f;

        internal static readonly Type LoopIterator = typeof(AutoEngineer).GetNestedTypes(BindingFlags.NonPublic)
            .FirstOrDefault(t => t.Name.StartsWith("<Loop>d__", StringComparison.Ordinal));
        internal static readonly FieldInfo FThis = LoopIterator != null ? AccessTools.Field(LoopIterator, "<>4__this") : null;
        internal static readonly MethodInfo Setter = AccessTools.PropertySetter(typeof(LocomotiveControlHelper), "TrainBrake");
        private static readonly FieldInfo FSpeedLoop = AccessTools.Field(typeof(AutoEngineer), "_speedControlLoop");
        private static readonly MethodInfo MMaxEffective = BrakeGuidance.TrainBrakeType != null ? AccessTools.Method(BrakeGuidance.TrainBrakeType, "CalculateMaxEffectiveApplication") : null;
        private static bool _logged;

        internal static bool Available => LoopIterator != null && FThis != null && Setter != null;

        /// <summary>Called in place of LocomotiveControlHelper.TrainBrake = value inside the engineer's main loop.</summary>
        internal static void SetTrainBrake(LocomotiveControlHelper control, float value, AutoEngineer ae)
        {
            try
            {
                if (value >= 0.99f && ae != null && Main.Active && Main.Settings.StopHoldFullService)
                {
                    float full = FullService(ae);
                    if (full < value)
                    {
                        if (!_logged && ae.Locomotive != null)
                        {
                            _logged = true;
                            Main.Mod.Logger.Log("Stop and hold: " + ae.Locomotive.DisplayName + " holds with a full service application (" + (full * 90f).ToString("0") + " psi) instead of emergency (logged once).");
                        }
                        value = full;
                    }
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
            control.TrainBrake = value;
        }

        private static float FullService(AutoEngineer ae)
        {
            try
            {
                object loop = FSpeedLoop?.GetValue(ae);
                object tb = loop != null ? BrakeTaps.FTrainBrake?.GetValue(loop) : null;
                if (tb != null && MMaxEffective != null)
                {
                    float m = Convert.ToSingle(MMaxEffective.Invoke(tb, null));
                    // A charged train gives about 0.29; the direction-stop limit (0.15) is only active above 2 mph, so it is not seen here.
                    if (m >= 0.15f && m < 0.99f) return m;
                }
            }
            catch (Exception) { }
            return FallbackFullService;
        }
    }

    [HarmonyPatch]
    internal static class AutoEngineer_Loop_Patch
    {
        private static MethodBase TargetMethod()
        {
            return StopHold.LoopIterator != null ? AccessTools.Method(StopHold.LoopIterator, "MoveNext") : null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var replacement = AccessTools.Method(typeof(StopHold), nameof(StopHold.SetTrainBrake));
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo m && m == StopHold.Setter)
                {
                    // Stack: control, value. Push the engineer (this.<>4__this) and call SetTrainBrake(control, value, ae).
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(ins.labels);
                    load.blocks.AddRange(ins.blocks);
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Ldfld, StopHold.FThis);
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    replaced++;
                    continue;
                }
                yield return ins;
            }
            Main.Mod?.Logger.Log("Stop and hold: " + replaced + " train brake write(s) in the engineer's loop go through the full-service cap.");
        }
    }
}
