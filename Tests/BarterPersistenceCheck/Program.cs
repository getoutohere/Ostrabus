using System;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Ships;
using Ostranauts.Core.Models;
using Ostranauts.Inventory;
using Ostranauts.ShipGUIs.Trade;
using UnityEngine;

partial class Program {
    static int checks,baseRemovals;
    static MethodInfo removeInternal=AccessTools.Method(typeof(Ship),"RemoveInternal");
    static Dictionary<string,double> balances=new Dictionary<string,double>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj,string field,object value)=>AccessTools.Field(obj.GetType(),field).SetValue(obj,value);
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static bool Skip()=>false;
    static bool Equal(UnityEngine.Object x,UnityEngine.Object y,ref bool __result){__result=ReferenceEquals(x,y);return false;}
    static bool Unequal(UnityEngine.Object x,UnityEngine.Object y,ref bool __result){__result=!ReferenceEquals(x,y);return false;}
    static bool Position(ref Vector3 __result){__result=new Vector3(0,0,0);return false;}
    static GameObject dummyGameObject;
    static bool GameObjectRead(ref GameObject __result){__result=dummyGameObject;return false;}
    static bool Layer(ref int __result){__result=0;return false;}
    static bool TileRead(ref Tile __result){__result=null;return false;}
    static bool Item(ref Item __result){__result=null;return false;}
    static bool Has(ref bool __result){__result=false;return false;}
    static bool Mass(ref double __result){__result=1;return false;}
    static bool ShipSet(CondOwner __instance,Ship value){Set(__instance,"_objShip",value);return false;}
    static bool Clear(CondOwner co){Set(co,"_objCOParent",null);return false;}
    static bool Remove(Ship __instance,CondOwner objCO){baseRemovals++;removeInternal.Invoke(__instance,new object[]{null,objCO});return false;}
    static bool AddMoney(CondOwner __instance,string strName,double fAmount){Check(strName=="StatMoney","Unexpected currency");balances[__instance.strID]=balances.TryGetValue(__instance.strID,out double old)?old+fAmount:fAmount;return false;}
    static void Patch(Harmony h,Type type,string method,string prefix,Type[] args=null)=>h.Patch(AccessTools.Method(type,method,args),prefix:new HarmonyMethod(typeof(Program),prefix));
    static CondOwner CO(JsonItem item,Ship ship){var co=Empty<CondOwner>();Set(co,"_strID",item.strID);co.strName=item.strName;co.strCODef=item.strName;Set(co,"_objShip",ship);co.tf=Empty<Transform>();co.aStack=new TrackingCollection<CondOwner>();co.aLot=new TrackingCollection<CondOwner>();return co;}
    static JsonShip Json(JsonItem[] items)=>new JsonShip{strRegID="O-GXD",aItems=items};
    public static void Main(string[] args){
        string save=args.Length>0?args[0]:Ostrabus.Development.InstallationPaths.Fixture("Barter/BARTER-SOURCE-O-GXD.json");
        byte[] before=System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(save));
        JObject raw;
        raw=(JObject)JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("Barter/BARTER-SOURCE-O-GXD.json")))[0];
        var source=raw["aItems"].ToObject<JsonItem[]>();
        var cargo=source.Single(i=>i.strID=="17babb0a-cdc5-486b-ba62-0c79392cbb34");
        var parent=source.Single(i=>i.strID==cargo.strParentID);
        Check(parent.strName=="ItmCrate01"&&parent.strParentID==null,"Fixture is not floor crate cargo");
        Console.WriteLine("Saved docking links: "+(raw["aDocked"] is JObject docks?docks.Count:0)+"; unloaded barter copy instantiated as an offline fixture.");
        Console.WriteLine("Save: "+save+"; cargo "+cargo.strName+" "+cargo.strID+"; parent "+parent.strName+" "+parent.strID);
        var h=new Harmony("offline.barter.persistence");
        Patch(h,typeof(UnityEngine.Object),"op_Equality",nameof(Equal));Patch(h,typeof(UnityEngine.Object),"op_Inequality",nameof(Unequal));
        h.Patch(AccessTools.PropertyGetter(typeof(Transform),"position"),prefix:new HarmonyMethod(typeof(Program),nameof(Position)));
        Patch(h,typeof(Transform),"SetParent",nameof(Skip),new[]{typeof(Transform)});
        h.Patch(AccessTools.PropertyGetter(typeof(CondOwner),"Item"),prefix:new HarmonyMethod(typeof(Program),nameof(Item)));
        h.Patch(AccessTools.PropertySetter(typeof(CondOwner),"ship"),prefix:new HarmonyMethod(typeof(Program),nameof(ShipSet)));
        Patch(h,typeof(CondOwner),"ValidateParent",nameof(Has));Patch(h,typeof(CondOwner),"HasCond",nameof(Has),new[]{typeof(string)});
        Patch(h,typeof(CondOwner),"AddMass",nameof(Skip));Patch(h,typeof(CondOwner),"GetTotalMass",nameof(Mass));
        Patch(h,typeof(Condowner.Utils.Scheduler),"RemoveTicker",nameof(Skip),new[]{typeof(CondOwner)});
        Patch(h,typeof(Ship),"RemoveInternalFromRooms",nameof(Skip));
        Patch(h,typeof(Ship),"RemoveCO",nameof(Remove),new[]{typeof(CondOwner),typeof(bool)});
        Patch(h,typeof(Container),"ClearIsInContainer",nameof(Clear));Patch(h,typeof(GridLayout),"Remove",nameof(Skip),new[]{typeof(string)});
        Patch(h,typeof(CondOwner),"AddCondAmount",nameof(AddMoney));
        var barter=Empty<BarterZoneShip>();barter.json=Json(source.ToArray());
        Check(((Ship)barter).json==null,"Shadow and base JSON unexpectedly aliased");
        Set(barter,"nLoadState",Ship.Loaded.Shallow);
        var map=new DictionaryArray<string,CondOwner>();Set(barter,"mapICOs",map);
        var co=CO(cargo,barter);var owner=CO(parent,barter);Set(co,"_objCOParent",owner);map[co.strID]=co;map[owner.strID]=owner;
        var container=Empty<Container>();Set(container,"co",owner);var contents=new TrackingCollection<CondOwner>();contents.Add(co);Set(container,"aCOs",contents);container.gridLayout=new GridLayout(6,6);
        // Native container removal -> instrumented base entry -> native RemoveInternal.
        container.RemoveCOSimple(co);
        Check(baseRemovals==1,"Container did not dispatch to base Ship.RemoveCO");
        Check(!map.ContainsKey(co.strID)&&co.ship==null&&contents.Count==0,"Runtime removal failed");
        Check(barter.json.aItems.Any(i=>i.strID==cargo.strID),"Container unexpectedly removed shadow JSON entry");
        var original=Empty<Ship>();original.json=Json(source.ToArray());
        barter.SaveChangedCOs(ref original);
        Check(original.json.aItems.Any(i=>i.strID==cargo.strID),"Native save-back did not retain item");
        Console.WriteLine("CONTAINER: runtime item removed; native SaveChangedCOs leaves its original JSON entry on O-GXD.");
        // Exercise the real payment method with scalar currency bookkeeping substituted.
        var gui=Empty<GUITrade>();var player=CO(new JsonItem{strID="offline-player",strName="player"},null);var trader=CO(new JsonItem{strID="offline-trader",strName="trader"},null);
        Set(gui,"_coUser",player);Set(gui,"coSelfTemp",trader);Set(gui,"_selectedCurrency","StatMoney");Set(gui,"strDesc","");Set(gui,"strItem",cargo.strName);
        AccessTools.Method(typeof(GUITrade),"RecordTransaction",new[]{typeof(float),typeof(bool)}).Invoke(gui,new object[]{100f,true});
        Check(balances[player.strID]==100&&balances[trader.strID]==-100,"Native payment failed");
        Check(original.json.aItems.Any(i=>i.strID==cargo.strID),"Payment changed source inventory");
        Console.WriteLine("PAYMENT: native RecordTransaction credits player 100 test units while persisted source still contains item.");
        // Native specialized helper is the loose-item persistence control.
        AccessTools.Method(typeof(BarterZoneShip),"RemoveFromLocalInstance").Invoke(barter,new object[]{cargo.strID});
        barter.SaveChangedCOs(ref original);
        Check(!original.json.aItems.Any(i=>i.strID==cargo.strID),"Specialized JSON removal did not persist");
        Check(original.json.aItems.Length==source.Length-1,"Control removed unrelated cargo");
        Console.WriteLine("CONTROL: specialized RemoveFromLocalInstance + native SaveChangedCOs removes exactly that item.");
        // Real uncontained, unstacked loose-item path from the same saved hull.
        var loose=source.Single(i=>i.strID=="13731798-c6ad-4508-8fdd-4b26496b1d00");
        var savedLooseCO=raw["aCOs"].Single(i=>(string)i["strID"]==loose.strID);
        Check(loose.strParentID==null&&loose.strSlotParentID==null,"Loose fixture is parented");
        Check(savedLooseCO["aStack"]==null||savedLooseCO["aStack"].Type==JTokenType.Null||!savedLooseCO["aStack"].Any(),"Loose fixture is stacked");
        dummyGameObject=Empty<GameObject>();
        h.Patch(AccessTools.PropertyGetter(typeof(Component),"gameObject"),prefix:new HarmonyMethod(typeof(Program),nameof(GameObjectRead)));
        h.Patch(AccessTools.PropertySetter(typeof(GameObject),"layer"),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        Patch(h,typeof(LayerMask),"NameToLayer",nameof(Layer));
        Patch(h,typeof(Ship),"UpdateTiles",nameof(Has));
        Patch(h,typeof(Ship),"GetTileAtWorldCoords1",nameof(TileRead));
        Patch(h,typeof(CondOwner),"CheckTrue",nameof(Skip));
        var looseBarter=Empty<BarterZoneShip>();looseBarter.json=Json(source.ToArray());
        Set(looseBarter,"nLoadState",Ship.Loaded.Shallow);
        var looseMap=new DictionaryArray<string,CondOwner>();Set(looseBarter,"mapICOs",looseMap);
        var looseCO=CO(loose,looseBarter);looseMap[looseCO.strID]=looseCO;
        int previousBaseRemovals=baseRemovals;
        // GUITrade.OnSell calls PopHeadFromStack; no stack -> RemoveFromCurrentHome
        // -> explicitly dispatched native BarterZoneShip.RemoveCO (not substituted).
        looseCO.PopHeadFromStack();
        Check(baseRemovals==previousBaseRemovals,"Loose path unexpectedly used ordinary Ship.RemoveCO");
        Check(looseCO.ship==null&&!looseMap.ContainsKey(loose.strID),"Loose runtime removal failed");
        Check(!looseBarter.json.aItems.Any(i=>i.strID==loose.strID),"Loose JSON entry survived native removal");
        var looseOriginal=Empty<Ship>();looseOriginal.json=Json(source.ToArray());
        Set(gui,"strItem",loose.strName);
        AccessTools.Method(typeof(GUITrade),"RecordTransaction",new[]{typeof(float),typeof(bool)}).Invoke(gui,new object[]{100f,true});
        looseBarter.SaveChangedCOs(ref looseOriginal);
        Check(balances[player.strID]==200,"Loose sale payment missing");
        Check(!looseOriginal.json.aItems.Any(i=>i.strID==loose.strID)&&looseOriginal.json.aItems.Length==source.Length-1,"Loose item survived save-back or unrelated cargo changed");
        Console.WriteLine("LOOSE: "+loose.strName+" "+loose.strID+" has no parent or stack in save. Native PopHeadFromStack -> RemoveFromCurrentHome -> BarterZoneShip.RemoveCO removes runtime + JSON; native payment credits 100; native save-back keeps item removed.");
        FixedChecks(h,source,cargo,parent,loose);
        MassDiagnosis(h);
        byte[] after=System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(save));Check(before.SequenceEqual(after),"Source input changed");
        Console.WriteLine("Passed "+checks+" checks. Source SHA256 unchanged: "+BitConverter.ToString(before).Replace("-",""));
    }
}





