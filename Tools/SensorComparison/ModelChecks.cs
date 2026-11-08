using Ostrabus.Sensors.Model;

namespace Ostrabus.SensorComparison;

static class ModelChecks {
    static int count;
    static void Check(bool ok, string why) { count++; if (!ok) throw new Exception(why); }
    internal static int Run() {
        var settings = ModelSettings.Default;
        var packages = ScenarioCatalog.Packages();
        foreach (var scenario in ScenarioCatalog.All()) {
            var result = ContactModel.Evaluate(scenario.Sensors, scenario.Target, settings);
            Check(result.HasEstimate && double.IsFinite(result.PositionRmsM), "Finite scenario: " + scenario.Id);
            foreach (var extra in new[] { new SensorSpec(SensorKind.Radar,400,1), new SensorSpec(SensorKind.Optical,25), new SensorSpec(SensorKind.EM,500) }) {
                var sensors = scenario.Sensors.Concat(new[] { extra }).ToArray();
                var improved = ContactModel.Evaluate(sensors, scenario.Target, settings);
                Check(improved.RangeSigmaM <= result.RangeSigmaM * (1+1e-12) && improved.CrossRangeSigmaM <= result.CrossRangeSigmaM * (1+1e-12), "Added sensors worsen estimate: " + scenario.Id);
            }
        }
        foreach (double distance in new[] { .1,1,5,30,100,250 }) {
            var wreck = new Observation(distance, emEmission:0, knownHull:false, transponderBroadcasting:false);
            var result = ContactModel.Evaluate(packages[0].Sensors, wreck, settings);
            Check(result.HasEstimate && result.PositionRmsM <= Math.Max(1, distance*.021), "Starter passive navigation must remain practical at " + distance + " km");
        }
        var current = packages[3].Sensors;
        var beforeCut = ContactModel.Evaluate(current,new Observation(1396.261689440373),settings);
        var afterCut = ContactModel.Evaluate(current,new Observation(1396.263689440373),settings);
        Check(Math.Abs(beforeCut.PositionRmsM-afterCut.PositionRmsM)<1,"No uncertainty cliff at the native cutoff");
        var young = ContactModel.Evaluate(current,new Observation(1000),settings);
        var old = ContactModel.Evaluate(current,new Observation(1000,sampleAgeSeconds:60),settings);
        Check(old.PositionRmsM > young.PositionRmsM, "Old observations increase uncertainty");
        var fast = ContactModel.Evaluate(current,new Observation(1000,relativeSpeedMps:10000),settings);
        Check(young.PositionRmsM == fast.PositionRmsM, "Known steady speed must not itself impose error");
        Check(Math.Abs(young.RoundTripSeconds - 2e6/ContactModel.LightSpeedMps) < 1e-15 && Math.Abs(young.ObservationAgeSeconds-1e6/ContactModel.LightSpeedMps)<1e-15, "One-way observation age versus round-trip acquisition delay");
        var em = new[] {new SensorSpec(SensorKind.EM,500)};
        Check(TransponderReception.Received(em,new Observation(700)) && !TransponderReception.Received(em,new Observation(710)),"500 km EM packet boundary follows inverse-square propagation");
        Check(!TransponderReception.Received(current,new Observation(100000)),"No unlimited timestamp reception");
        Check(TransponderReception.Received(em,new Observation(750),.25) && !TransponderReception.Received(em,new Observation(750)),"Sensor skill threshold affects packet acquisition");
        Check(!TransponderReception.Received(current,new Observation(300,emEmission:100,transponderBroadcasting:false)),"Active sensor emissions do not manufacture transponder packets");
        Check(!TransponderReception.Received(new[]{new SensorSpec(SensorKind.Optical,500)},new Observation(300)),"Optical detection cannot receive timestamps");
        Check(!TransponderReception.Received(em,new Observation(300,occluded:true)),"Occlusion blocks packet acquisition");
        Check(!TransponderReception.Received(new[]{new SensorSpec(SensorKind.EM,500,on:false)},new Observation(300)),"Off receiver cannot acquire packets");
        Check((ContactModel.Evaluate(current,new Observation(100000),settings).Sources & MeasurementSources.Timestamp)==0,"Distant broadcasting target must lose timing range");
        var brightNoPacket=ContactModel.Evaluate(current,new Observation(1000,emEmission:100,transponderBroadcasting:false),settings);
        Check((brightNoPacket.Sources & MeasurementSources.Timestamp)==0,"Bright unrelated EM has no timestamp");
        double packetEdge=500*Math.Sqrt(TransponderReception.BroadcastEmission/TransponderReception.NormalThreshold);
        var packetBefore=ContactModel.Evaluate(current,new Observation(packetEdge*Math.Sqrt(5)-.001),settings);
        var packetAfter=ContactModel.Evaluate(current,new Observation(packetEdge*Math.Sqrt(5)+.001),settings);
        Check(Math.Abs(packetBefore.PositionRmsM-packetAfter.PositionRmsM)<1,"No uncertainty jump at packet loss");
        var emKnown = ContactModel.Evaluate(em,new Observation(500),settings);
        var emDark = ContactModel.Evaluate(em,new Observation(500,emEmission:0),settings);
        Check(emKnown.HasMeasuredRange && !emDark.HasEstimate, "Timestamp requires received emissions");
        var emNoTimestamp = ContactModel.Evaluate(em,new Observation(500,transponderBroadcasting:false),settings);
        Check(!emNoTimestamp.HasMeasuredRange && emNoTimestamp.RangeSigmaM > emKnown.RangeSigmaM, "Emitter bearing alone does not provide precise range");
        Check(!ContactModel.Evaluate(new[]{new SensorSpec(SensorKind.IR,600)},new Observation(30,emEmission:0),settings).HasEstimate, "Cold IR target gives no observation");
        Check(!ContactModel.Evaluate(current,new Observation(30,occluded:true),settings).HasEstimate, "Occlusion respected");
        Check(ContactModel.Evaluate(current,new Observation(30,knownPosition:true,occluded:true),settings).PositionRmsM == 0, "Known charted positions remain exact");
        Check(!ContactModel.Evaluate(Array.Empty<SensorSpec>(),new Observation(30),settings).HasEstimate, "No sensors gives no manufactured track");
        Check(!ContactModel.Evaluate(current.Select(s=>new SensorSpec(s.Kind,s.BaseRangeKm,s.Strength,false)).ToArray(),new Observation(30),settings).HasEstimate, "Off sensors contribute nothing");
        foreach (var package in packages) foreach (double distance in new[] {.1,30,100,119.999,120}) {
            var result = ContactModel.Evaluate(package.Sensors,new Observation(distance,emEmission:0,transponderBroadcasting:false),settings);
            Check(result.HasEstimate && result.PositionRmsM == 0,"Fresh combat contact must remain exact: " + package.Id);
        }
        var edge = ContactModel.Evaluate(current,new Observation(120.001),settings);
        Check(edge.PositionRmsM < 1e-6,"Combat safeguard must blend continuously");
        Check(ContactModel.Evaluate(current,new Observation(30,sampleAgeSeconds:60),settings).PositionRmsM > 0,"Close stale track must not become omniscient");
        var distant = ContactModel.Evaluate(packages[0].Sensors,new Observation(100000,knownHull:false,emEmission:0,transponderBroadcasting:false),settings);
        Check(distant.CrossRangeSigmaM / 1e8 < 5 * ContactModel.ArcsecondRadians,"Starter bearing should remain useful at 100,000 km");
        var interplanetaryTarget = new Observation(60000000,knownHull:false,emEmission:0,transponderBroadcasting:false);
        var interplanetary = ContactModel.Evaluate(current,interplanetaryTarget,settings);
        double bearingDegrees = interplanetary.CrossRangeSigmaM / 6e10 * 180/Math.PI;
        Check(bearingDegrees>.95 && bearingDegrees<1.05,"Current package should have about 1 degree bearing scatter at Mercury-Venus scale");
        Check(interplanetary.RangeSigmaM/interplanetary.CrossRangeSigmaM<6,"Reference interplanetary uncertainty should form an elongated cloud rather than a needle");
        var previousScatterSettings = new ModelSettings(true,opticalScatterArcsec:2160);
        var beforeScatter = ContactModel.Evaluate(current,interplanetaryTarget,previousScatterSettings);
        Check(interplanetary.CrossRangeSigmaM>beforeScatter.CrossRangeSigmaM*9.9,"Interplanetary cross-range scatter increases tenfold");
        foreach(double nearRange in new[]{30d,120,250,1000,100000,1000000}) {
            var nearTarget = new Observation(nearRange);
            var before = ContactModel.Evaluate(current,nearTarget,previousScatterSettings);
            var after = ContactModel.Evaluate(current,nearTarget,settings);
            Check(before.RangeSigmaM==after.RangeSigmaM && before.CrossRangeSigmaM==after.CrossRangeSigmaM,"Nearby values unchanged by distant scatter tuning");
        }
        var starterFar = ContactModel.Evaluate(packages[0].Sensors,interplanetaryTarget,settings);
        Check(starterFar.CrossRangeSigmaM>interplanetary.CrossRangeSigmaM*3,"Interplanetary optical upgrades retain a substantial bearing advantage");
        var scatterEdgeBefore=ContactModel.Evaluate(current,new Observation(999999,emEmission:0,transponderBroadcasting:false),settings);
        var scatterEdgeAfter=ContactModel.Evaluate(current,new Observation(1000001,emEmission:0,transponderBroadcasting:false),settings);
        Check(Math.Abs(scatterEdgeBefore.CrossRangeSigmaM-scatterEdgeAfter.CrossRangeSigmaM)<1,"Interplanetary scatter starts continuously");
        Console.WriteLine($"60 million km reference target: current bearing sigma {bearingDegrees:F4} degrees; cross-range {interplanetary.CrossRangeSigmaM/1000:F0} km; starter cross-range {starterFar.CrossRangeSigmaM/1000:F0} km.");
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-1d}) Check(!ContactModel.Evaluate(current,new Observation(bad),settings).HasEstimate,"Invalid input rejected");
        foreach (var package in packages) foreach (double range in new[]{999999999d,1000000001d,1494119044d,6e9,15e9}) {
            var outer=ContactModel.Evaluate(package.Sensors,new Observation(range,emEmission:0,transponderBroadcasting:false),settings);
            Check(outer.HasEstimate && double.IsFinite(outer.PositionRmsM),"Finite outer-system estimate: "+package.Id+" at "+range+" km");
        }
        Check(!ContactModel.Evaluate(current,new Observation(double.MaxValue),settings).HasEstimate,"Overflowing distance safely falls back");
        var optical = new[]{new SensorSpec(SensorKind.Optical,50)};
        Check(ContactModel.Evaluate(optical,new Observation(1000,knownHull:true,emEmission:0,transponderBroadcasting:false),settings).RangeSigmaM <=
            ContactModel.Evaluate(optical,new Observation(1000,knownHull:false,emEmission:0,transponderBroadcasting:false),settings).RangeSigmaM,"Known dimensions improve optical ranging");
        var one = ContactModel.Evaluate(em,new Observation(500),settings);
        var two = ContactModel.Evaluate(new[]{em[0],em[0]},new Observation(500),settings);
        var four = ContactModel.Evaluate(new[]{em[0],em[0],em[0],em[0]},new Observation(500),settings);
        Check(one.RangeSigmaM>two.RangeSigmaM && two.RangeSigmaM>four.RangeSigmaM && four.RangeSigmaM>=ContactModel.LightSpeedMps*settings.ClockSigmaSeconds*.999,"Duplicate receivers cannot remove shared clock uncertainty");
        Benchmark(current,settings);
        return count;
    }
    static void Benchmark(SensorSpec[] sensors, ModelSettings settings) {
        var target=new Observation(1000); double sum=0;
        for(int i=0;i<10000;i++)sum+=ContactModel.Evaluate(sensors,target,settings).PositionRmsM;
        long before=GC.GetAllocatedBytesForCurrentThread(), began=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=0;i<1000000;i++)sum+=ContactModel.Evaluate(sensors,target,settings).PositionRmsM;
        long elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-began, allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated==0,"Steady model evaluation allocated "+allocated+" bytes");
        Console.WriteLine($"1,000,000 evaluations of 20 channels: {elapsed*1000d/System.Diagnostics.Stopwatch.Frequency:F1} ms; {allocated} bytes after setup (.NET 8, not a Unity performance guarantee). Guard {sum:F1}");
    }
}
