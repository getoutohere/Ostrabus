using System;
using System.Collections.Generic;
using Ostrabus.Sensors.Model;

namespace Ostrabus.SensorComparison;

public sealed class Package {
    public string Id;
    public SensorSpec[] Sensors;
    public Package(string id, params SensorSpec[] sensors) { Id = id; Sensors = sensors; }
}
public readonly struct Scenario {
    public readonly string Id, PackageId, TargetId;
    public readonly SensorSpec[] Sensors;
    public readonly Observation Target;
    public Scenario(string id, Package package, string targetId, Observation target) {
        Id = id; PackageId = package.Id; TargetId = targetId; Sensors = package.Sensors; Target = target;
    }
}
public static class ScenarioCatalog {
    static SensorSpec S(SensorKind kind, double range, double strength = 0) => new SensorSpec(kind, range, strength);
    public static Package[] Packages() {
        var actual = new[] { S(SensorKind.Radar,400,1),S(SensorKind.Radar,400,1),
            S(SensorKind.Lidar,200,2),S(SensorKind.Lidar,200,2),S(SensorKind.Lidar,200,2),
            S(SensorKind.Optical,25),S(SensorKind.Optical,25),S(SensorKind.Optical,50),S(SensorKind.Optical,50),S(SensorKind.Optical,50),S(SensorKind.Optical,50),
            S(SensorKind.IR,600),S(SensorKind.IR,600),S(SensorKind.IR,600),S(SensorKind.IR,600),S(SensorKind.IR,600),
            S(SensorKind.EM,500),S(SensorKind.EM,500),S(SensorKind.EM,500),S(SensorKind.EM,500),S(SensorKind.EM,500) };
        var passive = new List<SensorSpec>(); foreach (var s in actual) if (s.Kind != SensorKind.Radar && s.Kind != SensorKind.Lidar) passive.Add(s);
        return new[] {
            new Package("starter-passive", S(SensorKind.Optical,25),S(SensorKind.IR,300),S(SensorKind.EM,300)),
            new Package("mixed-upgrade", S(SensorKind.Radar,400,1),S(SensorKind.Lidar,200,2),S(SensorKind.Optical,50),S(SensorKind.IR,600),S(SensorKind.EM,500)),
            new Package("stellar-magnificence-passive", passive.ToArray()), new Package("stellar-magnificence", actual)
        };
    }
    public static Scenario[] All() {
        var result = new List<Scenario>();
        foreach (var package in Packages()) foreach (string target in new[] { "cooperative-cold", "unknown-dark-wreck", "known-silent-wreck", "dark-hot", "fast-steady", "uncertain-maneuver", "stale-60s" })
            foreach (double range in Distances(package)) {
                bool dark = target == "unknown-dark-wreck" || target == "known-silent-wreck" || target == "dark-hot";
                bool uncertain = target == "uncertain-maneuver" || target == "stale-60s";
                var observation = new Observation(range, heatSignature: target == "dark-hot" ? 1.5 : 0,
                    emEmission: dark ? 0 : .6, knownHull: target != "unknown-dark-wreck" && target != "dark-hot",
                    transponderBroadcasting: !dark, relativeSpeedMps: target == "fast-steady" ? 1000 : 50,
                    sampleAgeSeconds: target == "stale-60s" ? 60 : 0,
                    velocitySigmaMps: uncertain ? 100 : .1, accelerationSigmaMps2: uncertain ? 3 : .01);
                string id = package.Id + "/" + target + "/" + BitConverter.DoubleToInt64Bits(range).ToString("X16");
                result.Add(new Scenario(id, package, target, observation));
            }
        return result.ToArray();
    }
    static double[] Distances(Package package) {
        var ranges=new List<double>(new[] { .1,1,5,30,100,120,180,240,250,480,500,1000,1390,1396.2626894403729,1400,2000,5000,10000,100000,1000000,10000000,60000000,100000000 });
        double sum=0;
        foreach(var sensor in package.Sensors) if(sensor.Kind==SensorKind.EM && sensor.On) sum+=sensor.BaseRangeKm*sensor.BaseRangeKm;
        double edge=Math.Sqrt(sum*TransponderReception.BroadcastEmission/TransponderReception.NormalThreshold);
        foreach(double factor in new[]{.98,.9999,1.0001,1.02}) ranges.Add(edge*factor);
        ranges.Sort();
        return ranges.ToArray();
    }
}
