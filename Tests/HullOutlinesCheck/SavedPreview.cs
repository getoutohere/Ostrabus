using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.NavConsole.Outlines;

static class SavedHullPreview {
    internal static JsonShip ReadRecord(string path,string ship) {
        using(var zip=Ionic.Zip.ZipFile.Read(path)) {
            var entry=zip.Entries.Single(e=>e.FileName.Replace('\\','/')=="ships/"+ship+".json");
            using(var text=new StreamReader(entry.OpenReader()))using(var reader=new JsonTextReader(text)) {
                var record=new JObject();
                while(reader.Read()) {
                    if(reader.TokenType!=JsonToken.PropertyName)continue;
                    string name=(string)reader.Value;reader.Read();
                    if(name=="strRegID"||name=="aItems"||name=="objSS"||name=="nGridRotation"||name=="nCols"||name=="nRows")record[name]=JToken.Load(reader);
                    else reader.Skip();
                }
                if(record["aItems"] is JArray&&record["objSS"] is JObject)return record.ToObject<JsonShip>();
                throw new InvalidOperationException("Incomplete saved hull "+ship+": "+string.Join(",",record.Properties().Select(p=>p.Name)));
            }
        }
    }
    internal static JArray Read(string path,string ship) {
        using(var zip=Ionic.Zip.ZipFile.Read(path))foreach(var entry in zip.Entries.Where(e=>e.FileName.EndsWith(".json")&&Path.GetFileName(e.FileName)!="saveInfo.json"))
            using(var text=new StreamReader(entry.OpenReader()))using(var reader=new JsonTextReader(text)) {
                var owners=new Dictionary<int,string>();
                while(reader.Read()) {
                    if(reader.TokenType==JsonToken.StartObject){owners.Remove(reader.Depth+1);continue;}
                    if(reader.TokenType==JsonToken.EndObject){owners.Remove(reader.Depth+1);continue;}
                    if(reader.TokenType!=JsonToken.PropertyName)continue;
                    string name=(string)reader.Value;int depth=reader.Depth;
                    if(name=="strRegID"){reader.Read();if(reader.TokenType==JsonToken.String)owners[depth]=(string)reader.Value;}
                    else if(name=="aCOs"){reader.Read();reader.Skip();}
                    else if(name=="aItems") {
                        reader.Read();
                        if(reader.TokenType==JsonToken.StartArray&&owners.TryGetValue(depth,out string owner)&&owner==ship)return JArray.Load(reader);
                        reader.Skip();
                    }
                }
            }
        throw new InvalidOperationException("Saved hull not found: "+ship);
    }
}

