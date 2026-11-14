# College Educated Auto Engineer (Railroader)

Formerly Auto Engineer Brake Fix (versions up to 2.9.16). The mod folder and identifier are now `CollegeEducatedAutoEngineer` and `exp232/collegeeducatedautoengineer`; copy `Settings.xml` from the old `Mods\AutoEngineerBrakeFix` folder to keep your settings, and delete that old folder.

Makes the AI engineer drive like a person: fewer wasted coal and water, a smooth throttle,
gentle brake sets for signals, and no creeping past a stop.

The features are on by default and can be switched off in the in-game settings window (Ctrl+F8, applied at once). Options without a control there are edited in `Settings.xml` in the mod folder (read at start-up). A copy of `Settings.xml` is kept in `Documents\Railroader\CollegeEducatedAutoEngineer-Settings.xml` and restored if an update replaces the mod folder (delete both to reset to defaults). Logging is off by default.

## Brake fix
In road mode the AI shares one speed setpoint between throttle and brake, with no dead zone. A tiny overspeed applies about 3% train brake, and the AI only releases it once its brake output goes clearly negative, which on a climb at full throttle may never happen: throttle and brake both on for minutes.

While the throttle is open and the train is within a small margin of the AI's own setpoint (default 0.5 mph), the mod releases the brake and blocks new light applications. Real overspeed is left to the AI, so signals, slow orders and downhill control still brake normally.

Test on a 26-car train over 12 miles: throttle+brake overlap fell from 10.3% of the time to 0.4%, with no change in speed.

## Shorter cutoff
The stock AI uses the longest cutoff that reaches peak power, but steam and coal use rise with cutoff while pulling power does not. Above 5 mph the mod picks the shortest cutoff that gives up no more than a set percentage of that power (default 3%, `CutoffTolerancePercent` in `Settings.xml`), and never a longer one than stock.

Same route and train: coal fell from 4,136 lb to 2,334 lb (-44%) and water by 26% for a few seconds more running time. On climbs of 2.0% or more (adjustable 0.5-5%) the mod hands back to the stock cutoff.

## Throttle smoothing
The stock AI can flip the throttle between 0 and 25% every half second (on 0.6 the rollout controller sets a notch each tick; since 2.8.3 the filter works on that, as it did on the PID before). The mod low-pass filters it (default 1.6 s, `ThrottleSmoothingSeconds` in `Settings.xml`), limits how fast it can open (full in 5 s) or close ("Smooth diesel throttle" in the settings window switches this off on diesels, for use with another mod that smooths the diesel notches), and with "Glide" on moves the lever about 20 times a second between the AI's updates, so it moves smoothly in the cab. If another mod rounds a diesel's throttle to the nearest notch (the lever reads back as an exact notch after every write), the "Diesel notch guard" switches smoothing off for that locomotive after a few ticks so it can still notch up. When the train is over its setpoint and needs the brake, the throttle closes at once.

## Signals
- **Sight distance** (default 500 m, the game uses 200 m): how far ahead the AI reacts to a signal's aspect.
- **Approach signals**: the game applies the Approach speed limit "right now", which makes the AI brake at full effort. The mod applies it at the signal, so the train slows gradually, and keeps the limit until the next signal is registered so the train doesn't speed up in between.

## Slowing down and stopping
- **No power into reductions**: with a lower speed limit (5 mph or more) in sight and the train already at or above it, the throttle is closed so the train coasts down before braking. Not applied on climbs steeper than 1.5% or below 5 mph.
- **Stay stopped**: within 2.5 m of a stop target the AI now holds the stop; the stock AI can treat a train stopped 1-3 m short of a signal as "should be moving" and creep past it.

## Direction-change pause (1.3.3)
When the engineer is told to run the other way it stands still for at least `DirectionChangePauseSeconds` (3 by default, 0 = off; slider in the settings window) before moving off, with the brakes held and the throttle closed, and blows the stock stop blast first if the game skipped it (`DirectionChangeHorn`). Time the train has already stood counts. After the pause the game's own start-up runs (brake release, forward or reverse whistle, reverser).

## Hill-start spool-up (1.3.3)
Starting on an upgrade of `HillStartMinGradePercent` (0.5% by default, Settings.xml) or more, the throttle is raised to the start-up value while the engineer waits for the train brake to release with the locomotive brake still applied, so the locomotive is already pulling when the brake lets go (`HillStartSpoolUp`, toggle in the settings window).

