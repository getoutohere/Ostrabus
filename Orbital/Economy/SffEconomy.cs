using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostrabus.Inspection;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Trading;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.Orbital.Economy;

internal sealed class SffEconomy : IDisposable {
    internal static bool Enabled = true;
    internal static SffEconomy Current;
    static object last = SnapshotJson.Map("state", "No economic SFF recorded.");
    static readonly AccessTools.FieldRef<MarketManager, Dictionary<string, ShipMarket>> Markets = AccessTools.FieldRefAccess<MarketManager, Dictionary<string, ShipMarket>>("_market");
    static readonly AccessTools.FieldRef<MarketManager, double> MarketClock = AccessTools.FieldRefAccess<MarketManager, double>("_lastUpdateTime");
    static readonly AccessTools.FieldRef<MarketActorConfig, List<Producer>> Producers = AccessTools.FieldRefAccess<MarketActorConfig, List<Producer>>("_productionMaps");
    static readonly AccessTools.FieldRef<Producer, float> Rates = AccessTools.FieldRefAccess<Producer, float>("_outputPerHour");
    static readonly System.Reflection.FieldInfo TradeClock = AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck");
    static readonly AccessTools.FieldRef<AIShip, ICommand> Reaction = AccessTools.FieldRefAccess<AIShip, ICommand>("_activeReaction");
    readonly MarketManager manager;
    readonly StarSystem world;
    readonly double start, end;
    readonly EconomicTimeline timeline;
    readonly SffEnvironment environment = new SffEnvironment();
    readonly Dictionary<Ship, FreightRoute> freight = new Dictionary<Ship, FreightRoute>();
    readonly List<FreightRoute> flights = new List<FreightRoute>();
    readonly List<object> records = new List<object>();
    readonly HashSet<ShipMarket> dirty = new HashSet<ShipMarket>();
    bool ran, disposed;
    internal bool Running { get; private set; }
    internal bool Completed { get; private set; }
    int attempts, departures, deliveries, cycles, successfulCycles, rejected, errors;
    long workTicks, preparationTicks;

