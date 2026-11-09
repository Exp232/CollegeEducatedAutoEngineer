using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Game.Messages;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Model.Ops;
using RollingStock;
using Track;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// Train preparation when the engineer is given an order to move, in any mode (PrepTrain): releases the handbrakes on every car
    /// of the train, connects the air between each pair of coupled cars with both anglecocks open, and closes the
    /// anglecocks at the two ends of the train (the state the engineer's own "check the brake line" test wants).
    /// It runs once per new order (the engineer switched on, or from stopped to a speed, or a new mode or waypoint), when the train is standing. Cars are changed the way the game's own
    /// controls change them (the car's control properties and end gear), so the game applies its usual rules.
    ///
    /// LogCarKeys writes logs\carkeys_*.txt on each new order: every key-value entry on each car of the train, to find the
    /// names the game uses for doors, gates and lamps.
    /// </summary>
    internal static class TrainPrep
    {
        private sealed class State
        {
            public bool Seen;
            public AutoEngineerMode Mode;
            public OrderWaypoint? Waypoint;
            public bool Pending;
            public float Next;
            public float NextDump;
        }

        private static readonly ConditionalWeakTable<AutoEngineer, State> States = new ConditionalWeakTable<AutoEngineer, State>();
        private static readonly Car.LogicalEnd[] Ends = { Car.LogicalEnd.A, Car.LogicalEnd.B };

        internal static void Tick(AutoEngineer ae)
        {
            if (!Main.Active || ae == null || ae.Locomotive == null) return;
            if (!Main.Settings.PrepTrain && !Main.Settings.LogCarKeys) return;
            if (ae.Locomotive.set == null) return;   // being moved with the car placement tool: it has no set (couplings) for now

            State s = States.GetOrCreateValue(ae);
            if (Time.time < s.Next) return;
            s.Next = Time.time + 0.5f;

            Car loco = ae.Locomotive;
            Orders orders = new AutoEngineerPersistence(loco.KeyValueObject).Orders;
            // "Going": the engineer is on with a speed above 0 (a waypoint order also needs its waypoint).
            bool going = orders.Enabled && orders.MaxSpeedMph > 0 && (orders.Mode != AutoEngineerMode.Waypoint || orders.Waypoint.HasValue);
            if (!going)
            {
                s.Seen = false;
                s.Pending = false;
                return;
            }
            // New order: the engineer was off or stopped, or its mode or waypoint changed (a new speed alone is not one).
            if (!s.Seen || s.Mode != orders.Mode || !Equals(s.Waypoint, orders.Waypoint))
            {
                s.Seen = true;
                s.Mode = orders.Mode;
                s.Waypoint = orders.Waypoint;
                s.Pending = true;
                if (Main.Settings.LogCarKeys && Time.time >= s.NextDump)
                {
                    s.NextDump = Time.time + 20f;
                    DumpKeys(loco);
                }
            }
            if (!s.Pending || Mathf.Abs(loco.velocity) > 1f) return;   // wait until the train stands
            s.Pending = false;
            if (!Main.Settings.PrepTrain) return;
            // Waypoint Queue takes over every waypoint order and has its own, per-waypoint "Connect air" and "Release handbrakes"
            // options, and it sets handbrakes deliberately on the cars it cuts off. Leave the brakes to it.
            if (Main.Settings.WaypointQueueCompat && orders.Mode == AutoEngineerMode.Waypoint && SignalPlanning.WaypointQueueLoaded) return;
            Prepare(loco);
        }

        /// <summary>Walks the couplers from the locomotive: each car with its neighbour on each logical end (null = free end).</summary>
        internal static List<Car> Consist(Car loco)
        {
            var cars = new List<Car> { loco };
            var seen = new HashSet<Car> { loco };
            for (int i = 0; i < cars.Count && cars.Count < 200; i++)
            {
                foreach (Car.LogicalEnd end in Ends)
                {
                    Car other = cars[i].CoupledTo(end);
                    if (other != null && seen.Add(other)) cars.Add(other);
                }
            }
            return cars;
        }

        private static bool FacingEnd(Car car, Car other, out Car.LogicalEnd end)
        {
            foreach (Car.LogicalEnd e in Ends)
            {
                if (car.CoupledTo(e) == other)
                {
                    end = e;
                    return true;
                }
            }
            end = Car.LogicalEnd.A;
            return false;
        }

        private static void Prepare(Car loco)
        {
            List<Car> cars = Consist(loco);
            int brakes = 0, links = 0, closed = 0;

            foreach (Car car in cars)
            {
                if (car.air != null && car.air.handbrakeApplied)
                {
                    car.ControlProperties[PropertyChange.Control.Handbrake] = 0;
                    brakes++;
                }
            }

            if (cars.Count >= 2)
            {
                foreach (Car car in cars)
                {
                    foreach (Car.LogicalEnd end in Ends)
                    {
                        Car other = car.CoupledTo(end);
                        if (other == null)
                        {
                            // A free end: the train's own end, anglecock shut.
                            if (car[end].AnglecockSetting > 0.1f)
                            {
                                car.ApplyEndGearChange(end, Car.EndGearStateKey.Anglecock, 0f);
                                closed++;
                            }
                            continue;
                        }
                        if (!FacingEnd(other, car, out Car.LogicalEnd otherEnd)) continue;
                        if (!car[end].IsAirConnected)
                        {
                            car.ApplyEndGearChange(end, Car.EndGearStateKey.IsAirConnected, true);
                            if (!other[otherEnd].IsAirConnected) other.ApplyEndGearChange(otherEnd, Car.EndGearStateKey.IsAirConnected, true);
                            links++;
                        }
                        if (car[end].AnglecockSetting < 0.99f) car.ApplyEndGearChange(end, Car.EndGearStateKey.Anglecock, 1f);
                    }
                }
            }

            if (brakes > 0 || links > 0 || closed > 0)
            {
                Main.Mod.Logger.Log($"Train prep for {loco.DisplayName}: {brakes} handbrake(s) released, {links} air connection(s) made, {closed} end anglecock(s) closed ({cars.Count} cars).");
            }
        }

        private static void DumpKeys(Car loco)
        {
            try
            {
                Directory.CreateDirectory(LogWriter.LogDir);
                string path = Path.Combine(LogWriter.LogDir, $"carkeys_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                var sb = new StringBuilder();
                sb.AppendLine("CollegeEducatedAutoEngineer car key dump");
                sb.AppendLine("Time: " + DateTime.Now.ToString("s"));
                sb.AppendLine("Mod version: " + Main.Mod.Info.Version);
                foreach (Car car in Consist(loco))
                {
                    sb.AppendLine();
                    sb.AppendLine($"== {car.DisplayName}  id={car.id}  definition={car.DefinitionInfo.Identifier}  type={car.CarType}");
                    var keys = new List<string>(car.KeyValueObject.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (string key in keys)
                    {
                        string text;
                        try { text = car.KeyValueObject[key].ToString(); }
                        catch (Exception) { text = "?"; }
                        sb.AppendLine("  " + key + " = " + text);
                    }
                }
                File.WriteAllText(path, sb.ToString());
                Main.Mod.Logger.Log("Car key dump written to " + path);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.Warning("Car key dump failed: " + e.Message);
            }
        }
    }
}
