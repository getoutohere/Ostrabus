using System;
using System.Collections.Generic;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

namespace ShipExecutivePrototype;

static class NavyTimeChecks
{
    internal static readonly List<object> Evidence = new List<object>();
    internal static void Run(Action<string, Action> scenario, Action<bool, string> check, Definition model)
    {
        scenario("Navy real SFF suppresses a live legacy flight callback without swallowing player playback", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 40000 };
            var actor = new NativeNavyActor(w, model);
            actor.Travel.BeginNativeLowFuelReturn();
            NativeWorld.Set(w.AI, "_activeCommand", new FlyTo(w.Pilot));
            var player = CrewSim.coPlayer.ship;
            var playerRoute = new NavData(player);
            playerRoute.AddNavDataPoint(new NavDataPoint(1000, new ShipSitu(false) { vPosx = .1 }, 300, 0, 100));
            playerRoute.AddNavDataPoint(new NavDataPoint(41000, new ShipSitu(false) { vPosx = .2 }, 300, 0, 80));
            player.objSS.NavData = playerRoute;
            using (var bridge = new NativeWorldTimeBridge(actor))
            {
                bridge.Sff(12000);
                check(bridge.SuppressedCatchups > 0, "Real catch-up boundary was not exercised: segments=" + bridge.Segments + " epoch=" + StarSystem.fEpoch);
                check(!w.Pilot.PriorityQueue.CanExecute(w.Pilot.Blackboard), "Native catch-up injected a second flight controller: " + w.Pilot.PriorityQueue.ActiveCommandName);
                check(w.ArrivalCalls == 0 && Math.Abs(w.Subject.fShallowRCSRemass - 298.5) < 1e-6, "Partial real SFF teleported or resettled the whole flight");
                check(ReferenceEquals(player.objSS.NavData, playerRoute) && Math.Abs(player.objSS.vPosx - .13) < 1e-7 && Math.Abs(player.fShallowFusionRemain - 94) < 1e-5,
                    "Executive intercepted unrelated native torch-course playback: x=" + player.objSS.vPosx + " fuel=" + player.fShallowFusionRemain);
                bridge.Pass(12000);
                bridge.Normal(16000);
                check(w.Retired && w.ArrivalCalls == 1 && StarSystem.fEpoch == 41000 && Math.Abs(w.Subject.fShallowRCSRemass - 295) < 1e-6, "Mixed native entrypoints changed arrival or fuel accounting");
                check(NativeClock.ExcludedSceneCalls == 0, "Excluded SFF/PASS scene tail executed");
                Evidence.Add(new
                {
                    scenario = "mixed-native-world-entrypoints",
                    routeSeconds = 40000,
                    epoch = StarSystem.fEpoch,
                    fuel = w.Subject.fShallowRCSRemass,
                    bridge.Segments,
                    bridge.NativeDispatches,
                    bridge.SuppressedCatchups,
                    arrivalCalls = w.ArrivalCalls,
                    playerRouteRetained = ReferenceEquals(player.objSS.NavData, playerRoute),
                    playerTorchFuel = player.fShallowFusionRemain,
                    excludedSceneCalls = NativeClock.ExcludedSceneCalls
                });
            }
        });
        scenario("Navy actual world route reload consumes the residual interval exactly once", () =>
        {
            var w = new NativeWorld { LowFuel = true, RouteDuration = 400 };
            var actor = new NativeNavyActor(w, model);
            actor.Travel.BeginNativeLowFuelReturn();
            using (var bridge = new NativeWorldTimeBridge(actor))
                bridge.Pass(120);
            var loaded = NativeNavyActor.Restore(model, actor.Save());
            using (var bridge = new NativeWorldTimeBridge(loaded))
            {
                bridge.Sff(600);
                check(loaded.World.Retired && loaded.World.ArrivalCalls == 1 && StarSystem.fEpoch == 1720, "Reloaded native coordinator lost residual world time");
                check(Math.Abs(loaded.World.Subject.fShallowRCSRemass - 295) < 1e-6 && bridge.Segments == 2, "Reload duplicated fuel or route-boundary processing");
                Evidence.Add(new
                {
                    scenario = "native-world-reload-residual",
                    epoch = StarSystem.fEpoch,
                    fuel = loaded.World.Subject.fShallowRCSRemass,
                    bridge.Segments,
                    arrivalCalls = loaded.World.ArrivalCalls
                });
            }
        });
        scenario("Navy combat bulk advancement refuses before world integration", () =>
        {
            var w = new NativeWorld { Threat = true };
            var actor = new NativeNavyActor(w, model);
            actor.Tick();
            actor.Tick();
            double x = w.Subject.objSS.vPosx, fuel = w.Subject.fShallowRCSRemass, epoch = StarSystem.fEpoch;
            using (var bridge = new NativeWorldTimeBridge(actor))
            {
                bool refused = false;
                try
                {
                    bridge.Pass(600);
                }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException is NotSupportedException) { refused = true; }
                catch (NotSupportedException) { refused = true; }
                check(refused && bridge.RejectedCombatSkip && bridge.Segments == 0, "Bulk combat silently advanced or invented an outcome");
                check(StarSystem.fEpoch == epoch && w.Subject.objSS.vPosx == x && w.Subject.fShallowRCSRemass == fuel, "Refused skip partially integrated the world");
            }
        });
    }
}
