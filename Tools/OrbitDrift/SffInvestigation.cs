using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

// Offline prototype only. Not linked into the game mod and does not patch Harmony.
static class KeplerCoast {
    static void Stumpff(double z,out double c,out double s) {
        if (z>.0001) { double q=Math.Sqrt(z);c=(1-Math.Cos(q))/z;s=(q-Math.Sin(q))/(q*q*q); }
        else if (z<-.0001) { double q=Math.Sqrt(-z);c=(Math.Cosh(q)-1)/(-z);s=(Math.Sinh(q)-q)/(q*q*q); }
        else { c=.5-z/24+z*z/720-z*z*z/40320;s=1d/6-z/120+z*z/5040-z*z*z/362880; }
    }
    internal static bool Propagate(State start,double seconds,double mu,out State result) {
        result=start;
        double r0=start.P.Length,velocitySquared=start.V.X*start.V.X+start.V.Y*start.V.Y;
        if (!(r0>0) || !(mu>0) || !double.IsFinite(seconds) || !double.IsFinite(velocitySquared)) return false;
        double dot=start.P.X*start.V.X+start.P.Y*start.V.Y;
        double alpha=2/r0-velocitySquared/mu,sqrtMu=Math.Sqrt(mu);
        // Remove whole elliptical revolutions without changing the position/velocity.
        if (alpha>1e-12) {
            double period=2*Math.PI/Math.Sqrt(mu*alpha*alpha*alpha);
            seconds%=period;
        }
        if (seconds==0) return true;
        double chi=sqrtMu*Math.Abs(alpha)*seconds;
        if (Math.Abs(alpha)<1e-10) chi=sqrtMu*seconds/r0;
        else if (alpha<0) {
            double sign=Math.Sign(seconds);
            double arg=-2*mu*alpha*seconds/(dot+sign*Math.Sqrt(-mu/alpha)*(1-r0*alpha));
            if (arg>0) chi=sign*Math.Sqrt(-1/alpha)*Math.Log(arg);
        }
        bool solved=false;
        double c=0,s=0;
        for (int i=0;i<80;i++) {
            double z=alpha*chi*chi;Stumpff(z,out c,out s);
            double f=dot/sqrtMu*chi*chi*c+(1-alpha*r0)*chi*chi*chi*s+r0*chi-sqrtMu*seconds;
            double derivative=dot/sqrtMu*chi*(1-z*s)+(1-alpha*r0)*chi*chi*c+r0;
            double delta=f/derivative;
            if (!double.IsFinite(delta)) return false;
            chi-=delta;
            if (Math.Abs(delta)<1e-10) { solved=true;break; }
        }
        if (!solved) return false;
        Stumpff(alpha*chi*chi,out c,out s);
        double fFinal=1-chi*chi/r0*c,g=seconds-chi*chi*chi/sqrtMu*s;
        V p=start.P*fFinal+start.V*g;
        double r=p.Length;
        if (!(r>0) || !double.IsFinite(r)) return false;
        double fdot=sqrtMu/(r*r0)*(alpha*chi*chi*chi*s-chi),gdot=1-chi*chi/r*c;
        V v=start.P*fdot+start.V*gdot;
        if (!double.IsFinite(v.X) || !double.IsFinite(v.Y)) return false;
        result=new(p,v);return true;
    }
}