## Gentle direction-change stop (0.6.0)
Stopping a moving train with no speed order (changing direction, or a speed order of 0) now uses the train brake only: the locomotive (independent) brake is bailed off above 2 mph and only holds the train once it is nearly stopped (`DirectionStopTrainBrakeOnly`, also in the settings window).

Changing direction on a moving train clears the engineer's speed order and targets, so it plans a stop with no distance and brakes at the game's maximum (about 0.29 of a full pipe reduction). While the train is moving with no speed order and nothing ahead to stop for, the mod lowers the brake limit the planner works with to 0.15 (about 13 psi), so it plans a longer, gentler stop. The limit lifts as soon as a new order, target or signal appears, or when the train is almost stopped (under 2 mph). `DirectionStopBrakeLimit` in Settings.xml; 0 turns it off.

The train brake alone has nothing to hold on a locomotive running light, so this stop is only used when the cars behind the locomotives and tenders are at least half the train's weight (`DirectionStopMinTrailingPercent` in Settings.xml, 50 by default; 0 = always). Otherwise the game's own controller stops the train, using the locomotive brake. If the train has not slowed by 0.5 mph in 15 seconds under this stop, the stop is handed back to the controller as well.

## Planner weights (0.6.0)
The auto-engineer's speed controller scores simulated plans. Five multipliers scale its weights (1 = the game's value): `PlanOverspeedScale`, `PlanUndershootScale`, `PlanUndershootDeadbandScale`, `PlanCeilingScale` (these four in `Settings.xml` only) and `PlanIndependentUseScale` (the "Locomotive brake use" slider in the settings window, 0 to 5). For example a higher undershoot scale or a smaller deadband scale makes it hold closer to a limit it is slowing for. All 1 by default.

## Speed limits
- **No power into a reduction**: while a lower limit is in the AI's sight and the train is at or above that speed, the throttle stays closed. Power returns once the train is a mph under the lower limit.
- **Earlier, lighter braking**: a lower limit is treated as starting 10 s of travel sooner (adjustable, 6 s before 2.4.3), and the train brake is released when the speed 3 s ahead reaches the target. Speeds land on the limit instead of under it.
- **Brake with the bleed-down in mind**: a long train's brake cylinders drain slowly after a release and keep slowing the train (roughly psi squared / 60 mph on a 52-car train). The train brake is released once the cylinders will carry the train the rest of the way to the limit (within 0.5 mph), and a new application is held off until they no longer do. Not used near a stop or on a descent. The divisor and margin are adjustable.
- **Coasting band**: a new brake application on a steady limit waits until the train is 1 mph over it.
- Stops are not shifted. In Waypoint and Yard modes the game's own stopping accuracy is kept; the stay-stopped fix is Road mode only.

## Train preparation (on by default)
When a standing train gets a new order to move (any mode: Road, Yard or Waypoint; the engineer switched on, set from 0 mph to a speed, or given a new mode or waypoint), the mod releases the handbrake on every car, connects the air between each pair of coupled cars with both anglecocks open, and closes the anglecocks at the two ends of the train. It waits until the train is under about 2 mph and does it once per order. Switch it off in the settings window ("Prepare train on waypoint order"). With Waypoint Queue loaded it is skipped for waypoint orders, which that mod handles itself (`WaypointQueueCompat` in `Settings.xml`). `LogCarKeys` in `Settings.xml` writes a dump of each car's key-value entries to `logs\carkeys_*.txt` (used to find the names the game uses for doors, gates and lamps).

## Doors, gates and lamps
- **Doors at station stops (on):** the doors and gates of passenger cars, baggage cars, cabooses and work cars open car by car when the engineer stops at a station, each car closes its own late in the stop, and any still open close in turn when the engineer departs. In every mode, open doors and gates are closed before the train moves, and the gate or vestibule door on a car end coupled to a locomotive or tender is the only one kept shut.
- **Observation car headlight (on):** when the observation car leads the train (the engineer backing it), its headlight is bright while moving and dim while stopped, like the locomotive's, and goes off when it stops leading.
- **Marker lamps (on):** the marker lamp at the rear end of the train shows red and every other marker lamp on the train is switched off; set when the train is standing and the train, its cars or its direction change (also with the engineer off, using the direction it last had). Class lights are left to you. `MarkerRedPosition` in `Settings.xml` is the lamp position that shows red outward (0 to 3, 2 by default).

