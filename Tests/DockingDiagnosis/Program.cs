using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Trading;
using Ostranauts.Utils;
class Program {
    static readonly string Data =Ostrabus.Development.InstallationPaths.Data;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static Dictionary<string,T> Load<T>(string folder, Func<T,string> key) {
        var dict=new Dictionary<string,T>();
        foreach(var file in Directory.GetFiles(Data+"/"+folder,"*.json")) foreach(var item in JsonConvert.DeserializeObject<T[]>(File.ReadAllText(file))) dict[key(item)]=item;
        return dict;
    }
    static Ship Make(string id) {
        var raw=JArray.Parse(File.ReadAllText("obj/"+id+"-docking.json"))[0];
        var json=new JsonShip(); json.nCols=(int)raw["nCols"];json.nRows=(int)raw["nRows"];json.vShipPos=raw["vShipPos"].ToObject<UnityEngine.Vector2>();json.aItems=raw["aItems"].ToObject<JsonItem[]>();
        var s=Empty<Ship>();s.json=json;s.strRegID=id;
        AccessTools.Field(typeof(Ship),"nLoadState").SetValue(s,Ship.Loaded.Shallow);
        AccessTools.Field(typeof(Ship),"aPeople").SetValue(s,new List<PersonSpec>());
        AccessTools.Field(typeof(Ship),"aDockingPorts").SetValue(s,raw["aDockingPorts"].ToObject<List<string>>());
        AccessTools.Field(typeof(Ship),"strPrimaryDockingPortID").SetValue(s,(string)raw["strPrimaryDockingPortID"]);
        return s;
    }
    static void Main() {
        if(Environment.GetEnvironmentVariable("DOCK_DUMP_IL")=="1") {
            var reader=typeof(PatchProcessor).GetMethods().Single(m=>m.Name=="GetOriginalInstructions" && m.GetParameters().Length==2 && !m.GetParameters()[1].ParameterType.IsByRef);
            var il=((IEnumerable<CodeInstruction>)reader.Invoke(null,new object[]{AccessTools.Method(typeof(GridUtils),"CreateDockingPortGrid"),null})).ToList();
            for(int i=0;i<il.Count;i++) if(il[i].opcode==System.Reflection.Emit.OpCodes.Add) { Console.WriteLine("ADD "+i); foreach(var line in il.Skip(Math.Max(0,i-6)).Take(9))Console.WriteLine(line); } return;
        }
        if(Environment.GetEnvironmentVariable("DOCK_TEST")=="1") PrecisionChecks.Install();
        DataHandler.dictCOs=Load<JsonCondOwner>("condowners",j=>j.strName);
        DataHandler.dictCOOverlays=Load<JsonCOOverlay>("cooverlays",j=>j.strName);
        DataHandler.dictItemDefs=Load<JsonItemDef>("items",j=>j.strName);
        DataHandler.dictDataCOs=new Dictionary<string,DataCO>();
        // Geometry needs base conditions and footprint definitions, not overlay loot or spawned COs.
        foreach(var j in DataHandler.dictCOs.Values) {
            var d=new DataCO(j); DataHandler.dictItemDefs.TryGetValue(j.strItemDef??"",out d.JItmDef); DataHandler.dictDataCOs[j.strName]=d;
        }
        foreach(var j in DataHandler.dictCOOverlays.Values) if(DataHandler.dictDataCOs.TryGetValue(j.strCOBase,out var d)) DataHandler.dictDataCOs[j.strName]=d;
        var incoming=Make("O-GXD");var station=Make("OKLG");
        if(Environment.GetEnvironmentVariable("DOCK_TEST")=="1") { PrecisionChecks.Run(station,incoming);return; }
        foreach(var s in new[]{incoming,station}) {
            var missing=s.json.aItems.Where(j=>!DataHandler.dictDataCOs.ContainsKey(j.strName)).Select(j=>j.strName).Distinct();
            Console.WriteLine(s.strRegID+" missing definitions: "+string.Join(",",missing));
        }
        if(Environment.GetEnvironmentVariable("DOCK_SNAP_BRACE")=="1") {
            var brace=incoming.json.aItems.Single(i=>i.strID=="308f0834-36ba-4c06-be8e-edb1755eb871");
            Console.WriteLine("SNAP brace Y "+brace.fY.ToString("R")+" -> 8.5 (offline only)"); brace.fY=8.5f;
        }
        if(Environment.GetEnvironmentVariable("DOCK_SNAP_ALL")=="1") foreach(var s in new[]{incoming,station}) foreach(var it in s.json.aItems) {
            float x=(float)Math.Round(it.fX*2)/2, y=(float)Math.Round(it.fY*2)/2;
            if(Math.Abs(it.fX-x)<0.0001f)it.fX=x; if(Math.Abs(it.fY-y)<0.0001f)it.fY=y;
        }
        var a=GridUtils.CreateFullGrid(station);var b=GridUtils.CreateFullGrid(incoming);
        var portsA=GridUtils.GatherDockingPortData(station.GetAllDockingPorts(),a);var portsB=GridUtils.GatherDockingPortData(incoming.GetAllDockingPorts(),b);
        Console.WriteLine("Grid sizes station="+a.Width+"x"+a.Height+" incoming="+b.Width+"x"+b.Height);
        foreach(var pa in portsA)foreach(var pb in portsB) {
            var rotation=GridUtils.GetIncomingDockRotation(pa.Rotation,pb.Rotation,out var offset);
            var t=GridUtils.CreateOffset(pa.GridPos,pb.GridPos,b.Height,b.Width,rotation,offset);
            var clashes=(List<Ostranauts.Pathing.Vector2Int>)AccessTools.Method(typeof(GridUtils),"CanOverlayBOnA").Invoke(null,new object[]{a,b,t});
            Console.WriteLine("PORT "+pa.PortID+" / "+pb.PortID+" rotation="+rotation+" fit="+(clashes==null)+" clashes="+(clashes?.Count??0));
            if(clashes!=null)foreach(var cell in clashes.Take(10)) {
                Console.WriteLine("  station grid "+cell.x+","+cell.y+" "+a[cell.x,cell.y].DataCO.Name+" id="+a[cell.x,cell.y].ID);
                foreach(var pair in b) {
                    int x=pair.Key.x,y=pair.Key.y;
                    if(rotation==90){x=b.Height-1-pair.Key.y;y=pair.Key.x;}
                    if(rotation==180){x=b.Width-1-pair.Key.x;y=b.Height-1-pair.Key.y;}
                    if(rotation==270){x=pair.Key.y;y=b.Width-1-pair.Key.x;}
                    if(x+t.offsetCol==cell.x && y+t.offsetRow==cell.y && pair.Value!=null) {
                        var it=pair.Value.JItem;
                        Console.WriteLine("    incoming "+pair.Value.DataCO.Name+" id="+pair.Value.ID+" raw="+it?.strName+" pos="+it?.fX+","+it?.fY+" parent="+it?.strParentID);
                    }
                }
            }
        }
    }
}