    SffEconomy(StarSystem world, double seconds) {
        this.world = world; start = StarSystem.fEpoch; end = start + seconds;
        manager = MarketManager.Instance;
        timeline = new EconomicTimeline(start, end);
    }
    internal static bool ActiveForSkip => Enabled && OrbitalSimulation.Enabled && SffSimulation.Enabled;
    internal static bool IsFreight(Ship ship) {
        if (!ActiveForSkip || ship == null || ship.bDestroyed || ship.bRemove || ship.HideFromSystem || ship.LoadState != Ship.Loaded.Shallow ||
            !ship.IsAIShip || ship.HasDockGroup || ship.shipCombatTarget != null || ship.objSS == null ||
            ship.shipScanTarget == null || !ship.shipScanTarget.IsStation()) return false;
        // PASS transports share HaulerCargoPilot; only an actual registered cargo market qualifies.
        var market = MarketManager.GetShipMarket(ship.strRegID);
        if (market == null) return false;
        var ai = AIShipManager.GetAIShipByRegID(ship.strRegID);
        if (ai == null || ai.AIType != AIType.HaulerCargo || Reaction(ai) != null) return false;
        string command = ai.ActiveCommandName;
        return string.IsNullOrEmpty(command) || command == "FlyTo" || command == "DockCargoAndDespawn";
    }
    internal static SffEconomy Begin(StarSystem world, double seconds, bool sff) {
        if (!ActiveForSkip || !sff || Current != null || world?.dictShips == null || CrewSim.coPlayer == null ||
            AIShipManager.strATCLast == null || !OrbitAcceleration.Finite(seconds) || seconds < 60 || seconds > 86400) return null;
        SffEconomy context = null;
        try {
            context = new SffEconomy(world, seconds);
            if (context.manager == null || Markets(context.manager) == null) return null;
            long began = Stopwatch.GetTimestamp();
            context.Prepare();
            context.preparationTicks = Stopwatch.GetTimestamp() - began;
            Current = context;
            return context;
        } catch (Exception ex) {
            context?.Dispose();
            Ostrabus.OstrabusPlugin.Logger?.LogError("[SFF Economy] Preparation failed; native economy retained: " + ex);
            return null;
        }
    }
    void Prepare() {
        foreach (var body in world.aBOs.Values) {
            if (body == null || body.IsPlaceholder() || body.IsAsteroidField || body.fMass <= 0 || !OrbitAcceleration.Finite(body.fMass)) continue;
            if (OrbitalMotion.BodyAt(body, start, out _)) environment.Bodies.Add(new SffBody(body));
        }
        var seen = new HashSet<Producer>();
        long estimated = 0;
        foreach (var market in Markets(manager).Values) foreach (var config in market.GetConfigs()) foreach (var producer in Producers(config)) {
            if (!seen.Add(producer)) continue;
            float rate = Rates(producer);
            double interval = Math.Abs(3600f / rate);
            if (!(interval > 0) || !OrbitAcceleration.Finite(interval)) continue;
            double previous = producer.LastUpdateTime == 0 ? start : producer.LastUpdateTime;
            estimated += (long)Math.Max(0, (end - previous) / interval);
            if (estimated > 250000) throw new InvalidOperationException("Economic catch-up exceeds 250,000 production cycles; no changes applied.");
            double nextCycle = previous + interval;
            timeline.Add(nextCycle, 1, at => {
                // A failed cycle still consumes its time slot, as in vanilla.
                do {
                    cycles++;
                    if (producer.Run(market)) { successfulCycles++; dirty.Add(market); }
                    producer.LastUpdateTime = nextCycle;
                    nextCycle += interval;
                } while (nextCycle <= at);
                return nextCycle;
            });
        }
        foreach (var ship in world.dictShips.Values) if (IsFreight(ship)) {
            var route = new FreightRoute(AIShipManager.GetAIShipByRegID(ship.strRegID), ship.shipScanTarget, start, end, environment, ship.objSS.NavData);
            if (route.Prepare()) Add(route); else rejected++;
        }
        double due = (double)TradeClock.GetValue(null);
        if (!OrbitAcceleration.Finite(due)) throw new InvalidOperationException("Invalid trade dispatch clock.");
        timeline.Add(Math.Max(start, due), 2, at => {
            attempts++;
            TradeClock.SetValue(null, at + 7200);
            try { Dispatch(at); }
            catch (Exception ex) { errors++; Ostrabus.OstrabusPlugin.Logger?.LogError("[SFF Economy] Departure rejected at " + at + ": " + ex); }
            return at + 7200;
        });
    }
    void Add(FreightRoute route) {
        freight.Add(route.Ship, route); flights.Add(route);
        timeline.Add(route.Arrival, 0, at => {
            if (route.Ship.bDestroyed || route.Ship.shipScanTarget == null || route.Ship.shipScanTarget.bDestroyed) return double.PositiveInfinity;
            Record("arrival", route.Ship.strRegID, route.Ship.shipScanTarget.strRegID, at);
            MarketManager.UnregisterCargoShip(route.Ship.strRegID, route.Ship.shipScanTarget.strRegID);
            route.Delivered = true; deliveries++;
            return double.PositiveInfinity;
        });
    }
    void Record(string kind, string ship, string target, double epoch) {
        if (records.Count >= 128) return;
        var cargo = new Dictionary<string, object>(StringComparer.Ordinal);
        var market = MarketManager.GetShipMarket(ship);
        if (market != null) foreach (var config in market.GetConfigs()) foreach (var stock in config.Stock) {
            cargo.TryGetValue(stock.Key, out object count); cargo[stock.Key] = (count == null ? 0 : (int)count) + stock.Value.Count;
        }
        records.Add(SnapshotJson.Map("kind", kind, "ship", ship, "destination", target, "epoch", epoch, "cargo", cargo));
    }
    void Dispatch(double departure) {
        List<TradeRouteDTO> routes = MarketManager.GetTradeRoute(null);
        if (routes == null || routes.Count == 0) return;
        // GetTradeRoute repeats its first DTO in its bundled result. Do not load that consignment twice.
        var seen = new HashSet<TradeRouteDTO>();
        routes.RemoveAll(route => route == null || !seen.Add(route));
        if (routes.Count == 0) return;
        var first = routes[0];
        var source = world.GetShipByRegID(first.OriginStation); var destination = world.GetShipByRegID(first.DestinationStation);
        if (source == null || destination == null || destination.bDestroyed) return;
        string loot = AIShipManager.GetCargoShipForRoute(routes);
        if (string.IsNullOrEmpty(loot)) return;
        var pilot = new AIShip(first.OriginStation, AIType.HaulerCargo, loot);
        var ship = pilot.Ship;
        if (ship == null) return;
        try {
            // Factories execute at the real final epoch. Only this new hull's departure position is historical.
            var displacement = OrbitalMotion.Read(ship.objSS).Position - OrbitalMotion.Read(source.objSS).Position;
            var origin = FreightRoute.StationAt(source.objSS, departure);
            ship.objSS.bIsBO = ship.objSS.bBOLocked = ship.objSS.bOrbitLocked = false;
            ship.objSS.NavData = null;
            OrbitalMotion.Write(ship.objSS, new MotionState(origin.Position + displacement, origin.Velocity));
            ship.shipScanTarget = destination; ship.shipSituTarget = destination.objSS; ship.targetRegID = destination.strRegID;
            ship.TargetData = new TargetData(destination);
            MarketManager.RegisterAICargoHauler(ship, routes);
            var course = FreightRoute.Plan(pilot, destination, departure);
            if (course == null) { Reject(ship, source); return; }
            var flight = new FreightRoute(pilot, destination, departure, end, environment, course);
            if (!flight.Prepare()) { Reject(ship, source); return; }
            AIShipManager.AddAIToDict(pilot, "INTERREGIONAL");
            Add(flight); departures++;
            Record("departure", ship.strRegID, destination.strRegID, departure);
        } catch {
            // Return the manifest before destroying a hull which never entered managed flight.
            if (!freight.ContainsKey(ship)) Reject(ship, source);
            throw;
        } finally { RestoreBodies(); }
    }
    void Reject(Ship ship, Ship source) {
        rejected++;
        if (MarketManager.GetShipMarket(ship.strRegID) != null) MarketManager.UnregisterCargoShip(ship.strRegID, source.strRegID);
        CrewSim.DockAndDespawn(ship, source);
        ship.Destroy(isDespawning: false);
    }
    internal void Run() {
        if (ran) return;
        ran = true; Running = true;
        long began = Stopwatch.GetTimestamp();
        try {
            timeline.Run();
            Completed = true;
            // Producer initialization is not backdated by native IsReady's global epoch read.
            foreach (var market in Markets(manager).Values) foreach (var config in market.GetConfigs()) foreach (var producer in Producers(config))
                if (producer.LastUpdateTime == 0) producer.LastUpdateTime = start;
            MarketClock(manager) = end;
        } catch (Exception ex) {
            errors++;
            // This is not a transaction. Retain applied stock transfers and clocks; do not replay them.
            Completed = true;
            MarketClock(manager) = end;
            TradeClock.SetValue(null, end + 7200);
            Ostrabus.OstrabusPlugin.Logger?.LogError("[SFF Economy] Catch-up stopped after an error; applied events retained: " + ex);
        } finally {
            Running = false;
            foreach (var flight in flights) {
                if (flight.Ship.bDestroyed) continue;
                flight.Apply();
                if (flight.Delivered) CrewSim.DockAndDespawn(flight.Ship, flight.Ship.shipScanTarget);
            }
            RestoreBodies();
            foreach (var market in dirty) market.RequestTraderUpdate();
            workTicks = Stopwatch.GetTimestamp() - began;
        }
    }
    void RestoreBodies() { foreach (var body in world.aBOs.Values) body.UpdateTime(StarSystem.fEpoch); }
    internal bool Owns(Ship ship) => ship != null && freight.ContainsKey(ship);
    internal void DeferTrader(ShipMarket market) { dirty.Add(market); }
    internal static object Snapshot() => last;
    internal static void Reset() { Current?.Dispose(); Current = null; last = SnapshotJson.Map("state", "No economic SFF recorded."); }
    public void Dispose() {
        if (disposed) return;
        disposed = true;
        last = SnapshotJson.Map("startEpoch", start, "endEpoch", end, "completed", Completed, "dispatchChecks", attempts,
            "departures", departures, "deliveries", deliveries, "productionCycles", cycles, "successfulCycles", successfulCycles,
            "rejectedFlights", rejected, "errors", errors, "managedFlights", flights.Count, "events", timeline.Executed,
            "preparationMs", preparationTicks * 1000d / Stopwatch.Frequency,
            "workMs", workTicks * 1000d / Stopwatch.Frequency, "records", records.ToArray(), "recordsTruncated", departures + deliveries > records.Count);
        timeline.Clear(); freight.Clear(); flights.Clear(); dirty.Clear(); records.Clear(); environment.Bodies.Clear();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}