## Stall guard
The game has no stall check: on a grade the train cannot climb, the AI cycles between full throttle and light braking while rolling back. The mod latches the guard in three cases: the train **rolls back** more than 0.5 mph against its order (at once, once it has moved the ordered way or had power on while standing); it has been at full throttle for 10 s (`StallSeconds` in `Settings.xml`) under 4 mph on a climb of 1% or more without gaining speed; or it cannot start at all (under 0.5 mph with the throttle at 50% or more and the brakes off for the same time, on any grade). When it latches it closes the throttle, holds the brakes (train brake, plus the locomotive brake once nearly stopped) and tells you in the console and as a notice. In Waypoint and Road modes it also **sets the speed order to 0 mph**; set a speed again to retry. The hold ends when you change the engineer's direction, mode or waypoint, set a speed order again, or switch the engineer off and on.

## Realistic fuel use (on by default)
Switch it on or off with the "Realistic fuel" toggle in the settings window (Ctrl+F8); it takes effect at once. The steam rate, efficiency and coal scale are edited in `Settings.xml`.

The game's coal and water use rises with speed even though a locomotive's power falls with speed, so fuel per horsepower is about right at 10-20 mph and 1.5-2x too high above 30 mph. With this option, steam = steam rate (default 21.5 lb per indicated hp-hour) x the engine's own power x a cutoff factor, and coal follows from the steam through an evaporation curve (8 lb water per lb coal at light firing, 7 at moderate firing, 5.5 at heavy firing). On the Bryson to Sylva Interchange round trip (EMR 5000, the game's 2-10-2 after the Southern Railway Ss class, about 1,931 short tons, 38.4 miles) this comes to about 23 lb of water and 3.4 lb of coal per indicated horsepower-hour, and about 116 lb of coal and 95 gal of water per 1,000 gross ton-miles (replayed from the logs); the game's own use on the same train was about 36 lb of water and 4.8 lb of coal per indicated horsepower-hour, and 172 lb and 153 gal per 1,000 gross ton-miles, and it ran out of water 3.9 miles short of the end. For comparison: Goss's Purdue tests (*Superheated Steam in Locomotive Service*, 1910) measured 21.6 lb of steam and 2.97 lb of coal per indicated horsepower-hour for a superheated engine at 200 psi in steady running (25.5 and 3.43 saturated), and about 20% more in service with stops; the ICC's Class I road-freight averages were 162 lb of coal per 1,000 gross ton-miles in 1921, 137 in 1926, 125 in 1929 and 111 to 117 from 1937 to 1943 (all routes, locomotives included). Logged use matched the tender within 1%.

By design nothing is burned while the throttle is closed, so a locomotive left idling does not run out of coal and water. A realistic idle burn may come later as a separate "hardcore" option.

## Starting resistance (on by default)
The game uses the Davis equation for rolling resistance, which is only valid for a moving train: a standing car meets about 1.6 lb per ton, where real plain-bearing cars need 5-10 lb per ton to break away. This option adds a standing-start drag (default 7 lb per ton, fading out exponentially with speed: 37% left at 2.5 mph, about 13% at 5 mph). The "Fades out by" slider in the settings window sets the speed where the drag has faded out. Standing time (`StartingResistanceStandingTime` in `Settings.xml`, on by default) scales it by how long each car has stood: a car that has just stopped still has oil in its journals and starts at about 40% of the full drag, settling back over about 10 game minutes. It uses the game clock and is not saved, so cars count as fully settled when you load a save.

## Install
Needs Railroader 0.6.0 or later; uses the game's built-in mod loader (Unity Mod Manager is no longer needed).
1. Unzip into `Documents\Railroader\Mods`, so you have `Mods\CollegeEducatedAutoEngineer\CollegeEducatedAutoEngineer.dll`, `0Harmony.dll` and `Info.json`.
2. Start the game and enable "College Educated Auto Engineer" in the game's mod list.
3. Settings: press Ctrl+F8 in game; changes apply at once and are saved to `Mods\CollegeEducatedAutoEngineer\Settings.xml` (written with the defaults on first start). Options without a control are edited in that file and need a game restart.

At start-up the mod checks that everything it patches is still in the game. Features whose targets are missing are switched off and the rest keep working; the result is one summary block in the game log (`Player.log`).

## Fuel in fast-forward
The game charges fuel and water once per second of real update, whatever the time scale, so a fast-forwarded trip used about a third of its coal. The mod charges the game time that has actually passed (`FuelFollowsTimeWarp`, on by default; normal speed is unchanged).

