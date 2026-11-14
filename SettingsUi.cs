using System;
using TMPro;
using UI.Builder;
using UI.Windows;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// The mod's in-game settings window, built with the game's own panel builder (the same one its windows use) and
    /// opened with a hotkey (Ctrl+F8 by default; see SettingsHotkey in Settings.xml). Sliders change the live
    /// settings at once and every change is written back to Settings.xml. Only settings that are read while the
    /// game runs are offered here; the ones that choose which patches are installed still need a restart.
    /// </summary>
    internal static class SettingsUi
    {
        internal const string WindowId = "exp232.autoengineerbrakefix.settings";

        internal static void Toggle()
        {
            var windows = WindowManager.Shared;
            if (windows == null) return;   // main menu: the window system is not up yet
            EnsureRegistered(windows);
            windows.ToggleWindow(WindowId);
        }

        // The game forgets custom windows when a map unloads, so register again whenever the definition is gone.
        private static void EnsureRegistered(WindowManager windows)
        {
            if (windows.HasDefinition(WindowId)) return;
            windows.RegisterCustomWindow(new CustomWindowDefinition
            {
                Identifier = WindowId,
                Title = "College Educated Auto Engineer " + Main.Mod.Info.Version,
                Config = WindowConfig.Resizable(new Vector2Int(480, 640), new Vector2Int(380, 260)),
                BuilderAction = Build,
            });
        }

        // The window as the mod opens it itself: the page inside a scroll view.
        private static void Build(UIPanelBuilder builder)
        {
            try
            {
                builder.VScrollView(BuildPage);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }

        /// <summary>The page itself, without a scroll view, so it also fits into the Mod Settings Hub window.</summary>
        internal static void BuildPage(UIPanelBuilder page)
        {
            try
            {
                var s = Main.Settings;
                page.AddTitle("College Educated Auto Engineer", "Version " + Main.Mod.Info.Version);

                page.AddSection("Throttle", b =>
                {
                    b.AddFieldToggle("Throttle smoothing", () => s.SmoothThrottle, v => { s.SmoothThrottle = v; Save(); });
                    Note(b, "Moves the throttle lever like a hand on the regulator. A brake application or overspeed closes it at once.");
                    b.AddFieldToggle("Glide between updates", () => s.GlideThrottle, v => { s.GlideThrottle = v; Save(); });
                    b.AddFieldToggle("Smooth diesel throttle", () => s.SmoothDieselThrottle, v => { s.SmoothDieselThrottle = v; Save(); });
                    Note(b, "Switch off if another mod already smooths the diesel notch stepping (for example a diesel realism mod); two smoothers together can stop the engineer notching up.");
                    b.AddFieldToggle("Diesel notch guard", () => s.DieselNotchGuard, v => { s.DieselNotchGuard = v; Save(); });
                    Note(b, "If another mod rounds a diesel's throttle to the nearest notch, smoothing is switched off for that locomotive so it can still notch up.");
                });

                page.AddSection("Braking and speed limits", b =>
                {
                    Slider(b, "Brake tap limit", () => s.BrakeTapSuppressMph, v => s.BrakeTapSuppressMph = v, 0f, 5f, 0.5f,
                        v => v <= 0f ? "off" : v.ToString("0.0") + " mph");
                    Note(b, "When the train is no more than this far over its target and not speeding up, a first brake application becomes a coast.");
                    Slider(b, "Coast band", () => s.CoastBandMph, v => s.CoastBandMph = v, 0f, 3f, 0.5f,
                        v => v <= 0f ? "off" : v.ToString("0.0") + " mph");
                    Note(b, "Closes the throttle when coasting holds the speed this close under the target, and waits to brake until the train is this far over.");
                    Slider(b, "Slowing lead", () => s.SpeedReductionLeadSeconds, v => s.SpeedReductionLeadSeconds = v, 0f, 15f, 1f,
                        v => v <= 0f ? "off" : v.ToString("0") + " s");
                    Note(b, "How many seconds before a lower limit the engineer plans to be at that speed.");
                    b.AddFieldToggle("No power into reductions", () => s.AvoidPowerIntoReductions, v => { s.AvoidPowerIntoReductions = v; Save(); });
                    Note(b, "Closes the throttle when a lower limit is in sight and the train is already at that speed. Not applied on steep climbs.");
                    b.AddFieldToggle("Stop with the train brake", () => s.DirectionStopTrainBrakeOnly, v => { s.DirectionStopTrainBrakeOnly = v; Save(); });
                    Note(b, "Stopping a moving train with no speed order (changing direction, or a speed of 0) uses the train brake only; the locomotive brake holds the train once it is nearly stopped.");
                    Slider(b, "Direction change pause", () => s.DirectionChangePauseSeconds, v => s.DirectionChangePauseSeconds = v, 0f, 15f, 1f,
                        v => v <= 0f ? "off" : v.ToString("0") + " s");
                    Note(b, "When the engineer is told to run the other way, it stands still at least this long (brakes held, throttle closed) before it moves off. A stop blast is blown first when the game skipped it.");
                    b.AddFieldToggle("Blast before reversing", () => s.DirectionChangeHorn, v => { s.DirectionChangeHorn = v; Save(); });
                    b.AddFieldToggle("Hill start spool-up", () => s.HillStartSpoolUp, v => { s.HillStartSpoolUp = v; Save(); });
                    Note(b, "Starting on an upgrade, the throttle is raised while the brakes release, so the locomotive pulls the moment the locomotive brake lets go instead of rolling back while it spools up.");
                    Slider(b, "Locomotive brake use", () => s.PlanIndependentUseScale, v => s.PlanIndependentUseScale = v, 0f, 5f, 0.25f, Weight);
                    Note(b, "How much the planner dislikes using the locomotive brake. Higher = it slows with the train brake instead; 1 = the game's value.");
                });

                page.AddSection("Train preparation", b =>
                {
                    b.AddFieldToggle("Prepare train on new orders", () => s.PrepTrain, v => { s.PrepTrain = v; Save(); });
                    b.AddFieldToggle("Doors at station stops", () => s.DoorsAtStations, v => { s.DoorsAtStations = v; Save(); });
                    b.AddFieldToggle("Observation car headlight", () => s.ObservationHeadlight, v => { s.ObservationHeadlight = v; Save(); });
                    b.AddFieldToggle("Marker lamps (red at the rear)", () => s.AutoMarkers, v => { s.AutoMarkers = v; Save(); });
                    Note(b, "When a standing train gets a new order to move (Road, Yard or Waypoint): releases the handbrakes, connects the air between coupled cars with the anglecocks open, and closes the anglecocks at the two ends of the train. Doors and gates open at station stops, an observation car that leads the train lights its headlight in place of the locomotive's, and the marker lamp at the rear of the train shows red. Class lights are left to you.");
                });

                page.AddSection("Stall guard", b =>
                {
                    b.AddFieldToggle("Stall guard", () => s.StallGuard, v => { s.StallGuard = v; Save(); });
                    Note(b, "A train that cannot climb a grade, get moving or keeps rolling back is held with the brakes and the engineer says so. In Waypoint and Road modes it also sets the speed to 0 mph; set a speed again to retry.");
                });

                page.AddSection("Starting resistance", b =>
                {
                    b.AddFieldToggle("Starting resistance", () => s.StartingResistance, v => { s.StartingResistance = v; Save(); });
                    Slider(b, "Fades out by", () => s.StartingResistanceFadeMph, v => s.StartingResistanceFadeMph = v, 0.5f, 6f, 0.5f,
                        v => v.ToString("0.0") + " mph");
                    Note(b, "The game's resistance only covers a moving train. This adds a standing-start drag (7 lb/ton) that falls to 37% at this speed.");
                });

                page.AddSection("Fuel and water", b =>
                {
                    b.AddFieldToggle("Realistic fuel", () => s.RealisticFuel, v => { s.RealisticFuel = v; Save(); });
                    Note(b, "Steam and coal follow the engine's own power instead of the game's speed-based curve. Off = the game's own use.");
                    b.AddFieldToggle("Water stop (Road mode)", () => s.WaterStop, v => { s.WaterStop = v; Save(); });
                    Slider(b, "Stop below", () => s.WaterStopPercent, v => s.WaterStopPercent = v, 10f, 60f, 5f,
                        v => v.ToString("0") + " % water");
                    Note(b, "When the tender runs low, a Road-mode engineer takes water at the next column ahead on the lined route.");
                    b.AddFieldToggle("Fuel in fast-forward", () => s.FuelFollowsTimeWarp, v => { s.FuelFollowsTimeWarp = v; Save(); });
                    Note(b, "Charges the real game time in fast-forward; the game charges one second per update.");
                });

                page.AddSection("Speed limits and status", b =>
                {
                    b.AddFieldToggle("Fix legacy limits", () => s.LegacySpeedLimits, v => { s.LegacySpeedLimits = v; Save(); });
                    Note(b, "The game still enforces the map's old per-track limits but no longer shows them. On: they are cleared on the mainline and kept on branches, yards and spurs. Takes a few seconds.");
                    b.AddFieldToggle("Detailed status", () => s.DetailedStatus, v => { s.DetailedStatus = v; Save(); });
                    Note(b, "The status line names what sets the speed (limit, track class, curve, equipment, water stop).");
                    b.AddFieldToggle("Metric distances", () => s.StatusMetric, v => { s.StatusMetric = v; Save(); });
                    b.AddFieldToggle("Fusee in fast-forward", () => s.GuardFuseeCamera, v => { s.GuardFuseeCamera = v; Save(); });
                    Note(b, "Skips the game's fusee update while the camera is off in fast-forward, which would otherwise throw an error every frame.");
                });

                page.AddSection("Diagnostics and testing (off by default)", b =>
                {
                    b.AddFieldToggle("Diagnostic log", () => s.LogEnabled, v => { s.LogEnabled = v; Save(); });
                    Note(b, "Writes logs for tuning and bug reports to the mod's logs folder. A change applies the next time the engineer is switched on. Off = nothing is written.");
                    b.AddFieldToggle("Decisions", () => s.LogDecisions, v => { s.LogDecisions = v; Save(); });
                    b.AddFieldToggle("Signals", () => s.LogSignals, v => { s.LogSignals = v; Save(); });
                    b.AddFieldToggle("CTC dump", () => s.LogCtc, v => { s.LogCtc = v; Save(); });
                    b.AddFieldToggle("Fuel summary", () => s.LogFuelSummary, v => { s.LogFuelSummary = v; Save(); });
                    b.AddFieldToggle("Tender coal and water", () => s.LogFuel, v => { s.LogFuel = v; Save(); });
                    b.AddFieldToggle("Position and G forces", () => s.LogGForces, v => { s.LogGForces = v; Save(); });
                    Note(b, "Adds the locomotive's map position, track location and nearest area, and the G forces on it (forward, right, up).");
                    Note(b, "The sub-options only matter while the diagnostic log is on.");
                });

                page.AddSection("", b =>
                {
                    b.AddButton("Reset these to defaults", ResetDefaults);
                    Note(b, "Saved to Settings.xml as you change them. Other settings, and anything that switches a feature on or off at start-up, are edited in that file and need a restart.");
                });

                page.AddSection("Self-check", b =>
                {
                    foreach (string line in SelfCheck.Report) Note(b, line);
                });
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }

        // Explanatory text: smaller and dimmer than the controls, so the sliders stay in view.
        private static void Note(UIPanelBuilder b, string text)
        {
            b.AddLabel(text, (TMP_Text t) =>
            {
                t.fontSize *= 0.75f;
                t.alpha = 0.8f;
            });
        }

        private static string Weight(float v)
        {
            return v.ToString("0.00");
        }

        private static void Slider(UIPanelBuilder b, string label, Func<float> get, Action<float> set, float min, float max, float step, Func<float, string> format)
        {
            b.AddField(label, b.AddSliderQuantized(
                get,
                () => format(get()),
                v => set(Mathf.Clamp(Mathf.Round(v / step) * step, min, max)),
                step, min, max,
                v => Save()));
        }

        private static void ResetDefaults()
        {
            var d = new Settings();
            var s = Main.Settings;
            s.BrakeTapSuppressMph = d.BrakeTapSuppressMph;
            s.CoastBandMph = d.CoastBandMph;
            s.AvoidPowerIntoReductions = d.AvoidPowerIntoReductions;
            s.PrepTrain = d.PrepTrain;
            s.DoorsAtStations = d.DoorsAtStations;
            s.ObservationHeadlight = d.ObservationHeadlight;
            s.AutoMarkers = d.AutoMarkers;
            s.StallGuard = d.StallGuard;
            s.DirectionStopTrainBrakeOnly = d.DirectionStopTrainBrakeOnly;
            s.DirectionChangePauseSeconds = d.DirectionChangePauseSeconds;
            s.DirectionChangeHorn = d.DirectionChangeHorn;
            s.HillStartSpoolUp = d.HillStartSpoolUp;
            s.SmoothThrottle = d.SmoothThrottle;
            s.GlideThrottle = d.GlideThrottle;
            s.SmoothDieselThrottle = d.SmoothDieselThrottle;
            s.DieselNotchGuard = d.DieselNotchGuard;
            s.SpeedReductionLeadSeconds = d.SpeedReductionLeadSeconds;
            s.StartingResistance = d.StartingResistance;
            s.StartingResistanceFadeMph = d.StartingResistanceFadeMph;
            s.RealisticFuel = d.RealisticFuel;
            s.WaterStop = d.WaterStop;
            s.WaterStopPercent = d.WaterStopPercent;
            s.LegacySpeedLimits = d.LegacySpeedLimits;
            s.DetailedStatus = d.DetailedStatus;
            s.StatusMetric = d.StatusMetric;
            s.PlanIndependentUseScale = d.PlanIndependentUseScale;
            s.FuelFollowsTimeWarp = d.FuelFollowsTimeWarp;
            s.GuardFuseeCamera = d.GuardFuseeCamera;
            Save();
        }

        private static void Save()
        {
            try
            {
                Main.Settings.Save(Main.SettingsPath);
                Main.Mod.Logger.Log("Settings saved: " + Main.Settings.Describe());
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Could not save settings: " + e.Message);
            }
        }
    }

    /// <summary>Listens for the settings hotkey. Lives on its own object for as long as the mod is loaded.</summary>
    internal sealed class SettingsHotkey : MonoBehaviour
    {
        private InputAction _action;

        private void OnEnable()
        {
            try
            {
                string key = Main.Settings.SettingsHotkey;
                if (string.IsNullOrWhiteSpace(key)) return;

                _action = new InputAction("CollegeEducatedAutoEngineer.Settings", InputActionType.Button);
                if (Main.Settings.SettingsHotkeyCtrl)
                {
                    _action.AddCompositeBinding("ButtonWithOneModifier")
                        .With("Modifier", "<Keyboard>/ctrl")
                        .With("Button", key);
                }
                else
                {
                    _action.AddBinding(key);
                }
                _action.performed += OnPerformed;
                _action.Enable();
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Settings hotkey not available: " + e.Message);
                _action = null;
            }
        }

        private void OnPerformed(InputAction.CallbackContext context)
        {
            if (!Main.Active) return;
            try
            {
                SettingsUi.Toggle();
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException(e);
            }
        }

        private void OnDisable()
        {
            if (_action == null) return;
            _action.performed -= OnPerformed;
            _action.Disable();
            _action.Dispose();
            _action = null;
        }
    }
}
