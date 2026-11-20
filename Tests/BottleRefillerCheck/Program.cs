using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.BottleRefiller;
using UnityEngine;
using Runtime = Ostrabus.BottleRefiller.Runtime;

internal static partial class Program
{
    private static int checks, serial;
    private static string Assets;
    private static string Data => Ostrabus.Development.InstallationPaths.Data;
    private static readonly string[] Species = FluidStrings.mol.ToArray();
    private static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    private static void Check(bool test, string message) { checks++; if (!test) throw new Exception(message); }
    private static void Near(double a, double b, string message, double tolerance = 1e-8) => Check(Math.Abs(a-b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a),Math.Abs(b))), message+": "+a+" vs "+b);
    private static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0,__1); return false; }
    private static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0,__1); return false; }
    private static bool Shader(ref int __result) { __result=1;return false; }
    private static bool Quiet() => false;
    private static bool StringValue(string strName, ref string __result) { __result=strName;return false; }
    private static T[] Read<T>(string file) => JsonConvert.DeserializeObject<T[]>(File.ReadAllText(file));
    private static void Flag(CondOwner owner, string name, bool value)
    {
        if (!value) owner.mapConds.Remove(name);
        else owner.mapConds[name] = new Condition(null) { strName=name, fCount=1 };
    }
    private static void Value(CondOwner owner, string name, double value)
    {
        if (!owner.mapConds.TryGetValue(name, out var condition))
            owner.mapConds[name] = condition = DataHandler.GetCond(name) ?? new Condition(null) {strName=name};
        condition.fCount=value;
    }
    private static void LoadAssets()
    {
        DataHandler.dictConds=Read<JsonCond>(Data+"conditions/conditions.json").GroupBy(x=>x.strName).ToDictionary(g=>g.Key,g=>g.Last());
        var simple=new Dictionary<string,JsonSimple>();
        foreach(var path in Directory.GetFiles(Data+"conditions_simple","*.json"))
            foreach(var row in Read<JsonSimple>(path)) simple[row.strName]=row;
        foreach(var row in Read<JsonSimple>(Assets+"/data/conditions_simple/bottle_refiller.json")) simple[row.strName]=row;
        AccessTools.Method(typeof(DataHandler),"ParseConditionsSimple").Invoke(null,new object[]{simple});
        DataHandler.dictCTs=new Dictionary<string,CondTrigger>();
        foreach(var path in Directory.GetFiles(Data+"condtrigs","*.json",SearchOption.AllDirectories))
            foreach(var row in Read<CondTrigger>(path)) DataHandler.dictCTs[row.strName]=row;
        foreach(var row in Read<CondTrigger>(Assets+"/data/condtrigs/bottle_refiller.json")) DataHandler.dictCTs[row.strName]=row;
        foreach(var ct in DataHandler.dictCTs.Values) ct.PostInit();
        DataHandler.dictCOs=Read<JsonCondOwner>(Data+"condowners/condowners.json").GroupBy(x=>x.strName).ToDictionary(g=>g.Key,g=>g.Last());
        foreach(var row in Read<JsonCondOwner>(Assets+"/data/condowners/bottle_refiller.json")) DataHandler.dictCOs[row.strName]=row;
        DataHandler.dictInteractions=Read<JsonInteraction>(Data+"interactions/interactions.json").GroupBy(x=>x.strName).ToDictionary(g=>g.Key,g=>g.Last());
        foreach(var row in Read<JsonInteraction>(Assets+"/data/interactions/bottle_refiller.json")) DataHandler.dictInteractions[row.strName]=row;
        DataHandler.dictLoot=Read<Loot>(Data+"loot/loot.json").GroupBy(x=>x.strName).ToDictionary(g=>g.Key,g=>g.Last());
        foreach(var row in Read<Loot>(Assets+"/data/loot/bottle_refiller.json")) DataHandler.dictLoot[row.strName]=row;
        DataHandler.dictInstallables2=new Dictionary<string,JsonInstallable>();
        DataHandler.dictCOOverlays=new Dictionary<string,JsonCOOverlay>();
        DataHandler.dictCondRules=new Dictionary<string,CondRule>();
        DataHandler.dictCondRulesLookup=new Dictionary<string,string>();
        DataHandler.mapCOs=new Dictionary<string,CondOwner>();
        foreach(var row in Read<JsonInstallable>(Assets+"/data/installables/bottle_refiller.json")) Installables.Create(row);
    }
    private static Ship MakeShip()
    {
        var ship=Empty<Ship>(); ship.strRegID="O-TEST";
        Set(ship,"nLoadState",Ship.Loaded.Full);Set(ship,"mapICOs",new Ostranauts.Core.Models.DictionaryArray<string,CondOwner>());
        ship.aRooms=new List<Room>();
        CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>{{ship.strRegID,ship}};
        Set(typeof(CrewSim),"aLoadedShips",new List<Ship>{ship});
        return ship;
    }
    private static void Set(Type type,string name,object value) => AccessTools.Field(type,name).SetValue(null,value);
    private static CondOwner Owner(Ship ship, string definition="test")
    {
        var owner=Empty<CondOwner>();owner.strCODef=definition;Set(owner,"_strID","test-"+(serial++));
        Set(owner,"_objShip",ship);owner.Clock=new Condowner.Utils.Clock();
        owner.mapConds=new Dictionary<string,Condition>();owner.mapInfo=new Dictionary<string,string>();
        Set(owner,"mapCondRules",new Dictionary<string,CondRule>());owner.bFreezeCondRules=true;
        Set(owner,"dictAddCondEvents",new Dictionary<string,string[]>());Set(owner,"dictRemoveCondEvents",new Dictionary<string,string[]>());
        owner.aCondZeroes=new List<string>();
        Set(owner,"aCondsTimed",new List<Condition>());
        Set(owner,"aPriorities",Activator.CreateInstance(AccessTools.Field(typeof(CondOwner),"aPriorities").FieldType));
        Set(owner,"aDestructableConds",new List<string>());
        ((Ostranauts.Core.Models.DictionaryArray<string,CondOwner>)AccessTools.Field(typeof(Ship),"mapICOs").GetValue(ship))[owner.strID]=owner;
        DataHandler.mapCOs[owner.strID]=owner;
        return owner;
    }
    private static GasContainer AddGas(CondOwner owner, double temperature, double volume, params double[] amounts)
    {
        Value(owner,"StatGasTemp",temperature);Value(owner,"StatVolume",volume);Value(owner,"StatGasPressure",1e-20);
        Value(owner,"StatGasPressureMax",20684);
        var gas=Empty<GasContainer>();Set(gas,"co",owner);Set(gas,"bRequiresInit",false);
        Set(gas,"_timeNextPressureDifCheck",double.PositiveInfinity);
        gas.mapDGasMols=new Dictionary<string,double>();gas.mapGasMols1=new Dictionary<string,double>();
        double total=0;
        for(int i=0;i<Species.Length;i++)
        {
            double amount=i<amounts.Length?amounts[i]:0;
            Value(owner,Species[i],amount);gas.mapGasMols1[Species[i]]=amount;total+=amount;
            Value(owner,FluidStrings.pps[i],1e-20);
        }
        gas.mapGasMols1["StatGasMolTotal"]=total;
        Set(gas,"_condGasTemp",owner.mapConds["StatGasTemp"]);Set(gas,"_condVolume",owner.mapConds["StatVolume"]);
        Set(gas,"_condGasPressure",owner.mapConds["StatGasPressure"]);Set(owner,"_gasContainerComponentReference",gas);
        owner.GasChanged=true;gas.Run();
        return gas;
    }
    private static double[] Mix(double amount, double oxygen=.21)
    {
        var values=new double[Species.Length];values[Array.IndexOf(Species,"StatGasMolO2")]=amount*oxygen;
        values[Array.IndexOf(Species,"StatGasMolN2")]=amount*(1-oxygen);return values;
    }
    private static CondOwner Rack(Ship ship)
    {
        var rack=Owner(ship,Runtime.Item);
        foreach(var flag in new[]{"IsInstalled","IsOverrideOn","IsPowered","IsOstrabusBottleRefiller"})Flag(rack,flag,true);
        return rack;
    }
    private static CondOwner Bottle(Ship ship,CondOwner rack,double temperature=293,double initial=0)
    {
        var bottle=Owner(ship,"ItmCanisterO2Small");
        Set(bottle,"_objCOParent",rack);
        foreach(var flag in new[]{"IsVesselO2","IsHandheld","IsSolid","IsAirtight"})Flag(bottle,flag,true);
        AddGas(bottle,temperature,.003,Mix(initial));return bottle;
    }
    private static void NativeCases()
    {
        var random=new System.Random(70241);
        for(int test=0;test<200;test++)
        {
            var ship=MakeShip();var rack=Rack(ship);var source=Owner(ship);
            double supply=test%3==0?1:1000;
            AddGas(source,100+random.NextDouble()*800,10,Mix(supply,random.NextDouble()));
            var bottles=Enumerable.Range(0,1+test%4).Select(_=>Bottle(ship,rack,100+random.NextDouble()*500,random.NextDouble()*2)).ToList();
            var before=Species.Select(s=>source.GetCondAmount(s)+bottles.Sum(b=>b.GetCondAmount(s))).ToArray();
            double massBefore=source.GasContainer.Mass+bottles.Sum(b=>b.GasContainer.Mass);
            Check(NativeTransfer.Fill(rack,source,bottles),"Valid native transfer rejected");
            for(int s=0;s<Species.Length;s++)
                Near(before[s],source.GetCondAmount(Species[s])+bottles.Sum(b=>b.GetCondAmount(Species[s])),"Native species conservation");
            Near(massBefore,source.GasContainer.Mass+bottles.Sum(b=>b.GasContainer.Mass),"Native mass conservation");
            foreach(var bottle in bottles)
            {
                double pp=FluidStrings.pps.Sum(s=>bottle.GetCondAmount(s));
                Near(pp,bottle.GetCondAmount("StatGasPressure"),"Native partial pressures");
                Check(bottle.GetCondAmount("StatGasPressure")<=20684,"Native pressure exceeded cap");
                Check(bottle.GetCondAmount("StatGasMolN2")>0,"Incoming mixture filtered");
            }
            for(int tick=1;tick<31;tick++) NativeTransfer.Fill(rack,source,bottles);
            double sourceAfter=source.GasContainer.mapGasMols1["StatGasMolTotal"];
            NativeTransfer.Fill(rack,source,bottles);
            Near(sourceAfter,source.GasContainer.mapGasMols1["StatGasMolTotal"],"Repeated service double-spent gas",1e-7);
        }
        var live=MakeShip();var unit=Rack(live);var can=Owner(live);AddGas(can,293,10,Mix(100));
        var target=Bottle(live,unit);int o2=Array.IndexOf(Species,"StatGasMolO2");
        can.GasContainer.mapDGasMols[Species[o2]]=3;can.GasChanged=true;
        target.GasContainer.mapDGasMols[Species[o2]]=1;target.GasChanged=true;
        double pendingBefore=can.GetCondAmount(Species[o2])+target.GetCondAmount(Species[o2])+4;
        Check(NativeTransfer.Fill(unit,can,new[]{target}),"Pending-delta transfer rejected");
        Near(pendingBefore,can.GetCondAmount(Species[o2])+target.GetCondAmount(Species[o2]),"Pending gas lost");
        Flag(unit,"IsOverrideOff",true);Check(!NativeTransfer.Fill(unit,can,new[]{target}),"OFF fills");Flag(unit,"IsOverrideOff",false);
        Flag(unit,"IsPowered",false);Check(!NativeTransfer.Fill(unit,can,new[]{target}),"Unpowered fills");Flag(unit,"IsPowered",true);
        Flag(unit,"IsDamaged",true);Check(!NativeTransfer.Fill(unit,can,new[]{target}),"Damaged fills");Flag(unit,"IsDamaged",false);
        target.bFreezeConds=true;Check(!NativeTransfer.Fill(unit,can,new[]{target}),"Frozen object mutated");target.bFreezeConds=false;
        Check(!NativeTransfer.Fill(unit,can,new[]{target,target}),"Duplicate bottle admitted");
        Set(live,"nLoadState",Ship.Loaded.Shallow);Check(!Runtime.LiveShip(live),"Shallow ship admitted");
        Set(live,"nLoadState",Ship.Loaded.Full);
        var copy=Empty<Ship>();copy.strRegID=live.strRegID;Set(copy,"nLoadState",Ship.Loaded.Full);
        Check(!Runtime.LiveShip(copy),"Same-ID copy admitted");
        var room=Owner(live);Flag(room,"IsRoom",true);Flag(room,"IsAirtight",true);AddGas(room,293,20,Mix(1000));
        ((Ostranauts.Core.Models.DictionaryArray<string,CondOwner>)AccessTools.Field(typeof(Ship),"mapICOs").GetValue(live)).Remove(room.strID);
        var nativeRoom=Empty<Room>();Set(nativeRoom,"coRoom",room);live.aRooms.Add(nativeRoom);
        Check(Runtime.LiveOwner(room),"Native compartment absent from item registry rejected");
        Flag(can,"IsAirtight",true);
        Check(ReferenceEquals(Runtime.SelectSource(new[]{room,can},unit,out _),can),"Canister did not take priority");
        var second=Owner(live);Flag(second,"IsAirtight",true);AddGas(second,293,1,Mix(1));
        Check(Runtime.SelectSource(new[]{room,can,second},unit,out bool ambiguous)==null&&ambiguous,"Ambiguous canisters accepted");
        Check(ReferenceEquals(Runtime.SelectSource(new[]{room},unit,out _),room),"No-canister room fallback failed");
        var cabinBottle=Bottle(live,unit);
        double cabinBefore=room.GasContainer.mapGasMols1["StatGasMolTotal"];
        Check(NativeTransfer.Fill(unit,room,new[]{cabinBottle}),"Native compartment extraction failed");
        Near(cabinBefore,room.GasContainer.mapGasMols1["StatGasMolTotal"]+cabinBottle.GasContainer.mapGasMols1["StatGasMolTotal"],"Compartment gas not conserved");
        Value(room,"StatVolume",1e99);
        Check(!NativeTransfer.Fill(unit,room,new[]{Bottle(live,unit)}),"Void room supplies gas");
    }
    private static void AssetCases()
    {
        var jobs=Read<JsonInstallable>(Assets+"/data/installables/bottle_refiller.json");
        Check(jobs.Length==12,"Wrong native job inventory");
        foreach(var job in jobs)
        {
            Check(DataHandler.dictInteractions.ContainsKey("ACT"+job.strName),"Native work action missing");
            if(job.aLootCOs!=null&&job.aLootCOs.Length>0)
                Check(DataHandler.GetLoot("Output"+job.strName).bSuppress,"Native conversion can create fresh contents");
        }
        foreach(var suffix in new[]{"","Loose","Dmg","DmgLoose"})
        {
            var row=DataHandler.dictCOs[Runtime.Item+suffix];
            Check(row.nContainerWidth==2&&row.nContainerHeight==2,"Wrong native inventory dimensions");
            Check(row.strContainerCT=="TIsFitContainerEVABottleNotDamaged","Wrong bottle filter");
            Check(!row.aStartingConds.Any(s=>s.StartsWith("IsSignalable=")),"Signal behavior inherited");
            bool loose=suffix.EndsWith("Loose",StringComparison.Ordinal);
            Check(row.aStartingConds.Contains("IsHiddenInv=1.0x1")==!loose,"Installed/loose inventory visibility incorrect");
            Check(row.mapSlotEffects.Length==2,"Legacy slotted-save restore metadata missing");
            Check(!row.aUpdateCommands.Any(s=>s.StartsWith("Electrical,")||s.StartsWith("GasPump,")),"Native pump/signal component retained");
            Check(row.aStartingConds.Contains("IsOverrideOff=1.0x1"),"New item defaults ON");
            Check(row.strLoot==null,"Item spawns starting contents");
            Check(row.aInteractions.Contains("Inventory"),"No native rack inventory action");
            var item=Read<JsonItemDef>(Assets+"/data/items/bottle_refiller.json").Single(x=>x.strName==Runtime.Item+suffix);
            Check(item.strImg==Runtime.Item+suffix,"Equipment state selects the wrong sprite");
            Check(row.strPortraitImg==item.strImg,"Portrait does not match installed/loose damage state");
            Check((item.aLights?.Length??0)==(suffix==""?2:0),"Loose/damaged unit lights, or healthy unit lacks its indicators");
            if(suffix=="") Check(item.aLights[0]=="OstrabusBottleRefillerFilling","Native indicator definition not selected");
        }
        var lights=Read<JsonLight>(Assets+"/data/lights/bottle_refiller.json");
        var lamp=lights.Single(x=>x.strName=="OstrabusBottleRefillerFilling");
        Check(lamp.strName=="OstrabusBottleRefillerFilling"&&lamp.strImg=="ItmLitGreen"&&lamp.strColor=="Blank","Wrong native lamp or unwanted room illumination");
        Check(lamp.ptPos.x==3&&lamp.ptPos.y==-13&&!lamp.bCanBlink,"Lamp not aligned to the exposed bottle-side recess");
        var standby=lights.Single(x=>x.strName=="OstrabusBottleRefillerStandby");
        Check(standby.strImg=="ItmLitYellow"&&standby.strColor=="Blank"&&standby.ptPos==lamp.ptPos,"Amber indicator is not aligned with green");
        var ship=MakeShip();var rack=Rack(ship);var bottle=Bottle(ship,rack);
        var ct=DataHandler.GetCondTrigger("TIsFitContainerEVABottleNotDamaged");
        Check(ct.Triggered(bottle),"Native bottle trigger rejects bottle");
        Flag(bottle,"IsDamaged",true);Check(!ct.Triggered(bottle),"Damaged bottle accepted");
        Check(!ct.Triggered(Owner(ship)),"Unrelated inventory item accepted");
        var gui=Read<JsonGUIPropMap>(Assets+"/data/guipropmaps/bottle_refiller.json")[0];
        Check(gui.dictGUIPropMap.Contains("GUIOnOff"),"Custom UI introduced");
    }
    private static int Main()
    {
        string root=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(root,"Ostrabus.csproj")))root=Directory.GetParent(root).FullName;
        Assets=Path.Combine(root,"StandaloneMods/BottleRefiller/Assets");
        var environment=new Harmony("offline.bottle-refiller.environment");
        try
        {
            environment.Patch(AccessTools.Method(typeof(Shader),nameof(UnityEngine.Shader.PropertyToID),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(Shader)));
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
            environment.Patch(AccessTools.Method(typeof(DataHandler),nameof(DataHandler.GetString)),prefix:new HarmonyMethod(typeof(Program),nameof(StringValue)));
            Set(typeof(Ostranauts.Core.MonoSingleton<GUIRenderTargets>),"_instance",Empty<GUIRenderTargets>());
            environment.Patch(AccessTools.Method(typeof(GUIRenderTargets),nameof(GUIRenderTargets.UpdateFaces)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            // Scene-only room danger labels are outside this offline host. The
            // real native Run, condition changes, PPs, temperature and mass execute.
            environment.Patch(AccessTools.Method(typeof(GasContainer),nameof(GasContainer.UpdateStats)),prefix:new HarmonyMethod(typeof(Program),nameof(Quiet)));
            LoadAssets();AssetCases();NativeCases();MathCases();IntegrationCases(environment);CurveCases();AudioCases();
            var target=AccessTools.Method(typeof(CondOwner),nameof(CondOwner.AddCommand),new[]{typeof(string)});
            Check(target!=null,"Command-loader signature changed");
            Check(typeof(Plugin).Assembly.GetTypes().Count(t=>t.GetCustomAttributes(typeof(HarmonyPatch),false).Length>0)==1,"More than one Harmony hook");
            Check(!typeof(Plugin).Assembly.GetReferencedAssemblies().Any(a=>a.Name=="Ostrabus"),"Main mod dependency introduced");
            Console.WriteLine(checks+" Bottle Refiller checks passed (native GasContainer.Run executed).");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{environment.UnpatchSelf();}
    }
}
