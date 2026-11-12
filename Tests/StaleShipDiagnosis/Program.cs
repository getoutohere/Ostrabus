using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;

// Run native retirement, reserve selection, reset, registration, stale sweep and
// crew-recycling decisions. Replace Unity controls, condition storage and Task
// scheduling only. Run the unpatched reproduction, then actual production hooks;
// no live process is involved.
static partial class Program {
    static int checks;
    static readonly Dictionary<CondOwner,Dictionary<string,double>> values = new Dictionary<CondOwner,Dictionary<string,double>>();
    static readonly List<string> messages = new List<string>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj,string field,object value) => AccessTools.Field(obj.GetType(),field).SetValue(obj,value);
    static void Back(object obj,string property,object value) => Set(obj,"<"+property+">k__BackingField",value);
    static void Check(bool ok,string message) { checks++; if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static double Amount(CondOwner co,string name) => values.TryGetValue(co,out var conditions) && conditions.TryGetValue(name,out var value) ? value : 0;
    static void Write(CondOwner co,string name,double value) {
        if (!values.TryGetValue(co,out var conditions)) values[co] = conditions = new Dictionary<string,double>();
        conditions[name] = value;
    }
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) { __result=ReferenceEquals(__0,__1); return false; }
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) { __result=!ReferenceEquals(__0,__1); return false; }
    static Action<CondOwner,string> onRead;
    static bool GetAmount(CondOwner __instance,string __0,ref double __result) {
        __result=Amount(__instance,__0);
        onRead?.Invoke(__instance,__0);
        return false;
    }
    static bool Has(CondOwner __instance,string __0,ref bool __result) { __result=Amount(__instance,__0)>0; return false; }
    static bool Store(CondOwner __instance,string __0,double __1) { Write(__instance,__0,__1); return false; }
    static bool Zero(CondOwner __instance,string __0) { Write(__instance,__0,0); return false; }
    static bool Quiet() => false;
    static bool Log(object __0) { messages.Add(Convert.ToString(__0)); return false; }
    static bool InlineTask(Action __0,ref Task __result) { __0(); __result=Task.CompletedTask; return false; }
    static Ship Hull(string id,bool station=false) {
        var ship=Empty<Ship>();ship.strRegID=id;Back(ship,"objSS",Empty<ShipSitu>());Back(ship,"ShipCO",Empty<CondOwner>());
        ship.ShipCO.ship=ship;Set(ship.ShipCO,"_strID","CO-"+id);
        Set(ship,"nLoadState",Ship.Loaded.Shallow);Set(ship,"aPeople",new List<PersonSpec>());
        Set(ship,"aDocked",new Dictionary<string,Ship>());Set(ship,"aDockingPorts",new List<string>());
        Set(ship,"aFactions",new List<JsonFaction>());ship.objSS.bIsBO=station;
        if (station) Write(ship.ShipCO,"StationMaxPopulation",100);
        var damage=AccessTools.Property(typeof(Ship),"DamageSystem").PropertyType;
        Back(ship,"DamageSystem",FormatterServices.GetUninitializedObject(damage));
        return ship;
    }
    static CondOwner Person(Ship ship,string id) {
        var person=Empty<CondOwner>();person.ship=ship;person.bAlive=true;Set(person,"_strID",id);Set(person,"aMyShips",new HashSet<string>{ship.strRegID});
        var spec=Empty<PersonSpec>();spec.strCO=id;Set(spec,"co",person);person.pspec=spec;
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(ship)).Add(spec);
        return person;
    }
    static AIShip Controller(Ship ship) {
        var pilot=Empty<ScavPilot>();Back(pilot,"ShipUs",ship);pilot.Blackboard=new Blackboard();
        var ai=Empty<AIShip>();Set(ai,"_shipUs",ship);Set(ai,"_aiCharacter",pilot);Back(ai,"Blackboard",pilot.Blackboard);return ai;
    }
    static void Patch(Harmony h,Type type,string method,string prefix,Type[] parameters=null) =>
        h.Patch(AccessTools.Method(type,method,parameters),prefix:new HarmonyMethod(typeof(Program),prefix));
    static void Sweep() => AccessTools.Method(typeof(StarSystem),"CleanupStaleShips").Invoke(null,null);
    static bool Recycle(Ship ship) => (bool)AccessTools.Method(typeof(StarSystem),"TryRecycleCrew").Invoke(CrewSim.system,new object[]{ship});
    static void Main() {
        var boundary=new Harmony("offline.stale-ship.boundaries");
        Patch(boundary,typeof(UnityEngine.Object),"op_Equality",nameof(Equal));
        Patch(boundary,typeof(UnityEngine.Object),"op_Inequality",nameof(Unequal));
        Patch(boundary,typeof(CondOwner),"GetCondAmount",nameof(GetAmount),new[]{typeof(string),typeof(bool)});
        Patch(boundary,typeof(CondOwner),"HasCond",nameof(Has),new[]{typeof(string),typeof(bool)});
        Patch(boundary,typeof(CondOwner),"SetCondAmount",nameof(Store));
        Patch(boundary,typeof(CondOwner),"ZeroCondAmount",nameof(Zero));
        Patch(boundary,typeof(Ship),"Maneuver",nameof(Quiet));
        Patch(boundary,typeof(Ship),"SetThrust",nameof(Quiet));
        Patch(boundary,typeof(UnityEngine.Debug),"Log",nameof(Log),new[]{typeof(object)});
        Patch(boundary,typeof(Task),"Run",nameof(InlineTask),new[]{typeof(Action)});
        Patch(boundary,AccessTools.Property(typeof(Ship),"DamageSystem").PropertyType,"RemoveShallowDamage",nameof(Quiet));
        try {
            StarSystem.fEpoch=100000;
            var ship=Hull("SCAV");var station=Hull("STATION",true);var player=Hull("PLAYER");var pilot=Person(ship,"Healthy Pilot");
            CrewSim.coPlayer=Person(player,"Player");CrewSim.system=Empty<StarSystem>();
            CrewSim.system.dictShips=new Dictionary<string,Ship>{{ship.strRegID,ship},{station.strRegID,station},{player.strRegID,player}};
            Set(CrewSim.system,"dictShipOwners",new Dictionary<string,string>{{ship.strRegID,"StationScav"}});
            AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>();SetManagerQueue();
            var ai=Controller(ship);AIShipManager.AddAIToDict(ai,"REGION");
            Check(Amount(ship.ShipCO,"IsStale")==0,"Fresh active controller has no disposal timestamp");
            AIShipManager.UnregisterShip(ship);
            Check(Amount(ship.ShipCO,"IsStale")==100000,"Native unregistration stamps retirement time");
            ship.HideFromSystem=true;AIShipManager.ShipClosestPOI=station;ship.strAIDespawnedAt=station.strRegID;
            Check(ReferenceEquals(AIShip.FindShipForFaction("StationScav"),ship),"Native reserve selector reuses retired hull");
            AIShip.ResetShipState(ship);
            Check(!ship.HideFromSystem && ship.IsAIShip && Amount(ship.ShipCO,"IsStale")==100000,"Native active reset leaves old retirement timestamp intact");
            AIShipManager.AddAIToDict(Controller(ship),"REGION");
            Check(AIShipManager.GetAIShipByRegID(ship.strRegID)!=null && pilot.bAlive && pilot.ship==ship,"Reused ship has registered AI and living pilot aboard");
            StarSystem.fEpoch=103600;Sweep();Check(!ship.bRemove,"First one-hour skip stays below stale threshold");
            StarSystem.fEpoch=107200;Sweep();Check(!ship.bRemove,"Second one-hour skip stays below threshold for fresh retirement");
            StarSystem.fEpoch=110800;Sweep();
            Check(ship.bRemove && pilot.bAlive && AIShipManager.GetAIShipByRegID(ship.strRegID)!=null,"Three-hour sweep marks still-active crewed ship for destruction");
            ship.bRemove=false;ship.HideFromSystem=true;Sweep();
            Check(ship.bRemove,"Legitimately retired reserve remains eligible for normal cleanup");
            ship.bRemove=false;ship.HideFromSystem=false;Write(ship.ShipCO,"IsStale",0);Sweep();
            Check(!ship.bRemove,"Clearing completed retirement timestamp prevents erroneous removal");
            var friendly=new JsonFaction{strName="StationCitizens"};((List<JsonFaction>)AccessTools.Field(typeof(Ship),"aFactions").GetValue(station)).Add(friendly);
            Check(CrewSim.system.GetStations(true).Contains(station) && station.Population<station.MaxPopulation,"Crew recycle has station with spare capacity");
            Check(!Recycle(ship) && ship.GetShipFactions().Count==0,"Native crew recycle fails for factionless hull despite living pilot and spare station");
            ((List<JsonFaction>)AccessTools.Field(typeof(Ship),"aFactions").GetValue(ship)).Add(new JsonFaction{strName="DifferentFaction"});
            Check(!Recycle(ship),"Unmatched hull faction also prevents recycling");
            ReplayTwoHourSkips();
            RepairChecks();
            Console.WriteLine(checks+" native stale-lifecycle and repair checks passed.");
        } finally { boundary.UnpatchSelf(); }
    }
    static void ReplayTwoHourSkips() {
        // Ages measured in the pre-fix home save; anonymous fixtures preserve
        // only those scalar ages, never private NPCs, ships or inventories.
        const double origin=200000;
        var scav=Hull("ACTIVE-SCAV");var police=Hull("ACTIVE-POLICE");var hauler=Hull("ACTIVE-HAULER");
        var ships=new[]{scav,police,hauler};
        double[] ages={6149.2497,4166.4747,2693.9544};
        for(int i=0;i<ships.Length;i++) {
            CrewSim.system.dictShips[ships[i].strRegID]=ships[i];
            Person(ships[i],"Pilot-"+i);AIShipManager.AddAIToDict(Controller(ships[i]),"REGION");
            Write(ships[i].ShipCO,"IsStale",origin-ages[i]);
        }
        StarSystem.fEpoch=origin;Sweep();
        Check(ships.All(s=>!s.bRemove),"Saved marker ages do not remove active ships at home epoch");
        StarSystem.fEpoch=origin+3600;Sweep();
        Check(ships.All(s=>!s.bRemove),"First one-hour SFF does not yet expire saved markers");
        StarSystem.fEpoch=origin+7200;Sweep();
        Check(scav.bRemove && police.bRemove && !hauler.bRemove,"Second one-hour SFF expires scav and police markers, but not newer hauler marker");
        Check(ships.All(s=>AIShipManager.GetAIShipByRegID(s.strRegID)!=null && s.GetPeople(false).Single().bAlive),
            "Expiry ignores active controllers and their healthy pilots");
        scav.bRemove=false;police.bRemove=false;
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(scav))["port"]=police;
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(police))["port"]=scav;
        Sweep();Check(!scav.bRemove && !police.bRemove,"Docking temporarily masks expired disposal markers");
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(scav)).Clear();
        ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship),"aDocked").GetValue(police)).Clear();
        Sweep();Check(scav.bRemove && police.bRemove,"Separating exposes both expired markers without a new failure or injury");
    }
    static void SetManagerQueue() {
        // Keep the native readonly queue identity: Mono can embed its address
        // in already-JITted registration/unregistration code.
        ((AIShipQueue)AccessTools.Field(typeof(AIShipManager),"_aIQueue").GetValue(null)).Clear();
    }
}
