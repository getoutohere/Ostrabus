using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Utils;
using Ostrabus.MiscBugFixes;

// Request-only offline replay; never opens or writes the running game.
static class SavedFitStudy {
    internal static void Run(string folder) {
        var ships = new Dictionary<string, Ship>();
        foreach (string file in Directory.GetFiles(folder,"*.json")) {
            var raw = JArray.Parse(File.ReadAllText(file))[0];
            var name=Path.GetFileNameWithoutExtension(file);
            var id = name.Replace('%','|');
            if (!(raw["aDockingPorts"] is JArray)) { Console.WriteLine("SKIP no ports " + id); continue; }
            var ship = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
            ship.strRegID = id;
            ship.json = new JsonShip {
                nCols = (int)raw["nCols"], nRows = (int)raw["nRows"],
                vShipPos = raw["vShipPos"].ToObject<UnityEngine.Vector2>(),
                aItems = raw["aItems"].ToObject<JsonItem[]>(),
                aDocked = raw["aDocked"]?.ToObject<Dictionary<string,string>>()
            };
            AccessTools.Field(typeof(Ship), "nLoadState").SetValue(ship, Ship.Loaded.Shallow);
            AccessTools.Field(typeof(Ship), "aPeople").SetValue(ship, new List<PersonSpec>());
            AccessTools.Field(typeof(Ship), "aDockingPorts").SetValue(ship, raw["aDockingPorts"].ToObject<List<string>>());
            AccessTools.Field(typeof(Ship), "strPrimaryDockingPortID").SetValue(ship, (string)raw["strPrimaryDockingPortID"]);
            AccessTools.Field(typeof(Ship), "aDocked").SetValue(ship, new Dictionary<string,Ship>());
            ships.Add(id, ship);
            if(Environment.GetEnvironmentVariable("DOCK_STUDY_EXPORT") is string export) {
                Directory.CreateDirectory(export);
                var geometry=new JObject {
                    ["nCols"]=ship.json.nCols,["nRows"]=ship.json.nRows,
                    ["vShipPos"]=raw["vShipPos"].DeepClone(),
                    ["aDockingPorts"]=raw["aDockingPorts"].DeepClone(),
                    ["strPrimaryDockingPortID"]=(string)raw["strPrimaryDockingPortID"],
                    ["aDocked"]=raw["aDocked"]?.DeepClone(),
                    ["aItems"]=new JArray(ship.json.aItems.Where(it=>it.strParentID==null).Select(it=>new JObject {
                        ["strName"]=it.strName,["strID"]=it.strID,["strParentID"]=it.strParentID,
                        ["fX"]=it.fX,["fY"]=it.fY,["fRotation"]=it.fRotation
                    }))
                };
                File.WriteAllText(Path.Combine(export,name+".json"),new JArray(geometry).ToString(Newtonsoft.Json.Formatting.None));
            }
            Console.WriteLine("SHIP " + id + " primary=" + ship.PrimaryDockingPortID + " savedLinks=" + raw["aDocked"]);
            foreach (var item in ship.json.aItems.Where(it => ship.GetAllDockingPorts().Contains(it.strID)))
                Console.WriteLine("  PORT " + item.strID + " " + item.strName + " " + item.fX.ToString("R") + "," + item.fY.ToString("R") + " rot=" + item.fRotation + " parent=" + item.strParentID);
        }
        var sourceCoordinates=ships.Values.SelectMany(s=>s.json.aItems).Select(it=>(it,it.fX,it.fY)).ToArray();
        bool groups=Environment.GetEnvironmentVariable("DOCK_STUDY_GROUPS")=="1";
        foreach (var ship in ships.Values) {
            var links=new Dictionary<string,Ship>();
            if(groups && ship.strRegID!="O-GXD" && ship.json.aDocked!=null)
                foreach(var pair in ship.json.aDocked.Where(p=>p.Value!="O-GXD")) {
                    if(!ships.TryGetValue(pair.Value,out var peer)) throw new Exception("Missing linked hull " + pair.Value);
                    links.Add(pair.Key,peer);
                }
            AccessTools.Field(typeof(Ship),"aDocked").SetValue(ship,links);
            ship.json.aDocked=links.ToDictionary(p=>p.Key,p=>p.Value.strRegID);
        }
        var incoming = ships["O-GXD"];
        bool patched=Environment.GetEnvironmentVariable("DOCK_STUDY_PATCH")=="1";
        int validated=0;
        int scope=DockingGridPrecision.Enter();
        try {
        foreach (var station in ships.Values.Where(s => s.strRegID=="OKLG" || s.strRegID=="OKLG|RES_1")) {
            var a = GridUtils.CreateFullGrid(station); var b = GridUtils.CreateFullGrid(incoming);
            Console.WriteLine("TARGET " + station.strRegID);
            foreach(var id in station.GetAllDockingPorts()) {
                var dto=GridUtils.FindDockingPort(a,id);
                Console.WriteLine("PORT PRESENT " + id + " " + (dto!=null));
                if(dto==null) {
                    var source=station.json.aItems.First(it=>it.strID==id);
                    var offset=GridUtils.CalculateGridOffset(station);
                    foreach(var pair in a.Where(p=>Math.Abs(p.Key.x-source.fX-offset.x)<3 && Math.Abs(p.Key.y-source.fY-offset.y)<3))
                        if(pair.Value!=null) Console.WriteLine("  NEAR " + pair.Key.x+","+pair.Key.y+" "+pair.Value.DataCO.Name+" "+pair.Value.ID);
                }
            }
            foreach (var pa in GridUtils.GatherDockingPortData(station.GetAllDockingPorts(), a))
            foreach (var pb in GridUtils.GatherDockingPortData(incoming.GetAllDockingPorts(), b)) {
                int rotation = GridUtils.GetIncomingDockRotation(pa.Rotation, pb.Rotation, out var offset);
                var transform = GridUtils.CreateOffset(pa.GridPos, pb.GridPos, b.Height, b.Width, rotation, offset);
                var clashes = (List<Ostranauts.Pathing.Vector2Int>)AccessTools.Method(typeof(GridUtils), "CanOverlayBOnA").Invoke(null, new object[] { a,b,transform });
                Console.WriteLine("PAIR " + pa.PortID + " / " + pb.PortID + " clashes=" + (clashes?.Count ?? 0));
                if (clashes != null) foreach (var cell in clashes.Take(12)) {
                    Console.WriteLine("  STATION " + cell.x + "," + cell.y + " " + a[cell.x,cell.y].DataCO.Name + " " + a[cell.x,cell.y].ID);
                    foreach (var pair in b.Where(p => p.Value != null)) {
                        int x = pair.Key.x, y = pair.Key.y;
                        if (rotation == 90) { x = b.Height-1-pair.Key.y; y=pair.Key.x; }
                        if (rotation == 180) { x = b.Width-1-pair.Key.x; y=b.Height-1-pair.Key.y; }
                        if (rotation == 270) { x = pair.Key.y; y=b.Width-1-pair.Key.x; }
                        if (x+transform.offsetCol == cell.x && y+transform.offsetRow == cell.y)
                            Console.WriteLine("    INCOMING " + pair.Value.DataCO.Name + " " + pair.Value.ID + " pos=" + pair.Value.JItem?.fX + "," + pair.Value.JItem?.fY);
                    }
                }
            }
            var fits = station.GetAvailableDockingPorts(incoming, false);
            Console.WriteLine("NATIVE FIT " + station.strRegID + " " + (fits == null ? "NONE" : string.Join(";", fits)));
            bool primaryFound=GridUtils.FindDockingPort(a,station.PrimaryDockingPortID)!=null;
            if(patched) {
                if(!primaryFound || fits==null || !fits.Any(p=>p.Item1==station.PrimaryDockingPortID && p.Item2==incoming.PrimaryDockingPortID))
                    throw new Exception("Primary docking fit not restored for " + station.strRegID);
                if(station.strRegID=="OKLG" && fits.Any(p=>p.Item1=="eb0d51c8-0da8-4bb7-a38f-4133baec7cd5"))
                    throw new Exception("Real third-port obstruction was incorrectly accepted");
            }
            else if(station.strRegID=="OKLG|RES_1" && primaryFound) throw new Exception("Vanilla RES1 missing-port failure not reproduced");
            validated++;
        }
        } finally { DockingGridPrecision.Exit(scope); }
        foreach(var row in sourceCoordinates) if(row.it.fX!=row.fX || row.it.fY!=row.fY) throw new Exception("Stored coordinate mutated");
        if(DockingGridPrecision.Active) throw new Exception("Docking scope leaked");
        if(validated!=2) throw new Exception("Missing required target fixtures");
        if(patched) {
            // Exercise the actual outer hook without the diagnostic's manual scope.
            var station=ships["OKLG|RES_1"];
            var pairs=station.GetAvailableDockingPorts(incoming,false);
            if(pairs==null || !pairs.Any(p=>p.Item1==station.PrimaryDockingPortID && p.Item2==incoming.PrimaryDockingPortID))
                throw new Exception("Native outer scope failed to restore RES1 fit");
            if(DockingGridPrecision.Active) throw new Exception("Native outer scope leaked");
            bool failed=false;
            try { station.GetAvailableDockingPorts(null,false); } catch(NullReferenceException) { failed=true; }
            if(!failed || DockingGridPrecision.Active) throw new Exception("Native exception scope cleanup failed");
            int outer=DockingGridPrecision.Enter();
            int nested=DockingGridPrecision.Enter(); DockingGridPrecision.Exit(nested);
            if(!DockingGridPrecision.Active) throw new Exception("Nested scope cleared parent");
            DockingGridPrecision.Exit(outer);
            PrecisionChecks.RunGeometryChecks();
        }
        Console.WriteLine("PASS saved fit study: " + (patched?"corrected":"vanilla") + ", groups=" + groups + "; source geometry unchanged.");
    }
}