## Fusee in fast-forward
Fast-forward switches the main camera off, and the game's fusee code (`Effects.FuseeRenderer.Update`) then throws a `NullReferenceException` on every frame for as long as a fusee is near the train. The mod skips that update while there is no camera (`GuardFuseeCamera`, on by default). It only silences the error; nothing visible changes.

## Coast band (1 mph by default)
Set "Coast band" in the settings window (1 mph by default). The engineer then closes the throttle when coasting would hold its speed and it is within the band below its target, or when the downhill will pick the train up to its target within the pickup horizon (30 s by default, `CoastBandAssistSeconds` in `Settings.xml`; slower than that the engineer accelerates on its own), and it waits to brake until it is more than the band over its target, unless a lower limit or a stop ahead needs the brakes. This stops the power-then-brake hunting on downhill and rolling track. Road and Waypoint modes.

## Water stop for Road-mode engineers (on by default)
Switch on "Water stop" in the settings window (Ctrl+F8). When the tender's water drops below the "Stop below" level (25% by default), a Road-mode engineer looks ahead along the route as it is lined for the nearest water column or tower, tells you where it will take water, and slows to the approach speed (25 mph by default, `WaterStopApproachMph` in `Settings.xml`) about half a mile out, and further near the column so it can stop in the distance left; distances are measured from the tender's water hatch, so a shoved train with cars ahead of the engine is judged correctly. It keeps to your routing until the train is in the column's signal block, then switches to Waypoint mode just long enough to spot the tender under the spout, fills it with the column's own equipment, and goes back to Road mode at the speed and direction it had. With no column in sight it looks again every second. A train standing still counts a column any distance ahead of the hatch; a moving one needs about 30 m, since it could not stop in less. Changing its orders yourself cancels the stop. Works from Road or Waypoint orders (fast forward needs a Waypoint order); with a waypoint it only stops for a column before the destination and puts the waypoint back afterwards. Steam locomotives only; host only. The spotting method follows PrinceOfPluto's Waypoint Queue mod (MIT). Once stopped it checks where the hatch ended up and, if it is not under the spout, moves the train up or back (up to three times, as far as 100 m) before taking water. If the tender still can't be lined up, or no water comes in, the engineer stops at the column and hands over to you: take water by hand and it carries on with its road orders once the tender is full and the spout is up, or give it new orders yourself.

## Swinging water spouts (now a separate mod)
Swinging spouts moved out in 2.7.0 to their own mod, **College-Educated Spouts** (`exp232/collegeeducatedspouts`): water columns and towers that turn to the tender hatch before they lower. It is not required, but highly recommended with this mod: when it is installed, the water stop counts a hatch that a turning spout can reach as in reach (and logs "College-Educated Spouts found"). Remove any `spoutswing.txt` from this mod's folder and put it in College-Educated Spouts' folder instead; the `SpoutSwing*` and `HatchWidthFactor` entries in `Settings.xml` are ignored now.

## Legacy speed limits (on by default)
Since 0.6 the game no longer shows the map's per-track speed limits (the old speed boards are removed when a game loads), but the auto engineer still obeys them, so trains slow down for no visible reason ([forum topic 306](https://community.railroadergame.com/t/306)). Switch on "Fix legacy limits" in the settings window: on the mainline (track inside CTC signal blocks, which includes signalled passing sidings) these limits are cleared, so the railroad speed limit, curves and track class decide the speed. On branches, yards and spurs the limits stay (no boards are put up). Nothing is saved to the map; switching it off puts the limits back.

## Detailed engineer status (on by default)
Switch on "Detailed status" in the settings window. The status line under the auto engineer controls then says what is setting the speed and how far ahead it is, for example "Curve Speed: 15mph in 420 ft", "Equipment Speed: 15mph", "Track Class: 25mph", "Track Speed: 45mph", "Engine Speed: 41mph", "Water Approach: 20mph in 2,000 ft" or "Water Fill". The wording follows the game's own ("Track Speed", "Station Stop"), and a distance is shown only for a speed reduction ahead. Hover it for the details: which car's curve rating, what the track alone would allow, the posted or railroad limit, the tender's water level and the next targets ahead with their distances. It updates as often as the engineer replans (every half second to three seconds). Distances are in feet and miles; "Metric distances" switches to metres and kilometres. Only the text changes, never the speeds. The tooltip detail is shown on the host.