static partial class Program {
    static void SavedDockingPair(string path,string firstId,string firstPort,string secondId,string secondPort) {
        byte[] before=Hash(path);
        Ship LoadHull(string id,out Envelope geometry) {
            var saved=SavedHullPreview.ReadRecord(path,id);
            var build=AccessTools.Method(typeof(DataHandler),"BuildDataCO");
            foreach(string name in saved.aItems.Select(i=>i.strName).Distinct()) {
                DataHandler.dictCOOverlays.TryGetValue(name,out var overlay);DataHandler.dictCOs.TryGetValue(name,out var basis);
                build.Invoke(null,new object[]{name,overlay,basis});
            }
            var hull=Hull(Ship.Loaded.Shallow,saved.aItems);hull.strRegID=id;hull.json=saved;hull.nGridRotation=saved.nGridRotation;
            Field(hull,"<objSS>k__BackingField",new ShipSitu(saved.objSS));
            geometry=Take(hull,out _);
            return hull;
        }
        var a=LoadHull(firstId,out var ga);var b=LoadHull(secondId,out var gb);
        foreach(var vessel in new[]{a,b})Console.WriteLine("Saved docking devices "+vessel.strRegID+": "+string.Join("; ",vessel.json.aItems.Where(i=>i.strName.Contains("DockSys")).Select(i=>i.strName+" / "+i.strID+" / "+i.fRotation)));
        Check(a.json.aItems.Any(i=>i.strID==firstPort)&&b.json.aItems.Any(i=>i.strID==secondPort),"Both specified ports must exist in the saved snapshot; do not substitute native missing-port rotation zero");
        a.objSS.bBOLocked=a.objSS.bOrbitLocked=b.objSS.bBOLocked=b.objSS.bOrbitLocked=false;
        b.objSS.vPosx=b.objSS.vPosy=0;
        a.PlaceAtAirlocks(firstPort,secondPort,b); // Real native placement and saved port rotations.
        double nativeDistance=CollisionManager.GetCollisionDistanceAU(a,b)/HullZoom.MetresToAU;
        double placement=a.objSS.vPos.magnitude/HullZoom.MetresToAU;
        var direction=a.objSS.vPos.normalized;
        var solver=new Ostrabus.ShipNavigation.Collisions.HullContact();
        bool Hit(double distance,out Ostrabus.ShipNavigation.Collisions.HullContact.Contact contact) {
            var pos=new Ostrabus.ShipNavigation.Collisions.HullContact.Vec(direction.X*distance,direction.Y*distance);
            var motion=new Ostrabus.ShipNavigation.Collisions.HullContact.Motion(pos,pos,
                a.objSS.fRot-a.nGridRotation*Math.PI/180,a.objSS.fRot-a.nGridRotation*Math.PI/180,
                b.objSS.fRot-b.nGridRotation*Math.PI/180,b.objSS.fRot-b.nGridRotation*Math.PI/180,ga.MapRadius,gb.MapRadius,false);
            Check(solver.TrySolve(ga.Collision,gb.Collision,motion,out contact),"Docking-pair numeric solve converges");
            return contact.Hit;
        }
        bool placementHit=Hit(placement,out var placed);
        bool clampHit=Hit(nativeDistance*1.1,out _);
        double lo=placement,hi=ga.MapRadius+gb.MapRadius+100;
        if(placementHit)for(int i=0;i<45;i++){double mid=(lo+hi)*.5;if(Hit(mid,out _))lo=mid;else hi=mid;}
        var report=new {
            first=firstId,second=secondId,firstPortType=a.json.aItems.FirstOrDefault(i=>i.strID==firstPort)?.strName,
            secondPortType=b.json.aItems.FirstOrDefault(i=>i.strID==secondPort)?.strName,
            firstGridRotation=a.nGridRotation,secondGridRotation=b.nGridRotation,
            nativeRadiusA=a.objSS.Size,nativeRadiusB=b.objSS.Size,hullRadiusA=ga.MapRadius,hullRadiusB=gb.MapRadius,
            nativeClampCenterRangeMetres=nativeDistance*1.1,nativeDockedCenterDistanceMetres=placement,
            hullOverlapAtNativePlacement=placementHit,hullOverlapAtClampRange=clampHit,
            hullContactCenterDistanceMetres=placementHit?(object)hi:null,geometricSeparationAtNativePlacementMetres=placed.Depth
        };
        Directory.CreateDirectory("obj/HullPreview");
        File.WriteAllText("obj/HullPreview/docking-pair.json",JsonConvert.SerializeObject(report,Formatting.Indented));
        Console.WriteLine("Native docking / corrected hull diagnosis: "+JsonConvert.SerializeObject(report));
        DockingCases(a,b,ga,gb,firstPort,secondPort);
        Check(before.SequenceEqual(Hash(path)),"Docking diagnosis changed the save archive");
    }
    static void SavedSizes(string path,string first,string second) {
        byte[] hash=Hash(path);
        foreach(string id in new[]{first,second}) {
            var saved=SavedHullPreview.Read(path,id).ToObject<JsonItem[]>();
            var build=AccessTools.Method(typeof(DataHandler),"BuildDataCO");
            foreach(string name in saved.Select(i=>i.strName).Distinct()) {
                DataHandler.dictCOOverlays.TryGetValue(name,out var overlay);DataHandler.dictCOs.TryGetValue(name,out var basis);
                build.Invoke(null,new object[]{name,overlay,basis});
            }
            var original=Take(Hull(Ship.Loaded.Shallow,saved),out _);
            Check(original.Collision!=null&&original.MapRadius>0,"Saved hull has no usable sized geometry: "+id);
            double minX=Enumerable.Range(0,original.Count).Min(i=>original.Raw(i).X),maxX=Enumerable.Range(0,original.Count).Max(i=>original.Raw(i).X);
            double minY=Enumerable.Range(0,original.Count).Min(i=>original.Raw(i).Y),maxY=Enumerable.Range(0,original.Count).Max(i=>original.Raw(i).Y);
            foreach(var item in saved) {
                float x=item.fX;item.fX=-item.fY;item.fY=x;item.fRotation+=90;
            }
            var turned=Take(Hull(Ship.Loaded.Shallow,saved),out _);
            Check(Math.Abs(original.MapRadius-turned.MapRadius)<.01,"Rotating saved hull changed its size: "+id);
            Console.WriteLine("Saved hull "+id+": installed footprint "+(maxX-minX).ToString("F2")+" x "+(maxY-minY).ToString("F2")+
                " tiles; shared map radius "+original.MapRadius.ToString("F2")+" m; quarter-turn radius "+turned.MapRadius.ToString("F2")+" m.");
        }
        Check(hash.SequenceEqual(Hash(path)),"Read-only size diagnosis changed the save archive");
    }
    static byte[] Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return sha.ComputeHash(stream);}
    static double Turn(HullPoint a,HullPoint b,HullPoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    static Envelope PriorConvex(Envelope exterior) {
        var sorted=Enumerable.Range(0,exterior.Count).Select(exterior.Raw).OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray();
        var work=new HullPoint[sorted.Length*2];int count=0;
        foreach(var p in sorted){while(count>=2&&Turn(work[count-2],work[count-1],p)<=0)count--;work[count++]=p;}
        int lower=count+1;
        for(int i=sorted.Length-2;i>=0;i--){while(count>=lower&&Turn(work[count-2],work[count-1],sorted[i])<=0)count--;work[count++]=sorted[i];}
        if(count>1)count--;return new Envelope(work,count);
    }
    static object Geometry(Envelope shape) {
        var loops=new List<object>();
        for(int loop=0;loop<shape.Loops;loop++) {
            var points=new List<object>();
            for(int i=shape.Start(loop);i<shape.End(loop);i++){var point=shape.Unit(i);points.Add(new[]{point.X,point.Y});}
            loops.Add(points);
        }
        return new{vertices=shape.Count,loops};
    }
    static void SavedPreview(string path,string shipId) {
        byte[] hash=Hash(path);var saved=SavedHullPreview.Read(path,shipId).ToObject<JsonItem[]>();
        var build=AccessTools.Method(typeof(DataHandler),"BuildDataCO");
        foreach(string name in saved.Select(i=>i.strName).Distinct()) {
            DataHandler.dictCOOverlays.TryGetValue(name,out var overlay);DataHandler.dictCOs.TryGetValue(name,out var basis);
            build.Invoke(null,new object[]{name,overlay,basis});
        }
        var result=Take(Hull(Ship.Loaded.Shallow,saved),out int steps);
        Check(result.Count>4,"Saved hull exterior lost all recognizable geometry");
        Check(result.Collision!=null,"Saved hull collision cover failed: "+result.CollisionError);
        Check(hash.SequenceEqual(Hash(path)),"Read-only hull diagnosis changed the source archive");
        Directory.CreateDirectory("obj/HullPreview");
        File.WriteAllText("obj/HullPreview/comparison.json",JsonConvert.SerializeObject(new{shipId,previous=Geometry(PriorConvex(result)),exterior=Geometry(result)},Formatting.Indented));
        Console.WriteLine("Saved hull "+shipId+": "+saved.Length+" records, "+result.Count+" exterior vertices, "+result.Collision.Pieces.Length+" collision pieces, "+result.Loops+" components, "+steps+" capture steps; archive SHA unchanged; preview in obj/HullPreview.");
    }
}
