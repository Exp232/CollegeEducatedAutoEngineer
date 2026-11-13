using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Model;
using Model.AI;
using Model.AI.SpeedControl.Rollout;
using Model.Physics;
using Railroader.Modding;
using Track.Signals;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Start-up self-check. The game can move things under us in any update, so before patching, every method,
    /// field and property a feature relies on is looked up with the exact signature we use. A feature with a
    /// missing target is switched off (in memory only; Settings.xml is not touched) and the rest still load.
    /// Each patch class is applied on its own, so one failing patch never stops the others.
    ///
    /// It does not guess renamed members, show anything in game, write files or throw: a failure is a log
    /// line and a disabled feature. Everything goes to the game log in one summary block.
    /// </summary>
    internal static class SelfCheck
    {
        internal const string GameRequirement = "rr/game >=0.6.0";

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private sealed class Feature
        {
            public string Name;
            public Func<bool> On;
            public Action TurnOff;
            public string[] Needs;
            public Type[] Patches;
            public string Status;
            /// <summary>Install the patches when the targets exist even while the feature is off, so the in-game window can switch it on. The patch must do nothing while the setting is off.</summary>
            public bool AlwaysPatch;
        }

        /// <summary>One line per feature (name and status) from the last check, shown in the settings window.</summary>
        internal static readonly List<string> Report = new List<string>();

        // ---- Targets ----

        private static readonly Type AE = typeof(AutoEngineer);
        private static readonly Type Targets = typeof(AutoEngineer.Targets);

        private static Dictionary<string, Func<bool>> Probes()
        {
            Type planner = SignalPlanning.PlannerType;
            Type element = AccessTools.Inner(typeof(IntegrationSet), "Element");
            return new Dictionary<string, Func<bool>>
            {
                ["AutoEngineer.HeadlightOn(bool), HeadlightDim()"] = () => Method(AE, "HeadlightOn", new[] { typeof(bool) }, typeof(void)) && Method(AE, "HeadlightDim", Type.EmptyTypes, typeof(void)),
                ["AutoEngineer.OnEnable()"] = () => Method(AE, "OnEnable", Type.EmptyTypes, typeof(void)),
                ["AutoEngineer.CalculateLookaheadDistance()"] = () => Method(AE, "CalculateLookaheadDistance", Type.EmptyTypes, typeof(float)),
                ["AutoEngineer.CalculateMaxVelocityToSlowToSpeedAtDistance(float, float, float totalAvailableBraking)"] =
                    () => Method(AE, "CalculateMaxVelocityToSlowToSpeedAtDistance", new[] { typeof(float), typeof(float), typeof(float) }, typeof(float), null, null, "totalAvailableBraking"),
                ["AutoEngineer.ContextualTargetVelocity()"] = () => Method(AE, "ContextualTargetVelocity", Type.EmptyTypes, typeof(float)),
                ["AutoEngineer.SetTargets(Targets)"] = () => Method(AE, "SetTargets", new[] { Targets }, typeof(void)),
                ["AutoEngineer.StartMovement()"] = () => Method(AE, "StartMovement", Type.EmptyTypes, typeof(IEnumerator)),
                ["AutoEngineer.AverageTrainBrakeCylinder()"] = () => Method(AE, "AverageTrainBrakeCylinder", Type.EmptyTypes, typeof(float)),
                ["AutoEngineer._targets"] = () => Field(AE, "_targets", Targets),
                ["AutoEngineer._control"] = () => Field(AE, "_control", null),
                ["AutoEngineer._lastState"] = () => Field(AE, "_lastState", null),
                ["AutoEngineer.CalculateThrottleForTargetVelocityStarting(float, float, float)"] = () => Method(AE, "CalculateThrottleForTargetVelocityStarting", new[] { typeof(float), typeof(float), typeof(float) }, typeof(float)),
                ["AutoEngineer._config"] = () => Field(AE, "_config", typeof(AutoEngineerConfig)),
                ["AutoEngineer._debugContextualTargetVelocity"] = () => Field(AE, "_debugContextualTargetVelocity", typeof(float)),
                ["Graph.GetPositionRotation(Location, ...), Graph.LocationToString(Location)"] =
                    () => GraphPositionRotationFound()
                        && AccessTools.Method(typeof(Track.Graph), "LocationToString", new[] { typeof(Track.Location) }) != null,
                ["Car.LocationF, Car.LocationR"] = () => Member(typeof(Car), "LocationF") && Member(typeof(Car), "LocationR"),
                ["OpsController.ClosestAreaForGamePosition(Vector3)"] =
                    () => OpsAreaLookupFound(),

                ["AutoEngineer.TrainBrake"] = () => BrakeGuidance.TrainBrakeType != null,
                ["AutoEngineer.TrainBrake._ae"] = () => BrakeGuidance.TrainBrakeType != null && Field(BrakeGuidance.TrainBrakeType, "_ae", typeof(AutoEngineer)),
                ["AutoEngineer.TrainBrake.CalculateMaxEffectiveApplication()"] =
                    () => BrakeGuidance.TrainBrakeType != null && Method(BrakeGuidance.TrainBrakeType, "CalculateMaxEffectiveApplication", Type.EmptyTypes, typeof(float)),
                ["RolloutController.WeightsFor(AutoEngineer)"] =
                    () => Method(typeof(RolloutController), "WeightsFor", new[] { typeof(AutoEngineer) }, typeof(RolloutScoreWeights)),
                ["AutoEngineer._speedControlLoop"] = () => Field(AE, "_speedControlLoop", null),
                ["AutoEngineer.SpeedControlLoop._rolloutController"] =
                    () => AccessTools.Inner(AE, "SpeedControlLoop") != null && Field(AccessTools.Inner(AE, "SpeedControlLoop"), "_rolloutController", typeof(RolloutController)),
                ["RolloutController.Result"] = () => AccessTools.PropertyGetter(typeof(RolloutController), "Result") != null,
                ["RolloutController.DescribeRootCosts()"] = () => Method(typeof(RolloutController), "DescribeRootCosts", Type.EmptyTypes, typeof(string)),

                ["Targets.Mode"] = () => Field(Targets, "Mode", typeof(Game.Messages.AutoEngineerMode)),
                ["Targets.MaxSpeedMph"] = () => Field(Targets, "MaxSpeedMph", typeof(float)),
                ["Targets.AllTargets"] = () => Field(Targets, "AllTargets", typeof(List<AutoEngineer.Targets.Target>)),
                ["Targets.AverageGradeUnder"] = () => Field(Targets, "AverageGradeUnder", typeof(float)),
                ["Targets.AverageGradeAhead"] = () => Field(Targets, "AverageGradeAhead", typeof(float)),
                ["Targets.NextSignal"] = () => Field(Targets, "NextSignal", typeof(CTCSignal)),
                ["AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)"] =
                    () => BrakeTaps.LoopType != null && AccessTools.Method(BrakeTaps.LoopType, "ApplyRolloutCommand") != null,
                ["AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel"] = () => BrakeTaps.Available,
                ["CTCAutoSignal.blocks"] = () => Field(typeof(CTCAutoSignal), "blocks", typeof(List<CTCBlock>)),
                ["CTCSignal.SetNeedsUpdate()"] = () => AccessTools.Method(typeof(CTCSignal), "SetNeedsUpdate") != null,
                ["CTCSignal.id, LastShownAspect, DisplayName"] =
                    () => Member(typeof(CTCSignal), "id") && Member(typeof(CTCSignal), "LastShownAspect") && Member(typeof(CTCSignal), "DisplayName"),

                ["IntegrationSet.Element"] = () => element != null,
                ["IntegrationSet.Element.car"] = () => element != null && (AccessTools.Field(element, "car") != null || AccessTools.Property(element, "car") != null),
                ["IntegrationSet.CalculateRetardingForce(Element, float absVelocity)"] =
                    () => element != null && Method(typeof(IntegrationSet), "CalculateRetardingForce", new[] { element, typeof(float) }, typeof(float), null, "absVelocity"),
                ["SteamEngine.UpdateConsumption(float)"] = () => Method(typeof(SteamEngine), "UpdateConsumption", new[] { typeof(float) }, typeof(void)),
                ["SteamEngine._estimatedGrateSqFt"] = () => Field(typeof(SteamEngine), "_estimatedGrateSqFt", typeof(float)),
                ["SteamEngine.WaterConsumptionRate (set)"] = () => AccessTools.PropertySetter(typeof(SteamEngine), "WaterConsumptionRate") != null,
                ["SteamEngine.CoalConsumptionRate (set)"] = () => AccessTools.PropertySetter(typeof(SteamEngine), "CoalConsumptionRate") != null,
                ["SteamLocomotive.PeriodicUpdate(float dt)"] = () => Method(typeof(SteamLocomotive), "PeriodicUpdate", new[] { typeof(float) }, typeof(void), "dt"),
                ["Effects.FuseeRenderer.Update()"] = () => Method(typeof(Effects.FuseeRenderer), "Update", Type.EmptyTypes, typeof(void)),
                ["DieselLocomotive.PeriodicUpdate(float dt)"] = () => Method(typeof(DieselLocomotive), "PeriodicUpdate", new[] { typeof(float) }, typeof(void), "dt"),
                ["SteamLocomotive.CutoffSettingForVelocity(float velocityMps)"] =
                    () => Method(typeof(SteamLocomotive), "CutoffSettingForVelocity", new[] { typeof(float) }, typeof(float), "velocityMps"),

                ["SteamLocomotive.FuelCar()"] = () => Method(typeof(SteamLocomotive), "FuelCar", Type.EmptyTypes, typeof(Car)),
                ["AutoEngineer.SumGravityForce()"] = () => Method(AE, "SumGravityForce", Type.EmptyTypes, typeof(float)),
                ["AutoEngineerPlanner"] = () => planner != null,
                ["AutoEngineerPlanner.Say(string)"] = () => planner != null && Method(planner, "Say", new[] { typeof(string) }, typeof(void)),
                ["AutoEngineerPlanner.SetPlannerStatus(float, string, List<Target>)"] =
                    () => planner != null && Method(planner, "SetPlannerStatus", new[] { typeof(float), typeof(string), typeof(List<AutoEngineer.Targets.Target>) }, typeof(void)),
                ["AutoEngineerPlanner._engineer, _locomotive, _coupledCarsCached, _equipmentMaximumTrackCurvature, _maximumLength"] =
                    () => planner != null && Field(planner, "_engineer", typeof(AutoEngineer)) && Field(planner, "_locomotive", typeof(BaseLocomotive))
                        && Field(planner, "_coupledCarsCached", typeof(List<Car>)) && Field(planner, "_equipmentMaximumTrackCurvature", typeof(float)) && Field(planner, "_maximumLength", typeof(float)),
                ["TrackSegment.speedLimit, Graph.AllSegments, Graph.SegmentsConnectedTo(TrackNode)"] =
                    () => Field(typeof(Track.TrackSegment), "speedLimit", typeof(int)) && AccessTools.PropertyGetter(typeof(Track.Graph), "AllSegments") != null
                        && AccessTools.Method(typeof(Track.Graph), "SegmentsConnectedTo", new[] { typeof(Track.TrackNode) }) != null,
                ["TrackSpan.GetSegments()"] = () => AccessTools.Method(typeof(Track.TrackSpan), "GetSegments", Type.EmptyTypes) != null,
                ["SignController.RemoveSignsWithPrefix(string)"] =
                    () => AccessTools.Method(typeof(Game.Mow.SignController), "RemoveSignsWithPrefix", new[] { typeof(string) }) != null,
                ["AutoEngineer.WaitingForBrakes"] = () => AccessTools.PropertyGetter(AE, "WaitingForBrakes") != null,
                ["AutoEngineerControlSetBase.UpdateStatusLabel(), statusLabel"] =
                    () => Method(typeof(UI.EngineControls.AutoEngineerControlSetBase), "UpdateStatusLabel", Type.EmptyTypes, typeof(void))
                        && Field(typeof(UI.EngineControls.AutoEngineerControlSetBase), "statusLabel", null),
                ["AutoEngineerPlanner.UpdateTargets(float, int)"] =
                    () => planner != null && Method(planner, "UpdateTargets", new[] { typeof(float), typeof(int) }, typeof(void)),
                ["AutoEngineerPlanner.DistanceToNextSignalAfter(..., float) : float?"] =
                    () => planner != null && planner.GetMethods(All).Any(m => m.Name == "DistanceToNextSignalAfter"
                        && m.ReturnType == typeof(float?) && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(float)),
            };
        }

        // ---- Features ----

        private static List<Feature> Features()
        {
            var s = Main.Settings;

            return new List<Feature>
            {
                new Feature
                {
                    Name = "Throttle smoothing",
                    AlwaysPatch = true,
                    On = () => s.SmoothThrottle,
                    TurnOff = () => s.SmoothThrottle = false,
                    Needs = new[] { "AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)", "AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel", "AutoEngineer._control", "AutoEngineer._debugContextualTargetVelocity", "AutoEngineer.OnEnable()" },
                    Patches = new[] { typeof(SpeedControlLoop_ApplyRolloutCommand_Patch), typeof(AutoEngineer_OnEnable_Patch) },
                },
                new Feature
                {
                    Name = "No power into reductions",
                    AlwaysPatch = true,
                    On = () => s.AvoidPowerIntoReductions,
                    TurnOff = () => s.AvoidPowerIntoReductions = false,
                    Needs = new[] { "AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)", "AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel", "AutoEngineer._targets", "AutoEngineer._debugContextualTargetVelocity", "Targets.AllTargets", "Targets.AverageGradeUnder" },
                    Patches = new[] { typeof(SpeedControlLoop_ApplyRolloutCommand_Patch) },
                },
                new Feature
                {
                    Name = "Stay stopped at stop targets",
                    On = () => s.HoldStopAtStopTargets,
                    TurnOff = () => s.HoldStopAtStopTargets = false,
                    Needs = new[] { "AutoEngineer.ContextualTargetVelocity()", "AutoEngineer._targets", "Targets.AllTargets", "Targets.Mode" },
                    Patches = new[] { typeof(AutoEngineer_ContextualTargetVelocity_Patch) },
                },
                new Feature
                {
                    Name = "Stall guard",
                    AlwaysPatch = true,
                    On = () => s.StallGuard,
                    TurnOff = () => s.StallGuard = false,
                    Needs = new[] { "AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)", "AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel", "AutoEngineer.StartMovement()", "AutoEngineer.SetTargets(Targets)", "AutoEngineer._targets", "AutoEngineer._control", "Targets.Mode", "Targets.MaxSpeedMph", "Targets.AverageGradeUnder", "AutoEngineerPlanner.Say(string)" },
                    Patches = new[] { typeof(SpeedControlLoop_ApplyRolloutCommand_Patch), typeof(AutoEngineer_OnEnable_Patch), typeof(AutoEngineer_StartMovement_Patch), typeof(AutoEngineer_SetTargets_Patch) },
                },
                new Feature
                {
                    Name = "Signal braking",
                    On = () => s.ApproachTargetAtSignal || s.HoldApproachSpeed || s.SignalSightMeters > SignalPlanning.StockSightMeters || (s.ApproachNoNextSignalMph >= 5f && s.ApproachNoNextSignalMph < 25f),
                    TurnOff = () => { s.ApproachTargetAtSignal = false; s.HoldApproachSpeed = false; s.SignalSightMeters = SignalPlanning.StockSightMeters; s.ApproachNoNextSignalMph = 25f; },
                    Needs = new[] { "AutoEngineerPlanner", "AutoEngineerPlanner.UpdateTargets(float, int)", "AutoEngineerPlanner.DistanceToNextSignalAfter(..., float) : float?", "AutoEngineer.SetTargets(Targets)", "Targets.AllTargets" },
                    Patches = new[] { typeof(Planner_UpdateTargets_Patch), typeof(Planner_DistanceToNextSignalAfter_Patch), typeof(AutoEngineer_SetTargets_Patch) },
                },
                new Feature
                {
                    Name = "Early slowing for lower limits",
                    AlwaysPatch = true,
                    On = () => s.SpeedReductionLeadSeconds > 0f,
                    TurnOff = () => s.SpeedReductionLeadSeconds = 0f,
                    Needs = new[] { "AutoEngineer.SetTargets(Targets)", "Targets.AllTargets", "Targets.MaxSpeedMph" },
                    Patches = new[] { typeof(AutoEngineer_SetTargets_Patch) },
                },
                new Feature
                {
                    Name = "Shorter cutoff",
                    On = () => s.CutoffEnabled,
                    TurnOff = () => s.CutoffEnabled = false,
                    Needs = new[] { "SteamLocomotive.CutoffSettingForVelocity(float velocityMps)", "AutoEngineer._targets", "Targets.AverageGradeUnder", "Targets.AverageGradeAhead" },
                    Patches = new[] { typeof(SteamLocomotive_CutoffSettingForVelocity_Patch) },
                },
                new Feature
                {
                    Name = "Planning",
                    On = () => s.LookaheadMultiplier > 1.001f || s.BrakePlanningFraction < 0.999f,
                    TurnOff = () => { s.LookaheadMultiplier = 1f; s.BrakePlanningFraction = 1f; },
                    Needs = new[] { "AutoEngineer.CalculateLookaheadDistance()", "AutoEngineer.CalculateMaxVelocityToSlowToSpeedAtDistance(float, float, float totalAvailableBraking)" },
                    Patches = new[] { typeof(AutoEngineer_CalculateLookaheadDistance_Patch), typeof(AutoEngineer_CalculateMaxVelocityToSlowToSpeedAtDistance_Patch) },
                },
                new Feature
                {
                    Name = "Gentle direction-change stop",
                    On = () => s.DirectionStopBrakeLimit > 0f,
                    TurnOff = () => s.DirectionStopBrakeLimit = 0f,
                    Needs = new[] { "AutoEngineer.TrainBrake", "AutoEngineer.TrainBrake._ae", "AutoEngineer.TrainBrake.CalculateMaxEffectiveApplication()", "AutoEngineer._targets", "Targets.MaxSpeedMph", "Targets.AllTargets" },
                    Patches = new[] { typeof(TrainBrake_CalculateMaxEffectiveApplication_Patch) },
                },
                new Feature
                {
                    Name = "Direction stop with the train brake",
                    AlwaysPatch = true,
                    On = () => s.DirectionStopTrainBrakeOnly,
                    TurnOff = () => s.DirectionStopTrainBrakeOnly = false,
                    Needs = new[] { "AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)", "AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel", "AutoEngineer._targets", "Targets.MaxSpeedMph", "Targets.AllTargets" },
                    Patches = new[] { typeof(SpeedControlLoop_ApplyRolloutCommand_Patch) },
                },
                new Feature
                {
                    Name = "Planner weights",
                    AlwaysPatch = true,
                    On = () => s.PlanOverspeedScale != 1f || s.PlanUndershootScale != 1f || s.PlanUndershootDeadbandScale != 1f || s.PlanCeilingScale != 1f || s.PlanIndependentUseScale != 1f,
                    TurnOff = () => { s.PlanOverspeedScale = 1f; s.PlanUndershootScale = 1f; s.PlanUndershootDeadbandScale = 1f; s.PlanCeilingScale = 1f; s.PlanIndependentUseScale = 1f; },
                    Needs = new[] { "RolloutController.WeightsFor(AutoEngineer)" },
                    Patches = new[] { typeof(RolloutController_WeightsFor_Patch) },
                },
                new Feature
                {
                    Name = "Starting resistance",
                    AlwaysPatch = true,
                    On = () => s.StartingResistance,
                    TurnOff = () => s.StartingResistance = false,
                    Needs = new[] { "IntegrationSet.CalculateRetardingForce(Element, float absVelocity)", "IntegrationSet.Element", "IntegrationSet.Element.car" },
                    Patches = new[] { typeof(IntegrationSet_CalculateRetardingForce_Patch) },
                },
                new Feature
                {
                    Name = "Realistic fuel",
                    AlwaysPatch = true,
                    On = () => s.RealisticFuel,
                    TurnOff = () => s.RealisticFuel = false,
                    Needs = new[] { "SteamEngine.UpdateConsumption(float)", "SteamEngine._estimatedGrateSqFt", "SteamEngine.WaterConsumptionRate (set)", "SteamEngine.CoalConsumptionRate (set)" },
                    Patches = new[] { typeof(SteamEngine_UpdateConsumption_Patch) },
                },
                new Feature
                {
                    Name = "Decision logging",
                    On = () => s.LogEnabled && s.LogDecisions,
                    TurnOff = () => s.LogDecisions = false,
                    Needs = new[] { "AutoEngineer._speedControlLoop", "AutoEngineer.SpeedControlLoop._rolloutController", "RolloutController.Result", "RolloutController.DescribeRootCosts()" },
                    Patches = new Type[0],
                },
                new Feature
                {
                    Name = "Fuel use in fast-forward",
                    AlwaysPatch = true,
                    On = () => s.FuelFollowsTimeWarp,
                    TurnOff = () => s.FuelFollowsTimeWarp = false,
                    Needs = new[] { "SteamLocomotive.PeriodicUpdate(float dt)", "DieselLocomotive.PeriodicUpdate(float dt)" },
                    Patches = new[] { typeof(SteamLocomotive_PeriodicUpdate_Patch), typeof(DieselLocomotive_PeriodicUpdate_Patch) },
                },
                new Feature
                {
                    Name = "Water stop (Road mode)",
                    AlwaysPatch = true,
                    On = () => s.WaterStop,
                    TurnOff = () => s.WaterStop = false,
                    Needs = new[] { "AutoEngineer.OnEnable()", "SteamLocomotive.FuelCar()", "AutoEngineerPlanner.Say(string)" },
                    Patches = new[] { typeof(AutoEngineer_OnEnable_Patch) },
                },
                new Feature
                {
                    Name = "Hill start spool-up",
                    AlwaysPatch = true,
                    On = () => s.HillStartSpoolUp,
                    TurnOff = () => s.HillStartSpoolUp = false,
                    Needs = new[] { "AutoEngineer.WaitingForBrakes", "AutoEngineer.ContextualTargetVelocity()", "AutoEngineer.CalculateThrottleForTargetVelocityStarting(float, float, float)", "AutoEngineer._targets", "AutoEngineer._control", "Targets.AverageGradeUnder" },
                    Patches = new Type[0],
                },
                new Feature
                {
                    Name = "Direction change pause",
                    AlwaysPatch = true,
                    On = () => s.DirectionChangePauseSeconds > 0f,
                    TurnOff = () => s.DirectionChangePauseSeconds = 0f,
                    Needs = new[] { "AutoEngineer.StartMovement()", "AutoEngineer._targets", "AutoEngineer._control", "AutoEngineer._lastState", "Targets.AllTargets", "Targets.MaxSpeedMph" },
                    Patches = new[] { typeof(AutoEngineer_StartMovement_Patch) },
                },
                new Feature
                {
                    Name = "Observation car headlight",
                    AlwaysPatch = true,
                    On = () => s.ObservationHeadlight,
                    TurnOff = () => s.ObservationHeadlight = false,
                    Needs = new[] { "AutoEngineer.HeadlightOn(bool), HeadlightDim()" },
                    Patches = new[] { typeof(AutoEngineer_HeadlightOn_Patch), typeof(AutoEngineer_HeadlightDim_Patch) },
                },
                new Feature
                {
                    Name = "Fusee without camera",
                    AlwaysPatch = true,
                    On = () => s.GuardFuseeCamera,
                    TurnOff = () => s.GuardFuseeCamera = false,
                    Needs = new[] { "Effects.FuseeRenderer.Update()" },
                    Patches = new[] { typeof(FuseeRenderer_Update_Patch) },
                },
                new Feature
                {
                    Name = "Brake tap suppression",
                    AlwaysPatch = true,
                    On = () => s.BrakeTapSuppressMph > 0f,
                    TurnOff = () => s.BrakeTapSuppressMph = 0f,
                    Needs = new[] { "AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)", "AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel", "AutoEngineer.AverageTrainBrakeCylinder()", "AutoEngineer._targets", "AutoEngineer._debugContextualTargetVelocity", "Targets.Mode", "Targets.AllTargets" },
                    Patches = new[] { typeof(SpeedControlLoop_ApplyRolloutCommand_Patch) },
                },
                new Feature
                {
                    Name = "Legacy speed limits",
                    AlwaysPatch = true,
                    On = () => s.LegacySpeedLimits,
                    TurnOff = () => s.LegacySpeedLimits = false,
                    Needs = new[] { "TrackSegment.speedLimit, Graph.AllSegments, Graph.SegmentsConnectedTo(TrackNode)", "TrackSpan.GetSegments()", "SignController.RemoveSignsWithPrefix(string)" },
                    Patches = new Type[0],
                },
                new Feature
                {
                    Name = "Detailed status",
                    AlwaysPatch = true,
                    On = () => s.DetailedStatus,
                    TurnOff = () => s.DetailedStatus = false,
                    Needs = new[] { "AutoEngineerPlanner.SetPlannerStatus(float, string, List<Target>)", "AutoEngineerPlanner._engineer, _locomotive, _coupledCarsCached, _equipmentMaximumTrackCurvature, _maximumLength", "AutoEngineer.WaitingForBrakes", "AutoEngineerControlSetBase.UpdateStatusLabel(), statusLabel" },
                    Patches = new[] { typeof(Planner_SetPlannerStatus_Patch), typeof(ControlSet_UpdateStatusLabel_Patch) },
                },
                new Feature
                {
                    Name = "Coast band",
                    AlwaysPatch = true,
                    On = () => s.CoastBandMph > 0f,
                    TurnOff = () => s.CoastBandMph = 0f,
                    Needs = new[] { "AutoEngineer.SpeedControlLoop.ApplyRolloutCommand(in RolloutCommand)", "AutoEngineer.SpeedControlLoop._ae, _trainBrake, _measuredAccel", "AutoEngineer._targets", "AutoEngineer._debugContextualTargetVelocity", "Targets.Mode", "Targets.AllTargets", "AutoEngineer.SumGravityForce()" },
                    Patches = new[] { typeof(SpeedControlLoop_ApplyRolloutCommand_Patch) },
                },
                new Feature
                {
                    Name = "CTC block fix",
                    On = () => !string.IsNullOrWhiteSpace(s.CtcDropBlocks),
                    TurnOff = () => s.CtcDropBlocks = "",
                    Needs = new[] { "AutoEngineer.OnEnable()", "CTCAutoSignal.blocks", "CTCSignal.SetNeedsUpdate()" },
                    Patches = new[] { typeof(AutoEngineer_OnEnable_Patch) },
                },
                new Feature
                {
                    Name = "Signal logging",
                    On = () => s.LogEnabled && s.LogSignals,
                    TurnOff = () => s.LogSignals = false,
                    Needs = new[] { "AutoEngineer.SetTargets(Targets)", "AutoEngineer._targets", "Targets.AllTargets", "Targets.NextSignal", "CTCSignal.id, LastShownAspect, DisplayName" },
                    Patches = new[] { typeof(AutoEngineer_SetTargets_Patch) },
                },
                new Feature
                {
                    Name = "Position and G-force logging",
                    On = () => s.LogEnabled && s.LogGForces,
                    TurnOff = () => s.LogGForces = false,
                    Needs = new[] { "Graph.GetPositionRotation(Location, ...), Graph.LocationToString(Location)", "Car.LocationF, Car.LocationR", "OpsController.ClosestAreaForGamePosition(Vector3)" },
                    Patches = new Type[0],
                },
                new Feature
                {
                    Name = "Diagnostic logger",
                    On = () => s.LogEnabled,
                    TurnOff = () => s.LogEnabled = false,
                    Needs = new[] { "AutoEngineer.OnEnable()", "AutoEngineer._control", "AutoEngineer._targets", "AutoEngineer._debugContextualTargetVelocity" },
                    Patches = new[] { typeof(AutoEngineer_OnEnable_Patch) },
                },
            };
        }

        // ---- Run ----

        /// <summary>Checks, patches and logs. Returns false when nothing was applied.</summary>
        internal static bool Run(IModContext context, Harmony harmony)
        {
            var log = new StringBuilder();
            log.AppendLine($"College Educated Auto Engineer {Main.Mod.Info.Version} self-check, game {UnityEngine.Application.version}");

            bool gameOk;
            try { gameOk = context.CheckDependency(GameRequirement); }
            catch (Exception e) { gameOk = false; log.AppendLine("  Game version check failed: " + e.Message); }
            if (!gameOk)
            {
                context.Log.Error(log + $"  Needs {GameRequirement}. Nothing was applied.");
                return false;
            }

            // 1. Probe every target.
            var found = new Dictionary<string, bool>();
            foreach (var probe in Probes())
            {
                bool ok;
                try { ok = probe.Value(); }
                catch (Exception) { ok = false; } // e.g. an ambiguous match counts as not found
                found[probe.Key] = ok;
            }

            // 2. Decide which features can run.
            var features = Features();
            var wanted = new List<Type>();
            foreach (var f in features)
            {
                string[] missing = f.Needs.Where(n => !found.TryGetValue(n, out var okN) || !okN).ToArray();
                if (!f.On())
                {
                    if (f.AlwaysPatch && missing.Length == 0)
                    {
                        f.Status = "off (can be switched on in the settings window)";
                        foreach (var p in f.Patches) if (!wanted.Contains(p)) wanted.Add(p);
                    }
                    else
                    {
                        f.Status = "disabled in settings";
                    }
                }
                else if (missing.Length > 0)
                {
                    f.Status = "DISABLED, not found: " + string.Join(", ", missing);
                    f.TurnOff();
                }
                else
                {
                    f.Status = "enabled";
                    foreach (var p in f.Patches) if (!wanted.Contains(p)) wanted.Add(p);
                }
            }

            // 3. Apply each patch class on its own.
            var failed = new Dictionary<Type, string>();
            int applied = 0;
            foreach (var patch in wanted)
            {
                try
                {
                    harmony.CreateClassProcessor(patch).Patch();
                    applied++;
                }
                catch (Exception e)
                {
                    failed[patch] = (e.InnerException ?? e).Message;
                }
            }
            foreach (var f in features.Where(f => f.Status == "enabled"))
            {
                var bad = f.Patches.FirstOrDefault(failed.ContainsKey);
                if (bad == null) continue;
                f.Status = "DISABLED, patch " + bad.Name + " failed: " + failed[bad];
                f.TurnOff();
            }

            foreach (var f in features) log.AppendLine($"  {f.Name}: {f.Status}");
            Report.Clear();
            foreach (var f in features) Report.Add(f.Name + ": " + f.Status);

            // 4. Verify: every applied patch class targets exactly one method.
            int patched = harmony.GetPatchedMethods().Count();
            bool countOk = patched == applied;
            log.AppendLine($"  Patched methods: {patched} (expected {applied})");

            log.AppendLine("  Effective settings: " + DescribeSettings(Main.Settings));

            bool anyDisabled = features.Any(f => f.Status.StartsWith("DISABLED"));
            string text = log.ToString().TrimEnd();
            if (anyDisabled || !countOk) context.Log.Warning(text);
            else context.Log.Info(text);
            return true;
        }

        // ---- Helpers ----

        /// <summary>Exact match: declared on the type, same parameter types and return type; names checked where the patch binds by name.</summary>
        private static bool Method(Type type, string name, Type[] args, Type returns, params string[] paramNames)
        {
            MethodInfo m = type.GetMethod(name, All, null, args, null);
            if (m == null || m.ReturnType != returns) return false;
            var ps = m.GetParameters();
            for (int i = 0; i < paramNames.Length && i < ps.Length; i++)
            {
                if (paramNames[i] != null && ps[i].Name != paramNames[i]) return false;
            }
            return true;
        }

        /// <summary>Graph.GetPositionRotation(Location loc, PositionAccuracy accuracy = Standard): the optional second parameter is part of the signature, so the method is found by its first parameter.</summary>
        private static bool GraphPositionRotationFound()
        {
            foreach (MethodInfo m in typeof(Track.Graph).GetMethods(All))
            {
                if (m.Name != "GetPositionRotation" || m.ReturnType != typeof(Track.Graph.PositionRotation)) continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(Track.Location)) return true;
            }
            return false;
        }

        /// <summary>OpsController.ClosestAreaForGamePosition(Vector3 ...): found by name and first parameter, so an optional extra parameter does not hide it.</summary>
        private static bool OpsAreaLookupFound()
        {
            foreach (MethodInfo m in typeof(Model.Ops.OpsController).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "ClosestAreaForGamePosition") continue;
                var ps = m.GetParameters();
                if (ps.Length >= 1 && ps[0].ParameterType == typeof(UnityEngine.Vector3)) return true;
            }
            return false;
        }

        private static bool Member(Type type, string name)
        {
            return AccessTools.Field(type, name) != null || AccessTools.Property(type, name) != null;
        }

        private static bool Field(Type type, string name, Type fieldType)
        {
            FieldInfo f = AccessTools.Field(type, name);
            return f != null && (fieldType == null || f.FieldType == fieldType);
        }

        private static string DescribeSettings(Settings s)
        {
            return string.Join(", ", typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Select(f => f.Name + "=" + Convert.ToString(f.GetValue(s), CultureInfo.InvariantCulture)));
        }
    }
}
