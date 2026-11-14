using System;
using System.IO;
using System.Xml.Serialization;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>Kept in Settings.xml in the mod folder, in the same XML layout UMM used, so old files still load.</summary>
    public class Settings
    {
        /// <summary>Settings.xml layout version. Older files had features switched off by the start-up self-check (their PID targets were missing on 0.6); those are reset once.</summary>
        public int SettingsVersion = 0;

        internal const int CurrentSettingsVersion = 6;

        // ---- Overspeed ----

        /// <summary>
        /// How far (mph) above the auto-engineer's own padded setpoint the train may be before the brake is
        /// allowed to apply or stay applied while the throttle is open. The setpoint already sits about 1 mph
        /// under the track limit at 20 mph and above, so 0.5 mph here is still at or below the limit.
        /// </summary>
        public float OverspeedMarginMph = 0.5f;

        // ---- Cutoff (experimental) ----

        /// <summary>Use a shorter cutoff when it keeps nearly the same power.</summary>
        public bool CutoffEnabled = true;

        /// <summary>How much of the peak power multiplier (percent) the shorter cutoff may give up.</summary>
        public float CutoffTolerancePercent = 3f;

        /// <summary>At or above this uphill grade (percent) the stock cutoff is used.</summary>
        public float CutoffRevertGradePercent = 2f;

        // ---- Throttle smoothing ----

        /// <summary>Smooth the auto-engineer's throttle movements.</summary>
        public bool SmoothThrottle = true;

        /// <summary>Time constant of the smoothing (seconds). Larger is gentler and slower to respond.</summary>
        public float ThrottleSmoothingSeconds = 1.6f;

        /// <summary>Also move the lever continuously between the AI's half-second updates.</summary>
        public bool GlideThrottle = true;

        /// <summary>No throttle smoothing while the target speed is under this many mph or a coupling approach is within 60 m (0 = always smooth). Settings.xml only.</summary>
        public float CrawlSmoothingMaxMph = 6f;

        /// <summary>Throttle smoothing on diesel locomotives. Switch off when another mod already smooths the diesel notch stepping (the two together can leave the lever lagging the controller).</summary>
        public bool SmoothDieselThrottle = true;

        /// <summary>
        /// Switch smoothing off for a diesel whose throttle lever is rounded to the nearest notch by another mod (the lever reads back as an exact
        /// notch right after the mod wrote a value between notches); with both active the diesel stays in the notch it is on.
        /// </summary>
        public bool DieselNotchGuard = true;

        /// <summary>Stand still once within a couple of metres of a stop target instead of creeping past it.</summary>
        public bool HoldStopAtStopTargets = true;

        /// <summary>No power while a lower speed limit is in sight and the train is at or above that speed.</summary>
        public bool AvoidPowerIntoReductions = true;

        /// <summary>Start slowing for a lower limit as if it began this many seconds of travel sooner (0 = stock). 10 since 2.4.3 (6 before): on the Bryson to Sylva round trip it used about 3% less coal than 6 for the same time; 14 gave no more.</summary>
        public float SpeedReductionLeadSeconds = 10f;

        /// <summary>With Waypoint Queue loaded: leave the waypoint's speed target where it is (its passing speed is matched by distance), and skip train preparation (handbrakes, air, anglecocks) for waypoint orders, which it handles itself. Settings.xml only.</summary>
        public bool WaypointQueueCompat = true;

        /// <summary>Detect a stall on a grade (full throttle, almost stopped, climbing): brake, stay put and announce it.</summary>
        public bool StallGuard = true;

        /// <summary>Seconds of full throttle without making progress on a grade before the stall is declared.</summary>
        public float StallSeconds = 10f;

        // ---- Fuel use (experimental) ----

        /// <summary>Adds a standing-start resistance the game lacks (its Davis equation only covers a moving train). </summary>
        public bool StartingResistance = true;

        /// <summary>Extra resistance at 0 mph, in pounds per ton (real plain-bearing cars: roughly 5-10 or more; 7 is a middle value). No slider; edit Settings.xml.</summary>
        public float StartingResistanceLbPerTon = 7f;

        /// <summary>The extra resistance falls to 37% at this speed.</summary>
        public float StartingResistanceFadeMph = 2.5f;

        /// <summary>Scale the standing drag by how long the car has stood (oil drains from the journals). Cars that have not moved since the save was loaded count as fully settled.</summary>
        public bool StartingResistanceStandingTime = true;

        /// <summary>Share of the standing drag a car that has only just stopped still has (percent).</summary>
        public float StartingResistanceFloorPercent = 40f;

        /// <summary>Game minutes for a stopped car to get back to 63% of the way from the floor to the full drag.</summary>
        public float StartingResistanceSettleMinutes = 10f;

        /// <summary>
        /// Replace the game's coal and water use with a power-based model (see FuelModel). On by default. Can be switched in
        /// the settings window while the game runs (the patch is always installed and does nothing while this is off).
        /// </summary>
        public bool RealisticFuel = true;

        /// <summary>Pounds of steam per indicated horsepower-hour (modern locomotives about 16-20 at a good cutoff; 21.5 with the default cutoff term gives about 23 lb of water per indicated hp-hour on the Bryson to Sylva round trip with the EMR 5000; Goss measured 21.6 for a superheated engine at 200 psi in steady running).</summary>
        public float SteamLbPerIhpHour = 21.5f;

        /// <summary>How much the steam rate changes with cutoff: factor = 1 + slope x (cutoff - 0.30), limited to 0.8-1.4. 0 turns it off.</summary>
        public float CutoffSteamSlope = 0.5f;

        /// <summary>Rail horsepower divided by indicated horsepower.</summary>
        public float MechanicalEfficiency = 0.85f;

        /// <summary>Multiplier on coal burned (1 = the evaporation curve as is).</summary>
        public float CoalScale = 1f;

        // ---- Water stop (Road mode) ----

        /// <summary>
        /// Road-mode engineers stop at the next water column or tower ahead on the lined route when the tender runs low:
        /// they slow to the approach speed near it, switch to Waypoint mode only once the lead car is in the column's
        /// signal block (or within the approach distance where there is no CTC block), take water with the column's own
        /// loader, and go back to Road mode with the speed and direction they had. On by default; switchable in the
        /// settings window while the game runs.
        /// </summary>
        public bool WaterStop = true;

        /// <summary>Tender water (percent of capacity) below which the engineer plans a water stop.</summary>
        public float WaterStopPercent = 25f;

        /// <summary>How far ahead along the lined route to look for a water column (km).</summary>
        public float WaterStopSearchKm = 30f;

        /// <summary>Road-mode speed limit (mph) once within half a mile of the column, and the Waypoint-mode limit for the stop itself.</summary>
        public float WaterStopApproachMph = 25f;

        /// <summary>Where the column is not in a CTC block: distance (m) from the column at which the engineer switches to Waypoint mode.</summary>
        public float WaterStopApproachMeters = 600f;

        // ---- Track ----

        /// <summary>
        /// Legacy track speed limits (see LegacySpeedLimits). The game no longer shows the map's per-track speed limits but
        /// still enforces them. On: cleared on the mainline (track inside CTC signal blocks), kept with a speed board at each
        /// end of every limited stretch on branches, yards and spurs. In memory only; off puts everything back.
        /// </summary>
        public bool LegacySpeedLimits = true;

        // ---- Status ----

        /// <summary>
        /// Detailed auto-engineer status line (see DetailedStatus): names what sets a track speed (posted or railroad
        /// limit, track class, curve, or a curve held down by the equipment's rating), adds the distance to a limit or
        /// stop ahead, shows the engine's top speed when that is lower, and names this mod's own events. The tooltip
        /// lists the reasoning and the targets ahead. On by default; switchable in the settings window.
        /// </summary>
        public bool DetailedStatus = true;

        /// <summary>Distances in the detailed status in metres and kilometres instead of feet and miles.</summary>
        public bool StatusMetric = false;

        // ---- Signals ----

        /// <summary>How far ahead (m) the AI reacts to a signal's aspect. The game uses 200.</summary>
        public float SignalSightMeters = 500f;

        /// <summary>Put the Approach restriction at the signal instead of "now" (which makes the AI brake at full effort).</summary>
        public bool ApproachTargetAtSignal = true;

        /// <summary>After passing an Approach signal keep its speed limit until the next signal is registered.</summary>
        public bool HoldApproachSpeed = true;

        /// <summary>
        /// Speed (mph) of the Approach restriction when the game finds no signal after the Approach signal. The game
        /// uses 25, which is the default here (no change). A lower value (for example 15) only ever lowers it: the AI
        /// then slows sooner for a Stop signal it cannot see yet. Values of 25 or more, or below 5, change nothing.
        /// </summary>
        public float ApproachNoNextSignalMph = 25f;

        /// <summary>
        /// Temporary map-data workaround: "signalId:blockId" pairs (comma separated) naming blocks to take off a signal's
        /// protected-block list. The default fixes Sylva West eastbound, whose block lies behind the signal and drops it to
        /// Stop about 45 m before a train. Empty = off. Applied at runtime; no files are changed.
        /// </summary>
        public string CtcDropBlocks = "sy-w-eb:sy-w";

        // ---- Planning (experimental) ----

        /// <summary>Multiplier on how far ahead the AI looks for targets (1 = stock).</summary>
        public float LookaheadMultiplier = 1f;

        /// <summary>Fraction of the train's available braking the AI assumes when planning (1 = stock; lower = earlier, gentler).</summary>
        public float BrakePlanningFraction = 1f;

        // ---- Brake guidance (0.6.0 rollout controller) ----

        /// <summary>
        /// While the train is moving with no speed order and nothing ahead to stop for (what a direction change on a
        /// moving train leaves), cap the train brake at this fraction of a full pipe reduction (0.15 is about 13 psi;
        /// the game allows about 0.29 on a charged train). 0 = off. Ends when a target or speed limit appears again.
        /// </summary>
        public float DirectionStopBrakeLimit = 0.15f;

        /// <summary>
        /// Stopping a moving train that has no speed order and no target (a direction change, or a speed order of 0)
        /// uses the train brake alone: the independent brake is bailed off above 2 mph, and only holds the train
        /// once it is nearly stopped.
        /// </summary>
        public bool DirectionStopTrainBrakeOnly = true;

        /// <summary>
        /// The direction-change stop (above) is only used when the cars behind the locomotives and tenders are at least this
        /// share of the train's weight (percent); a locomotive running light, or with a few empty cars, is stopped by the
        /// controller as in the stock game, because the train brake alone has almost nothing to hold. 0 = always.
        /// </summary>
        public float DirectionStopMinTrailingPercent = 50f;

        /// <summary>
        /// When the engineer is told to run the other way, it holds the brakes with the throttle closed until the train has
        /// stood still for this many seconds (time already stood counts) before it starts moving. 0 = off. Heavy trains could
        /// otherwise go from the stop straight into the reverse.
        /// </summary>
        public float DirectionChangePauseSeconds = 3f;

        /// <summary>Blow the stock stop blast (one short) at the start of the direction-change pause when the stock engineer skipped it.</summary>
        public bool DirectionChangeHorn = true;

        /// <summary>
        /// Starting on an upgrade: while the engineer waits for the train brake to release (locomotive brake applied), raise the
        /// throttle to the start-up value so the locomotive is already pulling when the brake lets go, instead of spooling up
        /// while the train rolls back.
        /// </summary>
        public bool HillStartSpoolUp = true;

        /// <summary>The upgrade (percent) from which the hill-start spool-up applies. Settings.xml only.</summary>
        public float HillStartMinGradePercent = 0.5f;

        /// <summary>
        /// Fuel saving, off at 0; 2 since 2.4.3 (0 before). When the train is no more than this many mph over its target, is not gaining speed and
        /// nothing ahead needs it to slow, the controller's first train-brake application is turned into a coast. Road mode
        /// and Waypoint modes. About 1-2% less coal on downhill running, nothing measurable uphill.
        /// </summary>
        public float BrakeTapSuppressMph = 2f;

        /// <summary>
        /// Coast band in mph, off at 0 (see CoastBand). Road and Waypoint modes: the throttle closes when coasting would
        /// hold the speed and the train is within this band below its target (or gravity alone is still picking it up),
        /// and a brake application waits until the train is more than this band over its target unless a limit or a stop
        /// ahead needs it.
        /// </summary>
        public float CoastBandMph = 1f;

        /// <summary>
        /// With the coast band on: the throttle stays closed for a downhill pickup only when gravity alone will bring the
        /// train to its target within this many seconds. Slower than that, the engineer accelerates itself. 0 = the old
        /// rule (closed however far below target).
        /// </summary>
        public float CoastBandAssistSeconds = 30f;

        /// <summary>Multipliers on the rollout controller's score weights (1 = the game's own value).</summary>
        public float PlanOverspeedScale = 1f;
        public float PlanUndershootScale = 1f;
        public float PlanUndershootDeadbandScale = 1f;
        public float PlanCeilingScale = 1f;
        public float PlanIndependentUseScale = 1f;

        // ---- Logger ----

        public bool LogEnabled = false;

        /// <summary>Also log coal and water aboard a steam locomotive's tender (see FuelReader).</summary>
        public bool LogFuel = false;

        /// <summary>Also log the locomotive's position (posX/posY/posZ, trackLoc, area) and the G forces on it (gX forward, gY right, gZ up, in g; see GForceMeter).</summary>
        public bool LogGForces = false;

        /// <summary>Time constant (seconds) of the low-pass filter on gX/gY/gZ, which takes out slack and coupler jitter. 0 = raw per physics tick. Settings.xml only.</summary>
        public float GForceSmoothingSeconds = 1f;

        /// <summary>Also log what the rollout controller decided each tick (action, its cost, the cost of holding, and the cost of each first action).</summary>
        public bool LogDecisions = false;

        /// <summary>Also write logs\signals_*.csv: one line each time the signal an engineer is tracking, or the aspect it shows, changes (with the train's speed and distance at that moment), and add sigId, sigAspect and sigDistM to the main CSV.</summary>
        public bool LogSignals = false;

        /// <summary>Also write logs\ctc_dump_*.txt once per session: every CTC block's track spans and every signal's position and protected blocks (read-only; for tracking down signals that drop to Stop early).</summary>
        public bool LogCtc = false;

        /// <summary>When the engineer gets a new order to move (any mode) and the train is standing: release the handbrakes on every car, connect the air between coupled cars with the anglecocks open, and close the anglecocks at the two ends of the train.</summary>
        public bool PrepTrain = true;

        /// <summary>Also write logs\carkeys_*.txt on each new order (at most every 20 s): every key-value entry on each car of the train (to find the names for doors, gates and lamps). Settings.xml only.</summary>
        public bool LogCarKeys = false;

        /// <summary>Open the doors and gates of passenger cars while the engineer is stopped at a station stop, and close them when the train moves off.</summary>
        public bool DoorsAtStations = true;

        /// <summary>When the observation car (or another car with its own headlight) leads the train, light its headlight like the locomotive's: bright moving, dim stopped, off when it stops leading.</summary>
        public bool ObservationHeadlight = true;

        /// <summary>Marker lamps: show red at the rear end of the train and switch every other marker lamp on the train off (class lights are left to the player).</summary>
        public bool AutoMarkers = true;

        /// <summary>The marker lamp position (0 to 3, each a quarter turn of the lens) that shows red outward. Settings.xml only.</summary>
        public int MarkerRedPosition = 2;

        /// <summary>Also write logs\fuel_summary_*.csv: coal, water and distance per engineer about once a minute, split by powered, coasting, braking and "both" (throttle open while braked), with brake applications counted.</summary>
        public bool LogFuelSummary = false;

        /// <summary>Opens the in-game settings window: an Input System binding path (for example &lt;Keyboard&gt;/f8), empty for none.</summary>
        public string SettingsHotkey = "<Keyboard>/f8";

        /// <summary>Hold Ctrl with SettingsHotkey (the default, so the key cannot clash with the game's own).</summary>
        public bool SettingsHotkeyCtrl = true;

        /// <summary>
        /// Charge fuel and water for the game time that has really passed when time is sped up (fast-forward). The game
        /// charges one second per call however much time went by, so a fast-forwarded trip used about a third of its coal.
        /// Normal speed is not affected.
        /// </summary>
        public bool FuelFollowsTimeWarp = true;

        /// <summary>
        /// Skip a placed fusee's per-frame update while the main camera is off. Fast-forward disables the camera and the
        /// game's FuseeRenderer.Update then throws a NullReferenceException on every frame. Does nothing otherwise.
        /// </summary>
        public bool GuardFuseeCamera = true;

        /// <summary>Seconds between samples. The game's own control loop ticks about every 0.5 s.</summary>
        public float SampleIntervalSeconds = 0.25f;

        /// <summary>The tuning values the settings window controls, on one line (for the logs, so every run says how it was set).</summary>
        internal string Describe()
        {
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return "tap=" + BrakeTapSuppressMph.ToString("0.#", c) + ";lead=" + SpeedReductionLeadSeconds.ToString("0.#", c)
                + ";cut=" + CutoffTolerancePercent.ToString("0.#", c) + ";over=" + PlanOverspeedScale.ToString("0.##", c)
                + ";under=" + PlanUndershootScale.ToString("0.##", c) + ";margin=" + PlanUndershootDeadbandScale.ToString("0.##", c)
                + ";ceil=" + PlanCeilingScale.ToString("0.##", c) + ";indep=" + PlanIndependentUseScale.ToString("0.##", c)
                + ";band=" + CoastBandMph.ToString("0.#", c)
                + ";assist=" + CoastBandAssistSeconds.ToString("0.#", c);
        }

        /// <summary>Writes the file through a temporary copy, so a failed write cannot leave half a file.</summary>
        internal void Save(string path)
        {
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(stream, this);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            Backup(path);
        }

        /// <summary>Where a copy of Settings.xml is kept outside the mod folder, so a mod update that replaces the whole folder does not lose the settings.</summary>
        private static string BackupPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Railroader", "CollegeEducatedAutoEngineer-Settings.xml");
        }

        private static void Backup(string path)
        {
            try
            {
                string backup = BackupPath();
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(path, backup, true);
            }
            catch (Exception) { /* the backup is a convenience */ }
        }

        private static void RestoreIfMissing(string path)
        {
            try
            {
                string backup = BackupPath();
                if (!File.Exists(path) && File.Exists(backup)) File.Copy(backup, path);
            }
            catch (Exception) { /* start on defaults */ }
        }

        /// <summary>Reads the file, or returns defaults (and writes them out when there is no file yet).</summary>
        internal static Settings Load(string path, out string problem)
        {
            problem = null;
            var serializer = new XmlSerializer(typeof(Settings));
            RestoreIfMissing(path);
            if (File.Exists(path))
            {
                try
                {
                    Settings loaded;
                    using (var stream = File.OpenRead(path)) loaded = (Settings)serializer.Deserialize(stream);
                    if (loaded.SettingsVersion < CurrentSettingsVersion)
                    {
                        var d = new Settings();
                        if (loaded.SettingsVersion < 2)
                        {
                            loaded.StallGuard = d.StallGuard;
                            loaded.SmoothThrottle = d.SmoothThrottle;
                            loaded.AvoidPowerIntoReductions = d.AvoidPowerIntoReductions;
                        }
                        if (loaded.SettingsVersion < 4) loaded.StartingResistanceLbPerTon = d.StartingResistanceLbPerTon;   // 2.9.15: 7, no slider
                        if (loaded.SettingsVersion < 5) loaded.AutoMarkers = d.AutoMarkers;   // 1.1.6: on (it was a Settings.xml-only test switch)
                        if (loaded.SettingsVersion < 6) loaded.MarkerRedPosition = d.MarkerRedPosition;   // 1.3.6: 2, the value tested on the stock locomotives and cabooses (the default was 3 from 1.1.7 to 1.3.4)
                        loaded.SettingsVersion = CurrentSettingsVersion;
                        try { using (var w = File.Create(path)) serializer.Serialize(w, loaded); }
                        catch (Exception) { /* the next settings change saves it */ }
                    }
                    Backup(path);
                    return loaded;
                }
                catch (Exception e)
                {
                    // Leave the user's file alone and run on defaults.
                    problem = "could not read " + path + ", using defaults: " + e.Message;
                    return new Settings();
                }
            }
            var settings = new Settings { SettingsVersion = CurrentSettingsVersion };
            try
            {
                using (var stream = File.Create(path)) serializer.Serialize(stream, settings);
                Backup(path);
            }
            catch (Exception e)
            {
                problem = "could not write " + path + ": " + e.Message;
            }
            return settings;
        }
    }
}
