using System.Text.Json;
using System.Text.Json.Nodes;

static partial class Drift {
    sealed class NormalStats {
        internal string Id="";
        internal int Samples,Powered,Locked,Analytical,Changes;
        internal double FirstEpoch,LastEpoch,FirstEnergy,LastEnergy,MinEnergy,MaxEnergy,FirstPeri,LastPeri,FirstApo,LastApo,FirstRange,LastRange;
        internal double MinimumAltitude,MinimumEpoch,LastAltitude,FirstRadialSpeed,LastRadialSpeed;
        internal bool LastLocked,LastAnalytical;
    }
    internal static void NormalTest(string root,double? cutoff=null,string? directory=null) {
        directory??=Path.Combine(root,"Orbital/LiveTest");
        string path=Path.Combine(directory,"NORMAL-16X.jsonl");
        var stats=new Dictionary<string,NormalStats>();
        Orbit? venus=null,vorb=null;
        double mu=0,radius=0;
        int rows=0,errors=0,partial=0;
        JsonNode? lastOrbital=null;
        var primarySamples=new List<object>();
        double N(JsonNode? j,string key) => j is JsonObject obj && obj[key]!=null?obj[key]!.GetValue<double>():0;
        bool B(JsonNode? j,string key) => j is JsonObject obj && obj[key]!=null && obj[key]!.GetValue<bool>();
        void Read(string id,double epoch,JsonNode motion,bool nav) {
            if (venus==null || vorb==null) return;
            if (cutoff.HasValue && epoch>=cutoff.Value) return;
            V p=nav?Position(motion):new V(N(motion,"xAU"),N(motion,"yAU"))*Orbit.AU;
            V v=nav?Velocity(motion):new V(N(motion,"vxAUPerSecond"),N(motion,"vyAUPerSecond"))*Orbit.AU;
            Ephemeris center=venus.At(epoch,true),station=vorb.At(epoch,true);
            center=center with { V=(venus.At(epoch+32,true).P-venus.At(epoch-32,true).P)*(1d/64) };
            State state=new(p-center.P,v-center.V);
            var elements=JsonSerializer.SerializeToNode(Elements(state,mu,radius))!;
            double energy=D(elements,"specificEnergyKm2PerS2"),peri=D(elements,"periapsisAltitudeKm"),apo=elements["apoapsisAltitudeKm"]?.GetValue<double>()??double.PositiveInfinity;
            double altitude=state.P.Length-radius,radialSpeed=(state.P.X*state.V.X+state.P.Y*state.V.Y)/state.P.Length;
            if (id=="M-5N4") primarySamples.Add(new { epoch,altitudeKm=altitude,energyKm2S2=energy,radialSpeedKmPerS=radialSpeed });
            if (!double.IsFinite(energy)) return;
            bool locked=B(motion,"bodyOrbitLocked"),analytical=B(motion,"orbitLocked");
            if (!stats.TryGetValue(id,out var s)) {
                s=new NormalStats { Id=id,FirstEpoch=epoch,FirstEnergy=energy,MinEnergy=energy,MaxEnergy=energy,
                    FirstPeri=peri,FirstApo=apo,FirstRange=(p-station.P).Length,LastLocked=locked,LastAnalytical=analytical };
                s.MinimumAltitude=altitude;s.MinimumEpoch=epoch;s.FirstRadialSpeed=radialSpeed;
                stats[id]=s;
            }
            s.Samples++;s.LastEpoch=epoch;s.LastEnergy=energy;s.MinEnergy=Math.Min(s.MinEnergy,energy);s.MaxEnergy=Math.Max(s.MaxEnergy,energy);
            s.LastPeri=peri;s.LastApo=apo;s.LastRange=(p-station.P).Length;
            s.LastAltitude=altitude;s.LastRadialSpeed=radialSpeed;
            if (altitude<s.MinimumAltitude) { s.MinimumAltitude=altitude;s.MinimumEpoch=epoch; }
            if (locked) s.Locked++;if (analytical) s.Analytical++;
            if (locked!=s.LastLocked || analytical!=s.LastAnalytical) s.Changes++;
            s.LastLocked=locked;s.LastAnalytical=analytical;
            var rcs=motion[nav?"rcsAccelerationRaw":"rcsAcceleration"];
            var input=motion[nav?"inputAccelerationRaw":"inputAcceleration"];
            double ax=N(rcs,"x")+N(input,"x"),ay=N(rcs,"y")+N(input,"y");
            if (Math.Sqrt(ax*ax+ay*ay)*Orbit.AU*1000>.001) s.Powered++;
        }
        using (var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using (var reader=new StreamReader(stream)) {
            while (reader.ReadLine() is string line) {
                JsonNode row;
                try { row=JsonNode.Parse(line)!; } catch { partial++;continue; }
                rows++;
                if (row["error"]!=null) { errors++;continue; }
                var ex=row["excursion"]!;var data=ex["data"]!;
                if (venus==null) {
                    var bodies=data["bodies"]!;
                    var sun=Orbit.Parse(bodies["Sol"]!,null);venus=Orbit.Parse(bodies["Venus"]!,sun);vorb=Orbit.Parse(bodies["VORB"]!,venus);
                    mu=G*venus.Mass;radius=D(bodies["Venus"]!,"radiusAU")*Orbit.AU;
                }
                foreach (var ship in data["ships"]!.AsArray()) {
                    if (ship?["motion"] is not JsonObject motion) continue;
                    Read(ship["id"]!.GetValue<string>(),N(motion,"captureEpoch"),motion,false);
                }
                foreach (var ship in row["ships"]!.AsArray()) {
                    if (ship?["motion"] is not JsonObject motion) continue;
                    Read(ship["id"]!.GetValue<string>(),N(ship,"epoch"),motion,true);
                }
                lastOrbital=row["orbital"]!["data"]!.DeepClone();
            }
        }
        var items=stats.Values.Select(s=>new {
            id=s.Id,samples=s.Samples,elapsedGameMinutes=(s.LastEpoch-s.FirstEpoch)/60,
            poweredSamples=s.Powered,bodyLockedSamples=s.Locked,analyticalSamples=s.Analytical,modeChanges=s.Changes,
            firstEnergyKm2S2=s.FirstEnergy,lastEnergyKm2S2=s.LastEnergy,
            energyChangePercent=(s.LastEnergy-s.FirstEnergy)/Math.Abs(s.FirstEnergy)*100,
            energyRangePercent=(s.MaxEnergy-s.MinEnergy)/Math.Abs(s.FirstEnergy)*100,
            firstPeriapsisAltitudeKm=s.FirstPeri,lastPeriapsisAltitudeKm=s.LastPeri,
            firstApoapsisAltitudeKm=double.IsFinite(s.FirstApo)?(double?)s.FirstApo:null,lastApoapsisAltitudeKm=double.IsFinite(s.LastApo)?(double?)s.LastApo:null,
            firstVorbRangeKm=s.FirstRange,lastVorbRangeKm=s.LastRange });
        var passage=stats.Values.Select(s=>new { id=s.Id,minimumAltitudeKm=s.MinimumAltitude,minimumEpoch=s.MinimumEpoch,
            minimumAfterGameMinutes=(s.MinimumEpoch-s.FirstEpoch)/60,lastAltitudeKm=s.LastAltitude,
            firstRadialSpeedKmPerS=s.FirstRadialSpeed,lastRadialSpeedKmPerS=s.LastRadialSpeed });
        var report=new { input=path,actualRequestedSpeed="Initially 8x, then 16x; filename retained from initial proposal",cutoffEpoch=cutoff,rows,errors,partialLines=partial,
            capturedUtc=DateTime.UtcNow,ships=items,orbital=lastOrbital,
            periapsisPassages=passage,
            primarySamples,
            note="Elements use the native Newton solver tolerance for same-epoch Venus coordinates, a 64-second centered estimate of the coordinate curve's velocity, and the game's numerical gravity constant. This avoids both the native stale velocity cache and the tighter analytical solver's coordinate bias. Powered or locked objects are not energy-conservation tests. Stored ship positions can have frame-phase timing differences." };
        string output=Path.Combine(directory,cutoff.HasValue?"ANALYSIS-BEFORE-DOCK.json":"ANALYSIS.json");
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine(output);
    }
}