## Diagnostic log columns
The `ae_*.csv` log has `routeMph`, `routeDropM`, `routeCause` (the slowest limit in 1.5 km of route as the mod walks it, where it first drops, and why: class, equipment, curve, legacy, railroad) and `zones` (the first speed zones of the route profile the game gives the speed controller, `mph@startM`). In a test run the game listed every reduction 600-1,450 m ahead.

With **Position and G forces** on (Diagnostics section of the settings window, `LogGForces`), each row also has the locomotive's position and the forces on it: `posX`, `posY`, `posZ` (game coordinates in metres, `posY` = elevation), `trackLoc` (the front of the locomotive as a track location, in the format the `/location` command copies), `area` (the nearest ops area), and `gX`, `gY`, `gZ` in g. The axes are the locomotive's own: X forward (toward its front, F, end), Y to the right, Z up. Values are what an accelerometer on the locomotive would read, gravity included: standing on level track reads 0, 0, 1; a 2% grade adds about 0.02 to X (nose up) and a right-hand curve gives a positive Y (v²/r). The game simulates cars along the track only, so the mod works these out every physics tick from the locomotive's speed and the track geometry under it, then smooths them (`GForceSmoothingSeconds` in `Settings.xml`, 1 s; 0 = raw) to take out slack and coupler jitter. Only the locomotive is measured. Like the rest of the log, rows are written only while the engineer is running and the train is moving, and the first reading comes after the locomotive has moved half a metre.

## Settings window
Press **Ctrl+F8** in game (not at the main menu) to open the settings window. It keeps to the settings most people want: toggles for throttle smoothing, glide, no power into reductions, stopping with the train brake, the stall guard, starting resistance, realistic fuel, water stop, legacy limits, detailed status and the two fast-forward fixes, and six sliders (brake tap limit, coast band, slowing lead, locomotive brake use, starting-resistance fade speed, water stop level). A separate **Diagnostics and testing** section holds the logging switches, all off by default. The self-check result per feature is at the bottom. Changes apply immediately and are saved to `Settings.xml`. Everything else (smoothing time, stall time, the other planner weights, cutoff tolerance and so on) is still edited in that file.

With the [Mod Settings Hub](https://github.com/Exp232/ModSettingsHub) installed (optional), this page also appears in the hub's window (Ctrl+F7, or the button in the top-right menu). Ctrl+F8 still opens this mod's own window.

## Settings (summary)
In the settings window (Ctrl+F8): toggles for throttle smoothing, glide, no power into reductions, stopping with the train brake, stall guard, starting resistance, realistic fuel, water stop, fuel in fast-forward, legacy limits, detailed status, metric distances and the fusee fix; sliders for brake tap limit (`BrakeTapSuppressMph`, 2 by default, off at 0), coast band (`CoastBandMph`, 1), slowing lead (`SpeedReductionLeadSeconds`, 10), locomotive brake use (`PlanIndependentUseScale`), starting-resistance fade speed and the water stop level (25%). The **Diagnostics and testing** section holds the logging switches (all off by default): the diagnostic log (CSVs under the mod folder's `logs`), decision logging (the controller's chosen action and costs), signals (`signals_*.csv`), CTC dump (`ctc_dump_*.txt`, read-only), position and G forces (`LogGForces`), the fuel summary (`LogFuelSummary`, with gross tons and coal and water per 1,000 gross ton-miles, and for a diesel its fuel per mile; written only while the diagnostic log is on) and tender coal and water.

Only in `Settings.xml`: overspeed margin, cutoff tolerance and steep-grade limit, smoothing time, stall time, direction-change brake limit and its minimum car share, the other four planner weights, realistic-fuel steam rate, cutoff effect and coal scale, signal sight distance, approach limit at the signal, hold after the signal, approach speed when no signal follows (25 = stock), `CtcDropBlocks` (temporary map workaround for Sylva West eastbound, `signalId:blockId` pairs, empty = off), coast-band pickup horizon, water-stop approach speed and standing time for starting resistance.

## Compatibility
Built for Railroader 0.6.x (played on 0.6.2). Patches the auto-engineer's rollout controller commands, its target list and contextual target speed, and the planner's signal-sight distances. Other mods patching the same code may conflict (it has been run alongside WaypointQueue). Disable it in the game's mod list to restore stock behaviour.

## Known limits
Tested on steam-hauled freight runs, mostly up to 1% grades, and on a short diesel run (an SD7). Diesel fuel use follows the game's own model, which burns nothing at idle. Fuel savings depend on the route and train.
