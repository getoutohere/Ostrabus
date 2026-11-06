using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Utils;
using Ostranauts.Core.Models;
using Ostranauts.Trading;
using Ostrabus.MiscBugFixes;
using UnityEngine;

static class PrecisionChecks {
    static int checks;
    static Vector3 fakePosition;
    static void Check(bool ok,string message) { checks++;if(!ok)throw new Exception(message); }
    static bool Position(ref Vector3 __result) { __result=fakePosition;return false; }
    static bool Equal(UnityEngine.Object x,UnityEngine.Object y,ref bool __result) { __result=ReferenceEquals(x,y);return false; }
    static bool Unequal(UnityEngine.Object x,UnityEngine.Object y,ref bool __result) { __result=!ReferenceEquals(x,y);return false; }
    internal static void Install() {
        var h=new Harmony("offline.docking.precision");
        h.CreateClassProcessor(typeof(DockingFitScopePatch)).Patch();
        h.CreateClassProcessor(typeof(DockingSavedCoordinatePatch)).Patch();
        h.CreateClassProcessor(typeof(DockingLiveCoordinatePatch)).Patch();
    }
    static string Signature(Grid<DataCOWrapper> grid) => grid.Width+"x"+grid.Height+":"+string.Join(";",grid.Where(p=>p.Value!=null).Select(p=>p.Key.x+","+p.Key.y+":"+p.Value.DataCO.Name+":"+p.Value.ID+":"+p.Value.OriginX+","+p.Value.OriginY));
    static bool[] Fits(Ship station,Ship incoming) {
        var a=GridUtils.CreateFullGrid(station);var b=GridUtils.CreateFullGrid(incoming);
        var result=new List<bool>();
        foreach(var id in station.GetAllDockingPorts())foreach(var other in incoming.GetAllDockingPorts()) {
            var pa=GridUtils.FindDockingPort(a,id);var pb=GridUtils.FindDockingPort(b,other);
            Check(pa!=null&&pb!=null,"Lost a docking port");
            int rotation=GridUtils.GetIncomingDockRotation(pa.Rotation,pb.Rotation,out var offset);
            result.Add(GridUtils.CanOverlay(a,b,rotation,pa.GridPos,pb.GridPos,offset));
        }
        return result.ToArray();
    }
    internal static void Run(Ship station,Ship incoming) {
        Check(!DockingGridPrecision.Active,"Scope leaked during installation");
        Check(Fits(station,incoming).SequenceEqual(new[]{false,true,false,true,false,false}),"Saved failure not reproduced outside scope");
        var saved=new[]{station,incoming}.SelectMany(s=>s.json.aItems).Select(it=>(it,it.fX,it.fY)).ToArray();
        foreach(var row in saved) {row.it.fX=DockingGridPrecision.Snap(row.fX);row.it.fY=DockingGridPrecision.Snap(row.fY);}
        string stationExact=Signature(GridUtils.CreateFullGrid(station)),incomingExact=Signature(GridUtils.CreateFullGrid(incoming));
        foreach(var row in saved) {row.it.fX=row.fX;row.it.fY=row.fY;}
        int state=DockingGridPrecision.Enter();
        try {
            Check(Signature(GridUtils.CreateFullGrid(station))==stationExact,"Station grid differs from exact geometry");
            Check(Signature(GridUtils.CreateFullGrid(incoming))==incomingExact,"Incoming grid differs from exact geometry");
            Check(Fits(station,incoming).SequenceEqual(new[]{true,true,true,true,true,false}),"Port fit matrix incorrect");
        } finally {DockingGridPrecision.Exit(state);}
        foreach(var row in saved)Check(row.it.fX==row.fX&&row.it.fY==row.fY,"Stored coordinates mutated");
        // Exercise the actual outer scope hook and native selected-port geometry path.
        var pairs=station.GetAvailableDockingPorts(incoming,false);
        Check(pairs!=null&&pairs.Count==5,"Native docking call did not normalize coordinates");
        Check(pairs.Count(p=>p.Item2==incoming.PrimaryDockingPortID)==3,"Primary does not fit all station ports");
        Check(!DockingGridPrecision.Active,"Scope remained active after native call");
        try {station.GetAvailableDockingPorts(null,false);}catch(Exception){}
        Check(!DockingGridPrecision.Active,"Exception leaked scope");
        state=DockingGridPrecision.Enter();int nested=DockingGridPrecision.Enter();DockingGridPrecision.Exit(nested);
        Check(DockingGridPrecision.Active,"Nested scope cleared parent");DockingGridPrecision.Exit(state);
        for(int n=-2000;n<=2000;n++) {
            float exact=n*.5f;
            Check(DockingGridPrecision.Snap(exact)==exact,"Exact coordinate changed");
            foreach(float error in new[]{-.00004f,.00004f})Check(DockingGridPrecision.Snap(exact+error)==exact,"Near-grid coordinate not corrected");
            foreach(float error in new[]{-.0002f,.0002f})Check(DockingGridPrecision.Snap(exact+error)==exact+error,"Off-grid coordinate changed");
        }
        Check(float.IsNaN(DockingGridPrecision.Snap(float.NaN)),"NaN changed");
        Check(float.IsPositiveInfinity(DockingGridPrecision.Snap(float.PositiveInfinity)),"Infinity changed");
        var harmony=new Harmony("offline.docking.fixtures");
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(PrecisionChecks),nameof(Equal)));
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(PrecisionChecks),nameof(Unequal)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Transform),"position"),prefix:new HarmonyMethod(typeof(PrecisionChecks),nameof(Position)));
        var full=(Ship)FormatterServices.GetUninitializedObject(typeof(Ship));full.nCols=20;full.nRows=20;full.vShipPos=new Vector2(0,20);
        AccessTools.Field(typeof(Ship),"nLoadState").SetValue(full,Ship.Loaded.Full);
        AccessTools.Field(typeof(Ship),"aPeople").SetValue(full,new List<PersonSpec>());
        var co=(CondOwner)FormatterServices.GetUninitializedObject(typeof(CondOwner));co.strCODef="ItmTowingBrace01";co.tf=(Transform)FormatterServices.GetUninitializedObject(typeof(Transform));
        AccessTools.Field(typeof(CondOwner),"_strID").SetValue(co,"brace");
        var map=new DictionaryArray<string,CondOwner>();map["brace"]=co;AccessTools.Field(typeof(Ship),"mapICOs").SetValue(full,map);
        fakePosition=new Vector3(10,8.5f,0);string exactFull=Signature(GridUtils.CreateFullGrid(full));
        state=DockingGridPrecision.Enter();
        try {
            foreach(float y in new[]{8.499999f,8.500001f}) {fakePosition=new Vector3(10,y,0);Check(Signature(GridUtils.CreateFullGrid(full))==exactFull,"Loaded half-tile correction failed");}
            foreach(float y in new[]{8.999999f,9.000001f}) {fakePosition=new Vector3(10,9,0);string integerFull=Signature(GridUtils.CreateFullGrid(full));fakePosition=new Vector3(10,y,0);Check(Signature(GridUtils.CreateFullGrid(full))==integerFull,"Loaded whole-tile correction failed");}
        } finally {DockingGridPrecision.Exit(state);}
        Check(fakePosition.y==9.000001f,"Live transform position mutated");
        Check(!DockingGridPrecision.Active,"Final scope leak");
        Console.WriteLine($"Passed {checks} docking precision checks. Primary fits all three; Secondary's real obstruction remains.");
    }
}
