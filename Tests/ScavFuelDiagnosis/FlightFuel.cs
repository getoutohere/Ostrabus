using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static bool fixtureCollision;
    static int manualCalls,pathCalls;
    static bool ZeroCollision(ref float __result) {__result=0;return false;}
    static bool CollisionState(ref bool __result) {__result=fixtureCollision;return false;}
    sealed class SteeringBoundary : BaseCommand {
        readonly bool path;
        internal SteeringBoundary(bool planned) {path=planned;SaveData=new[]{"0"};}
        public override CommandStatus RunCommand(Blackboard b) {if(path)pathCalls++;else manualCalls++;return Running;}
    }
    static void StudyFlightFuel(Harmony h, bool patched) {
        var production=new Harmony("offline.scav-fuel.production");
        if(patched) foreach(string name in new[]{"AIFlightFuelPathPatch","AIFlightFuelManualPatch","AIFlightFuelSavePatch","AIFlightFuelLoadPatch","AIFlightPhysicalFuelPatch"})
            production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes."+name,true)).Patch();
        // Prescribed positions/times and zero handoff radius isolate accounting.
        // No trajectory generation, gravity or controller mode choice is asserted.
        h.Patch(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.UpdateTime)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetCollisionDistanceAU),new[]{typeof(ShipSitu),typeof(ShipSitu)}),prefix:new HarmonyMethod(typeof(Program),nameof(ZeroCollision)));
        h.Patch(AccessTools.Method(typeof(AIShipManager),nameof(AIShipManager.IsOnCollisionCourse)),prefix:new HarmonyMethod(typeof(Program),nameof(CollisionState)));
        var ship=MakeShip(100);var pilot=Empty<ScavPilot>();Back(pilot,"ShipUs",ship);
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(ship)).Clear();
        var flight=new FlyTo(pilot);var b=new Blackboard();b.Set("TripFuelPrediction","123456&80");
        ship.shipSituTarget=Empty<ShipSitu>();ship.shipSituTarget.vPosx=1;ship.shipSituTarget.bIsBO=true;
        Set(flight,"_flyToManual",new SteeringBoundary(false));Set(flight,"_flyToPath",new SteeringBoundary(true));
        fixtureCollision=true;flight.RunCommand(b);
        ship.objSS.vPosx=.25;flight.RunCommand(b);
        Check(Math.Abs(ship.GetRCSRemain()-80)<.001,"Manual progress did not debit expected 20 kg.");
        Console.WriteLine("Manual 25%: fuel="+ship.GetRCSRemain());
        // A later native planned segment covers the next 50% of this same
        // geometric trip and debits fuel using its own absolute fuel levels.
        var route=new NavData(ship);var origin=Empty<ShipSitu>();origin.vPosx=.25;var dest=Empty<ShipSitu>();dest.vPosx=.75;
        route.AddNavDataPoint(new NavDataPoint(1000,origin,80,0,0));
        route.AddNavDataPoint(new NavDataPoint(1100,dest,30,0,0));
        StarSystem.fEpoch=1100;ship.objSS.NavData=route;
        fixtureCollision=false;flight.RunCommand(b);
        Check(route.TimeAdvance(ship.objSS),"Native route did not advance.");
        Check(Math.Abs(ship.GetRCSRemain()-30)<.001 && Math.Abs(ship.objSS.vPosx-.75)<.001,"Planned segment state/fuel mismatch.");
        route.TimeAdvance(ship.objSS);
        Check(Math.Abs(ship.GetRCSRemain()-30)<.001,"Repeating planned accounting at the same epoch should not debit twice.");
        Console.WriteLine("Planned segment to 75%: fuel="+ship.GetRCSRemain());
        string[] saved=flight.SaveData;
        fixtureCollision=true;flight.RunCommand(b);
        Check(ship.GetRCSRemain()==(patched?30:0),"Resumed manual accounting had unexpected fuel.");
        Check(ship.objSS.NavData==null && pathCalls==1 && manualCalls==3,"Actual parent FlyTo did not switch modes and invalidate route as expected.");
        Console.WriteLine("Manual accounting resumes at SAME position: fuel="+ship.GetRCSRemain());
        // A repeated call at the same manual progress is itself idempotent;
        // the discontinuity is crossing accounting mechanisms, not each frame.
        flight.RunCommand(b);Check(ship.GetRCSRemain()==(patched?30:0),"Repeated manual accounting changed fuel.");
        if(patched) {
            Check(saved.Length==7,"Planned handoff save marker is missing.");
            Check(flight.SaveData.Length==5,"Completed handoff left a stale planned-mode save marker.");
            ship.objSS.vPosx=.875;flight.RunCommand(b);
            Check(Math.Abs(ship.GetRCSRemain()-20)<.001,"Manual travel after first handoff was free.");
            var nextRoute=new NavData(ship);var nextOrigin=Empty<ShipSitu>();nextOrigin.vPosx=.875;var nextEnd=Empty<ShipSitu>();nextEnd.vPosx=.9375;
            nextRoute.AddNavDataPoint(new NavDataPoint(1100,nextOrigin,20,0,0));nextRoute.AddNavDataPoint(new NavDataPoint(1200,nextEnd,10,0,0));ship.objSS.NavData=nextRoute;
            fixtureCollision=false;flight.RunCommand(b);StarSystem.fEpoch=1200;nextRoute.TimeAdvance(ship.objSS);fixtureCollision=true;flight.RunCommand(b);
            Check(Math.Abs(ship.GetRCSRemain()-10)<.001,"A second mode handoff duplicated planned consumption.");
            ship.fShallowRCSRemass=30;ship.objSS.vPosx=.75;
            var loaded=new FlyTo(pilot);loaded.SaveData=saved;
            Set(loaded,"_flyToManual",new SteeringBoundary(false));loaded.RunCommand(b);
            Check(ship.GetRCSRemain()==30,"Loading during a planned leg lost handoff ownership.");
            ship.objSS.vPosx=.875;loaded.RunCommand(b);
            Check(Math.Abs(ship.GetRCSRemain()-20)<.001,"Future manual progress stopped consuming fuel after handoff.");
            // Native still charges the final outstanding fraction, and can
            // legitimately exhaust the tank. The patch does not supply fuel.
            ship.fShallowRCSRemass=5;ship.objSS.vPosx=1;loaded.RunCommand(b);
            Check(ship.GetRCSRemain()==0,"Legitimate manual fuel exhaustion was suppressed.");
            var changedTrip=new FlyTo(pilot);changedTrip.SaveData=saved;Set(changedTrip,"_flyToManual",new SteeringBoundary(false));
            b.Set("TripFuelPrediction","222222&80");ship.fShallowRCSRemass=50;ship.objSS.vPosx=.75;changedTrip.RunCommand(b);
            Check(ship.GetRCSRemain()==50,"A changed prediction inherited the previous trip's handoff state.");
            ship.objSS.vPosx=.875;changedTrip.RunCommand(b);
            Check(Math.Abs(ship.GetRCSRemain()-10)<.001,"A new prediction stopped charging its own progress.");
            var materialized=new FlyTo(pilot);Set(materialized,"_flyToManual",new SteeringBoundary(false));
            b.Set("TripFuelPrediction","333333&80");ship.objSS.vPosx=0;ship.fShallowRCSRemass=100;materialized.RunCommand(b);
            ship.objSS.vPosx=.25;materialized.RunCommand(b);
            Check(ship.GetRCSRemain()==80,"Materialization control started with unexpected fuel.");
            Set(ship,"nLoadState",Ship.Loaded.Full);ship.objSS.vPosx=.75;materialized.RunCommand(b);
            Check(double.Parse(materialized.SaveData[3],System.Globalization.CultureInfo.CurrentCulture)==.75,"Physical-tank travel did not advance saved manual watermark.");
            // Physical spending is a boundary input. After materialization the
            // native shallow reconciliation budget contains the actual 30 kg.
            Set(ship,"nLoadState",Ship.Loaded.Shallow);ship.fShallowRCSRemass=30;materialized.RunCommand(b);
            Check(ship.GetRCSRemain()==30,"Unloading charged physical-tank travel again.");
            production.UnpatchSelf();
            var vanillaLoad=new FlyTo(pilot);vanillaLoad.SaveData=saved;
            Check(vanillaLoad.SaveData.Length==5 && vanillaLoad.SaveData[1]==saved[1],"Vanilla rejected/changed the optional handoff save fields.");
        }
        ship.fShallowRCSRemass=30;ship.objSS.vPosx=.75;b.Set("TripFuelPrediction","654321&80");flight.RunCommand(b);
        Check(ship.GetRCSRemain()==30,"A fresh trip baseline should not charge past progress at the handoff position.");
        ship.fShallowRCSRemass=100;ship.objSS.vPosx=0;var manualOnly=new FlyTo(pilot);
        Set(manualOnly,"_flyToManual",new SteeringBoundary(false));manualOnly.RunCommand(b);
        ship.objSS.vPosx=.25;manualOnly.RunCommand(b);ship.objSS.vPosx=.75;manualOnly.RunCommand(b);
        Check(Math.Abs(ship.GetRCSRemain()-40)<.001,"Manual-only control failed: 75% of an 80 kg budget should leave 40 of 100 kg.");
        Console.WriteLine("Controls: same-epoch planned debit is idempotent; fresh manual baseline preserves 30 kg; manual-only trip leaves 40 kg.");
        Console.WriteLine(checks+" mixed-accounting assertions passed. Native parent FlyTo chooses modes from prescribed collision results; steering/trajectory boundaries are fixtures. This does not identify O-KA2's historical depletion event.");
    }
}
