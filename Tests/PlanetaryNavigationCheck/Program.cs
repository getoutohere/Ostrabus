using System;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

static partial class Program {
    static int checks;
    static double rcsMaximum=1.9016982118751132E-10;
    static double lastDirectionX,lastDirectionY;
    static void Check(bool value,string reason) {checks++;if(!value)throw new Exception(reason);}
    const double AU = 149597870.7;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static double D(JToken t, string k) => (double)t[k];
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Accel(ref double __result) { __result = rcsMaximum; return false; }
    static bool Scale(ref float __result) { __result = 2; return false; }
    static bool Elapsed(ref float __result) { __result = 1f / 60; return false; }
    static bool Maneuver(Ship __instance, float fX, float fY) {
        double rot = __instance.objSS.fRot, x = fX * Math.Cos(rot) - fY * Math.Sin(rot), y = fX * Math.Sin(rot) + fY * Math.Cos(rot);
        double length = Math.Sqrt(x*x + y*y);
        if (forecasting) { __instance.objSS.vAccRCS=new Vector2((float)(x*physicalMax),(float)(y*physicalMax)); return false; }
        lastDirectionX=x/length;lastDirectionY=y/length;
        Console.WriteLine("Native AIIntercept2 commanded world direction: " + x/length + ", " + y/length);
        return false;
    }
    static int Main(string[] args) {
        if(args.Length>0 && args[0]=="extract-hauler") {
            var fixture=JObject.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture("Navigation/PlanetaryEvasion.json")));
            var saved=JArray.Parse(File.ReadAllText(args[1]))[0];var ships=(JArray)fixture["ships"];
            for(int i=0;i<ships.Count;i++)if((string)ships[i]["strRegID"]=="O-ENB") {
                var r=new JObject();foreach(string key in new[]{"objSS","strRegID","aDocked","bShipHidden","strScanTargetID","ShipType"})if(saved[key]!=null)r[key]=saved[key].DeepClone();
                r["strRegID"]="O-ENB";r["objSS"]["jnd"]["strRegID"]="O-ENB";ships[i]=r;
            }
            fixture["epoch"]=65632876040.6814;fixture["data"]["bodies"]=JObject.Parse(File.ReadAllText(args[2]))["data"]["bodies"];
            File.WriteAllText(args[3],fixture.ToString());Console.WriteLine("Extracted returning-hauler plan and anonymous geometries.");return 0;
        }
        if(args.Length>0 && args[0]=="extract") {
            var wanted=new System.Collections.Generic.HashSet<string>{"O-ENB","OKLG","OKLG|RES_1","O-GXD","E-7HAP","O-F4C6","J-2VC"};
            var root=JObject.Parse(File.ReadAllText(args[2]));var ships=new JArray();
            foreach(var file in Directory.GetFiles(args[1],"*.json")){var r=ReadShip(file);if(wanted.Contains((string)r["strRegID"]))ships.Add(r);}
            if(ships.Count!=wanted.Count)throw new Exception("Missing fixture hulls");
            File.WriteAllText(args[3],new JObject{["epoch"]=root["epoch"],["data"]=new JObject{["bodies"]=root["data"]["bodies"]},["ships"]=ships}.ToString());
            Console.WriteLine("Extracted "+ships.Count+" anonymous ship geometries.");return 0;
        }
        var h = new Harmony("offline.collision.snapshot");
        void Patch(Type t, string name, string fn) => h.Patch(AccessTools.Method(t, name), prefix: new HarmonyMethod(typeof(Program), fn));
        Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
        Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
        Patch(typeof(Ship), "get_RCSAccelMax", nameof(Accel));
        Patch(typeof(Time), "get_timeScale", nameof(Scale));
        Patch(typeof(CrewSim), "TimeElapsedScaled", nameof(Elapsed));
        Patch(typeof(Ship), "Maneuver", nameof(Maneuver));
        try {
            GeometryChecks();
            StationPolicyChecks(h);
            string fixture=Ostrabus.Development.InstallationPaths.Fixture("Navigation/PlanetaryEvasion.json");
            Avoidance(h,fixture,fixture);
            fixture=Ostrabus.Development.InstallationPaths.Fixture("Navigation/StationArrivalEvasion.json");
            Avoidance(h,fixture,fixture);
            Console.WriteLine(checks+" planetary navigation checks passed.");
            return 0;
        } finally { h.UnpatchSelf(); }
    }
}
