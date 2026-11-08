using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Comms;

static class Program {
    static int checks;
    static readonly HashSet<CondOwner> tutorial = new HashSet<CondOwner>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object o, string field, object value) => AccessTools.Field(o.GetType(), field).SetValue(o, value);
    static void Back(object o, string property, object value) => Set(o, "<"+property+">k__BackingField", value);
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result=ReferenceEquals(__0,__1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result=!ReferenceEquals(__0,__1); return false; }
    static bool HasCond(CondOwner __instance, string __0, ref bool __result) { __result=__0=="IsTutorialDerelict" && tutorial.Contains(__instance); return false; }
    static bool Random(int __0, ref int __result) { __result=__0; return false; }
    static Ship Make(string id, string owner, bool station=false, bool region=false, double x=0) {
        var ship=Empty<Ship>(); ship.strRegID=id; ship.DMGStatus=Ship.Damage.Used;
        Back(ship,"objSS",Empty<ShipSitu>()); ship.objSS.vPosx=x; ship.objSS.bIsBO=station; ship.objSS.bIsRegion=region;
        Back(ship,"ShipCO",Empty<CondOwner>()); ship.ShipCO.ship=ship;
        Set(ship,"nLoadState",Ship.Loaded.Shallow); Set(ship,"aDockingPorts",new List<string>{"port"});
        Set(ship,"aPeople",new List<PersonSpec>()); ship.CachedAllDockedShips=new List<Ship>();
        var comms=Empty<Comms>(); Set(comms,"_ship",ship); Set(comms,"_messageLog",new List<ShipMessage>()); Back(ship,"Comms",comms);
        CrewSim.system.dictShips[id]=ship;
        ((Dictionary<string,string>)AccessTools.Field(typeof(StarSystem),"dictShipOwners").GetValue(CrewSim.system))[id]=owner;
        return ship;
    }
    static HaulerRetrievingPilot Pilot(Ship ship, string home) {
        var pilot=Empty<HaulerRetrievingPilot>(); Back(pilot,"ShipUs",ship);
        var ai=Empty<AIShip>(); Set(ai,"_shipUs",ship); Set(ai,"_aiCharacter",pilot); Back(ai,"HomeStation",home);
        AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>{{"region",new List<AIShip>{ai}}};
        return pilot;
    }
    static List<ShipMessage> Messages(Ship ship) => (List<ShipMessage>)AccessTools.Field(typeof(Comms),"_messageLog").GetValue(ship.Comms);
    static ShipMessage Assignment(string sender, Ship target, double age=0) {
        var ia=Empty<Interaction>(); ia.obj3rd=target.ShipCO;
        return new ShipMessage{SenderRegId=sender,ReceiverRegId="HAULER",AvailableTime=StarSystem.fEpoch-age,Interaction=ia};
    }
    static void Main() {
        var boundary=new Harmony("offline.vorb-retrieval.boundaries");
        var production=new Harmony("offline.vorb-retrieval.production");
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Random),"Range",new[]{typeof(int),typeof(int)}),prefix:new HarmonyMethod(typeof(Program),nameof(Random)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.HasCond),new[]{typeof(string),typeof(bool)}),prefix:new HarmonyMethod(typeof(Program),nameof(HasCond)));
        try {
            StarSystem.fEpoch=65632479445;
            CrewSim.system=Empty<StarSystem>(); CrewSim.system.dictShips=new Dictionary<string,Ship>();
            Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>());
            PlotManager.dictPlotsActive=new Dictionary<string,JsonPlotSave>();
            DataHandler.mapCOs=new Dictionary<string,CondOwner>();
            var vnca=Make("VNCA","VNCACiv",true,true);
            var vorb=Make("VORB","VENCCiv",true);
            var oklg=Make("OKLG","OKLGCorp",true,true,1);
            var correct=Make("VENUS-WRECK","VENCCiv"); correct.DMGStatus=Ship.Damage.Derelict;
            var wrong=Make("NEWCAL-WRECK","VNCACiv"); wrong.DMGStatus=Ship.Damage.Derelict;
            var belt=Make("BELT-WRECK","OKLGCorp"); belt.DMGStatus=Ship.Damage.Derelict;
            var ship=Make("HAULER","VORBHauler"); ship.origin="VORB"; ship.fShallowRCSRemass=1000;
            var pilot=Pilot(ship,"VORB");
            Check(pilot.GetTarget().Ship==wrong,"Baseline: VORB retriever selects regional owner's wreck");
            wrong.HideFromSystem=true;
            Check(!pilot.GetTarget().HasValidTarget,"Baseline: VORB retriever finds no job despite eligible home wreck");
            Console.WriteLine("Baseline reproduced: native VORB collection uses VNCA ownership.");
            production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Venus.VorbRetrievalDepotPatch",true)).Patch();
            Check(pilot.GetTarget().Ship==correct,"Existing idle controller selects its home depot's wreck after patching");
            Check(pilot.GetTarget().Ship==correct,"Repeated selection remains functional without respawning controller");
            wrong.HideFromSystem=false;
            Check(pilot.GetTarget().Ship==correct,"Other owner's available wreck does not override home pool");
            Check(ship.shipScanTarget==null && ship.fShallowRCSRemass==1000 && correct.objSS.vPosx==0,"Selection does not issue navigation, refill fuel or teleport a wreck");
            foreach(string excluded in new[]{"hidden","full","used","tutorial","plot"}) {
                if(excluded=="hidden")correct.HideFromSystem=true;
                if(excluded=="full")Set(correct,"nLoadState",Ship.Loaded.Full);
                if(excluded=="used")correct.DMGStatus=Ship.Damage.Used;
                if(excluded=="tutorial")tutorial.Add(correct.ShipCO);
                if(excluded=="plot") {
                    DataHandler.mapCOs["plot-co"]=correct.ShipCO;
                    PlotManager.dictPlotsActive["plot"]=new JsonPlotSave{dictCOTokens=new Dictionary<string,string>{{"wreck","plot-co"}}};
                }
                Check(!pilot.GetTarget().HasValidTarget,"Native candidate protection retained: "+excluded);
                correct.HideFromSystem=false; Set(correct,"nLoadState",Ship.Loaded.Shallow); correct.DMGStatus=Ship.Damage.Derelict;
                tutorial.Clear(); PlotManager.dictPlotsActive.Clear();
            }
            var rescue=Make("PLAYER-RESCUE","player");
            Messages(ship).Add(Assignment("VORB",rescue));
            Check(pilot.GetTarget().Ship==rescue,"Explicit home-station rescue keeps precedence despite foreign ownership and Used status");
            Messages(ship).Insert(0,Assignment("VNCA",wrong));
            Check(pilot.GetTarget().Ship==rescue,"Regional station message cannot override VORB dispatch");
            Messages(ship).Clear(); Messages(ship).Add(Assignment("VORB",rescue,121));
            Check(pilot.GetTarget().Ship==correct,"Native assignment expiry retained"); Messages(ship).Clear();
            ship.CachedAllDockedShips.Add(correct);
            Check(pilot.GetTarget().Ship==vorb,"Already-towing native home return remains intact"); ship.CachedAllDockedShips.Clear();
            foreach(string invalid in new[]{"missing","destroyed","hidden","remove","notStation"}) {
                if(invalid=="missing")CrewSim.system.dictShips.Remove("VORB");
                if(invalid=="destroyed")vorb.bDestroyed=true;
                if(invalid=="hidden")vorb.HideFromSystem=true;
                if(invalid=="remove")vorb.bRemove=true;
                if(invalid=="notStation")vorb.objSS.bIsBO=false;
                Check(pilot.GetTarget().Ship==wrong,"Invalid dispatch station retains native fallback: "+invalid);
                CrewSim.system.dictShips["VORB"]=vorb; vorb.bDestroyed=false; vorb.HideFromSystem=false; vorb.bRemove=false; vorb.objSS.bIsBO=true;
            }
            ship.objSS.vPosx=1; pilot=Pilot(ship,"OKLG");
            Check(pilot.GetTarget().Ship==belt,"OKLG still selects OKLGCorp wrecks even with stale VORB origin");
            Messages(ship).Add(Assignment("OKLG",rescue));
            Check(pilot.GetTarget().Ship==rescue,"OKLG rescue behavior unchanged"); Messages(ship).Clear();
            ship.objSS.vPosx=0; pilot=Pilot(ship,"VNCA");
            Check(pilot.GetTarget().Ship==wrong,"Non-VORB dispatch unchanged");
            pilot=Pilot(ship,"VORB"); AIShipManager.dictAIs.Clear();
            Check(pilot.GetTarget().Ship==wrong,"Unregistered pilot retains native behavior");
            // Use the actual saved-controller constructor: no new save fields or migration.
            var restored=new AIShip(new JsonAIShipSave{strHomeStation="VORB",enumAIType=AIType.HaulerRetriever},ship);
            AIShipManager.dictAIs["VNCA"]=new List<AIShip>{restored};
            Check(restored.AICharacter.GetTarget().Ship==correct,"Native save-restored controller uses its saved VORB dispatch station");
            Console.WriteLine(checks+" native VORB retrieval checks passed.");
        } finally { production.UnpatchSelf(); boundary.UnpatchSelf(); }
    }
}
