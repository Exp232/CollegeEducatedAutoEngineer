# Changelog

## 1.3.1

- **Fix: the position and G-force logging switched itself off at start-up.** The self-check looked for `Graph.GetPositionRotation(Location)`, but the game's method has an optional second parameter (`PositionAccuracy`), which is part of its signature, so the check reported the target missing and turned `LogGForces` off (and saving the settings window would have written that). The check now finds the method by its name and first parameter, and the same is done for `OpsController.ClosestAreaForGamePosition`. The meter itself was checked against a simulation of a curve, a grade and a steady acceleration: a right-hand curve at 15 m/s on a 200 m radius reads gY +0.115, a 2% grade reads gX +0.020, an acceleration of 0.5 m/s² reads gX +0.051, and standing level reads (0, 0, 1), forward and backward.

## 1.3.0

- **Logger: locomotive position and G forces.** New "Position and G forces" switch in the Diagnostics section (`LogGForces`, off). Each `ae_*.csv` row gets `posX`, `posY`, `posZ` (game coordinates, m; `posY` = elevation), `trackLoc` (the locomotive's front as a track location, as `/location` writes it), `area` (nearest ops area, refreshed every 2 s) and `gX`, `gY`, `gZ`: the felt force on the locomotive in g, on its own axes (forward toward the F end, right, up), gravity included (level and standing = 0, 0, 1). Worked out every physics tick from the speed along the track and the track tangents at both ends of the locomotive (`Graph.GetPositionRotation`), then low-pass filtered (`GForceSmoothingSeconds`, 1 s, `Settings.xml` only) to take out slack. Locomotive only. Self-check feature "Position and G-force logging".

## 1.2.5

- **Waypoint Queue compatibility: train preparation is skipped for waypoint orders.** Waypoint Queue takes over waypoint orders, releases handbrakes and connects air on coupling through its own per-waypoint options, and sets handbrakes deliberately on the cars it cuts off. With it loaded, the mod no longer releases handbrakes, connects air or closes anglecocks when a waypoint order starts; Road and Yard orders still get it. Controlled by the same `WaypointQueueCompat` option in `Settings.xml` (on). At the Waypoint Queue author's suggestion.

## 1.2.4

- **Waypoint Queue compatibility: the waypoint's speed target is left where it is.** Waypoint Queue rewrites the speed of the "Running to waypoint" / "At waypoint" target (the passing speed of a waypoint that does not stop) and finds that target again by its reason and its distance, within 10 m. The early-slowing shift for lower limits (`SpeedReductionLeadSeconds`, 10 s, about 90 m at 20 mph) moved that same target, so the distances no longer matched and the passing speed was not applied. With Waypoint Queue loaded the shift now skips the waypoint targets. `WaypointQueueCompat` in `Settings.xml` (on) turns this off. Found with the Waypoint Queue author's permission and his pointer to the AE targets.

## 1.2.3

- **Settings are backed up outside the mod folder** (requested on Discord). Every time they are saved, a copy of `Settings.xml` goes to `Documents\Railroader\CollegeEducatedAutoEngineer-Settings.xml`. When the mod starts and the mod folder has no `Settings.xml` (a mod manager replaced the whole folder on an update), the copy is restored first. To reset to defaults, delete both files.
- **New option `SmoothDieselThrottle`** (on; a toggle "Smooth diesel throttle" in the settings window). Switch it off when another mod already smooths the diesel notch stepping, such as Rex's Toolbox for MOW: the two filters in series can leave the lever lagging the controller's request so that the engineer never notches up. With it off the controller's own notch goes through on diesels; steam locomotives are unchanged.

## 1.2.2

- **The observation car's headlight now follows the game's headlight calls at once.** It used to be set every half second from the train's speed, so it went bright a moment after the tender lamp would have. While the observation car stands in for the locomotive headlight, the game's own "bright" and "dim" calls are passed on to the observation car as they happen.

## 1.2.1

- **Fix: the tender headlight came back on while backing.** The game re-lights the locomotive headlight on nearly every update, so switching it off once was undone at once. While the observation car leads and carries the light, the game's own headlight calls (`HeadlightOn`, `HeadlightDim`) are now skipped, so the tender lamp stays off. When the observation car stops leading, the game takes the headlight back.

## 1.2.0

- **The observation car's headlight takes over from the locomotive's when it leads the train** (backing or pushing). The locomotive's own headlight, the tender lamp facing the train, is switched off through the game's headlight logic while the observation car is lit (bright moving, dim stopped). The game log lists the headlight states it found ("Headlight states: ...") the first time.

## 1.1.9

- **Fix: no marker lamp was ever lit.** The game log from 1.1.8 showed one marker lamp counted per car (a coach has two) and "no marker lamp on the rear car" even on the observation car. The definition's end for each marker lamp was read as a "front" or "rear" letter in a way that gave "front" for every lamp, so the rear lamp was never found. The lamp ends are now read as either car ends (F, R, 0, 1) or logical ends (A, B), converted for the way the car sits, and a lamp is red when its end is the free end of the last car. The first time each car type is seen the game log lists the marker lamp's definition fields ("Marker lamp in rr/...: ...") so a wrong reading can be spotted.

## 1.1.8

- **The game log now says what the marker lamps were set to**, each time they are redone: the train, how many cars have marker lamps, which end of which car was set red (and the position used), and how many other lamps were switched off, or that the rear car has no marker lamp. This is to tell from a log whether the lamps are being written, since a dump cannot tell the mod's changes from the player's.

## 1.1.7

- **Marker lamps are rechecked whenever cars are coupled or uncoupled**, and not only when the engineer is on: the rear of the train is found again every 2 seconds with the engineer off (using the direction it last had) and every half second with it on, and the lamps are redone when the list of cars, the rear car or the direction changes and the train is standing. (The check compared only the car count and the rear car before.)
- **`MarkerRedPosition` defaults to 3** (the value tested; it was 2). Your own `Settings.xml` value is kept.

## 1.1.6

- **All doors open at a station stop; only the vestibule door or gate immediately behind the locomotive is kept shut.** The locks from 1.1.5 (side-door cars, "vestibule" and "inner" names) and the free-end rule are gone. Every door and gate of the passenger, baggage, caboose and work cars now opens, bathroom and toilet doors included (windows are still left alone), except the gate or "Front Door"/"Rear Door" on a car end coupled to a locomotive or tender, which stays shut and is closed if open.
- **Marker lamps are automatic** (`AutoMarkers`, now on and a toggle in the settings window; the saved value is reset once with the settings version, 5). The marker lamp at the rear end of the train shows red (`MarkerRedPosition`, 2 by default) and every other marker lamp on the train, including the locomotive's and tender's, is switched off. It is set when the train is standing and the train or its direction changes. Class lights are left to the player.

## 1.1.5

- **Baggage cars, cabooses and work cars join the station stop.** Their doors open at a station stop and close as each car finishes, like the passenger cars', and are closed before the train moves. (They were only closed before moving, and baggage cars only for end doors.)
- **Inner vestibule doors and platform gates are locked shut.** On a car that has side doors (for example the 1915 Osgood-Bradley), the front and rear doors into the cabin and the front and rear gates are never opened by the mod, and are closed if they are open; only the side doors work. Controls whose name says "vestibule" or "inner" are locked too.
- **The baggage car's vestibule doors are now found.** Their definition names are "Door 5" and "Door 6" and only the clicked object is called "Rear Door" and "Front Door", so the end rule (shut beside a locomotive or tender, or at the end of the train) did not see them before. The object name is now read as well.

## 1.1.4

- **Fix: the observation car's side and vestibule doors were never worked.** Doors and gates were found by the title in the car definition, and the observation car's (`door.b`, `door.e`, `main.door`, `gate`) have no title. The name and the key are now read as well; bathroom, toilet and window controls are still left alone.
- **Fix: baggage car doors were not closed before the train moved.** Only a baggage car's end doors beside a locomotive were closed. Now every door of the baggage cars is closed, like the passenger cars', as soon as the engineer is to move. (Baggage cars' doors are not opened at stations.)

## 1.1.3

- **Fix: the gate on the wrong end was kept shut when a car sits the other way round.** Which end of a car a gate or vestibule door is on was taken from its "front" or "rear" name. It now comes from where the door or gate actually is in the world, found through the game's click toggle for it, compared with the car's coupled neighbours: a door on the side facing a locomotive or tender, or the side with no car, stays shut. The name is only the fallback if the toggle cannot be found.

## 1.1.2

- **Doors and gates are closed before the train moves, in any AE mode and at any place.** When the engineer is on with a speed above 0 and no station stop is being served, every open door and gate of the passenger cars is closed at once (also ones you opened by hand), and anything still open when the train moves faster than about 0.7 mph is closed. At a station stop the cars still close one by one as they finish; the cars still open at departure now close 0.15 s apart (was 0.5 s).
- **End gates and vestibule doors stay shut beside a locomotive or tender, or at the end of the train.** On a passenger or baggage car, a gate (for example the 1915 Osgood-Bradley's front and rear gates) or the "Front Door" or "Rear Door" vestibule door (for example on the baggage car) on a car end that is coupled to a locomotive or tender, or has no car on it, is never opened by the station stop, and is closed if it is open.

## 1.1.1

- **Fix: the doors never opened at a station stop** (1.0.9 and 1.1.0). The check used the game's `IsStoppedAtStation`, which needs the train within 0.1 m of the stop point, and the engineer halts about a metre short. It now treats the train as stopped at a station when the passenger stopper has a station stop registered within 10 m (its own arrival radius) and the train is standing. The game log says when it stops at a station and how many cars it will work, and a failure in the doors and lamps code is now logged once instead of being swallowed.

## 1.1.0

- **Doors and gates open and close gradually at a station stop.** They open car by car (about 0.8 s apart along the train) when the engineer stops at the station, and each car closes its own doors at its own time late in the stop (between 55% and 95% of the game's minimum passenger stop time), as if its loading were done. When the engineer decides to depart, any car still open closes in turn, half a second apart. The game does not track loading per car, so the closing times are spread over the stop rather than read from it.

## 1.0.9

- **Doors and gates open at station stops** (`DoorsAtStations`, on, a toggle in the settings window). While the engineer is stopped at a station stop (the game's own passenger-stop check), the doors and gates of the passenger cars open; they close when the train moves off. The key names come from each car's definition (title "Door", "Gate" or "Gates"; bathroom and toilet doors and windows are left alone), so every car model works.
- **Observation car headlight** (`ObservationHeadlight`, on, a toggle in the settings window). When a car with its own headlight leads the train (the observation car when the engineer backs the train), the headlight is bright (2) while moving and dim (1) while stopped, like the locomotive's, and goes off (0) when the car stops leading.
- **Marker lamps** (`AutoMarkers`, `Settings.xml` only, off: marker lamps are the player's). When on, the marker lamp at the rear end of the train shows red (`MarkerRedPosition`, 2 by default; each position is a quarter turn of the lens) and every other marker lamp on the train is switched off. Set when the train is standing and the train or its direction changes.

## 1.0.8

- **Train preparation works in every mode** (Road, Yard and Waypoint), not only for waypoint orders. It runs once when the engineer is switched on, set from 0 mph to a speed, or given a new mode or waypoint, and the train is standing.
- `LogCarKeys` now also writes a new dump on each of those new orders (at most one every 20 seconds), so it can be repeated without restarting the game, for example after opening a door or switching a lamp.

## 1.0.7

- **Train preparation on a waypoint order** (requested on Nexus; `PrepTrain`, on by default, a toggle in the settings window). When a standing train gets a new waypoint order, the mod releases the handbrake on every coupled car, connects the air between each pair of coupled cars with both anglecocks open, and closes the anglecocks at the two ends of the train. It waits until the train is under about 2 mph, then does it once per order, and logs what it changed to the game log. Cars are changed through the same car controls and end gear the game's own handbrake and anglecock controls use.
- `LogCarKeys` (`Settings.xml` only, off): writes `logs\carkeys_*.txt` with every key-value entry on each car of the train. This is to find the key names for passenger doors and gates and for lamps, which the next release will set at station stops. Run it with a passenger train, then a caboose and an observation car.

## 1.0.6

- **No throttle smoothing at crawling speeds** (reported on Nexus: the engineer slows down too much to couple, mostly on curved or slow industry track with an SW1 or other diesel). The smoothing and glide lag the lever by a second or two and limit its rise to 0.2 a second, which is enough for a diesel creeping at 3 mph to lose its speed before the notch comes up. Now the controller's own throttle goes through unfiltered while the target speed is under 6 mph, or a coupling approach is within 60 m (`CrawlSmoothingMaxMph` in `Settings.xml`; 0 = smooth always, as before). Not yet confirmed against a log.

## 1.0.5

- **Water stop looks again every second when it finds no column** (it waited 5 s between looks), so a column that comes into range is picked up at once.
- **A train standing still now accepts a column of any distance ahead of the tender hatch** (the minimum was 30 m, so a column just ahead of a standing train was never found). The stop then goes on at the next tick instead of waiting, and does not cancel for a close column. A moving train keeps the 30 m minimum (5 m once the stop is armed).

## 1.0.4

- **Fix: a locomotive running light could roll on without braking after a stop with no target** (reported on Nexus: it skips waypoints and coasts away until "Stop with the train brake" is switched off). That stop bails off the locomotive's own brake and uses the train brake alone, which has nothing to hold on a lone locomotive, so it rolled on unbraked until it was under 2 mph (the stop applies when the speed order is 0, the direction is changed on a moving train, or the game clears the targets, for example at a pitfall stop). Now it is only used when the cars behind the locomotives and tenders are at least half the train's weight (`DirectionStopMinTrailingPercent`, 50 by default, in `Settings.xml`; 0 = always); otherwise the game's own controller stops the train with the locomotive brake, as without the option.
- **Safety net:** if the train has not slowed by 0.5 mph in 15 s under that stop (a long descent, weak brakes), the mod hands the stop back to the controller for the rest of that stop, and the 13 psi limit lifts with it. Both are logged once in the game log.

## 1.0.2

- Added the Nexus Mods upload workflow (`.github/workflows/nexus-upload.yaml`). No code change.

## 1.0.1

- README brought in line with the settings window: the opening line, install step 3, the coast band, water stop, starting resistance, stall guard and planner-weight sections, and a rewritten settings summary that says which options are only in `Settings.xml`. No code change.

## 1.0.0

- **First release under the new name, College Educated Auto Engineer** (formerly Auto Engineer Brake Fix, which reached 2.9.16). Version numbers restart at 1.0.0. The mod identifier, folder, assembly and `Settings.xml` are unchanged, so an installed copy and its settings carry over; only the name shown in the game's mod list, the settings window and the Mod Settings Hub changes. The project moved to https://github.com/Exp232/CollegeEducatedAutoEngineer.
- Everything from the 2.x line is included: smooth throttle, brake-tap and coast-band filters, no power into speed reductions, gentle signal braking, stall guard with rollback detection, train-brake-only direction stops, water stop, shorter steam cutoff, optional realistic fuel use and starting resistance, and a simpler settings window with a separate diagnostics section (all logging off by default).

## 2.9.16

- **Water stop level defaults to 25%** (the code default was 60% while the README said 30%). A value already saved in Settings.xml is kept.

## 2.9.15

- **Starting resistance is 7 lb per ton** (was 5; real plain-bearing cars need roughly 5-10 or more to break away). The "Drag at 0 mph" slider is gone; its place is taken by a "Fades out by" slider for the speed where the drag falls to 37% (2.5 mph by default). The drag is still `StartingResistanceLbPerTon` in Settings.xml; a saved value is reset to 7 once (settings version 4).

## 2.9.14

- **Simpler settings window**: sixteen sliders became six (brake tap limit, coast band, slowing lead, locomotive brake use, starting-resistance drag, water stop level); the rest (smoothing time, pickup horizon, cutoff tolerance, stall time, resistance fade and standing time, water approach speed, the four other planner weights) stay in Settings.xml and keep working. Sections regrouped, notes shortened.
- **Diagnostics and testing section** with all the logging switches (diagnostic log, decisions, signals, CTC dump, fuel summary, tender coal and water). Decisions, signals, CTC dump and fuel summary now default to off, as the diagnostic log already did. An existing Settings.xml keeps the values it has. A change applies the next time the engineer is switched on.
- "Reset these to defaults" only resets what the window shows (not the diagnostics).

## 2.9.13

- Removed the experimental "Slowest limit in 1.5 km" setting and its code (it slowed the train far too early and was off by default). The route columns in the diagnostic log stay. An old `FullRouteTrackSpeed` entry in Settings.xml is ignored.

## 2.9.12

- Release pass: README brought up to date (stall guard and rollback behaviour, diagnostic route columns, compatibility and known limits); "Reset these to defaults" now also resets the fuel summary, fuel fast-forward and fusee toggles. No behaviour change.

## 2.9.11

- **"Slowest limit in 1.5 km" (2.9.9) is off by default and reset once** (settings version 3). The 17:14 run showed it applies the slowest limit in the next 1.5 km from the first speed drop, so the train crawled at 11-17 mph for about 140 s while the real 15 mph zone was still about 900 m away. The game's own route profile (the `zones` column) already listed every reduction 900-1450 m ahead, so it does not need this. The CSV route columns stay.

## 2.9.10

- **Route columns in the CSV** (to find why limits are met late): `routeMph`, `routeDropM` and `routeCause` give the slowest limit in 1.5 km of route as the mod walks it, where it first drops, and why (class, equipment, curve, legacy, railroad); `zones` lists the first speed zones of the route profile the game gives the rollout controller (`mph@startM`). Compare them with `targets`.

## 2.9.9

- **See the slowest track limit ahead**: in the 2.9.8 run, limits of 25 and 15 mph were listed only 0-30 m before the train reached them. The mod now walks 1.5 km of route itself and makes the Track Speed target the slowest limit on it (at the first drop), so there is no stepping down. It only lowers the game's target, and writes a line to the game log when it differs from the game's. Setting `FullRouteTrackSpeed`, on by default, toggle in the window.

## 2.9.8

- **Locomotive brake use slider**: the planner-weight slider for the locomotive (independent) brake now goes up to 5 (was 3) and has a note. Higher makes the planner trim speed and slow with the train brake instead. The wording in the settings window and README uses the game's name, locomotive brake.
## 2.9.7

- **Road mode too**: a rollback (and any other stall) in Road mode now has the same effect as in Waypoint mode. The guard latches, sets the speed order to 0 mph, holds the brakes and tells the player; setting a speed again clears it.
## 2.9.6

- **Rollback latches the stall guard at once** (Waypoint mode). The 4 s full-throttle count from 2.9.5 is gone: as soon as the train rolls back against its order (over 0.5 mph) the guard latches, sets the speed order to 0 mph, holds the brakes and tells the player. It is armed once the train has moved the ordered way or power has been applied to a standing train; a new direction or waypoint disarms it, so the run-out after a reversal does not count.
## 2.9.5

- **Stall guard in Waypoint mode**: a train at full throttle that slows to a crawl and then rolls back against its order (4 s or more of full throttle under 6 mph first) is now caught at once, wherever the planner's targets stand. In Waypoint mode the guard sets the speed order to 0 mph, holds the train with the brakes and says so; set a speed again (or a new waypoint) to retry. Before, the guard only counted full-throttle time, which the game's own change-direction stop cut short, so the train looped through throttle and rollback.
- **The guard no longer clears itself** when the planner swaps in empty targets while it stops the train: it now reads the engineer's orders (mode, direction, speed, waypoint) instead of its targets.
- **Stops use the train brake**: stopping a moving train with no speed order and nothing ahead (changing direction, or a speed order of 0) now bails off the independent brake above 2 mph and drives the train brake (still capped by `DirectionStopBrakeLimit`). The independent brake only holds the train once it is under 2 mph. The stall guard's hold does the same. New setting `DirectionStopTrainBrakeOnly` (on), also in the settings window.

## 2.9.4

- **Fix: the mod failed to load in 2.9.0-2.9.3** ("The given key 'AutoEngineer.SpeedControlLoop._ae' was not present"). The stall guard and no-power-into-reductions self-check entries named a target the check does not list. They now use the listed one, and an unknown target counts as missing instead of stopping the load.

## 2.9.3

- No code change. `CLAUDE.md` rewritten for the 0.6 rollout controller and the BuildWatcher build workflow.

## 2.9.2

- **New defaults** (your settings): realistic fuel, starting resistance, water stop (stop below 60% water, approach at 25 mph), legacy speed limit fix and detailed status are on; coast band is 1 mph. Diagnostic logging stays off by default. An existing `Settings.xml` keeps its own values; "Reset these to defaults" in the settings window now resets all of them.
- **Settings window reorganised**: sections are now Throttle, Braking and speed limits, Steam cutoff, Stall guard, Starting resistance (new: toggle, drag at 0 mph, fade-out speed, standing time), Fuel use, Water stop, Legacy speed limits, Engineer status, Planner weights, Logging and Fixes. Starting resistance can now be switched on from the window without a restart.

## 2.9.1

- **Old speed-loop code removed.** The brake-drag fix, the throttle and brake holds, the early brake release, residual braking, the PID registration and the PID columns in the diagnostic CSV (`thr_*`, `ind_*`, `tb_*`, `resMph`, `resNeedMph`, `fixBlocked`, `fixReleased`) are gone, along with their settings (`FixEnabled`, `FixLocomotiveBrake`, `SlamThrottleOnOverspeed`, `HoldThrottleWhileSlowing`, `HoldBrakeWhileSlowing`, `BrakeStartOverMph`, `BrakeReleaseLeadSeconds`, `BrakeResidual*`); an existing `Settings.xml` with them loads fine and they are dropped on the next save. `BrakeFix.cs` now only holds the control-state helpers, `DecelHold.cs` the shared target-list reads and the stay-stopped patch.

## 2.9.0

- **Stall guard is back** on the 0.6 rollout controller. It was built on the old PID loop and reported unavailable since 0.6.0. It now runs on every `ApplyRolloutCommand` (first, before the other filters): full throttle for `StallSeconds` on a grade at under 4 mph without gaining speed latches it, and a latched command becomes throttle 0, locomotive brake full, train brake 60% then full. The can't-start check, the start-up hold and the clear-on-direction/mode-change are unchanged. Settings window: "Stall guard" toggle and "Stall time" slider.
- **No power into reductions** (`AvoidPowerIntoReductions`, `ReductionHold.cs`) salvaged from "Holds while slowing" as a rollout filter: with a lower limit (5 mph or more) in sight and the train at or above it, the throttle is closed; power returns more than 1 mph under it. Skipped below 5 mph and on a climb steeper than 1.5%. Settings window toggle added.
- The rest of "Holds while slowing", the brake-drag fix and residual braking only worked on the PID loop; their self-check entries are removed.
- `Settings.xml` gets a `SettingsVersion`. Older files had `StallGuard`, `SmoothThrottle` and `AvoidPowerIntoReductions` saved as false by the self-check; they are reset to their defaults once on load.

## 2.8.4

- Throttle smoothing (2.8.3): fixed the throttle being cut to zero on a steep grade. When the train was a mph or so over a falling target the smoother's overspeed "slam" closed the lever from full to zero on a command that only lowered it one notch (EMR 1900 on a 4.6% grade: 15.5 mph to 10.3 mph over ten seconds, three times in one run, `cmdThr` 0.875 against `outThr` 0.0). That slam was written for the old PID loop, which could only lower the throttle 0.25 a tick. The rollout controller already lowers it one notch a tick and brakes when it must, so smoothing no longer slams on this path: an overspeed passes the controller's own command through. A command that brakes with the throttle already closed still closes it at once.

## 2.8.3

- **Throttle smoothing is back** on the 0.6 rollout controller. It was built on the old PID speed loop, which 0.6.0 replaced, so since 2.0.0 the self-check reported it as unavailable. The same filter as before (`ThrottleSmoothingSeconds`, default 1.6 s: low-pass, 0.2/s rise limit, 0.4/s fall limit, hand-over from the lever's real position) now runs on the notch the controller sets each tick (`RolloutThrottle.cs`, in the `ApplyRolloutCommand` prefix after the brake-tap and coast-band filters), and the glider eases the lever between ticks about 20 times a second (`GlideThrottle`). A command that brakes, any overspeed beyond `OverspeedMarginMph`, and a latched stall guard close or release the throttle at once. In the settings window (Ctrl+F8, "Fuel saving"): "Throttle smoothing" toggle, "Smoothing time" slider, "Glide between updates" toggle. `SmoothThrottle` is true in the code defaults, so a `Settings.xml` that has it false (as saved while it did nothing) must be switched on there. The `cmdMod` column shows `smooth` (also `tap+smooth`, `band+smooth`) when the lever differs from the controller's command. Untested in game: the controller reads the lever back each tick, so watch for it asking for more power than it gets (`cmdThr` against `outThr` in `ae_*.csv`).

## 2.8.2

- Water stop: a train stopped well short of the column (more than 10 m from the spout, waypoint still set) is no longer measured and "corrected" as if it had arrived. A maintenance-of-way train that stops for a task just before the water tower used to end in "I can't line the tender up with the spout" (issue #1); it now waits until it moves on and arrives. A train that never gets there still gives up after the approach timeout ("couldn't get to the column"). Untested in game; the cause is read from the code, not from a log.

## 2.8.1

- Mod Settings Hub: Ctrl+F8 opens this mod's own settings window again, as before 2.8.0, whether or not the hub is installed (2.8.0 sent it to the hub). The page still appears in the hub's window. No change to driving or fuel use.

## 2.8.0

- Works with the new **Mod Settings Hub** (`exp232/modsettingshub`): when it is installed, the settings page appears in its window (Ctrl+F7 or the button in the top-right menu). The link is by reflection (`HubLink.cs`), works in either load order, and is not listed in `Info.json`. No change to driving or fuel use.

## 2.7.5

- Logging only, no change to driving or fuel use: **diesel locomotives are now in the fuel logs**. The sampler log (`ae_*.csv`) fills `fuelId` ("diesel") and `fuelQty` (gallons in the locomotive's own tank, read from its fuel load slot) for a diesel, and has a new `dieselGps` column (the prime mover's fuel use, gallons a second, the same figure the game takes out of the tank). The fuel summary (`fuel_summary_*.csv`) has four new columns before `setup`: `dieselGal`, `dieselGalPerMile`, `dieselGalPer1000Gtm` and `dieselStandingGal` (fuel burned below 0.3 mph). Steam columns are unchanged and stay 0 for a diesel. A new column shifts the later columns; read them by name.

## 2.7.4

- Water stop: a station stop on the way to the column now comes first. At Almond the platform is 167 m short of the column, and the column's block is long, so the water stop switched to Waypoint mode 1.1 km out and the station stop ran under it. Now, while a "Station Stop" target is listed between the train and the column, the water stop stays armed in Road orders (the game serves the station as usual) and takes over once the platform is behind it. The log says "a station stop comes before the column ... waiting". The detailed status shows Station Stop, not Water Spotting, while the station is what the train is stopping for.

## 2.7.3

- Detailed status wording is closer to the game's own: **Curve Speed**, **Equipment Speed** (a curve held down by the train's tightest-rated car), **Track Class**, **Track Speed** (railroad-wide or legacy limit), **Engine Speed**, **Approach Hold**, **Stall Guard**, and for the water stop **Water Approach**, **Water Spotting**, **Water Fill** and **Water Hold**. No "(mod)" tags, curve degrees, class numbers, column names or water percentages in the status bar; they are in the tooltip (the tender's water level now reads there). The bar shows a distance only for a speed reduction ahead (a lower target than the limit in force, and the water approach); never for the limit under the train, a stall, a fill or the engine speed.
- Water stop: the approach hold (the speed step-down to the approach speed and what the train can stop from) now starts half a mile out, down from a mile (1,600 to 805 m). Heavy trains at track speed will be slowed later; the stoppable-speed ladder still applies inside it.

## 2.7.2

- Log only, no behaviour change: the `ae_*.csv` rows gain ten columns for the rollout controller's latest command: `cmdKind`, `cmdThr`, `cmdInd`, `cmdTb`, `cmdBail` as the game issued it, `outThr`, `outInd`, `outTb` as it left this mod's filters, `cmdMod` (`tap`, `band` or empty: which filter changed it) and `cmdAgeS` (seconds since it was applied). This shows who released the locomotive brake in the last metres of a station stop on an upgrade. The command is only recorded while the brake-tap or coast-band patch is installed (either setting on).

## 2.7.1

- Coast band: on a downhill the throttle used to stay closed however far below its target the train was, so it crept up to track speed at 0.1-0.2 mph a second. The grade now only keeps the power off when it will bring the train to its target within `CoastBandAssistSeconds` (new, 30 by default, slider "Pickup horizon"; 0 = the old rule). Slower than that the engineer opens the throttle and accelerates. Only affects runs with the coast band on.

## 2.7.0

- **Swinging spouts are now their own mod, College-Educated Spouts** (`exp232/collegeeducatedspouts`, 1.0.0), which is compatible with this one and highly recommended, but not required. All spout code, settings (`SpoutSwing`, `SpoutSwingDegrees`, `DropSpoutSwingDegrees`, `HatchWidthFactor`, `SpoutSwingDegreesPerSecond`), the "Swinging spouts" section of the settings window and its self-check entry are removed from this mod. Existing `Settings.xml` files still load: the old entries are ignored. Move `spoutswing.txt` to the new mod's folder if you edited it.
- The water stop still counts a hatch the column's spout can turn to as in reach, now by asking College-Educated Spouts when it is installed (`SpoutBridge.cs`, found by reflection; no reference to it). Without it, the water stop is as before.
- `Info.json` lists College-Educated Spouts as an optional dependency (`>=1.0.0`). The new mod refuses to load next to Auto Engineer Brake Fix older than 2.7.0, so there is never a double set of patches.

## 2.6.12

- Water column: takes back 2.6.11's mesh split. Asking the game's graphics side for the pole mesh's buffers emptied the mesh, so the pole was missing in game (`index buffer is null` in `Player.log`). The mesh is no longer touched. Instead the two still meshes are treated differently by height: the 5.4 m pole with its head bracket turns with the arm, and the 3.4 m lower mesh (the base) stays square.

## 2.6.10

- Water column: 2.6.9 could not split the pole mesh (the game's mesh is not readable, so `Player.log` showed nothing and the head stayed square to the track). A still mesh taller than 2.5 m (the pole with its head bracket) is no longer held square: it turns with the arm, so the head points with the spout. Only the low pieces (the square base) stay still. The split code is removed from use.

## 2.6.8

- Water column diagnostics only (no change in behaviour): after the arm swings out, and again once it is stowed, `Player.log` lists how far every mesh of the column has turned since it was parked (position about the axis / facing). The pipe swings 90° with the clip, so any piece of the stand that shows far less than that is the one left square to the track.

## 2.6.7

- Water column: takes back 2.6.6's swinging of the head, lever and hanging weights. It put the weights and crane out of place when stowed and spun them when the column turned. Those pieces are back to following the game's clip. Instead the log now records, while the clip plays, where the pipe and each of those pieces point (`clip down` / `clip up` lines in `Player.log`), so the next change can be built on what the clip really does.

## 2.6.5

- Swinging spouts: the stand now turns to the hatch first and only then lowers. The fill point is held out of reach until the turn has finished (within 1.5°), so loading, and with it the lowering animation, starts after the arm points at the hatch instead of while it is still swinging round.

## 2.6.4

- Water column swing: the click handle is now looked for again for ten seconds after the column loads, in case the game parks it on the arm late, and it is logged (where it sits, whether the mod turns it) once the arm has swung, to find out why it can miss.

## 2.6.3
- Swinging spouts, after the 2.6.2 test: the column's base now stays square and the tower is right, but the column's **click handle** did not engage once the arm had turned, and did not turn with it on the stow. The handle is a separate collider ("Control") that the prefab moves out of the animated arm when the column is built (`PrefabRuntimeReparent`), so it stays at the arm's parked spot while the arm turns away. Any such handle that is not on the animated arm is now turned with the arm each frame, about the same axis, and put back when the mod unloads. `Player.log` lists it under "also turning".

## 2.6.2
- Swinging spouts, after the 2.6.1 test: the tower's spout and hose chains turned as intended, and the column swung its arm over the tender, but the **column's base turned with the arm** (its square concrete pad ended up crooked beside the track). The base is part of the same animated object that turns. Renderers at the column's foot (within 0.5 m of its axis and no more than 0.25 m above it) are now turned back by the same angle about the same axis each frame, so the pad and flange stay square to the track and only the arm and pole turn. `Player.log` lists the parts held still. The click handle on the arm (outside the turning object) stays where it was; a column whose foot is built differently may need its parts adjusted.

## 2.6.1
- Swinging spouts, after the 2.6.0 test (Bryson, `Player.log`): the fills worked as before (EMR 1 44 s, EMR 5000 104 s) and nothing errored, but two things were wrong in the code.
  - **Both kinds were taken for drop-only spouts (15 degrees).** The test used `engageAfterNormalizedTime`, which is 0.00 on both the water column ("Water Column Arm") and the tower ("Scene"). The kind is now read from where the spout is parked: more than 1.5 m (flat) from the loader's fill point means it swings in from beside the track (the water column: 4.2 m, so up to `SpoutSwingDegrees`, 180); near it means it hangs and only drops (the tower: 0.7 m, `DropSpoutSwingDegrees`, 15). The dump in `Player.log` gives the distance.
  - **The tower turned its whole model.** The tower's animated object also holds the tank, legs and collider, so a larger swivel would have turned all of them about the tank's foot (7.3 m from the track). Drop-only spouts now turn only their own parts (the children named "spout" or "hose"), each frame after the animation, about the spout's hinge (about 3.1 m from the fill point), and put them back when the mod unloads. Side-to-side columns are unchanged: the whole column turns about its own axis (3.0 m arm).
  - The test only had trains stopped within 0.05 m of the spout (turns of 0.2 and 0.9 degrees), so a real turn toward an off-centre tender has not been seen yet.

## 2.6.0
- New, off by default: **swinging water spouts** (`SpoutSwing.cs`; "Swinging spouts" in the settings window, effective at once).
  - When a water column or tower spout is asked to load (by the player or the water stop), the host finds the nearest reachable tender hatch and the spout turns toward it about its own pivot before and while it drops. The game's clip (swing and drop, `ClearanceLimitedController`) plays unchanged on top, so the drop still stops 0.4 m above the tender. The angle is written to the column's synced key `aebf.swingYaw`; the spout turns back when it is put away.
  - Two spout kinds, told apart by the clip: side-to-side columns (clearance easing starts after a first phase, `engageAfterNormalizedTime` > 0) turn up to `SpoutSwingDegrees` (180) each way; drop-only spouts get a prototypical swivel of up to `DropSpoutSwingDegrees` (15) each way.
  - Hatch shape: the fill check (`CarLoadTargetLoader.LoadSlotFromCar`, prefix) uses an ellipse in the car's frame instead of the game's circle: half-length = the game's hatch radius, half-width = radius x `HatchWidthFactor` (2), each plus the loader's 0.2 m. The fill loop's position read (`LoadLoop`, transpiler) uses the turned spout tip.
  - `spoutswing.txt` in the mod folder: `hatch <car definition identifier> <half-length> <half-width>` for tenders that do not follow the rule, and `spout <map object id or structure name> <degrees>` for a column's own limit. Written with examples on first run; re-read at map load.
  - Diagnostics in `Player.log`: once per structure kind, the spout's object tree, clip, pivot, arm length, whether the loader rides on the spout, and the kind and limit chosen; the first time each spout is down, where it really fills; each turn (car, tender identifier, angle, fit, hatch size used).
  - Water stop: a hatch outside the fixed spot but within the turning spout's reach (fit 0.9 or better, 1 = hatch edge) counts as spotted, and the log says so.
  - Untested in game: the pivot is taken as the animated spout object's own origin; check the dump and the first swing at each kind of column.

## 2.5.14
- Water stop, fast forward test: EMR 5000 (1,907 t) took water at Almond (al-water) during fast forward (time scale 14 to 34) on the first spot, from Waypoint orders (2.5.8). The hatch ended 0.52 m past the spout, inside the 0.55 m reach but close to its edge. The train stopped 1.00 m before its waypoint there, against 1.24-1.78 m for the same train at Bryson, so the gap varies by about 0.4 m at one weight. Suspects: track grade, and the fast-forward time scale.
  - The "the train stopped ... before its waypoint" log line now also gives the track grade under the lead end in the direction of travel (rise over the next 30 m) and the time scale, so the variation can be matched against them. Logging only; nothing else changes.
  - Seed point for 1,907 t: 1.4 m (six measured stops, 1.00 to 1.78 m).

## 2.5.13
- Water stop weight table: new starting point for 146 t (EMR 1 with cars: 0.74 m, and the table's prediction of 0.74 m put the hatch 0.00 m from the spout), and the 1,907 t point is now the mean of five measured stops (1.5 m). Stops the mod measures still add to the table in `stopgaps.txt`.

## 2.5.12
- Water stop: one **weight table** for every train instead of gaps remembered per locomotive. How far a train stops before its waypoint depends on its weight, not on its engine: EMR 1 (74 t) stops 0.6-0.75 m short of its waypoint, EMR 5000 (1,907 t) 1.2-1.8 m. The table starts from those two measurements, reads smoothly between and beyond them (a Gaussian kernel across ln(weight), width 1.0), and every stop the mod measures (the approach, and each correction) adds a point at that train's weight, so weights that get hauled often follow their own results. The newest 20 of each kind are kept in `stopgaps.txt` in the mod folder (`a|c <gross tons> <gap>`); `Player.log` shows the weight and the gaps used. The per-locomotive file from 2.5.10 and 2.5.11 is ignored.

## 2.5.11
- Water stop: the learnt stop gaps are now kept per locomotive **and train weight** (`stopgaps.txt` has the car id, gross tons, approach gap and correction gap; up to 8 weights per locomotive). How far a train stops before its waypoint depends on its mass, and the same engine hauls different trains. A stop uses the entry nearest in weight (within 25%) and otherwise the defaults; each stop updates the entry for its weight, or adds one. `Player.log` says which it used. An old `stopgaps.txt` from 2.5.10 is ignored (the format changed) and replaced when a stop learns something.

## 2.5.10
- Water stop, 2.5.9 test: both trains filled. EMR 5000 stopped 0.21 m past (stop gap 1.24 m); EMR 1 stopped 0.78 m past on the approach (gap 0.67 m), then needed two corrections (0.60 m short, then 0.14 m short) because its first correction still used the default gap.
  - The learnt stop gaps are now kept per locomotive in `stopgaps.txt` in the mod folder and loaded next time, so the first approach of a later game already aims with them (they were forgotten at every restart).
  - The default gap for a correction's waypoint is 0.7 m (was 1.2 m): corrections measured 0.57-0.75 m on EMR 1.

## 2.5.9
- Water stop, small consists: the 2.5.8 test shows the train stops a different distance before its waypoint depending on the consist (EMR 5000 shoving 274 m: 1.4-1.8 m; EMR 1 alone: about 0.6-0.7 m). EMR 5000 filled on the first spot (0.33 m short, inside the 0.55 m reach). EMR 1 stopped 0.78 m past the spout, and its three corrections, each placed a fixed 1.2 m beyond the move, swung 0.78 past, 0.64 short, 0.62 past, 0.64 short, because the train really stops about 0.57 m before a correction's waypoint.
  - The mod now learns how far before its waypoint the train stops, per locomotive: from the approach (the aim past the spout against where the hatch ended up) and from each correction (the waypoint distance against the distance actually moved). The next approach aims with the learnt figure, and corrections use theirs (never under 1.15 m out, since a waypoint within about a metre counts as reached at once). The log shows both figures.

## 2.5.8
- Water stop now also works for an engineer on **Waypoint orders**. Fast forward (`FastForwardWrangler`) only runs while an engineer has a Waypoint order, so the stop, which started only from Road orders, never fired when the engineer was switched to Waypoint mode to fast forward. It now takes water for a column that lies before the waypoint (the engineer's own remaining waypoint distance decides; a column beyond the destination is skipped), and puts the original waypoint, direction and speed back afterwards. Hold (spotting failed) still stops in Road mode at speed 0. A stop in progress is cancelled if you change the mode, waypoint, direction or speed. Note: fast forward ends by itself when the engineer is about to arrive at a waypoint, which includes the spot waypoint at the column.

## 2.5.7
- Legacy speed limits: the speed boards are gone. Putting a board at each end of every limited stretch gave far too many on sidings. The mainline fix (clearing the legacy limit inside CTC blocks) is unchanged, and branches, yards and spurs keep their limits without boards. Boards left in a save by 2.5.5 are taken down once per load.
- Water stop: the log now records the game's time scale when a stop is planned and when it switches to Waypoint mode, to find out what happens during fast forward. Fast forward (`FastForwardWrangler`) only runs while an engineer has a Waypoint order, ends when none has, and stops on any game notice; a Road-mode water stop was not designed around that yet.

## 2.5.6
- Water stop, after the 2.5.5 run:
  - **Reach:** at Whittier the hatch stopped 0.45 m short, which is inside the spout's reach (column 0.2 m + F-71 hatch 0.4 m), but the mod's own tolerance was 0.35 m, so it tried to correct instead of filling. It now uses the loader's own reach (`CarLoadTargetLoader.radius` + `CarLoadTarget.radius`, less 5 cm) and counts the sideways offset too.
  - **Aim:** the two Whittier shoves show the train stopping 1.42-1.45 m before its waypoint (0.5 m allowance: 0.92 m short; 1.0 m: 0.45 m short), so the waypoint now goes 1.45 m past the spout position.
  - **Corrections:** Yard mode made no move for 0.45 m, just as a short waypoint made none for 0.92 m. Corrections are waypoints again, placed the move plus 1.2 m out (the game counts a waypoint within about a metre of the train as reached), at up to 10 mph, up to three, as far as 100 m either way. Backing up uses the other end of the train as the leading end.
  - **Approach speed:** at Dillsboro the column is about 60 m inside its block; the switch to Waypoint mode came at 25 mph with the hatch 62 m out, and the train ran 67 m past. Within the slow-down mile, the Road speed now also steps down to one the train can stop from in the distance left (gentle braking of 0.12 m/s², in 5 mph steps, at least 5 mph): about 25 mph at 600 m, 15 mph at 300 m, 5 mph under 100 m.
  - **Re-arming:** after you take over a stop, the engineer plans no other water stop for 5 minutes (it picked the next column within seconds).

## 2.5.5
- New, off by default: **legacy speed limits** (`LegacySpeedLimits`; "Fix legacy limits" in the settings window, effective within a few seconds). 0.6 removes the old speed boards (`TrackConditionController` deletes every "speed:" sign) but `TrackSegment.speedLimit` from the map still caps the auto engineer's track speed: on the Bryson map 382 segments are 25 mph, 83 are 15, 47 are 30 and 66 are 45, none of them shown (reported as forum topic 306).
  - Mainline, meaning any segment inside a CTC signal block (`CTCBlock` track spans; main tracks and signalled sidings): the legacy limit is cleared, so the railroad limit, curves and track class decide.
  - Branches, yards and spurs: the limit stays, and a speed board (the game's own "tsr" board, as used for player speed signs) goes up at each end of every limited stretch, with a tooltip. The boards are stored under the key prefix `aebf-legacy-speed:` and rebuilt on every load (host only).
  - In memory only: switching it off, or unloading the mod, puts the limits back and takes the boards down. `Player.log` gives the counts.
  - Side effect to know: the game's track wear and tie-paint checks use the same limit, so cleared mainline limits count as higher allowed speeds there too.
- Detailed status: a map limit now reads "Legacy limit" (it was "Posted limit", but nothing is posted), and the tooltip says where it comes from.
- Water stop spotting, after the second Whittier miss (hatch 0.92 m short, both corrections did nothing): the game completes a waypoint as soon as it is within 1 m of the train's end (`AutoEngineerPlanner.IsUnderTrain`), so a sub-metre waypoint is "reached" at once and the train never moves.
  - The waypoint now goes 1.0 m past the spout position (was 0.5 m, Waypoint Queue's figure).
  - Corrections are made in Yard mode with a move distance (the game counts down the metres actually travelled), up to three. A correction that moves the train less than 10 cm is counted as stuck; two of those end the attempt.
  - If no water flows within 30 s, the spout goes up, the spotting is measured again and corrected, and filling is tried once more.
- Water stop failure handling: when spotting fails or the water stops coming in below 95%, the engineer no longer drives off short of water. It stops at the column (Road mode, speed 0) and says so. If you take water by hand, it resumes its road orders once the tender is full and the spout is up; any orders you give end the stop. The detailed status shows "Water stop (mod): holding at …".

## 2.5.4
- New, off by default: **detailed engineer status** (`DetailedStatus`; "Detailed status" in the settings window under "Engineer status", effective at once). It replaces the planner's status line (`AutoEngineerPlanner.SetPlannerStatus`) with the same choice of limit, then:
  - says what sets a "Track Speed": "Posted limit", "Railroad limit", "Track class N", "Curve N°", or "Curve (equipment)" when the curve is sharper than the tightest-rated car in the train (the game cuts the speed towards 5 mph there);
  - adds the distance to a limit or stop ahead ("in 420 ft", "in 1.2 mi"), refreshed whenever the planner runs (every 0.5 s above 30 mph, 1 s below, 3 s stopped);
  - shows "Engine top speed" when the locomotive's top speed (where its tractive effort runs out) is below the limit;
  - names this mod's own events: water stop approach, spotting and filling (with the water level), approach hold, stall guard.
  - The status tooltip lists the reasoning (for equipment: which car, its rating, the curve and what the track alone allows), the engine top speed and the next five targets with distances. Host only for the tooltip; the line itself is the game's synced status.
  - `StatusMetric` ("Metric distances") switches feet and miles to metres and kilometres.
- Water stop: the tender stopped with its hatch out of the spout's reach on the 2.5.3 Whittier shove (no water flowed, it gave up after 90 s). The lead-end-to-hatch distance was a straight line, which is shorter than the track with 270 m of train on a curve; it is now measured along the track (the point on the train's own track nearest the hatch, in 1 m and then 5 cm steps).
- Water stop: once stopped, it measures how far the hatch is from the spout along the track and logs it. If it is more than 0.35 m off (the reach is 0.6 m), it pulls ahead or backs up by that much at 5 mph in Waypoint mode, up to twice, before calling for water.

## 2.5.3
- Water stop fix for shoved trains (cars ahead of the engine). On the reverse test the Whittier stop cancelled with "the column is no longer ahead on the lined route": the Whittier tower sits about 35 m inside block `wh-mm` from its `wh-e` end, so coming in from that end with 274 m of cars ahead of the tender the lead car reached the tower before it was seen in the block, and the "column ahead" check, which was measured from the lead car, failed.
  - Distances (search, slow-down point, "still ahead") are now measured from the tender's water hatch: the walk starts at the lead end moved back along the track by the lead-end-to-hatch distance. The stop is only abandoned once the hatch itself is within 5 m of the column or past it (a new column is still only picked 30 m or more ahead of the hatch).
  - The switch to Waypoint mode happens when any car of the train is in the column's block, or when the lead end has already passed the column.
  - With the engine leading nothing changes in practice (the hatch is about 23 m behind the pilot on the test train). `Player.log` lines now say "hatch ... m from".

## 2.5.2
- Water stop: the tender's water hatch now opens when the engineer calls for the spout and closes again when the spout is put away (left open if it was already open), with the same message as clicking it. The hatch key differs between tenders (`water.hatch` on the F-71 and T-17, `hatch.water` on the B-65, `hatch` / `top.hatch` on the K-28 saddle tank), so the mod picks a toggle with "hatch" in its key, preferring "water", then the one nearest the water filler; `Player.log` names the key used. Cosmetic: the column's loader does not check the hatch.

## 2.5.1
- New, off by default: **coast band** (`CoastBandMph`; slider "Coast band" in the settings window, effective at once; 1 mph suggested). Road and Waypoint modes, above 5 mph. It changes the speed controller's commands, not its plan:
  - Power off: the throttle closes when coasting would not lose speed (gravity at least the train's Davis resistance, the game's own `TrainPlantModel.DavisResistanceLbs`) and the train is within the band below its target. On a grade where gravity beats resistance by 10 lb per ton or more (about 0.1 mph a second of free acceleration), the throttle stays closed however far below the target the train is.
  - Brake on: a train-brake application waits until the train is more than the band over its target, unless a lower limit or a stop ahead needs it (the same look-ahead windows as brake tap suppression).
  - Why: on the 2.4.7 stock-fuel round trip the downhill leg spent 6.8 minutes powering and 7.6 minutes braking with only 3.1 minutes coasting while gravity was helping, and over the trip the throttle went straight to a brake application 46 times, usually within a second.
- The fuel summary has two new columns before `setup`: `powerCuts` and `brakesHeld` (commands changed by the coast band); `setup` has `band=`.
- Water stop and coast band share no state; nothing else changed.

## 2.5.0
- New, off by default: **water stop for Road-mode engineers** (setting `WaterStop`; switch and sliders in the settings window under "Water stop (Road mode)", effective at once).
  - When the tender's water falls below `WaterStopPercent` (default 30%), the engineer looks ahead along the route as it is lined (up to `WaterStopSearchKm`, default 30 km) for the nearest water column or tower, says where it will take water, and slows to `WaterStopApproachMph` (default 20 mph) within about a mile of it.
  - It switches to Waypoint mode only once the lead car is in the column's CTC signal block, so the player's routing and signals are kept up to that block (Waypoint mode may throw switches inside it). Where the column is not in a CTC block it switches within `WaterStopApproachMeters` (default 600 m).
  - The waypoint puts the tender's water hatch (the tender's `LoadTarget`) under the spout. Stopped there, it asks the column's own loader to load (the `CarLoaderSequencer` "wants loading" key, as the game's own sequence uses), waits until the tender is full, puts the spout away and returns to Road mode with the direction and speed it had.
  - The player changing the orders cancels it. If it cannot reach the column within 20 game minutes, or no water flows for 90 seconds, it gives up and returns to Road mode. A column it gave up on or cancelled is skipped for 10 game minutes. Host only.
  - `Player.log` lists the water columns found (id, place, block, fill rate, reach) once per session and every step of each stop.
  - The spotting arithmetic and the loader request follow the refuel feature of PrinceOfPluto's Waypoint Queue mod (MIT licence).

## 2.4.10
- Realistic fuel use (off by default): new evaporation curve, so less coal for the same steam. Water per pound of coal is now 8 at light firing, 7 at moderate firing (about 650 lb of water per sq ft of grate per hour) and 5.5 at heavy firing (935), falling slowly to 5 beyond; it was 7.5 at light firing falling to 5 at 1,000. The new points sit between Ralph Johnson's road figures (6.5 at 100 lb of coal per sq ft per hour, 5.5 above 170) and Goss's superheated test-plant result (about 7.3 at 200 psi; *Superheated Steam in Locomotive Service*, 1910). Water use is unchanged. Replayed over the logged Bryson to Sylva round trip (EMR 5000, about 1,931 tons), coal falls about 8% (9,247 to 8,505 lb; 126 to 116 lb per 1,000 gross ton-miles; 3.7 to 3.4 lb per indicated horsepower-hour) and water per pound of coal rises from 6.3 to 6.8. No setting or default changed; `CoalScale` still multiplies the result.
- README: the realistic fuel section now compares with Goss's test-plant figures and the ICC's Class I fuel statistics (1921-1943) instead of the second-hand McAuliffe figures.

## 2.4.9
- Documentation only, no change in behaviour. The README and the `SteamLbPerIhpHour` comment no longer claim a realistic range or "about 112 lb" for realistic fuel. They now give the measured figures (about 126 lb of coal and 95 gal of water per 1,000 gross ton-miles on the Bryson to Sylva round trip, against 172 and 153 with the game's own use) and cite the only outside figures found, McAuliffe's *Railway Fuel* (1927) as quoted on a Trains.com forum: 139, 161 and 159 lb of coal per 1,000 gross ton-miles for Union Pacific 1925, Santa Fe 1923 and all US railroads 1925. The older "80-110 lb" and "realistic water stops" wording had no source and is removed.

## 2.4.8
- Fix in realistic fuel use (off by default): the cutoff term of the steam rate now uses the real cutoff. `SteamEngine.reverser` is the lever position (the game maps lever 0.1 to 1 onto a cutoff of 0.15 to 0.85 with `TrainMath.ReverserToCutoff`), but the model used the lever value as if it were the cutoff, so it overstated the cutoff term at long cutoffs (lever 0.64 is cutoff 0.57). Replayed over the logged 38.4-mile round trips this lowers the modelled burn by about 2.4% coal and 1.7% water (9,477 to 9,246 lb coal, 7,105 to 6,985 gal water; 129.2 to 126.1 lb coal per 1,000 gross ton-miles). No setting or default changed (steam rate 21.5, cutoff effect 0.5); raise the steam rate by about 2% in `Settings.xml` to keep the old totals.

## 2.4.7
- Realistic fuel use can now be switched on and off in the settings window (Ctrl+F8, section "Fuel use"), with effect at once and no restart, so a stock-fuel run and a realistic run can be made in the same session. The patch on `SteamEngine.UpdateConsumption` is now installed whenever the engine members it needs exist, even when the setting is off at start-up, and does nothing while the setting is off (the game's own rates stay). The self-check line for it reads "off (can be switched on in the settings window)" when off. The steam rate, efficiency and coal scale are still edited in `Settings.xml`. Default unchanged (off). The "Reset these to defaults" button does not touch it.

## 2.4.6
- Logging only, no change to driving or fuel use. The sampler log (`ae_*.csv`) has a new `tons` column after `cars`: the gross weight in short tons of everything coupled to the locomotive (locomotive, tender and every car, loads included), refreshed every 2 seconds. The fuel summary (`fuel_summary_*.csv`) has four new columns before `setup`: `grossTons` (current), `gtMiles` (gross ton-miles run so far, using the weight at each moment), `coalPer1000Gtm` and `waterGalPer1000Gtm` (blank until 20 gross ton-miles). This lets runs with different trains be compared, and the realistic fuel steam rate be checked against published coal per 1,000 gross ton-miles. A new column shifts the later columns in the sampler log; read them by name.

## 2.4.5
- Fix (game bug worked around): a placed fusee no longer throws an error on every frame during fast-forward. `UI.FastForwardWrangler.Start` shows a `FadeOut`, and `FadeOutController.Show` sets the main camera's `enabled = false` half a second later (`Hide` turns it back on when fast-forward stops). `Camera.main` only returns an enabled camera, so it is null for the whole fast-forward, and `Effects.FuseeRenderer.Update` does `Camera.main.transform` without a check (the method it calls next, `UpdateEmission`, does check). Logged: 1,031 `NullReferenceException`s from `FuseeRenderer.Update` in one fast-forward with a fusee near the train, none in the same session without one. The new prefix on `FuseeRenderer.Update` skips the update while there is no main camera and does nothing otherwise. Setting `GuardFuseeCamera` (default on; also a switch in the settings window, effective at once). The first time it acts, `Player.log` says so. Reported to the developers.

## 2.4.4
- Fix (game bug worked around): fuel and water are charged for the game time that has really passed when time is sped up. `BaseLocomotive.PeriodicUpdateBody` waits `WaitForSeconds(1f)` and calls `PeriodicUpdate(1f)`; a coroutine resumes at most once per frame, so when a frame covers more than a second of game time (the 0.6.1 fast-forward) one second is charged and the rest is lost. Logged: at normal speed the tender lost 6,315 lb of coal against 6,322 lb computed from the burn rate; in fast-forward it lost 36% of the computed amount (coal and water alike, 2,250 lb against 6,319 lb). The new prefixes on `SteamLocomotive.PeriodicUpdate` and `DieselLocomotive.PeriodicUpdate` replace `dt` with the game time since the previous call for that locomotive, only when it is over 1.25 s (capped at 60 s), so normal speed is unchanged. Setting `FuelFollowsTimeWarp` (default on; also a switch in the settings window, effective at once). The first time it acts, `Player.log` says so.
- The logs' fuel numbers (`fuel_summary`, `coalLbs`) come from the burn rate in game time and were never affected.

## 2.4.3
- Defaults changed after A/B runs of Bryson to Sylva Interchange and back (EMR 5000, about 38.4 miles, 79 minutes moving, six configurations): `SpeedReductionLeadSeconds` 6 to **10** and `BrakeTapSuppressMph` 0 to **2**. Together they used about 3% less coal on the round trip (9,474 lb against 9,747 to 9,768 lb) for the same time, with the saving on downhill running (about 8%) and none uphill. A lead of 14 gave no further gain; Undershoot margin 1.5 and 2 and cutoff tolerance 8 gave nothing more and are left at their old values (1 and 3). Run-to-run noise was about 0.5 to 1%, and it is one route and one locomotive.
- Existing `Settings.xml` files are read as they are, so installs that already have the file keep their own values. To take the new defaults, set the two values by hand, use the window's reset button (which resets the window's values to the new defaults) or delete the file.
- The self-check now shows "Brake tap suppression: enabled" by default; set `BrakeTapSuppressMph` to 0 to turn it off.

## 2.4.2
- Logger: every `fuel_summary_*.csv` row gets a `setup` column with the settings window's values at that moment (for example `tap=2;lead=6;cut=3;over=1;under=1;margin=2;ceil=1;indep=1`), so a run records how it was set rather than relying on Settings.xml. The settings window also writes `Settings saved: ...` to `Player.log` on each save. Reason: a run meant to test Undershoot margin 2.0 left `PlanUndershootDeadbandScale` at 1 in Settings.xml and gave the same coal as the run before it, so it could not be told whether the value had been applied.
- Behaviour is otherwise unchanged.

## 2.4.1
- Fix: brake tap suppression never fired on a Bryson to Sylva run (`tapsSuppressed` 0 with the limit at 2 mph; 331 lb/mi against 329 without it). The run was in Waypoint mode ("Running to waypoint" in every target list) and the filter only allowed Road mode. It now allows Road and Waypoint. A stop target within the larger of 1500 m and 80 s of travel, or a lower limit within 600 m / 40 s, still leaves the application to the controller, so stopping accuracy at a waypoint is not changed. Yard mode is still left alone.
- Logger: `fuel_summary_*.csv` gains a `tapWhy` column, a running count of why first brake applications were not suppressed (`locoBrake`, `handleDown`, `cylinders`, `mode`, `slow`, `overLimit`, `gaining`, `limitAhead`, `stopAhead`), to show which condition is holding the filter back.
- Settings window: planner weights show as 1.00 (the "(stock)" text did not fit in the slider handle); the explanatory text and self-check lines are smaller and dimmer.
- Behaviour with `BrakeTapSuppressMph` at 0 is unchanged.

## 2.4.0
- New: in-game settings window, built with the game's own panel builder and opened with **Ctrl+F8** (`SettingsHotkey` and `SettingsHotkeyCtrl` in Settings.xml change the key). It has sliders for the brake tap limit, the slowing lead, the cutoff tolerance and the five planner weights, a switch for the fuel summary log, a reset button, and the self-check result for each feature. Changes apply at once and are written to Settings.xml as they are made (through a temporary file). The window is registered again after each map load, because the game forgets custom windows when a map unloads. It does nothing at the main menu.
- Features that the window can switch on while the game is running (brake tap suppression, early slowing, planner weights) now install their patches whenever the game members they need exist, even when the setting is off at start-up; each patch does nothing while its setting is off or at the game's value. Before this, a feature that was off at start-up needed a restart. Self-check lines for them read "off (can be switched on in the settings window)". Features that choose their patches by other settings (cutoff, realistic fuel, logging, and so on) are unchanged and are still edited in Settings.xml.
- Build: the project also references `Unity.InputSystem.dll`, `Unity.TextMeshPro.dll` and a few of the game's other assemblies when present, because the panel builder's method signatures mention their types.
- Behaviour with the default settings is otherwise unchanged.

## 2.3.1
- Fix: the fuel summary counted a train brake handle at a small reduction as braking. The engineer often parks the handle at a 5 psi reduction that leaves the car cylinders empty (log: set 0.0556, cylinders 0.02 psi, throttle 1.0 for 98 s), so the "both" column and the brake and tap counts were too high. Braking, overlap and brake applications are now judged by the average cylinder pressure (above 1 psi, locomotive or cars), read four times a second of game time.
- Fix: brake tap suppression only fired when the handle was fully released, so a handle parked at a small reduction defeated it. It now fires on the first real application: handle at most a 10 psi reduction and car cylinders at or below 1 psi. The self-check also needs `AutoEngineer.AverageTrainBrakeCylinder()` for this feature.
- Behaviour with `BrakeTapSuppressMph` at 0 is unchanged.

## 2.3.0
- New: fuel summary log. `logs\fuel_summary_*.csv` gets one cumulative row per engineer about every minute of game time (`LogFuelSummary`, on by default): minutes, miles, coal, coal per mile, water, and coal and minutes split into powered, coasting, braking, "both" (throttle open while the train brake is applied) and standing, plus counts of brake applications, brake taps (an application that took off under 3 mph) and taps suppressed. For a trip, subtract the first row from the last. Use it to compare settings on the same route.
- New: brake tap suppression (`BrakeTapSuppressMph`, default 0 = off, so defaults are unchanged). When the train is within this many mph over its target, is not gaining speed, and no lower limit or stop is near enough to matter, the controller's first train-brake application from released is replaced by a coast. If the speed climbs past the margin or something ahead comes into range, the controller's own command goes through. Road mode only. Suggested first value: 2. Does not depend on `FixEnabled`; the self-check turns it off if `SpeedControlLoop.ApplyRolloutCommand` or its fields are not found.

## 2.2.6
- Fix: the 2.2.4/2.2.5 workaround ran (`Player.log`: "CTC fix: block sy-w removed from signal sy-w-eb") but the signal still dropped to Stop in the same second block `sy-w` went occupied (23:22:05, emergency stop 23:22:09). Taking the block off the signal's own list was not enough: an interlocking signal also shows Stop when a block on the outlet its route leads into is occupied (`Interlocking.BlockAndNexSignal`). `CtcDropBlocks` now also removes the block from the interlocking outlet(s) that the signal's routes use in its direction of travel, and the log line says how many entries came from each place.
- Logger: `ctc_dump_*.txt` gains an interlockings section (switch sets, outlets with their blocks and next signal, routes) and each signal's interlocking and route mapping, so the outlet data can be checked.
- Behaviour is otherwise unchanged.

## 2.2.5
- Fix: the 2.2.4 CTC block workaround did not run on game 0.6.1. It was gated on `FixEnabled`, and the self-check switches `FixEnabled` off on 0.6.1 (the brake-drag members `trainBrakeController`, `independentController` and `throttleController` are not found, see the self-check in `Player.log`). The workaround now depends only on `CtcDropBlocks` being set. The statement in 2.2.4 that it applies with `FixEnabled` on is wrong.
- Behaviour is otherwise unchanged.

## 2.2.4
- Temporary workaround for the Sylva West eastbound signal (`sy-w-eb`) dropping to Stop about 45 m before a train, which makes the auto-engineer's emergency stop fire from 25 mph. Cause (from `ctc_dump_*.txt` and `Player.log`): the signal's protected-block list contains only block `sy-w`, whose span lies 12.9 to 50.6 m short of the signal, and the block goes occupied as the train enters it (the same second as the signal change in two runs). Every other signal with protected blocks has them starting 5 to 7 m ahead of it. New setting `CtcDropBlocks` (default `sy-w-eb:sy-w`; comma-separated `signalId:blockId` pairs; empty = off) takes the named block off the named signal's list at runtime each time a map loads, then asks the signal to recalculate. Nothing in the map or save is changed, and the pair is ignored if it is not on the map. Remove the setting when the game fixes the block (the game's own earlier fixes for the same kind of fault, RR-178, RR-783 and RR-787, moved block boundaries in the map).
- Does not need the logger. Self-check: new feature "CTC block fix".

## 2.2.3
- Logger: new file `logs\ctc_dump_*.txt`, written once per game session when the first engineer is enabled (`LogCtc`, default on; needs `LogEnabled`). It lists every CTC block with its track spans (segment, end, distance, position, length) and every signal with its position, its aspect, the blocks it protects and the distance from the signal to each span end of those blocks. Read-only; nothing in the game is changed.
- Why: the Sylva West eastbound signal (`sy-w-eb`) drops from Approach to Stop about 43 m before a train. `Player.log` shows block `sy-w` going occupied at the same second in two runs. A signal shows Stop whenever a block it protects is occupied, and a block is occupied when any car is on one of its spans, so the span of `sy-w` appears to start short of the signal. The game's own fixes for the same kind of fault (RR-178, RR-783, RR-787 in the release notes) moved block boundaries in the map. The dump gives the numbers needed to decide whether a runtime trim of that span is possible.
- Behaviour is unchanged.

## 2.2.2
- Logger: `sigMastM` is removed from the main CSV and from `signals_*.csv`. It read a constant (709.6 in the 28 Oct run) because the locomotive's `Component` transform does not follow the train, so it never measured anything.
- Logger: two columns replace it, in both files. `sigRawM` is the signal distance as the game built it, taken in `SetTargets` before this mod edits the target list, then reduced by the distance the locomotive has travelled since. `sigDistM` is still the distance in the list the engineer acts on, which includes this mod's edits (`SpeedReductionLeadSeconds` moves lower limits closer by speed x lead, about 40 m at 25 mph). `travelM` is the cumulative distance travelled by the locomotive since logging began, so two rows (or the signal flip and the emergency stop) can be compared by distance as well as by time.
- Why: in the Sylva West runs the signal flips from Approach to Stop about 4 s ahead of a train doing 25 mph, and the logged distance there was the shifted value, not the game's. `sigRawM` shows the true distance at the flip, so a game fault and a mod effect can be told apart from the CSV alone. A quick check without a new build: set `SpeedReductionLeadSeconds` to 0 in `Settings.xml`; then `sigDistM` and `sigRawM` should agree.
- Self-check: the "Signal logging" feature now also needs `AutoEngineer.SetTargets(Targets)` and the SetTargets patch, which captures the raw distance.
- Behaviour is unchanged.

## 2.2.1
- Logger: new column `sigMastM` in the main CSV and in `signals_*.csv`: the straight-line distance (3D) from the locomotive's centre to the tracked signal's mast. It does not depend on the planner, so comparing it with `sigDistM` (the AI's own distance) shows how far off the AI's distance is when a signal drops. The AI measures from the head of the train, so expect `sigMastM` to read up to about half a locomotive length more.
- Logger: reading a signal's shown aspect (`CTCSignal.LastShownAspect`, internal in the game) goes through reflection, as in the automatic compile fix to 2.2.0 (e1f8670).
- Behaviour is unchanged.

## 2.2.0
- Logger: signal events. With `LogSignals` on (default; needs `LogEnabled`) a new file `logs\signals_*.csv` gets one line each time the signal an engineer is tracking, or the aspect it shows, changes: game time, wall-clock time (matches `Player.log`), locomotive, event (`seen`, `aspect`, `next`, `passed`), signal id and name, the aspect before and after, speed, the distance to the signal, the first target and the full target list. It is checked every physics tick and also while the train stands still. The main CSV gets three columns, `sigId`, `sigAspect` and `sigDistM`. This is for catching a signal that drops from Approach to Stop while a train is still approaching it (reported for Sylva West eastbound on 0.6.1: Approach, then Stop 4-5 s later, then the game's emergency stop from about 25 mph).
- New setting `ApproachNoNextSignalMph` (default 25, which is what the game does, so nothing changes). When the game finds no signal after an Approach signal it sets 25 mph there; a lower value (for example 15) lowers that limit so the AI slows sooner for a Stop signal it cannot see. Only ever lowers; 25 or more, or below 5, change nothing.
- Self-check: new feature "Signal logging", and the signal-braking feature also covers the new setting.
- Known, not changed: the main CSV skips rows while the train is standing still, which is why a stop at a red signal shows as a gap in the log (the 250 s hole at 2509-2760 s in the 28 Oct run). The signal log is not affected.
- Known, not changed: the game's `EmergencyStop` (target distance below -3 m on a stop target) sets the consist's velocity to 0 outright, so a Stop signal that appears closer than the braking distance ends in a dead stop whatever the AI does.

## 2.1.0
- Gentle direction-change stop: changing direction on a moving train leaves the engineer with no speed order and no targets, and it stops at the game's maximum brake application (a 28 mph train stopped in 143 m). While the train is moving with no speed order and nothing ahead, the brake limit the planner uses (`TrainBrake.CalculateMaxEffectiveApplication`) is capped at `DirectionStopBrakeLimit` (default 0.15, about 13 psi; 0 = off). The cap ends when a target or speed limit appears again, or below 2 mph.
- Planner weights: five multipliers (`PlanOverspeedScale`, `PlanUndershootScale`, `PlanUndershootDeadbandScale`, `PlanCeilingScale`, `PlanIndependentUseScale`) scale the rollout controller's score weights through a postfix on `RolloutController.WeightsFor`. All 1 (the game's values) by default.
- Logger: new columns `maxApp`, `maxAppRaw`, `capActive` (the brake limit, before and after the cap) and, with `LogDecisions` on (default), `plan`, `planCost`, `planHoldCost`, `planAgeS` and `planRoots` (the controller's latest decision, its cost, the cost of holding, how old that decision is, and the best cost of each first action).
- Self-check covers the new targets (two new features, "Gentle direction-change stop" and "Planner weights", plus "Decision logging").

## 2.0.1
- Logger: the tender coal and water columns (`fuelQty`, `waterQty`) were blank on 0.6.0 because load identifiers there carry the pack prefix (`rr/coal`, `rr/water`); both forms are matched now.
- Logger: a short lock on the CSV (OneDrive sync, a viewer) no longer drops rows. The file is opened with shared access, and rows that fail to write are held and written ahead of the next row (up to 6000), with one warning in the game log.

## 2.0.0
- Now a mod for the game's built-in loader (Railroader 0.6.0 and later) instead of Unity Mod Manager: asset-pack `Info.json` (`exp232/autoengineerbrakefix`, requires `rr/game >=0.6.0`), entry point `ModEntryPoint` (IModEntryPoint), built for .NET Framework 4.7.2 against `Railroader.dll` and `Railroader.Modding.dll`. Harmony is no longer supplied by UMM, so `0Harmony.dll` ships in the mod folder.
- Settings live in `Settings.xml` in the mod folder (same layout as the UMM file, read at start-up). There is no in-game settings panel for now.
- Start-up self-check: before patching, every method, field and property each feature relies on is looked up with the exact signature used. A feature with a missing target is switched off (in memory only) and the rest still load; each patch class is applied on its own. One summary block goes to the game log: mod and game version, each feature's state (enabled, disabled in settings, or disabled with what was not found), the patched-method count against the expected count, and the effective settings.
- 0.6.0 added a static overload of `AutoEngineer.CalculateLookaheadDistance`; the planning patch and the logger now pick the instance overload explicitly.
- 0.6.0 removed `brakeErrorPower`, `trainBrakeReleaseBelowOutput` and `paddingForSpeedMph` from `AutoEngineerConfig`; they are read by reflection with the existing no-config fallbacks.
- Known: 0.6.0 replaced the auto-engineer's PID speed loop with a rollout controller. The brake-drag fix, throttle smoothing, holds while slowing, residual braking and stall guard hook that PID loop, so the self-check reports them as disabled on 0.6.0 until they are reworked.

## 1.13.1
- Stall guard: switching the engineer's direction or mode (Road, Yard, Waypoint) while a stall is latched now clears it, so you no longer have to switch the engineer off and on. The mode and direction are noted when the stall latches; direction is read from the sign of the speed limit, which the planner signs by direction of travel.

## 1.13.0
- Brake with the bleed-down in mind. A long train's brake cylinders drain slowly after a release (about 2 psi/s above 30 psi, 0.4-0.9 psi/s below), and until they are empty they keep slowing the train: roughly psi squared / 60 mph on a 52-car train. The stock logic ignores that, so a 5 mph reduction became 9-12 mph under the limit in the 1.12.0 log whenever the brake went past 0.33. While slowing for a lower limit (and 45 s after), the train brake is now released as soon as the cylinders will carry the train the rest of the way to the limit (within 0.5 mph, adjustable), and a new application is held off until they no longer do. Not used near a stop, or while the train is gaining speed (a descent). Settings: on/off, the divisor (60) and the margin.
- Logger: two new columns, `resMph` (speed the cylinders will still take off) and `resNeedMph` (reduction still needed), to tune the divisor.

## 1.12.1
- Stall guard, can't-start trigger: no longer fires on a train that is still gaining speed. The 1.12.0 log showed it cutting a 52-car train creeping to a waypoint (0.05 to 0.5 mph in 10 s) and a 72-car train breaking away at 0.03 mph/s. A gain of more than 0.15 mph from the slowest point now restarts the clock, so it only fires on a train that is truly not moving up.

## 1.12.0
- Starting resistance now has a standing-time option (on by default when starting resistance is on): a car that has only just stopped keeps part of its oil film and starts easier (default 40% of the full drag), settling back to the full drag over about 10 game minutes (both adjustable). Uses the game clock, so it follows pause and time acceleration. It is not saved: cars count as fully settled when a save is loaded.
- Default fade for starting resistance is now 2.5 mph (was 1.5).

## 1.11.1
- Stall guard: the throttle no longer reopens after the stall is declared. The throttle glider kept easing the lever toward its last (full) target, so the engine pulled against the brakes for about a second. The glider now stands down while the guard holds.
- Stall guard: the speed threshold is now under 4 mph (was 6), still after 10 s at full throttle on a 1% or steeper climb without gaining speed.
- Stall guard, second trigger: a train that cannot get moving at all (under 0.5 mph, throttle 50% or more, brakes off, any grade, for the stall time) is held the same way, with its own announcement ("We can't get this train moving..."). It runs every frame instead of in the throttle PID, which does not run during the engineer's start-up state.
- Stall guard: the brakes go on in two stages. The locomotive brake is full at once and the train brake is 60% while the train is still moving, then full once it has been still for a second (or after 8 s). This replaces the instant 100% application.

## 1.11.0
- New, off by default: starting resistance. The game's rolling resistance is the Davis equation, which only covers a moving train (about 1.6 lb per ton at 0 mph; real plain-bearing cars need 5-10). The option adds a standing-start drag (default 5 lb per ton, fading out with a 1.5 mph time constant) to the game's own retarding-force sum, so heavy trains start slower and marginal grades are harder to start on. Settings under "Train resistance".
## 1.10.0
- README now documents speed limits, the stall guard and realistic fuel use (no burn while the throttle is closed, on purpose).
- Lower speed limits (all speeds were compared signed, so the 1.9.0 short-zone cap never worked while running in reverse). Replaced by: no power while a lower limit is in sight (anywhere in the planner's lookahead) and the train is at or above that speed; power returns once it is a mph under it. Setting: "No power while a lower speed limit is in sight".
- Lower limits are treated as starting 6 s of travel sooner (adjustable): braking begins earlier and is lighter, and the speed the brakes keep bleeding off after release lands on the limit instead of under it. Stops and signals' stop targets are not shifted.
- The train brake is released when the speed 3 s ahead (adjustable) reaches the target, not when it is already there.
- Stall guard: full throttle, under 6 mph, climbing 1% or more and not gaining speed for 10 s is declared a stall. The throttle shuts, the brakes are held, the start-up routine is skipped, and the engineer announces it ("Stalled on grade"). Lasts until the engineer is switched off and on. The game has no stall check; before this, the AI cycled between full throttle and light braking while rolling back.
- Waypoint and Yard modes keep the game's own stopping accuracy: the stay-stopped and stop-line fixes now apply to Road mode only.
- Removed the 1.9.0 timer-based "skip short higher zones" setting.
## 1.9.0
- Short higher zones: when a lower speed limit is due within 60 s of travel (adjustable, 0 = off), the train no longer accelerates for the higher limit in between (a 400 m stretch of 45 mph inside 35 mph track made the AI go to full throttle and then brake). A train already above the lower speed is left to the stock braking curve.
- Coasting band: a new brake application on a steady limit now waits until the train is 1.0 mph over (adjustable, 0 = stock), so 34 = power off, 35 = coast, 36 = brake. Slowing for a lower limit or stop is unaffected.
- After a brake release the throttle now opens as soon as the speed the train is heading for (3 s ahead) is under the setpoint, instead of waiting until it is 2 mph under (that let the train undershoot a 30 mph zone to 27.8).
## 1.8.2
- Realistic fuel now depends on cutoff: steam per horsepower-hour = rate x (1 + 0.5 x (cutoff - 0.30)), so the shorter-cutoff option saves fuel again. New "Cutoff effect on steam" slider (0 turns it off). Default steam rate 21.5 keeps the example run at about 112 lb of coal per 1,000 gross ton-miles with the shortened cutoff.
## 1.8.1
- Realistic fuel: default steam rate raised from 17 to 20 lb per indicated hp-hour. On a 2,591-ton, 12-mile run this gives about 112 lb of coal per 1,000 gross ton-miles (17 gave 88, below the real-world range).
## 1.8.0
- New, experimental and off by default: realistic coal and water use. Steam is now steam rate (17 lb per indicated hp-hour) x the engine's own power, and coal follows from an evaporation curve, so fuel per horsepower stays realistic at every speed (the game's own figures are about right at 10-17 mph and far too high at 35-40 mph). Settings under "Fuel use".
- The logger now writes a separate steam_engine_<loco> file for each steam locomotive in play (the config dump only covered the first one).
## 1.7.5
- Diagnostics for a fuel-use comparison: the config dump now lists the steam engine's numbers (heating surface, boiler pressure, cylinders, estimated grate area, ...), and the CSV log has teLbf, waterGps and coalLbs columns (tractive effort in lbf, water in gal/s, coal in lb/s). No behaviour change.
## 1.7.4
- Default throttle smoothing is now 1.6 s. Author set. README rewritten.
## 1.7.3
- Stopping at a signal/stop target: the AI used to treat a train stopped 1-3 m short of its target as "stopped but should be moving" and ran its start-up routine, creeping past the signal. The contextual target speed is now 0 within 2.5 m of a stop target, so the AI keeps the train stopped. Setting: "Stay stopped at a stop signal or stop target".
## 1.7.2
- Throttle bursts between brake pulses are now also suppressed when cruising at the limit on a gentle downhill (while the brake is on or was applied in the last 10 s), unless the train is clearly under the setpoint.
- Brake holding only applies to light applications (up to 2 notches) and with a smaller margin, so the train no longer overshoots 3 mph under the target and then needs throttle (seen on the way down to a stop).
- No throttle within 3 m of a stop target: the planner's speed curve never reaches 0, so the AI used to creep past a signal it had just stopped at.
- The throttle hold now works down to 1.5 mph (was 3).
## 1.7.1
- New: while slowing for a falling target the train brake is no longer released the moment the train dips a hair under the setpoint; it stays applied until the train is clearly under the target. Cuts the 0/1/2-notch brake flicker on the way down to a stop. Setting: "Keep the brake applied while slowing...".
## 1.7.0
- New: after passing an Approach signal the 25 mph limit is kept ("Approach Hold" target) until the next signal is registered, so the train no longer speeds up in the gap.
- New: while slowing for a falling target (stop, signal, speed drop) the throttle stays closed unless the train drops clearly below the target. Removes the throttle bursts between brake pulses on the way down to a stop. Applies above 3 mph.
## 1.6.0
- Signals: the AI now reacts to a signal's aspect from a configurable distance (default 500 m; the game uses 200 m), and the Approach speed limit is applied at the signal instead of "right now". Together these remove the full-effort brake application at Approach signals. Settings under "Signals".
## 1.5.0
- New experimental planning sliders (stock by default): look-ahead distance multiplier, and assumed braking when planning (start slowing earlier and brake more gently for known targets).
- Logger: new columns lookaheadM and targets (every target the AI knows about, "mph@metres:reason").
## 1.4.5
- New: when the train is over the setpoint and the brake is on (or clearly over), the throttle closes at once instead of stepping down 0.25 per half second, removing the throttle/brake overlap on speed-limit drops. Setting: "Close the throttle at once when overspeed needs the brake".
## 1.4.4
- After the start-up routine hands over, the throttle holds at its 50% until the speed controller catches up, instead of dipping toward 25% first.
## 1.4.3
- Throttle smoothing and glide now reach the target in finite time instead of creeping the last few percent (97% to 100%).
## 1.4.2
- Smooth handover from the AI's start-up routine: the throttle no longer drops from 50% to 25% when the speed controller takes over; it eases from the lever's actual position.
## 1.4.1
- Throttle smoothing now steps aside at the same overspeed margin as the brake fix, so the throttle can no longer stay open while the brake applies (seen as 1% throttle/brake overlap in a 45 mph run).
## 1.4.0
- New: throttle glide (on by default). Between the auto-engineer's half-second updates the lever moves smoothly (about 20 updates a second) toward the smoothed target. Stands down if anything else moves the throttle.
## 1.3.0
- New: throttle smoothing (on by default, 2.0 s). Low-pass filter and rate limit on the auto-engineer's throttle; real overspeed bypasses it.
## 1.2.0
- Shorter-cutoff option is now on by default (3% power tolerance), no longer experimental.
- New: stock cutoff is used on steep climbs (default 2.0% uphill or more, eased in over the last 0.5%), judged from the grade under and just ahead of the train.
## 1.1.0
- Experimental shorter-cutoff option (off by default): picks the shortest cutoff within a power tolerance of the stock choice. Can only shorten the stock cutoff.
## 1.0.0
- Renamed from AEBrakeLogger. Logging and fuel logging off by default. Attaches to engineers already running when enabled.
- Brake fix for train brake and locomotive brake with adjustable overspeed margin.