static partial class Drift {
    internal static void SffStudy(string root) {
        int checks=0;
        void Check(bool ok,string name) { checks++;if (!ok) throw new Exception(name); }
        const double testMu=320000;
        // Independent RK4 reference and invariants: circular, eccentric, parabolic and escaping cases.
        foreach (double speedFactor in new[]{.6,1d,Math.Sqrt(2),1.6}) {
            State startCase=new(new(10000,0),new(0,Math.Sqrt(testMu/10000)*speedFactor));
            Check(KeplerCoast.Propagate(startCase,6000,testMu,out State propagated),"Universal propagation failed");
            State reference=Integrate(startCase,0,6000,.1,(_,p)=>Gravity(p,testMu));
            Check((propagated.P-reference.P).Length<.0001,"Universal coast differs from RK4 position");
            Check((propagated.V-reference.V).Length<1e-7,"Universal coast differs from RK4 velocity");
            Check(KeplerCoast.Propagate(propagated,-6000,testMu,out State reversed) && (reversed.P-startCase.P).Length<.00001,"Coast not reversible");
        }
        var random=new Random(1947);
        for (int i=0;i<1000;i++) {
            double r=7000+random.NextDouble()*100000,angle=random.NextDouble()*Math.PI*2;
            double speed=Math.Sqrt(testMu/r)*(.55+random.NextDouble()*1.1);
            V p=new(r*Math.Cos(angle),r*Math.Sin(angle));
            V v=new(-speed*Math.Sin(angle),speed*Math.Cos(angle));
            State state=new(p,v);
            double dt=random.NextDouble()*86400;
            Check(KeplerCoast.Propagate(state,dt,testMu,out var next),"Random coast failed");
            double e0=speed*speed/2-testMu/r,e1=next.V.Length*next.V.Length/2-testMu/next.P.Length;
            double h0=p.X*v.Y-p.Y*v.X,h1=next.P.X*next.V.Y-next.P.Y*next.V.X;
            Check(Math.Abs(e0-e1)<1e-8 && Math.Abs((h0-h1)/h0)<1e-9,"Coast changed energy/angular momentum");
        }
        JsonNode j=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"VORB-BOUND-CURRENT.json")))!;
        var bodies=j["data"]!["bodies"]!;
        Orbit sol=Orbit.Parse(bodies["Sol"]!,null),venus=Orbit.Parse(bodies["Venus"]!,sol),vorb=Orbit.Parse(bodies["VORB"]!,venus);
        var m=j["data"]!["ships"]!.AsArray().First(x=>x!["id"]!.GetValue<string>()=="M-5N4")!["motion"]!;
        double epoch=D(m,"captureEpoch");var planet=venus.At(epoch);
        State start=new(new V(D(m,"xAU"),D(m,"yAU"))*Orbit.AU-planet.P,new V(D(m,"vxAUPerSecond"),D(m,"vyAUPerSecond"))*Orbit.AU-planet.V);
        double mu=G*venus.Mass,radius=D(bodies["Venus"]!,"radiusAU")*Orbit.AU;
        var cases=new List<object>();
        foreach (double hours in new[]{1d,2d,6d,24d,168d}) {
            double seconds=hours*3600;
            Check(KeplerCoast.Propagate(start,seconds,mu,out var coast),"Captured state coast failed");
            var reference=Integrate(start,epoch,epoch+seconds,1,(_,p)=>Gravity(p,mu));
            var endPlanet=venus.At(epoch+seconds);var endVorb=vorb.At(epoch+seconds);
            cases.Add(new { skipHours=hours, vanillaSingleStepOrbit=Elements(NativeStep(start,epoch,seconds,venus,mu),mu,radius),
                proposedCoastOrbit=Elements(coast,mu,radius),
                independentRk4PositionErrorMeters=(coast.P-reference.P).Length*1000,
                independentRk4VelocityErrorMps=(coast.V-reference.V).Length*1000,
                distanceFromVorbKm=(coast.P-(endVorb.P-endPlanet.P)).Length });
        }
        const int repetitions=10000;
        var timer=Stopwatch.StartNew();
        double checksum=0;
        for (int i=0;i<repetitions;i++) { KeplerCoast.Propagate(start,21600+i*.01,mu,out var coast);checksum+=coast.P.X; }
        timer.Stop();
        string output=Path.Combine(root,"VORB-DRIFT-ANALYSIS");
        var report=new { checksPassed=checks,benchmark=new { iterations=repetitions,totalMs=timer.Elapsed.TotalMilliseconds,
            microsecondsPerCoast=timer.Elapsed.TotalMilliseconds*1000/repetitions,checksum,
            note="Offline .NET 8 solver-only timing; excludes Unity/Mono, body selection, ephemeris evaluation, collision/event detection and bookkeeping." },
            initialOrbit=Elements(start,mu,radius),cases,
            scope="Offline prototype; no Harmony SFF patch installed. Applicable to unpowered, atmosphere-free coasting within one gravity domain. A successful endpoint does not imply a collision-free arc." };
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true });
        File.WriteAllText(Path.Combine(output,"sff-study.json"),json);Console.WriteLine(json);
    }
}
