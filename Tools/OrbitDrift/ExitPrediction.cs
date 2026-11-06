using System.Text.Json;
using System.Text.Json.Nodes;

static partial class Drift {
    internal static void ExitPrediction(string root,string? capturedStart=null) {
        string dir=Path.Combine(root,"Orbital/LiveTest/SFF-VENUS-EXIT");
        string input=capturedStart??Path.Combine(dir,"BASELINE.json");
        var baseline=JsonNode.Parse(File.ReadAllText(input))!;
        var bodies=baseline["excursion"]!["data"]!["bodies"]!;
        var sun=Orbit.Parse(bodies["Sol"]!,null);var venus=Orbit.Parse(bodies["Venus"]!,sun);
        var motion=baseline["player"]!["data"]!["navigation"]!["currentMotion"]!;
        State initial=new(Position(motion),Velocity(motion));
        double epoch=D(baseline["player"]!,"epoch"),duration=3600,mu=G*venus.Mass,radius=D(bodies["Venus"]!,"radiusAU")*Orbit.AU;
        Orbit Select(double t,V p) => venus.Mass/Math.Pow((p-venus.At(t).P).Length,2)>sun.Mass/Math.Pow((p-sun.At(t).P).Length,2)?venus:sun;
        V Force(double t,V p) { var body=Select(t,p);var center=body.At(t);return Gravity(p-center.P,G*body.Mass)+center.A; }
        var c0=venus.At(epoch);var rel=new State(initial.P-c0.P,initial.V-c0.V);
        var predicted=Integrate(initial,epoch,epoch+duration,1,Force);
        var finer=Integrate(initial,epoch,epoch+duration,.25,Force);
        var end=venus.At(epoch+duration);var r=finer.P-end.P;var v=finer.V-end.V;
        double initialRadial=(rel.P.X*rel.V.X+rel.P.Y*rel.V.Y)/rel.P.Length;
        var report=new { source=input,startEpoch=epoch,endEpoch=epoch+duration,durationSeconds=duration,
            initialOrbit=Elements(rel,mu,radius),initialRadialSpeedKmPerS=initialRadial,
            predictedEndDistanceFromVenusKm=r.Length,predictedEndAltitudeKm=r.Length-radius,
            predictedEndRelativeSpeedKmPerS=v.Length,predictedEndGravityBody=ReferenceEquals(Select(epoch+duration,finer.P),venus)?"Venus":"Sol",
            predictedPositionKm=new{x=finer.P.X,y=finer.P.Y},predictedVelocityKmPerS=new{x=finer.V.X,y=finer.V.Y},
            rk4ConvergenceMeters=(finer.P-predicted.P).Length*1000,
            note="Independent RK4 reference for unpowered, unlocked, no-NavData flight; strongest of Venus/Sol plus selected-frame acceleration, using the current SFF tight-ephemeris convention. No atmospheric forces assumed: check that the predicted path stays outside the atmosphere. Requires unchanged starting state before the jump." };
        File.WriteAllText(Path.Combine(dir,capturedStart==null?"PREDICTION.json":"EXACT-START-REFERENCE.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}
