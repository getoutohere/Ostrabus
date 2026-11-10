using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

static partial class Program {
    static double availableDV;
    static bool sendOK, interregional;
    static int tensionResets;
    static bool Available(ref double __result) {__result=availableDV;return false;}
    static bool Needed(ref double __result) {__result=10;return false;}
    static bool Interregional(ref bool __result) {__result=interregional;return false;}
    static bool RecoverySend(string ia,ref bool __result) {sends++;lastSend=ia;__result=sendOK;return false;}
    static bool Tension() {tensionResets++;return false;}
    static void CheckRecovery(Harmony h) {
        var production=new Harmony("offline.scav-recovery.production");
        try {
            foreach(string name in new[]{"ScavVisitRegistrationPatch","ScavVisitFuelPatch","ScavHomeFeasibilityPatch","ScavNullHomePlanPatch","ScavHelpLifecyclePatch"})
                production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes."+name,true)).Patch();
            h.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.DeltaVRemainingRCS)),prefix:new HarmonyMethod(typeof(Program),nameof(Available)));
            h.Patch(AccessTools.Method(typeof(AIShipManager),nameof(AIShipManager.GetDeltaVNeededToTargetFullTrip)),prefix:new HarmonyMethod(typeof(Program),nameof(Needed)));
            h.Patch(AccessTools.Method(typeof(FlyToPath),nameof(FlyToPath.IsInterregionalPath)),prefix:new HarmonyMethod(typeof(Program),nameof(Interregional)));
            var send=AccessTools.Method(typeof(Comms),nameof(Comms.SendMessage),new[]{typeof(string),typeof(string),typeof(string)});
            h.Unpatch(send,HarmonyPatchType.Prefix,h.Id);
            h.Patch(send,prefix:new HarmonyMethod(typeof(Program),nameof(RecoverySend)));
            var tension=AccessTools.Method(typeof(BeatManager),nameof(BeatManager.ResetTensionTimer));
            h.Unpatch(tension,HarmonyPatchType.Prefix,h.Id);
            h.Patch(tension,prefix:new HarmonyMethod(typeof(Program),nameof(Tension)));

            help=false;var ship=MakeShip(0);var scav=new ScavPilot(null,ship);
            var branches=(List<ICommand>)AccessTools.Field(typeof(SelectorCommand),"_childCommands").GetValue(scav.BehaviorTree);
            var main=(StickySequenceCommand)branches.Single(c=>(string)AccessTools.Field(typeof(BaseCommand),"_nodeName").GetValue(c)=="Main");
            var leaves=(List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand),"_childCommands").GetValue(main);
            main.SaveData=new[]{"4"};ship.fAIDockingExpire=20;
            Check(main.RunCommand(scav.Blackboard).Status==CommandCode.Running,"Visit timer was bypassed.");
            StarSystem.fEpoch+=30;
            Check(main.RunCommand(scav.Blackboard).Status==CommandCode.Finished && main.SaveData[0]=="0","Empty ordinary visit did not release Main.");
            leaves[0]=new UndockBoundary();int oldUndocks=undocks;
            Check(main.RunCommand(scav.Blackboard).Status==CommandCode.Finished && undocks==oldUndocks+1,"Released visit did not reach native sequence's Undock slot.");
            ship.fAIDockingExpire=0;help=true;
            Check(leaves[4].RunCommand(scav.Blackboard).Status==CommandCode.Running,"Requested help no longer preserves Hold.");
            help=false;ship.fAIDockingExpire=0;
            Check(new Hold(scav).RunCommand(scav.Blackboard).Status==CommandCode.Running,"Unmarked queue/combat Hold was changed.");

            ship=HelpShip();var pilot=new HelpPilot(ship);var target=new TargetNewIfNone(pilot);
            lowFixture=true;availableDV=20;int sent=sends;
            target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==homeFixture && ship.objSS.NavData==null && sends==sent,"Affordable null-plan manual fallback rejected.");
            availableDV=10;ship.ClearShipTarget();target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==homeFixture,"Exact one-leg fuel boundary rejected.");
            ship.ClearShipTarget();availableDV=0;plannedFixture=new NavData(ship);target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==homeFixture && ReferenceEquals(ship.objSS.NavData,plannedFixture),"Accepted native plan was rejected by manual fallback heuristic.");
            plannedFixture=null;
            ship.ClearShipTarget();availableDV=0;ship.LiftRotorsThrustStrength=1;target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==homeFixture,"Rotor exception rejected.");
            ship.ClearShipTarget();ship.LiftRotorsThrustStrength=0;ship.bFusionReactorRunning=true;interregional=true;target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==homeFixture,"Interregional fusion exception rejected.");
            ship.ClearShipTarget();interregional=false;ship.bFusionReactorRunning=false;
            CrewSim.coPlayer.ship.objSS.vPosx=1E-4;sendOK=true;int retired=unregisters;sent=sends;
            Check(target.RunCommand(pilot.Blackboard).Status==CommandCode.Finished,"Stranded ATC path did not retain controller.");
            Check(ship.shipScanTarget==null && sends==sent+1 && lastSend=="SHIPSosStranded" && unregisters==retired,"Infeasible home failed to reach native ATC interaction without retirement.");
            Check(ship.fAIPauseTimer==StarSystem.fEpoch+64,"Existing native pause was not retained.");
            sendOK=false;sent=sends;target.RunCommand(pilot.Blackboard);
            Check(sends==sent+1 && unregisters==retired,"Failed ATC send retired controller.");
            CrewSim.coPlayer.ship.objSS.vPosx=0;tensionResets=0;target.RunCommand(pilot.Blackboard);
            Check(lastSend=="SHIPSosAskPlayer" && tensionResets==0 && unregisters==retired,"Failed player send falsely reset tension/retired controller.");
            sendOK=true;target.RunCommand(pilot.Blackboard);
            Check(tensionResets==1,"Successful player send did not reset native tension.");
            help=true;sent=sends;target.RunCommand(pilot.Blackboard);
            Check(sends==sent && unregisters==retired,"Existing help request was resent/retired.");
            help=false;pilot.MissingHome=true;sent=sends;target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==null && sends==sent+1 && unregisters==retired,"Missing home did not safely reach native SOS routing.");
            pilot.MissingHome=false;pilot.Role=AIType.Navy;target.RunCommand(pilot.Blackboard);
            Check(ship.shipScanTarget==homeFixture,"Non-scav home selection was changed.");
            ship.ClearShipTarget();lowFixture=false;sent=sends;target.RunCommand(pilot.Blackboard);
            Check(sends==sent && unregisters>retired,"Non-scav retirement/SOS policy changed.");
            Console.WriteLine(checks+" cumulative SOS/recovery assertions passed. Propulsion budgets and send outcomes are fixture boundaries; no dispatch success is inferred.");
        } finally {production.UnpatchSelf();}
    }
}
