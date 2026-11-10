using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

static partial class Program {
    static bool lowFixture;
    static int sends,unregisters,plans;
    static string lastSend;
    static Ship homeFixture;
    static double commsRange;
    sealed class HelpPilot : BaseCharacter {
        internal AIType Role=AIType.Scav;
        internal bool MissingHome;
        internal Ship Candidate;
        internal HelpPilot(Ship ship) {ShipUs=ship;}
        public override AIType AIType => Role;
        public override TargetData GetTarget() => Candidate == null ? null : new TargetData(Candidate);
        public override TargetData GetTargetHome() => MissingHome ? null : new TargetData(homeFixture);
        public override double MaxSpeed(bool? atmo) => 1E-9;
        public override ICommand FFWD(ICommand last,Blackboard b) => last;
    }
    static bool LowFixture(ref bool __result) {__result=lowFixture;return false;}
    static NavData plannedFixture;
    static bool FailedPlan(ref NavData __result) {plans++;__result=plannedFixture;return false;}
    static bool SendFixture(string ia,ref bool __result) {sends++;lastSend=ia;__result=true;return false;}
    static bool UnregisterFixture() {unregisters++;return false;}
    static bool RegionFixture(ref Ship __result) {__result=homeFixture;return false;}
    static bool RangeFixture(ref double __result) {__result=commsRange;return false;}
    static Ship HelpShip() {
        var ship=MakeShip(0);((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(ship)).Clear();return ship;
    }
    static void CheckSosPaths(Harmony h,string game) {
        string data=Path.Combine(game,"Ostranauts_Data","StreamingAssets","data");
        JObject Entry(string table,string id) => JArray.Parse(File.ReadAllText(Path.Combine(data,table))).OfType<JObject>().Single(e=>(string)e["strName"]==id);
        JObject Ia(string id) => Entry("interactions/interactions_ship_to_ship.json",id);
        JObject Loot(string id) => Entry("loot/loot.json",id);
        var playerAsk=Ia("SHIPSosAskPlayer");var stationAsk=Ia("SHIPSosStranded");var dispatch=Ia("SHIPSOSDispatchHauler");
        Check((string)playerAsk["LootCondsUs"]=="ShipRequestedHelp" && (string)playerAsk["CTTestUs"]=="TIsHasNotRequestedHelp","Player SOS flag/gate definition changed.");
        Check(playerAsk["aInverse"].Values<string>().Contains("SHIPSosAskPlayerAffirmative") && playerAsk["aInverse"].Values<string>().Contains("SHIPSosAskPlayerNegative"),"Player SOS reply choices missing.");
        Check(Loot("TXTShipStandardInteractions")["aCOs"].Values<string>().Any(s=>s.StartsWith("SHIPSosAskPlayer=")),"Player SOS is absent from standard interaction inventory.");
        Check(Loot("TXTStationInteractions")["aCOs"].Values<string>().Any(s=>s.StartsWith("SHIPSosStranded=")),"ATC SOS is absent from station interaction inventory.");
        Check((string)stationAsk["ShipTestThem"]=="AnyStationRegional" && stationAsk["aInverse"].Values<string>().Contains("SHIPSOSDispatchHauler"),"Station SOS reply link changed.");
        Check((string)dispatch["ShipTestThem"]=="IsNotDocked" && (double)dispatch["fTargetPointRange"]==1000,"Station dispatch constraints changed.");
        Check((int)Entry("shipspecs/shipspecs.json","IsNotDocked")["nIsDocked"]==-1,"Undocked specification changed.");
        Check((string)dispatch["LootCTsUs"]=="CMDDeployHauler" && Loot("CMDDeployHauler")["aCOs"].Values<string>().Contains("DeployHauler=1x1"),"Hauler command wiring changed.");
        Check((string)Ia("SHIPTransferFuelAllow")["LootCTsUs"]=="CMDTakeFuel" && Loot("CMDTakeFuel")["aCOs"].Values<string>().Contains("TakeFuel=1x1"),"Player fuel transfer command wiring changed.");
        Check((string)Ia("SHIPTransferFuelComplete")["LootCondsUs"]=="ShipRemoveHelpRequest" && (string)Ia("SHIPTransferFuelComplete")["strPledgeAdd"]=="DisembarkAndUndock","Completed transfer cleanup changed.");
        Check((string)Entry("pledges/pledges.json","DisembarkAndUndock")["strIATrigger"]=="PLGDisembarkCommand","Transfer completion pledge is missing.");
        var condition=Entry("conditions/conditions.json","ShipRequestedHelp");
        var ticker=new JsonTicker {fPeriod=(double)condition["fDuration"],fEpochStart=1000};
        Check(Math.Abs(ticker.GetEpochEndS()-1180)<.001,"Help flag nominal duration is not three minutes.");
        h.Patch(AccessTools.Method(typeof(AIShipManager),nameof(AIShipManager.LowFuel)),prefix:new HarmonyMethod(typeof(Program),nameof(LowFixture)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Debug),nameof(UnityEngine.Debug.Log),new[]{typeof(object)}),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.SetThrust)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(AIShipManager),nameof(AIShipManager.Log)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(TargetNewIfNone),"GenerateNavData"),prefix:new HarmonyMethod(typeof(Program),nameof(FailedPlan)));
        h.Patch(AccessTools.Method(typeof(TargetNewIfNone),"FindTarget"),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(Comms),nameof(Comms.SendMessage),new[]{typeof(string),typeof(string),typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(SendFixture)));
        h.Patch(AccessTools.Method(typeof(BeatManager),nameof(BeatManager.ResetTensionTimer)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(DataHandler),nameof(DataHandler.GetPersonSpec)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(AIShipManager),nameof(AIShipManager.UnregisterShip),new[]{typeof(Ship)}),prefix:new HarmonyMethod(typeof(Program),nameof(UnregisterFixture)));
        h.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestStationRegional)),prefix:new HarmonyMethod(typeof(Program),nameof(RegionFixture)));
        h.Patch(AccessTools.Method(typeof(StarSystem),nameof(StarSystem.GetNearestStation)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(BaseCommand),"TryInstantCleanup"),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
        CrewSim.coPlayer=Empty<CondOwner>();Set(CrewSim.coPlayer,"_strID","PLAYER");CrewSim.coPlayer.ship=HelpShip();
        homeFixture=HelpShip();homeFixture.strRegID="OKLG";homeFixture.objSS.bIsBO=true;
        var ship=HelpShip();var pilot=new HelpPilot(ship);var select=new TargetNewIfNone(pilot);
        lowFixture=true;
        Check(select.RunCommand(pilot.Blackboard).Status==CommandCode.Finished,"Low-fuel failed-home-plan branch did not finish.");
        Check(plans==1 && ship.shipScanTarget==homeFixture && ship.objSS.NavData==null && sends==0,"Low-fuel route failure unexpectedly requested help instead of accepting home target.");
        Console.WriteLine("Native low fuel + null home route -> target accepted, SOS calls="+sends);
        // A successful player send is a fixture boundary. This checks which
        // native branch calls SendMessage, not complete comms delivery/loot.
        ship.ClearShipTarget();lowFixture=false;
        Check(select.RunCommand(pilot.Blackboard).Status==CommandCode.Finished && lastSend=="SHIPSosAskPlayer" && sends==1,"Failed ordinary search did not reach nearby-player SOS.");
        Console.WriteLine("Native ordinary search failure + nearby player -> "+lastSend);
        ship.ClearShipTarget();ship.fAIPauseTimer=0;CrewSim.coPlayer.ship.objSS.vPosx=1E-4;
        Check(select.RunCommand(pilot.Blackboard).Status==CommandCode.Failure && lastSend=="SHIPSosStranded" && sends==2 && unregisters>0,"Failed ordinary search did not reach ATC SOS.");
        Console.WriteLine("Native ordinary search failure + distant player -> "+lastSend);
        int previousSends=sends;
        ship.shipSituTarget=Empty<ShipSitu>();new SosOutOfFuel(pilot).RunCommand(pilot.Blackboard);
        Check(sends==previousSends,"Standalone SOS did not preserve near-current-target suppression.");
        ship.shipSituTarget=null;new SosOutOfFuel(pilot).RunCommand(pilot.Blackboard);
        Check(sends==previousSends+1 && lastSend=="SHIPSosStranded","Standalone SOS couldn't reach ATC without nearby target.");
        Check(new AmIOutOfFuel(pilot).RunCommand(pilot.Blackboard).Status==CommandCode.Failure,"Targetless out-of-fuel predicate unexpectedly succeeds.");
        var notDocked=Entry("shipspecs/shipspecs.json","IsNotDocked").ToObject<JsonShipSpec>();
        var links=(Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(ship);links["port"]=homeFixture;ship.ResetDockedCache();
        Check(!notDocked.Matches(ship),"Actual dispatch shipspec accepted a docked ship.");
        links.Clear();ship.ResetDockedCache();Check(notDocked.Matches(ship),"Actual dispatch shipspec rejected an undocked ship.");
        h.Patch(AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetRangeToCollisionKM)),prefix:new HarmonyMethod(typeof(Program),nameof(RangeFixture)));
        var atc=Empty<CondOwner>();atc.ship=homeFixture;var caller=Empty<CondOwner>();caller.ship=ship;
        var ia=Empty<Interaction>();ia.objUs=atc;ia.objThem=caller;ia.fTargetPointRange=(float)dispatch["fTargetPointRange"];
        var within=AccessTools.Method(typeof(Interaction),"IsWithinShipRange");
        commsRange=999;Check((bool)within.Invoke(ia,null),"Dispatch should accept 999 km collision-boundary range.");
        commsRange=1000;Check(!(bool)within.Invoke(ia,null),"Dispatch strict 1000 km boundary was lost.");
        Console.WriteLine(checks+" asset/native SOS checks passed. Planner failure, collision range, delivery, captain lookup and cleanup are explicit fixture boundaries; no live messages or dispatch occurred.");
    }
}
