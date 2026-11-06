using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Core.Models;
using UnityEngine;
using Ostrabus.MiscBugFixes;

partial class Program {
    static bool Hash(UnityEngine.Object __instance,ref int __result){__result=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance);return false;}
    static bool Same(UnityEngine.Object __instance,object __0,ref bool __result){__result=ReferenceEquals(__instance,__0);return false;}
    static int routedCalls;
    static bool routedForce;
    static Item stackItem;
    static bool ShaderID(ref int __result){__result=0;return false;}
    static void Routed(bool bForce) { routedCalls++;routedForce=bForce; }
    static bool ParentSet(CondOwner __instance,CondOwner value){Set(__instance,"_objCOParent",value);return false;}
    static bool Rotation(ref Quaternion __result){__result=Quaternion.identity;return false;}
    static bool Euler(ref Vector3 __result){__result=default;return false;}
    static bool StackComponent(ref Item __result){__result=stackItem;return false;}
    static BarterZoneShip Fixture(JsonItem[] source,params JsonItem[] entries) {
        var barter=Empty<BarterZoneShip>();barter.json=Json(source.ToArray());
        Set(barter,"nLoadState",Ship.Loaded.Shallow);
        var map=new DictionaryArray<string,CondOwner>();Set(barter,"mapICOs",map);
        foreach(var entry in entries){var co=CO(entry,barter);map[co.strID]=co;}
        return barter;
    }
    static Container Contents(CondOwner parent,params CondOwner[] entries) {
        var container=Empty<Container>();Set(container,"co",parent);
        var list=new TrackingCollection<CondOwner>();foreach(var co in entries){list.Add(co);Set(co,"_objCOParent",parent);}
        Set(container,"aCOs",list);container.gridLayout=new Ostranauts.Inventory.GridLayout(6,6);parent.objContainer=container;
        return container;
    }
    static void Persisted(BarterZoneShip barter,JsonItem[] source,params string[] removed) {
        var original=Empty<Ship>();original.json=Json(source.ToArray());barter.SaveChangedCOs(ref original);
        var expected=source.Where(i=>!removed.Contains(i.strID)).Select(i=>i.strID).ToArray();
        Check(original.json.aItems.Select(i=>i.strID).SequenceEqual(expected),"Persisted source differs beyond transferred item IDs");
        barter.SaveChangedCOs(ref original);
        Check(original.json.aItems.Select(i=>i.strID).SequenceEqual(expected),"Repeated save-back resurrected cargo");
    }
    static void FixedChecks(Harmony fixture,JsonItem[] source,JsonItem cargo,JsonItem crate,JsonItem loose) {
        var remove=AccessTools.Method(typeof(Ship),nameof(Ship.RemoveCO),new[]{typeof(CondOwner),typeof(bool)});
        fixture.Unpatch(remove,HarmonyPatchType.Prefix,fixture.Id);
        var patch=new Harmony("offline.barter.production");patch.CreateClassProcessor(typeof(BarterRemovalPatch)).Patch();
        patch.Patch(AccessTools.Method(typeof(BarterZoneShip),nameof(BarterZoneShip.RemoveCO)),prefix:new HarmonyMethod(typeof(Program),nameof(Routed)));
        patch.Patch(AccessTools.Method(typeof(UnityEngine.Object),"GetHashCode"),prefix:new HarmonyMethod(typeof(Program),nameof(Hash)));
        patch.Patch(AccessTools.Method(typeof(UnityEngine.Object),"Equals",new[]{typeof(object)}),prefix:new HarmonyMethod(typeof(Program),nameof(Same)));
        var ordinary=Empty<Ship>();var ordinaryJson=Json(source.ToArray());ordinary.json=ordinaryJson;
        Check(BarterRemovalPatch.Prefix(ordinary,null,false),"Ordinary ships redirected");
        Check(ReferenceEquals(ordinary.json,ordinaryJson),"Ordinary ship mutated by guard");
        patch.Patch(AccessTools.PropertySetter(typeof(CondOwner),"objCOParent"),prefix:new HarmonyMethod(typeof(Program),nameof(ParentSet)));
        // Contained item: the exact native call which previously bypassed JSON cleanup.
        var contained=Fixture(source,crate,cargo);var map=contained.GetMappedCos();
        var inventory=Contents(map[crate.strID],map[cargo.strID]);int calls=routedCalls;
        inventory.RemoveCOSimple(map[cargo.strID]);
        Check(routedCalls==calls+1&&!routedForce,"Container removal was not redirected exactly once");
        Check(!map.ContainsKey(cargo.strID)&&inventory.ContainedCOs.Count==0,"Container runtime removal failed");
        Persisted(contained,source,cargo.strID);
        Console.WriteLine("FIXED CONTAINER: native container removal now deletes the persisted item; repeat save-back stays clean.");
        // Direct base reference and bForce forwarding.
        var forced=Fixture(source,loose);calls=routedCalls;
        ((Ship)forced).RemoveCO(forced.GetMappedCos()[loose.strID],true);
        Check(routedCalls==calls+1&&routedForce,"bForce not forwarded");Persisted(forced,source,loose.strID);
        // Previously working loose path still uses the native specialized method once.
        var free=Fixture(source,loose);calls=routedCalls;free.GetMappedCos()[loose.strID].PopHeadFromStack();
        Check(routedCalls==calls+1,"Loose path double removal");Persisted(free,source,loose.strID);
        // A container sale includes its contents, while preserving unrelated cargo.
        var subtree=new List<JsonItem>{crate};var ids=new HashSet<string>{crate.strID};
        for(int i=0;i<subtree.Count;i++) foreach(var item in source) {
            if((item.strParentID==subtree[i].strID||item.strSlotParentID==subtree[i].strID)&&ids.Add(item.strID))subtree.Add(item);
        }
        var box=Fixture(source,subtree.ToArray());map=box.GetMappedCos();
        foreach(var item in subtree) {
            var children=subtree.Where(child=>child.strParentID==item.strID||child.strSlotParentID==item.strID).Select(child=>map[child.strID]).ToArray();
            if(children.Length>0)Contents(map[item.strID],children);
        }
        ((Ship)box).RemoveCO(map[crate.strID]);
        Check(map.Count==0,"Container descendants remained mapped");Persisted(box,source,subtree.Select(item=>item.strID).ToArray());
        Console.WriteLine("FIXED CONTAINER CONTENTS: native descendant traversal removes the crate and all "+(subtree.Count-1)+" saved descendants.");
        // Controlled three-item stack: remove one member through native CondOwner.RemoveCO.
        var members=new[]{new JsonItem{strID="stack-head",strName="ItmFloorAERO07Loose"},new JsonItem{strID="stack-sold",strName="ItmFloorAERO07Loose"},new JsonItem{strID="stack-kept",strName="ItmFloorAERO07Loose"}};
        var stackSource=source.Concat(members).ToArray();var stack=Fixture(stackSource,members);map=stack.GetMappedCos();
        var head=map[members[0].strID];var sold=map[members[1].strID];var kept=map[members[2].strID];
        head.aStack.Add(sold);head.aStack.Add(kept);sold.coStackHead=head;kept.coStackHead=head;
        patch.Patch(AccessTools.Method(typeof(Shader),"PropertyToID"),prefix:new HarmonyMethod(typeof(Program),nameof(ShaderID)));
        stackItem=Empty<Item>();
        Patch(patch,typeof(Transform),"SetParent",nameof(Skip),new[]{typeof(Transform),typeof(bool)});
        patch.Patch(AccessTools.PropertyGetter(typeof(Transform),"rotation"),prefix:new HarmonyMethod(typeof(Program),nameof(Rotation)));
        patch.Patch(AccessTools.PropertyGetter(typeof(Quaternion),"eulerAngles"),prefix:new HarmonyMethod(typeof(Program),nameof(Euler)));
        patch.Patch(AccessTools.PropertySetter(typeof(Transform),"position"),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        Patch(patch,typeof(CondOwner),"UpdateAppearance",nameof(Skip));
        patch.Patch(AccessTools.Method(typeof(Quaternion),"Internal_ToEulerRad"),prefix:new HarmonyMethod(typeof(Program),nameof(Euler)));
        patch.Patch(AccessTools.PropertySetter(typeof(Item),"fLastRotation"),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        var component=typeof(Component).GetMethods().Single(m=>m.Name=="GetComponent"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==0).MakeGenericMethod(typeof(Item));
        patch.Patch(component,prefix:new HarmonyMethod(typeof(Program),nameof(StackComponent)));
        fixture.Unpatch(AccessTools.PropertyGetter(typeof(CondOwner),"Item"),HarmonyPatchType.Prefix,fixture.Id);
        patch.Patch(AccessTools.PropertyGetter(typeof(CondOwner),"Item"),prefix:new HarmonyMethod(typeof(Program),nameof(StackComponent)));
        Check(ReferenceEquals(head.RemoveCO(sold,true),sold),"Native stack member removal failed");
        Check(head.aStack.Count==1&&ReferenceEquals(head.aStack[0],kept)&&ReferenceEquals(kept.coStackHead,head),"Unsold stack member damaged");
        Check(!map.ContainsKey(sold.strID)&&map.ContainsKey(head.strID)&&map.ContainsKey(kept.strID),"Wrong stack runtime members removed");
        Persisted(stack,stackSource,sold.strID);stackItem=null;
        Console.WriteLine("FIXED PARTIAL STACK: sold member removed from runtime and JSON; head and unsold member preserved.");
    }
}





