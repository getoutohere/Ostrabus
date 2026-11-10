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
