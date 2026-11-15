using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Ships.Commands;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;

namespace ShipExecutivePrototype;

static class WorldServiceChecks
{
    sealed class NativeDispatchProbe : BaseCommand
    {
        internal int Runs;
        internal NativeDispatchProbe(NavyPilot pilot) : base(pilot) { }
        public override CommandStatus RunCommand(Blackboard board)
        {
            Runs++;
            return base.Running;
        }
    }
    internal static readonly List<object> Evidence = new List<object>();
    static Dictionary<string, object> Item(string id) => ((object[])RetainedWorldServices.Snapshot("SffSimulation")["items"])
        .Cast<Dictionary<string, object>>().Single(x => ((string)x["ships"]).Split(',').Contains(id));
    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("world services baseline: real GUI SFF scopes run around executive route", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices())
            {
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                bridge.Sff(7200);
                var motion = RetainedWorldServices.Snapshot("SffSimulation");
                var economy = RetainedWorldServices.Snapshot("Economy.SffEconomy");
                check(motion.ContainsKey("nativeCourses") && (int)motion["nativeCourses"] >= 1, "Real motion scope did not exclude native route playback");
                check(economy.ContainsKey("completed") && (bool)economy["completed"], "Real economic scope did not run: " + Newtonsoft.Json.JsonConvert.SerializeObject(economy) + "; faults: " + string.Join(";", data.Errors));
                check(data.Errors.Count == 0 && RetainedWorldServices.Scope("SffSimulation") == null && RetainedWorldServices.Scope("Economy.SffEconomy") == null, "Retained service faulted or leaked its scope: " + string.Join(";", data.Errors));
                check(services.Windows.Count == 2 && (double)economy["startEpoch"] == 1000 && (double)economy["endEpoch"] == 8200,
                    "Outer prefix prepared a second interval after the native world advanced");
            }
        });
        scenario("world services baseline: production scopes do not cover PASS", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices())
            {
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                bridge.Pass(7200);
                check(services.Windows.Count == 0, "Baseline no longer matches GUI-only production scope boundary; revalidate PASS binding");
                Evidence.Add(new
                {
                    scenario = "production-PASS-negative-control",
                    openedScopes = services.Windows.Count,
                    nativeWorldEpoch = StarSystem.fEpoch
                });
            }
        });
        scenario("world services shared binding preserves normal, SFF and PASS production and route fuel", () =>
        {
            foreach (string source in new[] { "normal", "SFF", "PASS" })
            {
                var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
                using (var data = new WorldServiceFixtures(w))
                using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
                using (var services = new RetainedWorldServices(true))
                {
                    var market = data.Market("HOME", data.Production("fixture-mine", "Metal", 3));
                    bridge.Actor.Travel.BeginNativeLowFuelReturn();
                    if (source == "normal")
                        for (int i = 0; i < 360; i++)
                            bridge.Normal(20);
                    else if (source == "SFF")
                        bridge.Sff(7200);
                    else
                        bridge.Pass(7200);
                    check(market.GetStockCountForCategory("Metal") == 6 && StarSystem.fEpoch == 8200, "Production/clock mismatch for " + source);
                    check(Math.Abs(w.Subject.fShallowRCSRemass - (double)298.2f) < 1e-6 && w.ArrivalCalls == 0, "Fuel or premature arrival mismatch for " + source + ": fuel=" + w.Subject.fShallowRCSRemass + " arrivals=" + w.ArrivalCalls);
                    check(services.Windows.Count == (source == "normal" ? 0 : 2) && data.Errors.Count == 0, "Missing/duplicate scope or service fault for " + source + ": " + string.Join(";", data.Errors));
                    if (source != "normal")
                        check(data.TradeRequests == 1, "Scheduled dispatch check lost or duplicated for " + source);
                    Evidence.Add(new
                    {
                        scenario = "shared-world-production",
                        source,
                        epoch = StarSystem.fEpoch,
                        metal = market.GetStockCountForCategory("Metal"),
                        fuel = w.Subject.fShallowRCSRemass,
                        windows = services.Windows.ToArray(),
                        dispatchChecks = data.TradeRequests
                    });
                }
            }
        });
        scenario("world services route-loss negative control proves stale status can reacquire navigation", () =>
        {
            foreach (bool binding in new[] { false, true })
            {
                var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
                using (var data = new WorldServiceFixtures(w))
                using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
                using (var services = new RetainedWorldServices(binding))
                {
                    bridge.Actor.Travel.BeginNativeLowFuelReturn();
                    NativeWorld.Set(w.AI, "_activeCommand", new FlyTo(w.Pilot));
                    bridge.BeforeSegment = () => { w.Subject.objSS.ResetNavData(); w.PlanFails = true; };
                    bridge.Sff(600);
                    string mode = (string)Item("SUBJECT")["mode"];
                    check(mode == (binding ? "coast" : "partial"), "Lost-route ownership case did not distinguish native status inference from binding: " + mode);
                    check(w.Subject.fShallowRCSRemass == 300 && w.ArrivalCalls == 0, "Lost route fabricated fuel settlement or arrival");
                    check(data.Errors.Count == 0, "Route loss encountered service error: " + string.Join(";", data.Errors));
                    Evidence.Add(new
                    {
                        scenario = "route-loss-ownership",
                        binding,
                        mode,
                        fuel = w.Subject.fShallowRCSRemass,
                        displacementAU = w.Subject.objSS.vPosx - 1.1e-5
                    });
                }
            }
        });
        scenario("world services shared scopes preserve player torch playback and unadopted native dispatch", () =>
        {
            foreach (string source in new[] { "SFF", "PASS" })
            {
                var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
                using (var data = new WorldServiceFixtures(w))
                using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
                using (var services = new RetainedWorldServices(true))
                {
                    bridge.Actor.Travel.BeginNativeLowFuelReturn();
                    var player = CrewSim.coPlayer.ship;
                    var route = new NavData(player);
                    route.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false) { vPosx = .1 }, 300, .8, 100));
                    route.AddNavDataPoint(new NavDataPoint(21000, new ShipSitu(false) { vPosx = .2 }, 300, .8, 80));
                    player.objSS.NavData = route;
                    var other = w.Vessel("UNADOPTED", .01);
                    var pilot = new NavyPilot("HOME", other);
                    var probe = new NativeDispatchProbe(pilot);
                    var ai = NativeWorld.Empty<AIShip>();
                    NativeWorld.Set(ai, "_shipUs", other);
                    NativeWorld.Set(ai, "_aiCharacter", pilot);
                    NativeWorld.Set(ai, "_behaviorTree", new SelectorCommand("fixture-native-root", pilot.PriorityQueue, probe));
                    NativeWorld.Set(ai, "_activeCommand", probe);
                    NativeWorld.Back(ai, "Blackboard", pilot.Blackboard);
                    NativeWorld.Back(ai, "HomeStation", "HOME");
                    AIShipManager.dictAIs["HOME"].Add(ai);
                    if (source == "SFF")
                        bridge.Sff(7200);
                    else
                        bridge.Pass(7200);
                    // Native queue budget may schedule the adopted actor first.
                    // Give the unrelated actor ordinary scheduler opportunities,
                    // without advancing the clock or invoking it directly.
                    for (int i = 0; i < 16 && probe.Runs == 0; i++)
                        AIShipManager.Update();
                    check(ReferenceEquals(route, player.objSS.NavData) && Math.Abs(player.objSS.vPosx - .136) < 1e-7 && Math.Abs(player.fShallowFusionRemain - (double)92.8f) < 1e-6,
                        "Retained scope changed unrelated player course/fuel: " + source);
                    check(data.Playbacks["PLAYER"] == 1 && data.Playbacks["SUBJECT"] == 1, "Player/Navy route playback was repeated by a scope");
                    check(probe.Runs > 0 && ReferenceEquals(ai.AICharacter, pilot) && ai.ActiveCommandName == nameof(NativeDispatchProbe), "Unadopted native dispatcher was suppressed or replaced");
                    check(!pilot.PriorityQueue.CanExecute(pilot.Blackboard), "Native unadopted catch-up wrapper was left unconsumed");
                    check(data.NativeWrappers.TryGetValue("UNADOPTED", out int wrappers) && wrappers == 1 && !data.NativeWrappers.ContainsKey("SUBJECT"),
                        "Legacy catch-up was blocked globally or admitted for executive-owned actor");
                    check(data.Errors.Count == 0, "Player/native control isolation encountered service error: " + string.Join(";", data.Errors));
                }
            }
        });
        scenario("world services leave authored holding under executive control", () =>
        {
            var w = new NativeWorld();
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices(true))
            using (var admission = new NativeNavyAdmission(bridge.Actor))
            {
                NavyFixtures.Current.CollisionRadius = 1e-8f;
                try
                {
                    bridge.Actor.Tick();
                    CrewSim.coPlayer.ship.objSS.vPosx = w.Subject.objSS.vPosx + 1e-10;
                    w.AI.AddCommandLoot("ComeHere");
                    bridge.Actor.Tick();
                    bridge.Actor.State.Inbox.Single().DockingRemaining = 10000;
                    w.Subject.fAIDockingExpire = 10000;
                    double x = w.Subject.objSS.vPosx;
                    bridge.Pass(120);
                    check(bridge.Actor.State.Inbox.Single().Phase == "Hold" && w.Subject.fAIDockingExpire == 9880 && w.Subject.objSS.bIgnoreGrav,
                        "Physical scope advanced the authored hold or replaced its timer");
                    check(w.Subject.objSS.vPosx == x && w.ArrivalCalls == 0 && w.Subject.objSS.NavData == null, "Holding gained an independent movement/arrival controller");
                    check((int)RetainedWorldServices.Snapshot("SffSimulation")["nativeOther"] > 0 && data.Errors.Count == 0, "Holding exclusion/service result invalid");
                }
                finally { NavyFixtures.Current.CollisionRadius = 0; }
            }
        });
        scenario("world services freight delivers before production while Navy retires once", () =>
        {
            foreach (string source in new[] { "SFF", "PASS" })
            {
                var w = new NativeWorld { LowFuel = true, RouteDuration = 40 };
                using (var data = new WorldServiceFixtures(w))
                using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
                using (var services = new RetainedWorldServices(true))
                {
                    var market = data.Market("HOME", data.Production("fixture-factory", "Product", .5f, "Metal"));
                    var cargo = data.Cargo();
                    bridge.Actor.Travel.BeginNativeLowFuelReturn();
                    if (source == "SFF")
                        bridge.Sff(10800);
                    else
                        bridge.Pass(10800);
                    check(w.Retired && w.ArrivalCalls == 1 && Math.Abs(w.Subject.fShallowRCSRemass - 295) < 1e-6, "Navy arrival interfered with economic timeline: " + source);
                    check(cargo.HideFromSystem && !data.Markets.ContainsKey("CARGO") && data.Arrivals["CARGO"] == 1, "Cargo was lost, repeated or left alive: " + source);
                    check(data.Playbacks.TryGetValue("CARGO", out int playback) && playback == 1, "Managed freight repeated native playback after first unscoped 40-second interval");
                    check(market.GetStockCountForCategory("Product") == 1 && market.GetStockCountForCategory("Metal") == 3, "Delivery/production ordering or stock conservation failed: " + source);
                    check(data.Errors.Count == 0 && RetainedWorldServices.Scope("SffSimulation") == null && RetainedWorldServices.Scope("Economy.SffEconomy") == null, "Economic/physical scope failed cleanup: " + string.Join(";", data.Errors));
                    Evidence.Add(new
                    {
                        scenario = "freight-and-Navy-arrival",
                        source,
                        navyFuel = w.Subject.fShallowRCSRemass,
                        cargoFuel = cargo.fShallowRCSRemass,
                        product = market.GetStockCountForCategory("Product"),
                        metal = market.GetStockCountForCategory("Metal"),
                        arrivals = data.Arrivals.ToArray(),
                        windows = services.Windows.ToArray()
                    });
                }
            }
        });
        scenario("world services scopes close after native arrival throws without replay", () =>
        {
            foreach (string source in new[] { "SFF", "PASS" })
            {
                var w = new NativeWorld { LowFuel = true, RouteDuration = 600 };
                using (var data = new WorldServiceFixtures(w))
                using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
                using (var services = new RetainedWorldServices(true))
                {
                    data.Market("HOME", data.Production("fixture-mine", "Metal", 3));
                    bridge.Actor.Travel.BeginNativeLowFuelReturn();
                    bool entered = false;
                    w.BeforeArrival = () => { entered = true; throw new InvalidOperationException("fixture arrival boundary failure"); };
                    bool failed = false;
                    try
                    {
                        if (source == "SFF")
                            bridge.Sff(7200);
                        else
                            bridge.Pass(7200);
                    }
                    catch (Exception) { failed = true; }
                    check(entered && failed && bridge.Actor.Machine.State.PendingArrival != null, "Native arrival fault was hidden or treated as successful retirement: entered=" + entered + " failed=" + failed + " phase=" + bridge.Actor.Machine.State.Active.Phase + " pending=" + (bridge.Actor.Machine.State.PendingArrival != null) + " epoch=" + StarSystem.fEpoch);
                    check(RetainedWorldServices.Scope("SffSimulation") == null && RetainedWorldServices.Scope("Economy.SffEconomy") == null,
                        "Exception left a physical/economic writer active: " + source);
                    check(!GUIFFWD.Active && StarSystem.fEpoch == 1600 && !w.Retired && w.ArrivalCalls == 0, "Failed arrival consumed residual time or repeated side effects");
                    w.BeforeArrival = null;
                    bridge.Actor.Tick();
                    check(w.ArrivalCalls == 0, "Uncertain native arrival was replayed after exception");
                }
            }
        });
        scenario("world services partial freight switches ownership without double settlement", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 40 };
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices(true))
            {
                var destination = data.Market("HOME");
                var cargo = data.Cargo(duration: 20000);
                var ai = AIShipManager.GetAIShipByRegID("CARGO");
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                bridge.Sff(7200);
                bridge.Pass(7200);
                check(!cargo.HideFromSystem && data.Markets.ContainsKey("CARGO") && ReferenceEquals(ai, AIShipManager.GetAIShipByRegID("CARGO")),
                    "Partial freight was retired or had its native controller replaced");
                check(Math.Abs(cargo.fShallowRCSRemass - 292.8) < 2e-5 && Math.Abs(cargo.fShallowFusionRemain - 92.8) < 2e-5,
                    "Partial freight settled fuel twice: RCS=" + cargo.fShallowRCSRemass + " fusion=" + cargo.fShallowFusionRemain);
                // Native main advances one successful child per scheduler visit.
                // After the first skip it reports TargetNewIfNone; production
                // economy deliberately declines that next window. Native route
                // playback resumes, with the existing paid-clock watermark.
                check(cargo.objSS.NavData != null && data.Playbacks["CARGO"] == 2 && data.MixedPlaybackOwners == 0 && destination.GetStockCountForCategory("Metal") == 0,
                    "Partial freight had overlapping owners or unloaded before arrival");
                check((int)RetainedWorldServices.Snapshot("Economy.SffEconomy")["managedFlights"] == 0,
                    "Native setup-command boundary no longer matches the conservative production economy gate; revalidate handoff");
                check(data.Markets["CARGO"].GetStockCountForCategory("Metal") == 4 && data.Errors.Count == 0, "Partial freight manifest changed or native service faulted");
                Evidence.Add(new
                {
                    scenario = "partial-freight-native-controller",
                    epoch = StarSystem.fEpoch,
                    rcs = cargo.fShallowRCSRemass,
                    fusion = cargo.fShallowFusionRemain,
                    cargo = 4,
                    nativePlaybackCalls = data.Playbacks["CARGO"],
                    mixedOwners = data.MixedPlaybackOwners
                });
            }
        });
        scenario("world services multi-day PASS uses bounded windows and retains scheduled clocks", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 300000 };
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices(true))
            {
                var market = data.Market("HOME", data.Production("fixture-slow-mine", "Metal", 1));
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                bridge.Pass(172800);
                check(bridge.Segments == 2 && services.Windows.Count == 4 && StarSystem.fEpoch == 173800, "Multi-day PASS escaped bounded service windows");
                check(market.GetStockCountForCategory("Metal") == 48 && data.TradeRequests == 24, "Multi-day production or dispatch checks duplicated/skipped");
                check(w.ArrivalCalls == 0 && data.Errors.Count == 0, "Multi-day interval fabricated arrival or encountered service error: " + string.Join(";", data.Errors));
                Evidence.Add(new
                {
                    scenario = "multi-day-PASS",
                    elapsed = 172800,
                    windows = services.Windows.ToArray(),
                    metal = market.GetStockCountForCategory("Metal"),
                    dispatchChecks = data.TradeRequests,
                    fuel = w.Subject.fShallowRCSRemass
                });
            }
        });
        scenario("world services real finalizers release scopes when native physical body throws", () =>
        {
            foreach (string source in new[] { "SFF", "PASS" })
            {
                var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
                using (var data = new WorldServiceFixtures(w))
                using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
                using (var services = new RetainedWorldServices(true))
                {
                    bridge.Actor.Travel.BeginNativeLowFuelReturn();
                    bool scopeWasActive = false, failed = false;
                    bridge.BeforePhysicalShip = ship =>
                    {
                        if (ship != w.Enemy)
                            return;
                        scopeWasActive = RetainedWorldServices.Scope("SffSimulation") != null && RetainedWorldServices.Scope("Economy.SffEconomy") != null;
                        throw new InvalidOperationException("fixture physical body failure");
                    };
                    try
                    {
                        if (source == "SFF")
                            bridge.Sff(7200);
                        else
                            bridge.Pass(7200);
                    }
                    catch (Exception) { failed = true; }
                    check(scopeWasActive && failed && bridge.Segments == 0, "Fault never entered a real service scope or was silently accepted");
                    check(RetainedWorldServices.Scope("SffSimulation") == null && RetainedWorldServices.Scope("Economy.SffEconomy") == null && !GUIFFWD.Active,
                        "Native physical failure leaked a scope or GUI flag");
                }
            }
        });
        scenario("world services shared binding honors disabled production modules", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices(true))
            {
                AccessTools.Field(RetainedWorldServices.Type("OrbitalSimulation"), "<Enabled>k__BackingField").SetValue(null, false);
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                bridge.Pass(7200);
                check(services.Windows.Count == 0 && RetainedWorldServices.Scope("SffSimulation") == null && RetainedWorldServices.Scope("Economy.SffEconomy") == null,
                    "Experimental PASS binding bypassed a disabled production module");
                check(w.ArrivalCalls == 0 && Math.Abs(w.Subject.fShallowRCSRemass - (double)298.2f) < 1e-6 && data.Errors.Count == 0,
                    "Disabled world scopes altered executive native route playback");
            }
        });
        scenario("world services negative control: scopes alone do not repair unmanaged PASS route completion", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices(true))
            {
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                var player = CrewSim.coPlayer.ship;
                var route = new NavData(player);
                var start = new ShipSitu(false) { vPosx = .1, vVelX = 1e-10 };
                var finish = new ShipSitu(false) { vPosx = .1001, vVelX = 0 };
                finish.vAccRCS.x = -1e-12f;
                route.AddNavDataPoint(new NavDataPoint(1000, start, 300, 0, 0));
                route.AddNavDataPoint(new NavDataPoint(1300, finish, 299, 0, 0));
                player.objSS.NavData = route;
                bridge.Pass(600);
                double extraMps = Math.Abs(player.objSS.vVelX) * 149597872000;
                check(player.objSS.NavData == null && extraMps > 80 && extraMps < 100,
                    "Known non-GUI native completion fallthrough changed; revalidate shared playback boundary");
                check(data.Playbacks["PLAYER"] == 1 && data.Errors.Count == 0, "Negative control was confounded by repeated playback or service error");
                Evidence.Add(new
                {
                    scenario = "unresolved-unmanaged-PASS-completion",
                    expectedNativeDefect = true,
                    productionReady = false,
                    extraVelocityMps = extraMps,
                    intervalSeconds = 600,
                    arrivalAfterSeconds = 300,
                    nativePlaybackCalls = data.Playbacks["PLAYER"]
                });
            }
        });
        scenario("world services rebind a saved Navy route without reconstructing economic scopes", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 20000 };
            string save;
            using (var data = new WorldServiceFixtures(w))
            using (var bridge = new NativeWorldTimeBridge(new NativeNavyActor(w, model), true))
            using (var services = new RetainedWorldServices(true))
            {
                bridge.Actor.Travel.BeginNativeLowFuelReturn();
                bridge.Sff(7200);
                save = bridge.Actor.Save();
            }
            var loaded = NativeNavyActor.Restore(model, save);
            using (var data = new WorldServiceFixtures(loaded.World, resetSubject: false))
            using (var bridge = new NativeWorldTimeBridge(loaded, true))
            using (var services = new RetainedWorldServices(true))
            {
                bridge.Pass(15000);
                check(loaded.World.Retired && loaded.World.ArrivalCalls == 1 && StarSystem.fEpoch == 23200, "Rebound route lost arrival or residual time");
                check(Math.Abs(loaded.World.Subject.fShallowRCSRemass - 295) < 1e-6 && services.Windows.Count == 4, "Rebound route recharged fuel or leaked an old service window");
                check(data.Errors.Count == 0 && RetainedWorldServices.Scope("SffSimulation") == null && RetainedWorldServices.Scope("Economy.SffEconomy") == null,
                    "Rebound world scopes failed: " + string.Join(";", data.Errors));
            }
        });
    }
}
