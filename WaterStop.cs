using System;
using System.Collections.Generic;
using System.Linq;
using Game.Messages;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Ops;
using RollingStock;
using RollingStock.Controls;
using Track;
using Track.Signals;
using UI.EngineControls;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Water stop for a Road-mode engineer (setting WaterStop, on by default).
    ///
    /// When the tender's water falls below the threshold, the engineer looks along the route as it is lined (Road mode
    /// does not throw switches) for the nearest water column or tower ahead, says where it will take water, and
    /// slows to the approach speed when it gets close. Distances are measured from the tender's water hatch, not the lead
    /// end, so a train being shoved (cars ahead of the engine) is judged by where the tender is. Only once a car of the
    /// train is in the column's signal block, or the lead end has already passed the column (or, where there is no CTC
    /// block, within the approach distance) does it switch to Waypoint mode with a waypoint
    /// that puts the tender's water hatch under the spout, so the player's routing is kept up to that block.
    /// Stopped there, it asks the column's own loader sequence to load (the same key value the game's loader
    /// sequencer reads), waits until the tender is full, puts the spout away and goes back to Road mode with the
    /// direction and speed it had.
    ///
    /// The spotting arithmetic and the use of CarLoaderSequencer's "wants loading" key follow the refuel feature of
    /// the Waypoint Queue mod by PrinceOfPluto (MIT licence, https://github.com/PrinceOfPluto/WaypointQueue).
    ///
    /// Host only. One component per engineer; it does nothing unless the setting is on, the engine is a steam
    /// locomotive with a tender, and the engineer is in Road mode (or in the Waypoint mode this component set).
    /// The player changing the orders at any point cancels the stop.
    /// </summary>
    public class WaterStop : MonoBehaviour
    {
        private enum Phase { Idle, Armed, Approaching, Filling, Holding }

        private const float TickSeconds = 1f;
        private const float SearchEverySeconds = 5f;
        private const float SearchNoColumnSeconds = 1f;   // while no column was found, look again every tick (a column can come into range at any moment)
        private const float MinAheadStandstillMeters = 0f; // standing still, a column any distance ahead of the hatch counts (the hatch may be just short of it)
        private const float SlowDownWithinMeters = 805f;   // half a mile
        private const float MinAheadMeters = 30f;         // a column closer than this to the hatch is not picked
        private const float MinAheadArmedMeters = 5f;     // once picked, the stop goes on until the hatch is about at the spout
        // The game completes a waypoint once it is within about a metre of the train's end (AutoEngineerPlanner.IsUnderTrain),
        // so the train stops short of it. Measured at Whittier with 274 m of train shoved: waypoint 0.5 m past the spout
        // left the hatch 0.92 m short (2.5.4), 1.0 m past left it 0.45 m short (2.5.5): the train stops 1.42-1.45 m before
        // the waypoint. Waypoint Queue uses 0.5 m. The same allowance makes a correction waypoint land where it should.
        private const float StopPastSpoutMeters = 1.45f;
        // A correction is measured standing, so only the arrival radius (about 1 m) and some creep apply.
        private const float CorrectionPastMeters = 0.7f;   // EMR 1 corrections stopped 0.6-0.75 m before their waypoint
        private const float ApproachTimeoutSeconds = 1200f;
        private const float NoFlowTimeoutSeconds = 90f;
        private const float FullFraction = 0.995f;
        private const float ColumnCooldownSeconds = 600f;
        private const float ReachMarginMeters = 0.05f;    // under the loader's own reach (column radius + hatch radius)
        private const float DefaultReachMeters = 0.6f;    // column 0.2 + F-71 hatch 0.4, if either cannot be read
        private const float MaxCorrectionMeters = 100f;   // further off than this: hold for the player
        private const int MaxSpotCorrections = 3;
        private const float StopDecelMps2 = 0.12f;        // braking assumed for the approach speed ladder (heavy train, gentle)
        private const float QuietAfterTakeoverSeconds = 300f;
        private const float FirstFlowSeconds = 30f;      // spout down and water flowing; otherwise check the spotting again
        private const float MovedAtLeastMeters = 0.1f;   // a correction that moved less than this did not take

        private sealed class Column
        {
            public CarLoadTargetLoader Loader;
            public CarLoaderSequencer Sequencer;
            public string Id;
            public string Place;
            public Location Location;
            public CTCBlock Block;
        }

        private static List<Column> _columns;
        private static bool _loggedColumns;

        private AutoEngineer _ae;
        private bool _loggedStationWait;
        private bool _loggedHeldShort;
        private const float HeldShortMeters = 10f;   // stopped this far short of the spout with the waypoint set: held by something else
        private BaseLocomotive _loco;
        private Phase _phase = Phase.Idle;
        private float _nextTick;
        private float _nextSearch;
        private bool _pickedStanding;   // the column was picked while standing, closer than MinAheadMeters: no minimum on the Armed check
        private float _phaseStart;
        private float _graceUntil;      // our own order changes take a moment to show in the orders
        private bool _syncSpeed;

        private Column _column;
        private bool _origForward;
        private int _origMaxSpeed;
        private AutoEngineerMode _origMode = AutoEngineerMode.Road;   // Road, or Waypoint (fast forward needs a Waypoint order)
        private OrderWaypoint? _origWaypoint;
        private int _setMaxSpeed;
        private string _waypointString;
        private bool _requested;
        private string _hatchObjectId;   // the tender's key value object
        private string _hatchKey;
        private bool _hatchWasOpen;
        private float _lastWater;
        private float _lastFlow;
        private bool _saidNoColumn;
        private int _corrections;
        private float _lastMiss = float.NaN;
        // How far before its waypoint this engine's train really stops, learnt from the last stops (per locomotive):
        // the first approach aims StopPastSpoutMeters past the spout; the corrections' waypoints are placed a gap
        // beyond the move. Small consists stop closer to the waypoint than long ones (EMR 1: 0.6-0.7 m, EMR 5000: 1.4-1.8 m).
        private float _approachGap = StopPastSpoutMeters;
        private float _correctionGap = CorrectionPastMeters;
        private float _approachAim = float.NaN;   // the aim of the approach waypoint (past the spout), until measured
        private float _lastCorrOut;               // the last correction's waypoint distance
        private float _lastCorrSign;              // +1 pulled ahead (short), -1 backed up (past), 0 none yet
        private int _stuckCorrections;
        private AutoEngineerMode _expectMode = AutoEngineerMode.Waypoint;   // the mode this component last ordered
        private bool _flowSeen;
        private float _quietUntil;   // the player took over a stop: plan no other for a while
        private int _refills;
        private float _correctedAt = -1f;   // a correction was ordered; wait for the train to move before measuring again
        private float _offsetTime = -1f;
        private float _offsetValue;
        private float _lastDistance = float.NaN;   // hatch to column at the last Armed check
        private float _lastDistanceTime;
        private readonly Dictionary<string, float> _cooldown = new Dictionary<string, float>();

        // How far before its waypoint a train stops depends on its weight (EMR 1, 74 t: 0.6-0.75 m; EMR 5000, 1,907 t:
        // 1.2-1.8 m), not on which locomotive it is. One table serves every train: seed points (weight, gap) from the
        // measured stops, smoothed across ln(weight) with a Gaussian kernel, plus every stop this mod measures, kept
        // in stopgaps.txt in the mod folder ("a|c <gross tons> <gap>", newest 20 of each kind). A new weight reads
        // between its neighbours; a weight with measured stops of its own follows them.
        private const float GapKernelWidth = 1.0f;   // in ln(tons)
        private const int MaxGapSamples = 20;
        // Measured stops (see claude/water-stop-research.md in the project): approach gaps 74 t 0.67; 146 t 0.74;
        // 1,907 t 1.00, 1.24, 1.42, 1.45, 1.62, 1.78 (mean 1.4). Correction gaps: 74 t 0.57, 0.60, 0.75; none yet for heavy trains.
        private static readonly float[][] ApproachSeeds = { new[] { 74f, 0.67f }, new[] { 146f, 0.74f }, new[] { 1907f, 1.4f } };
        private static readonly float[][] CorrectionSeeds = { new[] { 74f, 0.65f }, new[] { 1907f, 1.5f } };
        private static List<float[]> _approachSamples;     // { tons, gap }
        private static List<float[]> _correctionSamples;

        private static string GapFile => System.IO.Path.Combine(Main.Mod.Path, "stopgaps.txt");

        private float GrossTons()
        {
            float pounds = 0f;
            foreach (Car car in _loco.EnumerateCoupled()) pounds += car.Weight;
            return pounds / 2000f;
        }

        private static void LoadSamples()
        {
            if (_approachSamples != null) return;
            _approachSamples = new List<float[]>();
            _correctionSamples = new List<float[]>();
            try
            {
                if (!System.IO.File.Exists(GapFile)) return;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (string line in System.IO.File.ReadAllLines(GapFile))
                {
                    string[] p = line.Split(' ');
                    if (p.Length == 3 && (p[0] == "a" || p[0] == "c")
                        && float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out float t)
                        && float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out float g))
                        (p[0] == "a" ? _approachSamples : _correctionSamples).Add(new[] { t, g });
                }
            }
            catch (Exception e) { Main.Mod.Logger.Warning("Water stop: could not read stopgaps.txt: " + e.Message); }
        }

        private static float Predict(float[][] seeds, List<float[]> samples, float tons)
        {
            float lt = Mathf.Log(Mathf.Max(1f, tons));
            float sum = 0f, weight = 0f;
            void Add(float t, float g)
            {
                float d = (lt - Mathf.Log(Mathf.Max(1f, t))) / GapKernelWidth;
                float w = Mathf.Exp(-0.5f * d * d);
                sum += w * g;
                weight += w;
            }
            foreach (float[] x in seeds) Add(x[0], x[1]);
            foreach (float[] x in samples) Add(x[0], x[1]);
            return weight > 1e-6f ? sum / weight : seeds[0][1];
        }

        /// <summary>Sets the gaps for the train as it is now from the weight table.</summary>
        private void LoadGaps()
        {
            _approachGap = StopPastSpoutMeters;
            _correctionGap = CorrectionPastMeters;
            try
            {
                LoadSamples();
                float tons = GrossTons();
                _approachGap = Mathf.Clamp(Predict(ApproachSeeds, _approachSamples, tons), 0.3f, 2.5f);
                _correctionGap = Mathf.Clamp(Predict(CorrectionSeeds, _correctionSamples, tons), 0.3f, 2f);
                Main.Mod.Logger.Log($"Water stop ({Name}): train {tons:0} t; stop gaps from the weight table: {_approachGap:0.00} m (approach), {_correctionGap:0.00} m (correction).");
            }
            catch (Exception) { }
        }

        /// <summary>Records a measured gap for this train's weight (kind "a" approach, "c" correction) and saves the table.</summary>
        private void SaveGap(bool approach, float gap)
        {
            try
            {
                LoadSamples();
                List<float[]> list = approach ? _approachSamples : _correctionSamples;
                list.Add(new[] { GrossTons(), gap });
                while (list.Count > MaxGapSamples) list.RemoveAt(0);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var lines = new List<string>();
                foreach (float[] x in _approachSamples) lines.Add(string.Format(inv, "a {0:0} {1:0.00}", x[0], x[1]));
                foreach (float[] x in _correctionSamples) lines.Add(string.Format(inv, "c {0:0} {1:0.00}", x[0], x[1]));
                System.IO.File.WriteAllLines(GapFile, lines.ToArray());
            }
            catch (Exception e) { Main.Mod.Logger.Warning("Water stop: could not write stopgaps.txt: " + e.Message); }
        }

        private void Awake()
        {
            _ae = GetComponent<AutoEngineer>();
            _loco = GetComponent<BaseLocomotive>();
        }

        private void OnDisable()
        {
            if (_phase == Phase.Filling) Request(false);
            _phase = Phase.Idle;
        }

        private void Update()
        {
            if (!Main.Active) return;
            float now = Time.time;
            if (now < _nextTick) return;
            _nextTick = now + TickSeconds;

            try
            {
                if (!Main.Settings.WaterStop || !StateManager.IsHost || _loco == null || _loco.AutoEngineerPlanner == null)
                {
                    if (_phase != Phase.Idle) Cancel("water stop switched off");
                    return;
                }
                Tick(now);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Water stop (" + Name + ") failed: " + e.Message);
                if (_phase == Phase.Filling) Request(false);
                _phase = Phase.Idle;
            }
        }

        private string Name => _loco != null ? _loco.DisplayName : "?";

        private AutoEngineerOrdersHelper Helper => new AutoEngineerOrdersHelper(_loco, new AutoEngineerPersistence(_loco.KeyValueObject));

        private Orders CurrentOrders => new AutoEngineerPersistence(_loco.KeyValueObject).Orders;

        private void Tick(float now)
        {
            Orders orders = CurrentOrders;
            Car tender = FuelReader.Tender(_loco);
            float water = FuelReader.WaterFraction(tender);

            switch (_phase)
            {
                case Phase.Idle:
                {
                    bool roadOrders = orders.Mode == AutoEngineerMode.Road;
                    bool waypointOrders = orders.Mode == AutoEngineerMode.Waypoint && orders.Waypoint.HasValue;
                    if (!(roadOrders || waypointOrders) || orders.MaxSpeedMph <= 0 || float.IsNaN(water)) return;
                    if (now < _quietUntil) return;
                    float threshold = Mathf.Clamp(Main.Settings.WaterStopPercent, 5f, 90f) / 100f;
                    if (water >= threshold) { _saidNoColumn = false; return; }
                    if (now < _nextSearch) return;
                    _nextSearch = now + SearchEverySeconds;

                    bool standing = _loco.IsStopped(1f);
                    if (!TryFindColumnAhead(orders.Forward, standing ? MinAheadStandstillMeters : MinAheadMeters, out Column column, out float distance))
                    {
                        _nextSearch = now + SearchNoColumnSeconds;
                        if (!_saidNoColumn)
                        {
                            _saidNoColumn = true;
                            Say($"Water's at {water * 100f:0}% and I don't see a water column ahead on this route.");
                            Main.Mod.Logger.Log($"Water stop ({Name}): water {water * 100f:0}%, no column ahead within {Main.Settings.WaterStopSearchKm:0} km.");
                        }
                        return;
                    }
                    if (waypointOrders)
                    {
                        // Waypoint orders (also what fast forward needs): only stop for a column before the destination.
                        float? remaining = _loco.AutoEngineerPlanner.RemainingWaypointDistance;
                        if (remaining.HasValue && distance > remaining.Value + 30f)
                        {
                            if (!_saidNoColumn)
                            {
                                _saidNoColumn = true;
                                Main.Mod.Logger.Log($"Water stop ({Name}): water {water * 100f:0}%, the nearest column ({column.Id}, {distance:0} m) is beyond the waypoint ({remaining.Value:0} m); no stop.");
                            }
                            return;
                        }
                    }
                    _column = column;
                    LoadGaps();
                    _origMode = orders.Mode;
                    _origWaypoint = waypointOrders ? orders.Waypoint : null;
                    _origForward = orders.Forward;
                    _origMaxSpeed = orders.MaxSpeedMph;
                    _setMaxSpeed = orders.MaxSpeedMph;
                    _syncSpeed = false;
                    _corrections = 0;
                    _correctedAt = -1f;
                    _lastMiss = float.NaN;
                    _lastCorrSign = 0f;
                    _stuckCorrections = 0;
                    _refills = 0;
                    _lastDistance = distance;
                    _lastDistanceTime = now;
                    // Picked while standing and closer than the usual minimum: go on at the next tick (a Road-mode train
                    // would otherwise move off during the wait) and do not cancel for a column that is close ahead.
                    _pickedStanding = standing && distance < MinAheadMeters;
                    if (_pickedStanding) _nextSearch = now;
                    _phase = Phase.Armed;
                    _phaseStart = now;
                    Say($"Water's at {water * 100f:0}%. We'll take water at {column.Place}.");
                    Main.Mod.Logger.Log($"Water stop ({Name}): water {water * 100f:0}%, column {column.Id} at {column.Place}, {distance:0} m ahead of the hatch (block {column.Block?.id ?? "none"}), time scale {Time.timeScale:0.#}.");
                    return;
                }

                case Phase.Armed:
                {
                    if (now < _graceUntil) return;
                    if (_syncSpeed)
                    {
                        _setMaxSpeed = orders.MaxSpeedMph;   // as the game clamped it
                        _syncSpeed = false;
                    }
                    if (orders.Mode != _origMode || orders.Forward != _origForward || orders.MaxSpeedMph != _setMaxSpeed
                        || (_origMode == AutoEngineerMode.Waypoint && !Nullable.Equals(orders.Waypoint, _origWaypoint)))
                    {
                        Cancel("orders changed by the player");
                        return;
                    }
                    if (now < _nextSearch) return;
                    _nextSearch = now + SearchEverySeconds / 2f;

                    if (!TryDistanceTo(_column, orders.Forward, _pickedStanding ? MinAheadStandstillMeters : MinAheadArmedMeters, out float distance, out Location hatchAt, out float headToHatch))
                    {
                        // Passed it, or the route was lined away from it: give back the speed we took off.
                        if (_setMaxSpeed != _origMaxSpeed) Helper.SetOrdersValue(null, null, _origMaxSpeed, null, null);
                        Cancel("the column is no longer ahead on the lined route");
                        return;
                    }
                    _lastDistance = distance;
                    _lastDistanceTime = now;

                    int approach = Mathf.Clamp(Mathf.RoundToInt(Main.Settings.WaterStopApproachMph), 5, 45);
                    // Close in, also a speed it can stop from in the distance left (Dillsboro, 2.5.5: the column is
                    // about 60 m into its block, the switch came at 25 mph with the hatch 62 m out and it ran 67 m past).
                    int ladder = StoppableMph(distance);
                    int cap = distance < SlowDownWithinMeters ? Mathf.Min(approach, ladder) : int.MaxValue;
                    if (_setMaxSpeed > cap)
                    {
                        _setMaxSpeed = cap;
                        _syncSpeed = true;
                        _graceUntil = now + 3f;
                        Helper.SetOrdersValue(null, null, cap, null, null);
                        Main.Mod.Logger.Log($"Water stop ({Name}): hatch {distance:0} m from {_column.Id}, slowing to {cap} mph.");
                        return;
                    }

                    // Shoving, the lead end can reach (and pass) a column near the block boundary before it is
                    // seen in the block, so any car of the train in the block counts, and so does the lead end being
                    // past the column (the hatch distance is then shorter than the train ahead of it).
                    bool headPast = distance <= headToHatch;
                    bool inBlock;
                    if (_column.Block != null)
                    {
                        inBlock = headPast || _column.Block.CarsInBlock().Overlaps(_loco.EnumerateCoupled());
                    }
                    else
                    {
                        inBlock = headPast || distance <= Mathf.Max(100f, Main.Settings.WaterStopApproachMeters);
                    }
                    if (!inBlock) return;

                    // A station stop between the train and the column comes first: stay in Road orders, which serve the
                    // station, and take over once it has been served (Almond: the platform is 167 m short of the column).
                    if (!headPast && DecelHold.StationStopWithin(_ae, distance))
                    {
                        if (!_loggedStationWait)
                        {
                            _loggedStationWait = true;
                            Main.Mod.Logger.Log($"Water stop ({Name}): a station stop comes before the column ({distance:0} m to the hatch); waiting until it has been served.");
                        }
                        return;
                    }

                    if (!TryWaypointFor(_column, hatchAt, headToHatch, out Location target))
                    {
                        Cancel("could not work out where to stop");
                        return;
                    }
                    _loggedStationWait = false;
                    _approachAim = _approachGap;
                    _waypointString = Graph.LocationToString(target.Clamped());
                    Helper.SetOrdersValue(AutoEngineerMode.Waypoint, _origForward, Mathf.Min(_setMaxSpeed, approach), null, new OrderWaypoint(_waypointString, null));
                    _phase = Phase.Approaching;
                    _expectMode = AutoEngineerMode.Waypoint;
                    _phaseStart = now;
                    _graceUntil = now + 3f;
                    Say($"Pulling up to the water column at {_column.Place}.");
                    Main.Mod.Logger.Log($"Water stop ({Name}): {(headPast ? "lead end past the column" : _column.Block != null ? "in block " + _column.Block.id : "in approach distance")}, hatch {distance:0} m from {_column.Id}; Waypoint mode to {_waypointString} (lead end to hatch {headToHatch:0.0} m), time scale {Time.timeScale:0.#}.");
                    return;
                }

                case Phase.Approaching:
                {
                    if (now < _graceUntil) return;
                    bool waypointChanged = _expectMode == AutoEngineerMode.Waypoint && orders.Waypoint.HasValue && orders.Waypoint.Value.LocationString != _waypointString;
                    if (orders.Mode != _expectMode || waypointChanged)
                    {
                        Cancel("orders changed by the player");
                        return;
                    }
                    if (now - _phaseStart > ApproachTimeoutSeconds)
                    {
                        GiveUp("couldn't get to the column");
                        return;
                    }
                    if (_correctedAt >= 0f)
                    {
                        // A correction: wait for it to start moving (or 30 s), then for it to stop.
                        if (!_loco.IsStopped(1f) || now - _correctedAt > 30f) _correctedAt = -1f;
                        return;
                    }
                    if (!_loco.IsStopped(3f)) return;
                    // Stopped: either at the waypoint (the game may clear it on arrival) or held by something else.
                    if (_expectMode == AutoEngineerMode.Waypoint && orders.Waypoint.HasValue && !NearWaypoint(orders.Forward)) return;
                    // Stopped well short of the column with the waypoint still set: something else is holding the train (a
                    // maintenance-of-way task stop, issue #1). Wait for it to move on instead of measuring and "correcting".
                    if (_corrections == 0 && _expectMode == AutoEngineerMode.Waypoint && orders.Waypoint.HasValue
                        && TrySpotError(out float heldMiss, out _) && heldMiss > HeldShortMeters)
                    {
                        if (!_loggedHeldShort)
                        {
                            _loggedHeldShort = true;
                            Main.Mod.Logger.Log($"Water stop ({Name}): stopped {heldMiss:0} m short of the spout with the waypoint still set; something else is holding the train, waiting.");
                        }
                        return;
                    }
                    _loggedHeldShort = false;
                    if (!CheckSpot(now, out string why))
                    {
                        if (why != null) Hold(now, why);
                        return;   // a correction is under way
                    }
                    StartFilling(now, water);
                    return;
                }

                case Phase.Filling:
                {
                    if (orders.Mode != _expectMode)
                    {
                        Request(false);
                        Cancel("orders changed by the player");
                        return;
                    }
                    if (water >= FullFraction)
                    {
                        Request(false);
                        Main.Mod.Logger.Log($"Water stop ({Name}): full ({water * 100f:0.0}%) after {now - _phaseStart:0} s at {_column.Id}.");
                        Finish("Tender's full. Back to road orders.");
                        return;
                    }
                    if (water > _lastWater + 0.0005f)
                    {
                        _lastWater = water;
                        _lastFlow = now;
                        _flowSeen = true;
                        return;
                    }
                    if (!_flowSeen && now - _phaseStart > FirstFlowSeconds)
                    {
                        // Nothing came in: put the spout up, measure again and move the tender if it can.
                        Request(false);
                        Main.Mod.Logger.Log($"Water stop ({Name}): no water after {FirstFlowSeconds:0} s at {_column.Id}; checking the spotting again.");
                        _phase = Phase.Approaching;
                        _phaseStart = now;
                        if (!CheckSpot(now, out string why))
                        {
                            if (why != null) Hold(now, why);
                            return;
                        }
                        // Measured as in reach but still no water: try once more from the top, then hold.
                        if (_refills++ == 0)
                        {
                            StartFilling(now, water);
                            return;
                        }
                        Hold(now, $"the spout gives no water although the hatch measures {Mathf.Abs(_lastMiss):0.00} m from it");
                        return;
                    }
                    if (_flowSeen && now - _lastFlow > NoFlowTimeoutSeconds)
                    {
                        Request(false);
                        if (water >= 0.95f)   // the loader stops at its own idea of full, which can sit a little under the slot size
                        {
                            Main.Mod.Logger.Log($"Water stop ({Name}): flow stopped at {water * 100f:0.0}%, taken as full.");
                            Finish("Tender's full. Back to road orders.");
                        }
                        else
                        {
                            Hold(now, $"the water stopped at {water * 100f:0}%");
                        }
                    }
                    return;
                }

                case Phase.Holding:
                {
                    // Stopped at the column for the player. Their own orders end it; a full tender resumes the road orders.
                    if (now < _graceUntil) return;
                    if (water >= FullFraction && !SpoutRequested())
                    {
                        Main.Mod.Logger.Log($"Water stop ({Name}): tender filled ({water * 100f:0.0}%) while holding at {_column.Id}.");
                        Finish("Tender's full, thanks. Back to our orders.");
                        return;
                    }
                    if (orders.Mode != AutoEngineerMode.Road || orders.Forward != _origForward || orders.MaxSpeedMph != 0)
                    {
                        Main.Mod.Logger.Log($"Water stop ({Name}): the player took over at {_column.Id} (water {water * 100f:0}%); no new water stop for {QuietAfterTakeoverSeconds / 60f:0} minutes.");
                        _quietUntil = now + QuietAfterTakeoverSeconds;
                        if (_column != null) _cooldown[_column.Id] = Time.time + ColumnCooldownSeconds;
                        _phase = Phase.Idle;
                        _column = null;
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// The loader's own reach for this tender, less a small margin: CarLoadTargetLoader fills a LoadTarget whose centre is
        /// within target.radius + loader.radius of the spout, horizontally. 0.55 m for the F-71 tender at a water column.
        /// </summary>
        private float ReachMeters()
        {
            float reach = DefaultReachMeters;
            try
            {
                Car tender = FuelReader.Tender(_loco);
                int slot = FuelReader.WaterSlot(tender);
                CarLoadTarget hatch = tender != null ? tender.GetComponentsInChildren<CarLoadTarget>().FirstOrDefault(t => t.slotIndex == slot) : null;
                if (hatch != null && _column?.Loader != null) reach = hatch.radius + _column.Loader.radius;
            }
            catch (Exception) { }
            return Mathf.Max(0.2f, reach - ReachMarginMeters);
        }

        /// <summary>A Road speed (mph, in steps of 5, at least 5) the train can stop from in the distance left, braking gently.</summary>
        private static int StoppableMph(float meters)
        {
            float v = Mathf.Sqrt(2f * StopDecelMps2 * Mathf.Max(0f, meters - 30f)) * 2.23694f;
            return Mathf.Max(5, Mathf.FloorToInt(v / 5f) * 5);
        }

        /// <summary>True while the column's loader is asked to load (its spout is down or coming down), by anyone.</summary>
        private bool SpoutRequested()
        {
            var kvo = _column?.Sequencer?.keyValueObject;
            if (kvo == null) return false;
            try { return kvo[_column.Sequencer.readWantsLoadingKey].BoolValue; }
            catch (Exception) { return false; }
        }

        private void StartFilling(float now, float water)
        {
            _phase = Phase.Filling;
            _phaseStart = now;
            _lastWater = water;
            _lastFlow = now;
            _flowSeen = false;
            Request(true);
            Say("Taking water.");
            Main.Mod.Logger.Log($"Water stop ({Name}): stopped at {_column.Id}, water {water * 100f:0.0}%, loader requested.");
        }

        /// <summary>
        /// Measures the hatch against the spout. True when it is within reach (or cannot be measured), so filling can
        /// start. False with why == null when a correction was ordered; false with a reason when spotting has
        /// failed and the train should hold for the player.
        /// </summary>
        private bool CheckSpot(float now, out string why)
        {
            why = null;
            if (!TrySpotError(out float miss, out float lateral)) return true;
            Main.Mod.Logger.Log($"Water stop ({Name}): hatch {Mathf.Abs(miss):0.00} m {(miss >= 0f ? "short of" : "past")} the spout along the track ({lateral:0.00} m to the side).");

            float reach = ReachMeters();
            float off = Mathf.Sqrt(miss * miss + lateral * lateral);
            bool reachable = off <= reach;
            if (!reachable)
            {
                // A swinging spout (the companion mod College-Educated Spouts) can turn to a hatch the fixed spot misses.
                Car tender = FuelReader.Tender(_loco);
                if (SpoutBridge.CanReach(_column?.Loader, tender, FuelReader.WaterSlot(tender), out string how)) reachable = true;
                if (how != null) Main.Mod.Logger.Log($"Water stop ({Name}): hatch {how}.");
            }
            if (!float.IsNaN(_approachAim) && _corrections == 0)
            {
                // The approach waypoint was _approachAim past the spout; the hatch stopped -miss past it.
                _approachGap = Mathf.Clamp(_approachAim + miss, 0.3f, 2.5f);
                Main.Mod.Logger.Log($"Water stop ({Name}): the train stopped {_approachGap:0.00} m before its waypoint (aim {_approachAim:0.00} m past the spout); {GradeText()}, time scale {Time.timeScale:0.#}.");
                _approachAim = float.NaN;
                SaveGap(true, _approachGap);
            }
            else if (_lastCorrSign != 0f && !float.IsNaN(_lastMiss))
            {
                float moved = _lastCorrSign * (_lastMiss - miss);
                if (moved > MovedAtLeastMeters)
                {
                    _correctionGap = Mathf.Clamp(_lastCorrOut - moved, 0.3f, 2f);
                    Main.Mod.Logger.Log($"Water stop ({Name}): the correction moved {moved:0.00} m of {_lastCorrOut:0.00} m, so the train stops {_correctionGap:0.00} m before a correction's waypoint.");
                }
                _lastCorrSign = 0f;
                if (moved > MovedAtLeastMeters) SaveGap(false, _correctionGap);
            }
            if (!float.IsNaN(_lastMiss) && Mathf.Abs(miss - _lastMiss) < MovedAtLeastMeters && !reachable)
            {
                _stuckCorrections++;
                Main.Mod.Logger.Log($"Water stop ({Name}): the last correction did not move the train ({_stuckCorrections}).");
            }
            _lastMiss = miss;

            if (reachable) return true;
            if (Mathf.Abs(miss) > MaxCorrectionMeters) { why = $"the hatch is {Mathf.Abs(miss):0} m from the spout"; return false; }
            if (_corrections >= MaxSpotCorrections || _stuckCorrections >= 2)
            {
                why = $"the hatch is still {Mathf.Abs(miss):0.00} m {(miss >= 0f ? "short of" : "past")} the spout after {_corrections} correction{(_corrections == 1 ? "" : "s")}";
                return false;
            }

            // A waypoint again, placed past where the train should end up: one within about a metre of the train counts
            // as reached at once, and the train stops about that far before it. Neither a short waypoint nor a short Yard-mode move makes the train move
            // (2.5.4 and 2.5.5). Short: pull ahead. Past: back up with the other end leading, so that end arrives.
            bool fwd = miss > 0f ? _origForward : !_origForward;
            float corrOut = Mathf.Max(Mathf.Abs(miss) + _correctionGap, 1.15f);   // under about a metre counts as reached at once
            Location lead = HeadLocation(fwd, out _);
            Location target = lead.IsValid ? Graph.Shared.LocationByMoving(lead, corrOut, false, true) : Location.Invalid;
            if (!target.IsValid) { why = "there is no track to move along"; return false; }
            _corrections++;
            _lastCorrOut = corrOut;
            _lastCorrSign = miss > 0f ? 1f : -1f;
            _waypointString = Graph.LocationToString(target.Clamped());
            Helper.SetOrdersValue(AutoEngineerMode.Waypoint, fwd, Mathf.Min(_setMaxSpeed, 10), null, new OrderWaypoint(_waypointString, null));
            _expectMode = AutoEngineerMode.Waypoint;
            _graceUntil = now + 3f;
            _correctedAt = now;
            Say(miss > 0f ? "Pulling up to the spout." : "Backing up to the spout.");
            Main.Mod.Logger.Log($"Water stop ({Name}): correction {_corrections}, {(miss > 0f ? "ahead" : "back")} {Mathf.Abs(miss):0.00} m (waypoint {corrOut:0.00} m out, {_waypointString}).");
            return false;
        }

        /// <summary>
        /// Spotting failed: stop at the column (Road mode, speed 0, same direction) and hand over to the player rather
        /// than run on short of water. A full tender (the player took water by hand) resumes the road orders; any
        /// orders the player gives end the stop.
        /// </summary>
        private void Hold(float now, string why)
        {
            Request(false);
            Helper.SetOrdersValue(AutoEngineerMode.Road, _origForward, 0, null, null);
            _phase = Phase.Holding;
            _phaseStart = now;
            _graceUntil = now + 3f;
            Say($"I can't line the tender up with the spout at {_column.Place}: {why}. Holding here; take water by hand and I'll carry on, or give me new orders.");
            Main.Mod.Logger.Log($"Water stop ({Name}): holding at {_column.Id}, {why}.");
        }

        // ---- Status (for DetailedStatus) ----

        internal enum StatusKind { Planned, Slowing, Spotting, Filling, Holding }

        internal struct StatusInfo
        {
            public StatusKind Kind;
            public string Label;
            public float? DistanceM;
            public string Detail;
        }

        /// <summary>What this locomotive's water stop is doing, for the detailed status. False when no stop is under way.</summary>
        internal static bool TryStatus(BaseLocomotive loco, out StatusInfo info)
        {
            info = default;
            WaterStop ws = loco != null ? loco.GetComponent<WaterStop>() : null;
            if (ws == null || !Main.Settings.WaterStop || ws._column == null || ws._phase == Phase.Idle) return false;
            string place = ws._column.Place;
            switch (ws._phase)
            {
                case Phase.Armed:
                {
                    float? d = null;
                    if (!float.IsNaN(ws._lastDistance))
                    {
                        // Counted down between the 2.5 s checks at the current speed.
                        float travelled = Mathf.Abs(loco.velocity) * Mathf.Max(0f, Time.time - ws._lastDistanceTime);
                        d = Mathf.Max(0f, ws._lastDistance - travelled);
                    }
                    bool slowing = ws._setMaxSpeed < ws._origMaxSpeed;
                    info.Kind = slowing ? StatusKind.Slowing : StatusKind.Planned;
                    info.Label = "Water Approach";
                    info.DistanceM = d;
                    info.Detail = $"Water stop (mod) planned at {place}{(d.HasValue ? ", hatch " + DetailedStatus.Distance(d.Value) + " from the column" : "")}"
                        + (slowing ? $"; speed held to {ws._setMaxSpeed} mph (orders were {ws._origMaxSpeed} mph)." : ".");
                    return true;
                }
                case Phase.Approaching:
                    info.Kind = StatusKind.Spotting;
                    info.Label = "Water Spotting";
                    info.Detail = $"Water stop (mod): Waypoint mode to put the tender hatch under the spout at {place}{(ws._corrections > 0 ? $" (correction {ws._corrections})" : "")}; Road orders resume after.";
                    return true;
                case Phase.Holding:
                    info.Kind = StatusKind.Holding;
                    info.Label = "Water Hold";
                    info.Detail = "Water stop (mod): the tender could not be lined up with the spout. Take water by hand and the engineer resumes its road orders, or give it new orders.";
                    return true;
                case Phase.Filling:
                {
                    float water = FuelReader.WaterFraction(FuelReader.Tender(loco));
                    info.Kind = StatusKind.Filling;
                    info.Label = "Water Fill";
                    info.Detail = $"Water stop (mod) at {place}: tender at {(float.IsNaN(water) ? "?" : (water * 100f).ToString("0"))}%. Filling to {FullFraction * 100f:0}%, then back to {(ws._origMode == AutoEngineerMode.Waypoint ? "the waypoint" : "Road")} orders at {ws._origMaxSpeed} mph.";
                    return true;
                }
            }
            return false;
        }

        // ---- Phases ----

        private void Cancel(string why)
        {
            Main.Mod.Logger.Log($"Water stop ({Name}): cancelled, {why}.");
            if (_column != null) _cooldown[_column.Id] = Time.time + ColumnCooldownSeconds;
            _phase = Phase.Idle;
            _column = null;
            _requested = false;
        }

        private void GiveUp(string why)
        {
            Main.Mod.Logger.Log($"Water stop ({Name}): giving up at {_column?.Id}, {why}.");
            Finish($"Couldn't take water here: {why}. Back to road orders.");
        }

        private void Finish(string say)
        {
            if (_column != null) _cooldown[_column.Id] = Time.time + ColumnCooldownSeconds;
            Helper.SetOrdersValue(_origMode, _origForward, _origMaxSpeed, null, _origMode == AutoEngineerMode.Waypoint ? _origWaypoint : null);
            Say(say);
            _phase = Phase.Idle;
            _column = null;
            _requested = false;
        }

        private void Request(bool loading)
        {
            if (_column?.Sequencer == null || _column.Sequencer.keyValueObject == null) return;
            if (!loading && !_requested) return;
            string id = _column.Sequencer.keyValueObject.RegisteredId;
            if (string.IsNullOrEmpty(id)) return;
            StateManager.ApplyLocal(new PropertyChange(id, _column.Sequencer.readWantsLoadingKey, new BoolPropertyValue(loading)));
            _requested = loading;
            try { SetHatch(loading); }
            catch (Exception e) { Main.Mod.Logger.Warning($"Water stop ({Name}): tender hatch: {e.Message}"); }
        }

        /// <summary>
        /// Opens the tender's water hatch with the column (and puts it back as it was afterwards) by sending the same
        /// message as clicking it. The hatch is a KeyValuePickableToggle built from the tender definition's
        /// ToggleAnimation; its key differs between tenders ("water.hatch", "hatch.water", "hatch"), so the one used is a
        /// toggle with "hatch" in its key, preferring "water", then the one nearest the water LoadTarget. Cosmetic: the
        /// column's loader does not check it.
        /// </summary>
        private void SetHatch(bool open)
        {
            if (open)
            {
                _hatchObjectId = null;
                _hatchKey = null;
                Car tender = FuelReader.Tender(_loco);
                if (tender == null) return;
                int slot = FuelReader.WaterSlot(tender);
                CarLoadTarget target = tender.GetComponentsInChildren<CarLoadTarget>().FirstOrDefault(t => t.slotIndex == slot);
                KeyValuePickableToggle hatch = tender.GetComponentsInChildren<KeyValuePickableToggle>(true)
                    .Where(t => t != null && !string.IsNullOrEmpty(t.key) && t.key.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(t => t.key.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ThenBy(t => target != null ? Vector3.Distance(t.transform.position, target.transform.position) : 0f)
                    .FirstOrDefault();
                KeyValueObject kvo = hatch != null ? hatch.GetComponentInParent<KeyValueObject>() : null;
                if (kvo == null || string.IsNullOrEmpty(kvo.RegisteredId)) return;
                _hatchObjectId = kvo.RegisteredId;
                _hatchKey = hatch.key;
                _hatchWasOpen = kvo[hatch.key].BoolValue;
                if (!_hatchWasOpen) StateManager.ApplyLocal(new PropertyChange(_hatchObjectId, _hatchKey, new BoolPropertyValue(true)));
                Main.Mod.Logger.Log($"Water stop ({Name}): tender hatch '{_hatchKey}' {(_hatchWasOpen ? "already open" : "opened")}.");
            }
            else if (_hatchObjectId != null)
            {
                if (!_hatchWasOpen) StateManager.ApplyLocal(new PropertyChange(_hatchObjectId, _hatchKey, new BoolPropertyValue(false)));
                _hatchObjectId = null;
                _hatchKey = null;
            }
        }

        private void Say(string text)
        {
            try { _loco.AutoEngineerPlanner?.Say(text); }
            catch (Exception) { }
        }

        private bool NearWaypoint(bool forward)
        {
            Graph graph = Graph.Shared;
            if (graph == null || string.IsNullOrEmpty(_waypointString)) return true;
            Location target = graph.ResolveLocationString(_waypointString);
            Location head = HeadLocation(forward, out _);
            if (!head.IsValid || !target.IsValid) return true;
            return Vector3.Distance(graph.GetPosition(head), graph.GetPosition(target)) < 25f;
        }

        // ---- Track geometry ----

        /// <summary>The leading end of the consist in the direction of travel, facing ahead (as the planner's StartLocation).</summary>
        /// <summary>
        /// The track grade in the direction of travel under the lead end (rise over the next 30 m), for the log: the
        /// stop gap varied from 1.0 to 1.8 m for the same 1,907 t train at different columns and grade is a suspect.
        /// </summary>
        private string GradeText()
        {
            try
            {
                Graph graph = Graph.Shared;
                Location head = HeadLocation(_origForward, out _);
                if (graph == null || !head.IsValid) return "grade unknown";
                Location ahead = graph.LocationByMoving(head, 30f, false, true);
                if (!ahead.IsValid) return "grade unknown";
                Vector3 a = graph.GetPosition(head);
                Vector3 b = graph.GetPosition(ahead);
                float run = new Vector2(b.x - a.x, b.z - a.z).magnitude;
                if (run < 5f) return "grade unknown";
                float grade = (b.y - a.y) / run * 100f;
                return $"grade {grade:+0.0;-0.0;0.0}% in the direction of travel";
            }
            catch (Exception) { return "grade unknown"; }
        }

        private Location HeadLocation(bool forward, out Car headCar)
        {
            Car.End end = forward ? Car.End.F : Car.End.R;
            List<Car> cars = _loco.EnumerateCoupled(end).ToList();
            headCar = cars.Count > 0 ? cars[0] : _loco;
            Car.LogicalEnd logical = _loco.EndToLogical(end);
            Location loc = headCar.LocationFor(logical);
            return logical == Car.LogicalEnd.A ? loc : loc.Flipped();
        }

        private bool TryFindColumnAhead(bool forward, float minAhead, out Column best, out float bestDistance)
        {
            best = null;
            bestDistance = float.MaxValue;
            List<Column> columns = Columns();
            if (columns.Count == 0) return false;

            float now = Time.time;
            foreach (Column c in columns)
            {
                if (_cooldown.TryGetValue(c.Id, out float until) && now < until) continue;
                if (!TryDistanceTo(c, forward, minAhead, out float d, out _, out _)) continue;
                if (d < bestDistance)
                {
                    best = c;
                    bestDistance = d;
                }
            }
            return best != null;
        }

        /// <summary>
        /// Distance from the tender's water hatch to the column along the route as it is lined now, if the column is at
        /// least minAhead ahead of the hatch. The walk starts at the hatch's point on the track (the lead end moved back
        /// by the lead-end-to-hatch distance), so the cars ahead of a shoving engine are counted. Falls back to the lead
        /// end when the hatch cannot be found (headToHatch is then 0). from is where the walk started.
        /// </summary>
        private bool TryDistanceTo(Column column, bool forward, float minAhead, out float distance, out Location from, out float headToHatch)
        {
            distance = float.MaxValue;
            Location head = HeadLocation(forward, out _);
            from = head;
            headToHatch = 0f;
            Graph graph = Graph.Shared;
            if (graph == null || !head.IsValid || column == null || !column.Location.IsValid) return false;

            float offset = HeadToHatch(head);
            if (offset > 0.5f)
            {
                Location hatch = graph.LocationByMoving(head, -offset, false, true);
                if (hatch.IsValid)
                {
                    from = hatch;
                    headToHatch = offset;
                }
            }

            float maxMeters = Mathf.Clamp(Main.Settings.WaterStopSearchKm, 1f, 100f) * 1000f + headToHatch;
            RouteWalker walker = graph.WalkRoute(from, maxMeters);
            while (walker.TryNext(out WalkSegment ws))
            {
                if (ws.Segment != column.Location.segment) continue;
                float fromEntry = graph.GetDistanceBetweenClose(ws.EntryLocation, column.Location);
                float toExit = graph.GetDistanceBetweenClose(column.Location, ws.ExitLocation);
                if (Mathf.Abs(fromEntry + toExit - ws.TraversedLength) > 1f) continue;   // on this segment but not in the walked part
                float d = ws.EntryDistance + fromEntry;
                if (d < minAhead) continue;
                distance = d;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The waypoint that stops the lead end so that the tender's water hatch is under the spout: the spout's track
        /// point moved ahead by the lead-end-to-hatch distance (plus Waypoint Queue's 0.5 m allowance). The spout is
        /// oriented toward the hatch, which is always short of it, so "ahead" stays the direction of travel even when
        /// the lead end of a shoved train has already passed the column.
        /// </summary>
        private bool TryWaypointFor(Column column, Location hatchAt, float headToHatch, out Location target)
        {
            target = Location.Invalid;
            Graph graph = Graph.Shared;
            if (graph == null || headToHatch <= 0.5f) return false;

            Location toward = graph.LocationOrientedToward(column.Location, hatchAt);
            target = graph.LocationByMoving(toward, -(headToHatch + _approachGap), false, true);
            return target.IsValid;
        }

        /// <summary>
        /// Distance along the track from the lead end back to the tender's water hatch (its LoadTarget), or 0 when
        /// there is no hatch to find. Measured along the track, not in a straight line: with a long train ahead of the
        /// tender on a curve the straight line is shorter, and the tender stopped short of the spout (2.5.3, Whittier).
        /// Cached for the rest of the tick, since the column search asks once per column.
        /// </summary>
        private float HeadToHatch(Location head)
        {
            if (Time.time == _offsetTime) return _offsetValue;
            _offsetTime = Time.time;
            _offsetValue = 0f;
            Graph graph = Graph.Shared;
            if (graph == null || !head.IsValid || !TryHatchGame(out Vector3 hatchGame)) return 0f;
            _offsetValue = AlongBehind(graph, head, hatchGame, out _);
            return _offsetValue;
        }

        /// <summary>The tender's water hatch in game coordinates, as CarLoadTargetLoader.LoadSlotFromCar works it out.</summary>
        private bool TryHatchGame(out Vector3 hatchGame)
        {
            hatchGame = Vector3.zero;
            Graph graph = Graph.Shared;
            Car tender = FuelReader.Tender(_loco);
            int slot = FuelReader.WaterSlot(tender);
            if (graph == null || tender == null || slot < 0) return false;
            CarLoadTarget hatch = tender.GetComponentsInChildren<CarLoadTarget>().FirstOrDefault(t => t.slotIndex == slot);
            if (hatch == null) return false;
            Matrix4x4 m = tender.GetTransformMatrix(graph);
            hatchGame = m.MultiplyPoint3x4(tender.transform.InverseTransformPoint(hatch.transform.position));
            return true;
        }

        /// <summary>
        /// How far back along the track from 'from' (facing ahead) the point nearest to 'game' is (horizontally), and
        /// how far to the side of the track 'game' then is. Steps of 1 m, then 5 cm around the best one. The search
        /// starts at the straight-line distance, which an arc is never shorter than.
        /// </summary>
        private static float AlongBehind(Graph graph, Location from, Vector3 game, out float offset)
        {
            float chord = Horizontal(graph.GetPosition(from), game);
            float lo = Mathf.Max(0f, chord - 2f);
            float hi = chord * 1.15f + 20f;
            float best = lo;
            float bestD = float.MaxValue;
            for (float d = lo; d <= hi; d += 1f) Probe(d);
            float a = Mathf.Max(0f, best - 1f), b = best + 1f;
            for (float d = a; d <= b; d += 0.05f) Probe(d);
            offset = bestD;
            return best;

            void Probe(float d)
            {
                Location p = graph.LocationByMoving(from, -d, false, true);
                if (!p.IsValid) return;
                float h = Horizontal(graph.GetPosition(p), game);
                if (h < bestD)
                {
                    bestD = h;
                    best = d;
                }
            }
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        /// <summary>
        /// Stopped at the column: how far the hatch is from the spout along the track (positive = short of it, so the
        /// train must pull ahead), and how far to the side. Both are measured back from the lead end, so they share
        /// one reference.
        /// </summary>
        private bool TrySpotError(out float miss, out float lateral)
        {
            miss = 0f;
            lateral = 0f;
            Graph graph = Graph.Shared;
            if (graph == null || _column == null || !TryHatchGame(out Vector3 hatchGame)) return false;
            Location lead = HeadLocation(_origForward, out _);
            if (!lead.IsValid) return false;
            float toHatch = AlongBehind(graph, lead, hatchGame, out lateral);
            float toSpout = AlongBehind(graph, lead, graph.GetPosition(_column.Location), out _);
            miss = toHatch - toSpout;
            return true;
        }

        // ---- Columns ----

        private static List<Column> Columns()
        {
            if (_columns != null && _columns.All(c => c.Loader != null)) return _columns;

            var list = new List<Column>();
            Graph graph = Graph.Shared;
            if (graph == null) return list;

            CarLoadTargetLoader[] loaders = UnityEngine.Object.FindObjectsByType<CarLoadTargetLoader>(FindObjectsSortMode.None);
            CarLoaderSequencer[] sequencers = UnityEngine.Object.FindObjectsByType<CarLoaderSequencer>(FindObjectsSortMode.None);
            CTCBlock[] blocks = UnityEngine.Object.FindObjectsByType<CTCBlock>(FindObjectsSortMode.None);

            foreach (CarLoadTargetLoader loader in loaders)
            {
                if (loader == null || !loader.enabled || loader.keyValueObject == null) continue;
                if (!string.Equals(loader.loadId, "rr/water", StringComparison.OrdinalIgnoreCase)) continue;
                string id = loader.keyValueObject.RegisteredId;
                if (!graph.TryGetLocationFromWorldPoint(loader.transform.position, 10f, out Location loc)) continue;

                CarLoaderSequencer sequencer = sequencers.FirstOrDefault(s => s != null && s.keyValueObject != null && s.keyValueObject.RegisteredId == id);
                if (sequencer == null) continue;

                Vector3 game = graph.GetPosition(loc);
                string place = OpsController.Shared?.ClosestAreaForGamePosition(game)?.name ?? id;
                CTCBlock block = blocks.FirstOrDefault(b => b != null && b.GetComponentsInChildren<TrackSpan>(true).Any(s => s != null && s.IsValid && s.Contains(loc)));
                list.Add(new Column { Loader = loader, Sequencer = sequencer, Id = id, Place = place, Location = loc, Block = block });
            }

            _columns = list;
            if (!_loggedColumns && list.Count > 0)
            {
                _loggedColumns = true;
                Main.Mod.Logger.Log("Water stop: " + list.Count + " water columns found: " + string.Join(", ", list.Select(c =>
                    $"{c.Id} ({c.Place}, block {c.Block?.id ?? "none"}, rate {c.Loader.outputRate}/s, reach {c.Loader.radius} m, max {c.Loader.maximumSpeedInMph} mph, key {c.Sequencer.readWantsLoadingKey})")));
            }
            return list;
        }
    }
}
