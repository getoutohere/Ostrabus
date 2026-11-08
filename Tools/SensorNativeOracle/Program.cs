using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Diagnostics;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Ostrabus.Development;
using Ostranauts.Ships;
using Ostranauts.Utils.Models;
using Ostranauts.ShipGUIs.Utilities;
using Ostrabus.Sensors.Model;
using Ostrabus.SensorComparison;
using Ostranauts.Ships.Sensors;
using Ostrabus.EMPropagation;

static class Program {
    static ShipSensor Make(SensorSpec spec) {
        Type type = spec.Kind == SensorKind.Radar ? typeof(Radar) : spec.Kind == SensorKind.Lidar ? typeof(Lidar) :
            spec.Kind == SensorKind.Optical ? typeof(Optical) : spec.Kind == SensorKind.IR ? typeof(IR) : typeof(EM);
        var sensor = (ShipSensor)FormatterServices.GetUninitializedObject(type);
        typeof(ShipSensor).GetField("<RangeKm>k__BackingField", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sensor,spec.BaseRangeKm);
        typeof(ShipSensor).GetField("_sensorStrength", BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sensor,spec.Strength);
        sensor.SensorState = spec.On ? 1 : 0;
        return sensor;
    }
    static void Main(string[] args) {
        if (args.Length != 1) throw new ArgumentException("Specify an output JSONL path.");
        ValidateCombatRange();
        ValidateNavOffsetConsumers();
        ValidateEmDistances();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        var packages = new Dictionary<string,ShipSensor[]>();
        foreach (var package in ScenarioCatalog.Packages()) {
            var sensors = new ShipSensor[package.Sensors.Length];
            for (int i=0;i<sensors.Length;i++) sensors[i]=Make(package.Sensors[i]);
            packages[package.Id]=sensors;
        }
        int count=0;
        var cutoffs=new Dictionary<string,double>();
        using(var writer=new StreamWriter(args[0])) foreach(var scenario in ScenarioCatalog.All()) {
            var target=(ShipSignature)FormatterServices.GetUninitializedObject(typeof(ShipSignature));
            target.RadarCrossSection=(float)scenario.Target.CrossSection;target.HeatSignature=scenario.Target.HeatSignature;
            target.EMEmissions=scenario.Target.EMEmission;target.VRelMS=scenario.Target.RelativeSpeedMps;target.BRGDegr=(float)scenario.Target.BearingDegrees;
            double signal=0;foreach(var sensor in packages[scenario.PackageId])signal+=sensor.GetSignalStrength(target,scenario.Target.RangeKm,1);
            bool visible=signal >= (double).3f;
            double correctedSignal=Score(packages[scenario.PackageId],target,scenario.Target.RangeKm,true);
            bool correctedVisible=correctedSignal >= (double).3f;
            string key=scenario.PackageId+"/"+scenario.TargetId;
            if(!cutoffs.TryGetValue(key,out double cutoff)) {
                double lo=.001,hi=1e9;
                for(int step=0;step<90;step++) {
                    double mid=(lo+hi)/2,total=0;
                    foreach(var sensor in packages[scenario.PackageId])total+=sensor.GetSignalStrength(target,mid,1);
                    if(total>=(double).3f)lo=mid;else hi=mid;
                }
                cutoff=lo;cutoffs[key]=cutoff;
            }
            string correctedKey=key+"/inverse-square";
            if(!cutoffs.TryGetValue(correctedKey,out double correctedCutoff)) {
                double lo=.001,hi=1e9;
                for(int step=0;step<90;step++) {
                    double mid=(lo+hi)/2;
                    if(Score(packages[scenario.PackageId],target,mid,true)>=(double).3f)lo=mid;else hi=mid;
                }
                correctedCutoff=lo;cutoffs[correctedKey]=correctedCutoff;
            }
            double maximum=visible?0:scenario.Target.RangeKm*200;
            var proposed=ContactModel.Evaluate(scenario.Sensors,scenario.Target,ModelSettings.Default);
            writer.WriteLine(JsonConvert.SerializeObject(new {scenario.Id,Signal=signal,Visible=visible,ExactCutoffKm=cutoff,
                CorrectedSignal=correctedSignal,CorrectedVisible=correctedVisible,CorrectedCutoffKm=correctedCutoff,
                ProposedRangeSigmaM=proposed.RangeSigmaM,ProposedCrossSigmaM=proposed.CrossRangeSigmaM,
                MaximumErrorM=maximum,MeanErrorM=maximum*2/3,RmsErrorM=maximum*Math.Sqrt(8d/15),
                MaximumOffsetIntervalSeconds=visible?0:7+scenario.Target.RangeKm/5}));
            count++;
        }
        Console.WriteLine(count+" synthetic scenarios evaluated through shipped sensor methods. Visibility threshold and offset statistics derived from inspected vanilla code; no Unity rendering fixture.");
        var fit=ScenarioCatalog.Packages()[3].Sensors;var observation=new Observation(1000);var settings=ModelSettings.Default;
        double checksum=0;for(int i=0;i<10000;i++)checksum+=ContactModel.Evaluate(fit,observation,settings).PositionRmsM;
        var timer=Stopwatch.StartNew();for(int i=0;i<100000;i++)checksum+=ContactModel.Evaluate(fit,observation,settings).PositionRmsM;timer.Stop();
        using(var stream=File.OpenRead(typeof(ShipSensor).Assembly.Location))using(var hash=SHA256.Create())
            File.WriteAllText(args[0]+".metadata.json",JsonConvert.SerializeObject(new {
                NativeAssembly=Path.GetFileName(typeof(ShipSensor).Assembly.Location),Sha256=BitConverter.ToString(hash.ComputeHash(stream)).Replace("-",""),
                ScenarioCount=count,Threshold=(double).3f,MonoEvaluations=100000,MonoElapsedMs=timer.Elapsed.TotalMilliseconds,
                Note="Controlled signatures; native sensor methods; proposed-model timing under bundled Mono. Allocation counter intentionally not used."
            },Formatting.Indented));
        Console.WriteLine("Bundled Mono prototype: 100,000 evaluations / "+timer.Elapsed.TotalMilliseconds+" ms. Guard "+checksum);
    }
    static double Score(ShipSensor[] sensors, ShipSignature target, double rangeKm, bool corrected) {
        double score=0;
        foreach(var sensor in sensors) {
            if(sensor is EM && sensor.On) {
                double native=sensor.GetSignalStrength(target,rangeKm,1);
                double replica=PropagationModel.Signal(rangeKm,sensor.RangeKm,target.EMEmissions,false);
                if(Math.Abs(native-replica)>1e-12*Math.Max(1,Math.Abs(native))) throw new Exception("EM baseline replica differs from shipped method.");
                score+=corrected?PropagationModel.Signal(rangeKm,sensor.RangeKm,target.EMEmissions):native;
            } else score+=sensor.GetSignalStrength(target,rangeKm,1);
        }
        return score;
    }
    static void ValidateEmDistances() {
        var sensor=Make(new SensorSpec(SensorKind.EM,500));
        var target=(ShipSignature)FormatterServices.GetUninitializedObject(typeof(ShipSignature));
        foreach(double emission in new[]{0,.1,.6,50d}) foreach(float threshold in new[]{.25f,.3f}) {
            target.EMEmissions=emission;
            double native=sensor.GetDetectionDistance(target,1,threshold);
            double replica=PropagationModel.DetectionDistance(500,emission,threshold,false);
            double corrected=PropagationModel.DetectionDistance(500,emission,threshold);
            if(Math.Abs(native-replica)>1e-9 || corrected+1e-9<native) throw new Exception("EM distance calculation disagrees with native baseline or propagation improvement.");
            if(emission>0 && Math.Abs(PropagationModel.Signal(corrected,500,emission)-threshold)>1e-12)
                throw new Exception("Revised EM signal and detection distance disagree.");
        }
        Console.WriteLine("EM range formula verified against shipped method and revised signal thresholds (including sensor skill).");
    }
    static void ValidateCombatRange() {
        var definitions = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data,"condowners","condowners_ship_combat.json")));
        double longest = 0;
        foreach (var definition in definitions) {
            if (!((string)definition["strName"]).StartsWith("ItmShipWeaponMassThrower",StringComparison.Ordinal)) continue;
            foreach (var cond in definition["aStartingConds"]) {
                string value = (string)cond;
                if (value.StartsWith("IsShipWeaponArcRange=",StringComparison.Ordinal))
                    longest = Math.Max(longest,double.Parse(value.Substring(value.LastIndexOf('x')+1),CultureInfo.InvariantCulture));
            }
        }
        double maximum = longest * 4; // Ship.UpdateCrewSkills: SkillOpsGunnery, verified source audit.
        if (longest != 120000 || longest/1000 != ModelSettings.Default.CombatRangeKm) throw new Exception("Shipped railgun range changed; revisit combat safeguard.");
        const double auM = 149597863936;
        if (!WeaponsSystem.IsPointInView(new Point(0,0),new Point(1,0),new Point((maximum-1)/auM,0),maximum,10) ||
            WeaponsSystem.IsPointInView(new Point(0,0),new Point(1,0),new Point((maximum+1)/auM,0),maximum,10))
            throw new Exception("Native weapon range geometry differs from combat safeguard.");
        Console.WriteLine("Native railgun verified: 120 km base / 480 km with gunnery skill; auto-fire geometry checked. Model clamp uses base 120 km.");
    }
    static void ValidateNavOffsetConsumers() {
        var ship = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
        typeof(Ship).GetField("<objSS>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(ship,
            FormatterServices.GetUninitializedObject(typeof(ShipSitu)));
        ship.objSS.vPosx = 1; ship.objSS.vPosy = 2; ship.objSS.vVelX = .001; ship.objSS.vVelY = -.002;
        var poi = new NavPOI(0,0) { SensorOffset = new Point(.1,-.2), fOffsetSX = .01, fOffsetSY = -.02 };
        typeof(NavPOI).GetField("_ship",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(poi,ship);
        poi.GetSXY(out double x,out double y); poi.GetVSXY(out double vx,out double vy);
        if (Math.Abs(x-1.11)>1e-14 || Math.Abs(y-1.78)>1e-14 || vx!=.001 || vy!=-.002)
            throw new Exception("Native nav offset/velocity consumers changed.");
        var target = new TargetData(ship);
        if (!ReferenceEquals(target.Situ,ship.objSS) || ship.objSS.vPosx!=1 || ship.objSS.vPosy!=2)
            throw new Exception("Nav observation unexpectedly altered authoritative target state.");
        Console.WriteLine("Native NavPOI verified: position includes observation/user offsets; velocity and TargetData retain actual ship state.");
    }
}
