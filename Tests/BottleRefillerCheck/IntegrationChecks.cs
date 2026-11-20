using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.BottleRefiller;
using Ostranauts.Core.Models;
using Ostranauts.Inventory;
using UnityEngine;
using Runtime = Ostrabus.BottleRefiller.Runtime;

internal static partial class Program
{
    private static Vector3 position, right;
    private static bool Position(ref Vector3 __result){__result=position;return false;}
    private static bool Right(ref Vector3 __result){__result=right;return false;}

    private static void IntegrationCases(Harmony environment)
    {
        var ship=MakeShip();var rack=Rack(ship);
        var panel=Empty<GUIOnOff>();Set(panel,"coSelfTemp",rack);
        var knob=AccessTools.Method(typeof(GUIOnOff),"SetPower");
        knob.Invoke(panel,new object[]{0});
        Check(rack.HasCond("IsOverrideOff")&&!rack.HasCond("IsOverrideOn"),"Native OFF control failed");
        knob.Invoke(panel,new object[]{1});
        Check(!rack.HasCond("IsOverrideOff")&&rack.HasCond("IsOverrideOn"),"Native ON control failed");
        Check(DataHandler.GetCondTrigger("TOstrabusBottleRefillerUsePower").Triggered(rack),"Native power trigger failed");
        Flag(rack,"IsDamaged",true);
        Check(!DataHandler.GetCondTrigger("TOstrabusBottleRefillerUsePower").Triggered(rack),"Damaged device draws power");
        Flag(rack,"IsDamaged",false);

        var container=Empty<Container>();Set(container,"co",rack);Set(container,"aCOs",new TrackingCollection<CondOwner>());
        container.gridLayout=new GridLayout(2,2);container.ctAllowed=DataHandler.GetCondTrigger("TIsFitContainerEVABottleNotDamaged");
        rack.objContainer=container;
        for(int i=0;i<4;i++)
        {
            var bottle=Bottle(ship,rack);
            bottle.strName="ItmCanisterO2Small";
            var item=Empty<Item>();item.nWidthInTiles=1;item.nHeightInTiles=1;Set(bottle,"_itemComponentReference",item);
            Check(container.AllowedCO(bottle),"Native container refuses valid bottle");
            Check(container.CanAddSimple(bottle,out var at),"Native grid cannot fit four bottles");
            container.gridLayout.gridID[at.x,at.y]=bottle.strID;
        }
        var fifth=Bottle(ship,rack);var fifthItem=Empty<Item>();fifthItem.nWidthInTiles=1;fifthItem.nHeightInTiles=1;Set(fifth,"_itemComponentReference",fifthItem);
        fifth.strName="ItmCanisterO2Small";
        Check(!container.CanAddSimple(fifth,out _),"Native grid allows a fifth bottle");
        Flag(fifth,"IsDamaged",true);Check(!container.AllowedCO(fifth),"Native container allows damaged bottle");

        var production=new Harmony("offline.bottle-refiller.production");
        try
        {
            var bound=production.CreateClassProcessor(typeof(CommandPatch)).Patch();
            Check(bound.Count==1,"Command hook failed binding");
            // This is the real loader hook, exercised without Unity attachment:
            // loose forms only need grid normalization and have no service component.
            rack.strCODef=Runtime.Item+"Loose";container.gridLayout=new GridLayout(6,6);
            rack.AddCommand(CommandPatch.Command);
            Check(container.gridLayout.gridMaxX==2&&container.gridLayout.gridMaxY==2,"Legacy 6x6 grid was not normalized");
            rack.strCODef="unrelated";container.gridLayout=new GridLayout(6,6);
            rack.AddCommand(CommandPatch.Command);
            Check(container.gridLayout.gridMaxX==6,"Command hook changed another definition");
            var unknown=Owner(ship);unknown.AddCommand("UnknownUnrelatedCommand");
            Check(unknown.strCODef=="test","Native unrelated command changed");
        }
        finally{production.UnpatchSelf();}

        // Execute native point transformation with only Transform getters mocked.
        environment.Patch(AccessTools.PropertyGetter(typeof(Transform),nameof(Transform.position)),prefix:new HarmonyMethod(typeof(Program),nameof(Position)));
        environment.Patch(AccessTools.PropertyGetter(typeof(Transform),nameof(Transform.right)),prefix:new HarmonyMethod(typeof(Program),nameof(Right)));
        var pump=Owner(ship);pump.tf=Empty<Transform>();pump.mapPoints=new Dictionary<string,Vector2>{{"GasInput",new Vector2(0,16)}};
        position=new Vector3(10,20,0);
        for(int angle=0;angle<360;angle+=90)
        {
            double radians=angle*Math.PI/180;right=new Vector3((float)Math.Cos(radians),(float)Math.Sin(radians),0);
            var point=pump.GetPos("GasInput");
            Near(point.x,10-Math.Sin(radians),"Rotated intake X");Near(point.y,20+Math.Cos(radians),"Rotated intake Y");
        }
        var preview=Empty<Ostranauts.Ships.AsyncPreviewShip>();preview.strRegID=ship.strRegID;Set(preview,"nLoadState",Ship.Loaded.Full);
        Check(!Runtime.LiveShip(preview),"Async preview admitted");
        var barter=Empty<Ostranauts.Ships.BarterZoneShip>();barter.strRegID=ship.strRegID;Set(barter,"nLoadState",Ship.Loaded.Full);
        Check(!Runtime.LiveShip(barter),"Barter preview admitted");
        var rating=Empty<Ostranauts.Ships.AsyncRatingShip>();rating.strRegID=ship.strRegID;Set(rating,"nLoadState",Ship.Loaded.Full);
        Check(!Runtime.LiveShip(rating),"Rating preview admitted");

        foreach(string suffix in new[]{"","Loose","Dmg","DmgLoose"})
        {
            var co=Owner(ship,Runtime.Item+suffix);
            var definition=DataHandler.dictCOs[co.strCODef];
            co.mapSlotEffects=new Dictionary<string,JsonSlotEffects>();
            for(int i=0;i<definition.mapSlotEffects.Length;i+=2)
                co.mapSlotEffects[definition.mapSlotEffects[i]]=new JsonSlotEffects();
            if(!suffix.EndsWith("Loose",StringComparison.Ordinal))
            {
                Flag(co,"IsInstalled",true);
                Check(co.mapSlotEffects.ContainsKey("drag"),"Cannot reconstruct 0.1.0 held installed item");
                Runtime.ApplyInstalledHandling(co);
            }
            var drag=Empty<Slot>();drag.strName="drag";drag.bHoldSlot=true;drag.aCOs=new CondOwner[1];
            Check(drag.CanFit(co,bAuto:false)==suffix.EndsWith("Loose",StringComparison.Ordinal),"Native drag eligibility incorrect: "+suffix);
        }
        var legacy=Rack(ship);legacy.mapSlotEffects=new Dictionary<string,JsonSlotEffects>();
        Runtime.ApplyInstalledHandling(legacy);
        Check(legacy.HasCond("IsHiddenInv"),"Old installed save did not regain inventory protection");
        Check(!DataHandler.GetCondTrigger("TIsInvGroundItem").Triggered(legacy),"Installed device appears as movable ground inventory");
        Check(DataHandler.dictInteractions["Inventory"].strRaiseUI=="Inventory","Explicit inventory action changed");
        Set(legacy,"_objCOParent",Owner(ship));Flag(legacy,"IsSlotted",true);
        Flag(legacy,"IsOstrabusBottleRefiller",true);
        Runtime.ApplyInstalledHandling(legacy);
        Check(!legacy.HasCond("IsHiddenInv"),"Mis-carried installed item hidden before recovery");
        Check(DataHandler.GetCondTrigger("TOstrabusBottleRefillerCarried").Triggered(legacy),"Carried recovery action inaccessible");
        Set(legacy,"_objCOParent",null);Flag(legacy,"IsSlotted",false);
        Check(!DataHandler.GetCondTrigger("TOstrabusBottleRefillerCarried").Triggered(legacy),"Recovery bypasses normal uninstallation on world equipment");

        var unit=Rack(ship);var empty=Owner(ship);Flag(empty,"IsAirtight",true);AddGas(empty,293,1,Mix(0));
        var a=Bottle(ship,unit);
        Check(!NativeTransfer.Fill(unit,empty,new[]{a}),"Empty canister creates gas");
        Near(a.GasContainer.mapGasMols1["StatGasMolTotal"],0,"Empty source filled bottle");
        var dirty=Owner(ship);var mix=Mix(100,.15);mix[Array.IndexOf(Species,"StatGasMolCO2")]=10;
        mix[Array.IndexOf(Species,"StatGasMolSmoke")]=1;AddGas(dirty,400,1,mix);
        Check(NativeTransfer.Fill(unit,dirty,new[]{a}),"Contaminated source transfer failed");
        Check(a.GetCondAmount("StatGasMolCO2")>0&&a.GetCondAmount("StatGasMolSmoke")>0,"Contaminants filtered");
        var shared=Owner(ship);AddGas(shared,293,1,Mix(1));
        var rackB=Rack(ship);var b=Bottle(ship,rackB);
        a=Bottle(ship,unit);
        Check(NativeTransfer.Fill(unit,shared,new[]{a}),"First shared-source fill failed");
        Check(!NativeTransfer.Fill(rackB,shared,new[]{b}),"Second device spent consumed source");
        Near(b.GasContainer.mapGasMols1["StatGasMolTotal"],0,"Second device invented gas");
    }
}
