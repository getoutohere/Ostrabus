using System.Text.Json;
using System.Text.Json.Nodes;

static partial class Drift {
    record CoastSample(double Epoch,State World,double Step,int Frame);
    internal static void TimestepStudy(string root) {
        const double cutoff=65631772606.059853;
        var samples=new List<CoastSample>();
        Orbit? venus=null;
        double radius=0;
        foreach(string line in File.ReadLines(Path.Combine(root,"Orbital/LiveTest/NORMAL-16X.jsonl"))) {
            JsonNode row=JsonNode.Parse(line)!;
            if (row["error"]!=null) continue;
            var ex=row["excursion"]!;var data=ex["data"]!;
            if (venus==null) {
                var bodies=data["bodies"]!;var sun=Orbit.Parse(bodies["Sol"]!,null);venus=Orbit.Parse(bodies["Venus"]!,sun);
                radius=D(bodies["Venus"]!,"radiusAU")*Orbit.AU;
            }
            foreach(var ship in data["ships"]!.AsArray()) {
                if (ship?["id"]?.GetValue<string>()!="M-5N4" || ship["motion"]==null) continue;
                var m=ship["motion"]!;double t=D(m,"captureEpoch");
                if (t>=cutoff || samples.Count>0 && t==samples[samples.Count-1].Epoch) continue;
                samples.Add(new CoastSample(t,new State(new V(D(m,"xAU"),D(m,"yAU"))*Orbit.AU,
                    new V(D(m,"vxAUPerSecond"),D(m,"vyAUPerSecond"))*Orbit.AU),
                    D(row["orbital"]!["data"]!,"lastWorldStepSeconds"),data["frame"]!.GetValue<int>()));
            }
        }
        if (venus==null || samples.Count<2) throw new Exception("No live free-coast interval");
        double mu=G*venus.Mass;
        Ephemeris Center(double t,bool game) {
            var c=venus.At(t,game);
            if (game) c=c with { V=(venus.At(t+32,true).P-venus.At(t-32,true).P)*(1d/64) };
            return c;
        }
        State Relative(State w,double t,bool game) { var c=Center(t,game);return new(w.P-c.P,w.V-c.V); }
        double Energy(State s) => s.V.X*s.V.X/2+s.V.Y*s.V.Y/2-mu/s.P.Length;
        var first=samples[0];var last=samples[samples.Count-1];
        var oldStart=Relative(first.World,first.Epoch,false);var oldEnd=Relative(last.World,last.Epoch,false);
        var start=Relative(first.World,first.Epoch,true);var end=Relative(last.World,last.Epoch,true);
        double energy0=Energy(start);
        double largestBias=0,firstBias=0,lastBias=0;
        foreach(var sample in samples) {
            double bias=(venus.At(sample.Epoch,true).P-venus.At(sample.Epoch).P).Length;
            if (sample==first) firstBias=bias;if (sample==last) lastBias=bias;
            largestBias=Math.Max(largestBias,bias);
        }
        V Force(V world,double t,bool single) {
            V delta=venus.At(t,true).P-world;
            V g=single?NativeGravity(delta,venus.Mass):Gravity(default(V)-delta,mu);
            V parent=venus.At(t).A;
            if (!single) return g+parent;
            return new((float)(g.X/Orbit.AU+parent.X/Orbit.AU)*Orbit.AU,(float)(g.Y/Orbit.AU+parent.Y/Orbit.AU)*Orbit.AU);
        }
        var cases=new List<object>();
        foreach(string method in new[]{"native-end-float","native-end-double","native-start-float","relative-frozen-float","velocity-verlet-float","velocity-verlet-double"}) {
            foreach(double maxStep in new[]{.05,.1,.25,.5,1d,2d,5.333333333333333}) {
                State state=method.StartsWith("relative")?start:first.World;
                double elapsed=0,total=last.Epoch-first.Epoch;
                while(elapsed<total) {
                    double t=first.Epoch+elapsed,step=Math.Min(maxStep,total-elapsed);
                    step=(t+step)-t;
                    if (step<=0) break;
                    if (method.StartsWith("relative")) {
                        V a=NativeGravity(default(V)-state.P,venus.Mass);
                        state=new(state.P+state.V*step+a*(.5*step*step),state.V+a*step);
                    } else if (method.StartsWith("velocity-verlet")) {
                        bool single=method.EndsWith("float");
                        V a=Force(state.P,t,single);
                        V p=state.P+state.V*step+a*(.5*step*step);
                        V b=Force(p,t+step,single);
                        state=new(p,state.V+(a+b)*(.5*step));
                    } else {
                        double forceTime=method.StartsWith("native-end")?t+step:t;
                        V a=Force(state.P,forceTime,method.EndsWith("float"));
                        state=new(state.P+state.V*step+a*(.5*step*step),state.V+a*step);
                    }
                    elapsed+=step;
                }
                State rel=method.StartsWith("relative")?state:Relative(state,last.Epoch,true);
                State world=method.StartsWith("relative")?new State(Center(last.Epoch,true).P+state.P,Center(last.Epoch,true).V+state.V):state;
                cases.Add(new { method,stepSeconds=maxStep,energyChangePercent=(Energy(rel)-energy0)/Math.Abs(energy0)*100,
                    positionResidualToLiveKm=(world.P-last.World.P).Length,velocityResidualToLiveMps=(world.V-last.World.V).Length*1000,
                    finalOrbit=Elements(rel,mu,radius) });
            }
        }
        // Reproduce the public diagnostic calculation using actual recorded body
        // positions as an independent check of the loose-solver coordinate offset.
        double maximumGameModelErrorMeters=0;
        foreach(string line in File.ReadLines(Path.Combine(root,"Orbital/LiveTest/NORMAL-16X.jsonl"))) {
            var row=JsonNode.Parse(line)!;var b=row["excursion"]?["data"]?["bodies"]?["Venus"];
            if (b==null) continue;
            V p=new(D(b,"xAU")*Orbit.AU,D(b,"yAU")*Orbit.AU);
            maximumGameModelErrorMeters=Math.Max(maximumGameModelErrorMeters,(venus.At(D(b,"positionCalculationEpoch"),true).P-p).Length*1000);
        }
        var report=new {
            sampleCount=samples.Count,gameMinutes=(last.Epoch-first.Epoch)/60,
            sampledStepSeconds=new { min=samples.Min(s=>s.Step),mean=samples.Average(s=>s.Step),max=samples.Max(s=>s.Step),
                note="Only the timestep present at each polling sample; not the full per-frame timestep history." },
            referenceAudit=new { firstPositionBiasKm=firstBias,lastPositionBiasKm=lastBias,maximumPositionBiasKm=largestBias,
                nativeEphemerisReconstructionErrorMeters=maximumGameModelErrorMeters,
                previousEnergyChangePercent=(Energy(oldEnd)-Energy(oldStart))/Math.Abs(Energy(oldStart))*100,
                correctedEnergyChangePercent=(Energy(end)-energy0)/Math.Abs(energy0)*100,
                oldStartOrbit=Elements(oldStart,mu,radius),oldEndOrbit=Elements(oldEnd,mu,radius),
                correctedStartOrbit=Elements(start,mu,radius),correctedEndOrbit=Elements(end,mu,radius) },
            cases,
            limits=new[]{"Constant-step experiments isolate error terms; actual timestep history was not recorded, so these are not exact replay of the live run.",
                "Native gravity uses loose-solver ephemeris positions; the Orbital correction uses the analytical parent acceleration, matching the installed patch.",
                "Corrected observed energy uses the native coordinate curve and a 64-second centered estimate of the parent's velocity, avoiding its stale velocity cache.",
                "No game library code is changed by this offline study."}
        };
        string output=Path.Combine(root,"Orbital/LiveTest/TIMESTEP-STUDY.json");
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine(output);
    }
}
