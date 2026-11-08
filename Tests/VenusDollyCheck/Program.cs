using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core.Models;
using SolarSystem;

static class Program {
    static int checks;
    static readonly Dictionary<CondOwner, Dictionary<string,double>> values = new Dictionary<CondOwner, Dictionary<string,double>>();
    static readonly List<double> writes = new List<double>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(),name).SetValue(o,value);
    static void Back(object o, string name, object value) => Set(o,"<"+name+">k__BackingField",value);
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Near(double got, double expected, string why) => Check(Math.Abs(got-expected)<1e-7,why+": "+got+" != "+expected);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result=ReferenceEquals(__0,__1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result=!ReferenceEquals(__0,__1); return false; }
    static double Read(CondOwner co, string name) => values.TryGetValue(co,out var map) && map.TryGetValue(name,out var v) ? v : 0;
    static bool Get(CondOwner __instance, string __0, ref double __result) { __result=Read(__instance,__0); return false; }
    static bool Has(CondOwner __instance, string __0, ref bool __result) { __result=Read(__instance,__0)>0; return false; }
    static bool Add(CondOwner __instance, string __0, double __1) {
        values[__instance][__0]=Read(__instance,__0)+__1;
        if (__0=="StatEncumbrance") writes.Add(Read(__instance,__0));
        return false;
    }
    static bool SkipWear() => false;
    static CondOwner Item(double mass, params string[] flags) {
        var co=Empty<CondOwner>(); values[co]=new Dictionary<string,double>{{"StatMass",mass}};
        foreach (var flag in flags) values[co][flag]=1;
        co.aStack=new TrackingCollection<CondOwner>(); co.mapInfo=new Dictionary<string,string>();
        return co;
    }
    static Ship Ship(string id, string reference, bool station, bool grav) {
        var ship=Empty<Ship>(); ship.strRegID=id;
        Back(ship,"objSS",Empty<ShipSitu>()); ship.objSS.bIsBO=station; ship.objSS.strBOPORShip=reference;
        Back(ship,"ShipCO",Item(0,grav ? "StationHasGrav" : "Unused"));
        Set(ship,"fGravity",1d); Set(ship,"nLoadState",global::Ship.Loaded.Full); Set(ship,"aPeople",new List<PersonSpec>());
        ship.bNeedsGravSet=true; CrewSim.system.dictShips[id]=ship;
        return ship;
    }
    static CondOwner Person(Ship ship, double mass) {
        var co=Item(mass,"IsHuman"); Set(co,"_objShip",ship);
        Set(co,"_compSlots",Empty<Slots>()); Set(co.compSlots,"aSlots",new TrackingCollection<Slot>());
        var ps=Empty<PersonSpec>(); Set(ps,"co",co);
        ((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(ship)).Add(ps);
        return co;
    }
    static Slot Hold(CondOwner person, CondOwner item, string name="drag") {
        var slot=new Slot(new JsonSlot{strName=name,nItems=1}); slot.aCOs[0]=item;
        Set(item,"_objCOParent",person); item.slotNow=slot;
        ((TrackingCollection<Slot>)AccessTools.Field(typeof(Slots),"aSlots").GetValue(person.compSlots)).Add(slot);
        return slot;
    }
    static void Connect(Ship first, Ship second) {
        var group=Empty<DockGroup>();
        Set(group,"DockedShips",new List<DockGroupMember>{new DockGroupMember(first,false),new DockGroupMember(second,false)});
        first.objSS._dockGroup=second.objSS._dockGroup=group;
    }
    // Unity docking physics is outside this harness; only emulate the connection
    // mutation, leaving production prefix/postfix and native burden updates live.
    static bool Dock(Ship __instance, Ship __0) { Connect(__instance,__0); return false; }
    static bool Undock(Ship __instance, Ship __0) { __instance.objSS._dockGroup=__0.objSS._dockGroup=null; return false; }
    static void Burden(CondOwner co, double expected, string why) {
        writes.Clear(); co.UpdateGravity(); Near(Read(co,"StatEncumbrance"),expected,why);
        foreach (double write in writes) Near(write,expected,why+" transient write");
    }
    static void Main() {
        var boundary=new Harmony("offline.venus-dolly.boundaries");
        var production=new Harmony("offline.venus-dolly.production");
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
        boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.GetCondAmount),new[]{typeof(string),typeof(bool)}),prefix:new HarmonyMethod(typeof(Program),nameof(Get)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.HasCond),new[]{typeof(string),typeof(bool)}),prefix:new HarmonyMethod(typeof(Program),nameof(Has)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.AddCondAmount)),prefix:new HarmonyMethod(typeof(Program),nameof(Add)));
        boundary.Patch(AccessTools.Method(typeof(Ship),"WearManeuver",new[]{typeof(CondTrigger),typeof(bool),typeof(float)}),prefix:new HarmonyMethod(typeof(Program),nameof(SkipWear)));
        try {
            CrewSim.system=Empty<StarSystem>(); CrewSim.system.dictShips=new Dictionary<string,Ship>();
            CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>();
            var venus=Empty<BodyOrbit>(); venus.strName="Venus"; CrewSim.system.aBOs["Venus"]=venus;
            var earth=Empty<BodyOrbit>(); earth.strName="Earth"; CrewSim.system.aBOs["Earth"]=earth;
            var station=Ship("VENC","Venus",true,true);
            var stationBody=Empty<BodyOrbit>(); stationBody.strName="VENC"; stationBody.boParent=venus;
            CrewSim.system.aBOs["VENC"]=stationBody; station.objSS.strBOPORShip="VENC";
            var person=Person(station,650); var dolly=Item(500,"IsDolly"); Hold(person,dolly);
            Burden(person,650,"Native baseline includes entire loaded dolly");
            var asm=typeof(Ostrabus.OstrabusPlugin).Assembly;
            production.CreateClassProcessor(asm.GetType("Ostrabus.Venus.DollyEncumbranceCalculationPatch",true)).Patch();
            production.CreateClassProcessor(asm.GetType("Ostrabus.Venus.DollyDockingBurdenPatch",true)).Patch();
            var helper=asm.GetType("Ostrabus.Venus.DollyEncumbrance",true);
            var eligible=(Func<Ship,bool>)Delegate.CreateDelegate(typeof(Func<Ship,bool>),AccessTools.Method(helper,"Eligible"));
            Burden(person,200,"Loaded dolly discounted; other 150 kg unchanged");
            Near(Read(person,"StatMass"),650,"Person physical mass unchanged"); Near(Read(dolly,"StatMass"),500,"Payload physical mass unchanged");
            dolly.AddMass(200,true); Burden(person,220,"Nested payload increase propagated by native AddMass");
            Near(Read(person,"StatMass"),850,"Native parent mass propagation preserved");
            Check(person.mapInfo["StatMass"].Contains("850"),"Inventory label uses physical mass");
            dolly.AddMass(-200,true); Burden(person,200,"Payload removal restores effective burden");
            writes.Clear(); station.Gravity=2;
            Check(writes.Count>=2,"Gravity setter and UpdateGravity both exercised");
            foreach (double v in writes) Near(v,400,"Gravity setter never transiently writes full burden");
            station.Gravity=1;
            var alias=new Slot(new JsonSlot{strName="hand",nItems=1}); alias.aCOs[0]=dolly;
            ((TrackingCollection<Slot>)AccessTools.Field(typeof(Slots),"aSlots").GetValue(person.compSlots)).Add(alias);
            Burden(person,200,"Secondary slot alias not double counted");
            var second=Item(100,"IsDolly"); Hold(person,second,"hand2"); person.AddMass(100,true);
            Burden(person,210,"Two independent dollies");
            Set(second,"_objCOParent",null); person.AddMass(-100,true); Burden(person,200,"Dropping dolly removes allowance");
            // A packed nested dolly is represented in the outer aggregate, never independently visited.
            var nested=Item(100,"IsDolly"); Set(nested,"_objCOParent",dolly);
            Burden(person,200,"Nested dolly payload does not multiply discount");
            nested.AddMass(150,true); Burden(person,215,"Native nested payload propagation discounted once");
            nested.AddMass(-150,true); Burden(person,200,"Nested payload removal");
            values[dolly]["IsDolly"]=0; Burden(person,650,"Ordinary container receives no allowance"); values[dolly]["IsDolly"]=1;
            var stacked=Item(50,"IsDolly"); dolly.aStack.Add(stacked); stacked.coStackHead=dolly; person.AddMass(50,true);
            Burden(person,205,"Stack members included once");
            dolly.aStack.Clear(); person.AddMass(-50,true);
            dolly.aStack.Add(dolly); Burden(person,650,"Cyclic stack safely rejected"); dolly.aStack.Clear();
            values[dolly]["StatMass"]=double.NaN; Burden(person,650,"Nonfinite item mass rejected");
            values[dolly]["StatMass"]=-1; Burden(person,650,"Negative item mass rejected");
            values[dolly]["StatMass"]=900; Burden(person,650,"Impossible aggregate does not conceal corruption");
            values[dolly]["StatMass"]=103; values[person]["StatMass"]=253; Burden(person,160.3,"Empty SuperHandy plus ordinary gear");
            values[dolly]["StatMass"]=500; values[person]["StatMass"]=650;
            var apartment=Ship("VENC|RES_1","VENC",true,false);
            Check(eligible(apartment),"Apartment follows actual parent station reference");
            foreach (var s in new[]{Ship("VORB","Venus",true,false),Ship("OTHER","Earth",true,true),Ship("MISSING","absent",true,true)}) Check(!eligible(s),"Excluded location: "+s.strRegID);
            var free=Ship("TUG","Venus",false,false); Check(!eligible(free),"Free Venus ship excluded");
            person.ship=free; Burden(person,650,"Transit/free flight immediately loses allowance");
            person.ship=apartment; Burden(person,200,"Transit to aerostat apartment restores allowance");
            person.ship=free;
            var dockMethod=AccessTools.Method(typeof(Ship),nameof(global::Ship.Dock),new[]{typeof(Ship),typeof(bool),typeof(string),typeof(string)});
            boundary.Patch(dockMethod,prefix:new HarmonyMethod(typeof(Program),nameof(Dock)){priority=Priority.Last});
            boundary.Patch(AccessTools.Method(typeof(Ship),nameof(global::Ship.Undock),new[]{typeof(Ship)}),prefix:new HarmonyMethod(typeof(Program),nameof(Undock)){priority=Priority.Last});
            // Move cached person record as native movement would, so refresh finds them.
            ((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(free)).Add(((List<PersonSpec>)AccessTools.Field(typeof(Ship),"aPeople").GetValue(station))[0]);
            free.Dock(station,false,null,null); Near(Read(person,"StatEncumbrance"),200,"Dock hook refreshes crew without inventory edits");
            Check(eligible(free),"Docked ship inherits station eligibility");
            free.Undock(station); Near(Read(person,"StatEncumbrance"),650,"Undock hook clears allowance immediately");
            Connect(free,station); free.objSS._dockGroup.DockedShips.RemoveAt(0);
            Check(!eligible(free),"Stale group reference without membership rejected");
            Near(Read(person,"StatMass"),650,"Physical/save mass unchanged across all transitions");
            Console.WriteLine("Venus dolly checks passed: "+checks);
        } finally { production.UnpatchSelf(); boundary.UnpatchSelf(); }
    }
}
