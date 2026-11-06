using System.Text.Json;
using System.Text.Json.Nodes;

static partial class Drift {
    static object Elements(State s, double mu, double radius) {
        double r = s.P.Length, v = s.V.Length, dot = s.P.X*s.V.X+s.P.Y*s.V.Y;
        double energy = v*v/2-mu/r;
        V eccVector = (s.P*(v*v-mu/r)-s.V*dot)*(1/mu);
        double e = eccVector.Length;
        double h = s.P.X*s.V.Y-s.P.Y*s.V.X, p = h*h/mu;
        double peri = p/(1+e);
        double? apo = energy < 0 ? p/(1-e) : null;
        double? axis = energy < 0 ? -mu/(2*energy) : null;
        return new { radiusKm = r, altitudeKm = r-radius, speedKmPerS = v,
            escapeSpeedKmPerS = Math.Sqrt(2*mu/r), specificEnergyKm2PerS2 = energy,
            bound = energy < 0, eccentricity = e, periapsisRadiusKm = peri, periapsisAltitudeKm = peri-radius,
            apoapsisRadiusKm = apo, apoapsisAltitudeKm = apo-radius,
            periodHours = axis.HasValue ? (double?)(2*Math.PI*Math.Sqrt(Math.Pow(axis.Value,3)/mu)/3600) : null };
    }
    internal static void Binding(string root) {
        JsonNode Load(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(root,file)))!;
        JsonNode current = Load("VORB-BOUND-CURRENT.json");
        JsonNode bodies = current["data"]!["bodies"]!;
        Orbit sol = Orbit.Parse(bodies["Sol"]!,null), venus = Orbit.Parse(bodies["Venus"]!,sol), station = Orbit.Parse(bodies["VORB"]!,venus);
        double mu = G*venus.Mass, radius = D(bodies["Venus"]!,"radiusAU")*Orbit.AU;
        var states = new List<object>();
        State Relative(JsonNode motion, double epoch) {
            var planet = venus.At(epoch);
            return new(Position(motion)-planet.P, Velocity(motion)-planet.V);
        }
        JsonNode initial = Load("VORB-DRIFT-INITIAL.json");
        states.Add(new { name = "before-salvage", epoch = D(initial,"epoch"), orbit = Elements(Relative(initial["motion"]!,D(initial,"epoch")),mu,radius) });
        JsonNode after = Load("VORB-DRIFT-M-5N4.json");
        states.Add(new { name = "after-salvage", epoch = D(after,"epoch"), orbit = Elements(Relative(after["data"]!["navigation"]!["currentMotion"]!,D(after,"epoch")),mu,radius) });
        JsonNode watched = current["data"]!["ships"]!.AsArray().First(x=>x!["id"]!.GetValue<string>()=="M-5N4")!["motion"]!;
        double t0 = D(watched,"captureEpoch");
        var planet0=venus.At(t0);
        State start = new(new V(D(watched,"xAU"),D(watched,"yAU"))*Orbit.AU-planet0.P,
            new V(D(watched,"vxAUPerSecond"),D(watched,"vyAUPerSecond"))*Orbit.AU-planet0.V);
        states.Add(new { name = "current", epoch=t0, orbit = Elements(start,mu,radius) });
        var currentStation=station.At(t0);
        State stationState=new(currentStation.P-planet0.P,currentStation.V-planet0.V);
        states.Add(new { name = "VORB-reference", epoch=t0, orbit = Elements(stationState,mu,radius) });
        var predictions = new List<object>();
        double horizon = 7*86400;
        Func<double,V,V> coherent = (_,r)=>Gravity(r,mu);
        Func<double,V,V> strongest = (t,r)=> {
            var planet=venus.At(t);
            V sun=Gravity(planet.P+r,G*sol.Mass), local=Gravity(r,mu);
            return (sun.Length>local.Length?sun:local)-planet.A;
        };
        Func<double,V,V> sum = (t,r)=> {
            var planet=venus.At(t);
            return Gravity(r,mu)+Gravity(planet.P+r,G*sol.Mass)-planet.A;
        };
        foreach (var model in new[] { ("consistent-two-body",coherent), ("strongest-body-force-law",strongest), ("sun-plus-venus-force-law",sum) }) {
            foreach (double dt in new[] { 4d, 1d }) predictions.Add(Forecast(model.Item1,"RK4",start,t0,horizon,dt,model.Item2,venus,station,radius,mu,false));
        }
        // Reproduce StarSystem.Update's ordering: advance planet to end epoch, then
        // sample gravity at the ship's OLD absolute position; ShipSitu holds that
        // acceleration constant for its position and velocity update.
        foreach (double dt in new[] { .1, .25, 1d, 4d, 16d, 64d, 3600d, 7200d, 21600d })
            predictions.Add(Forecast("strongest-body-game-step","constant-acceleration/end-epoch-body",start,t0,horizon,dt,strongest,venus,station,radius,mu,true));
        var liveChecks = new List<object>();
        string liveFile=Path.Combine(root,"VORB-BOUND-VALIDATION.json");
        if (File.Exists(liveFile)) {
            var live=Load("VORB-BOUND-VALIDATION.json");
            var m=live["data"]!["ships"]!.AsArray().First(x=>x!["id"]!.GetValue<string>()=="M-5N4")!["motion"]!;
            double end=D(m,"captureEpoch");var planet=venus.At(end);
            State actual=new(new V(D(m,"xAU"),D(m,"yAU"))*Orbit.AU-planet.P,new V(D(m,"vxAUPerSecond"),D(m,"vyAUPerSecond"))*Orbit.AU-planet.V);
            foreach (var model in new[] { ("consistent-two-body",coherent), ("strongest-body-force-law",strongest) }) {
                var predicted=Integrate(start,t0,end,.25,model.Item2);
                liveChecks.Add(new { model=model.Item1, elapsedGameSeconds=end-t0,
                    positionErrorKm=(predicted.P-actual.P).Length, velocityErrorMps=(predicted.V-actual.V).Length*1000,
                    observedOrbit=Elements(actual,mu,radius) });
            }
        }
        var report = new { ship="M-5N4", capturedEpoch=t0, venusRadiusKm=radius,
            assumptions=new[] { "No thrust, collisions, atmosphere, relocking or UI orbital-mode transitions; stop at Venus surface or 1,000,000 km from Venus.",
                "Predicts Venus/Sun gravity only; strongest-body force law selects the stronger of Venus and Sol at each evaluation, matching the relevant local body choices.",
                "Native-step runs reproduce float gravity and constant-acceleration scheduling at stated FIXED simulated timesteps, not recorded actual Unity frame times.",
                "Stored ship coordinates lack their own integration timestamp; same-epoch analytical Venus position/velocity is used as reference." },
            states, horizonDays=7, predictions, liveChecks };
        string output=Path.Combine(root,"VORB-DRIFT-ANALYSIS"); Directory.CreateDirectory(output);
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true });
        File.WriteAllText(Path.Combine(output,"binding.json"),json);
        Console.WriteLine(json);
    }
    internal static void BindingTests() {
        const double mu=320000,r=10000;
        var circular=JsonSerializer.SerializeToNode(Elements(new(new(r,0),new(0,Math.Sqrt(mu/r))),mu,6000))!;
        if (!circular["bound"]!.GetValue<bool>() || Math.Abs(D(circular,"periapsisRadiusKm")-r)>1e-8 || Math.Abs(D(circular,"apoapsisRadiusKm")-r)>1e-8) throw new Exception("Circular elements failed");
        var hyperbolic=JsonSerializer.SerializeToNode(Elements(new(new(r,0),new(0,Math.Sqrt(2*mu/r)*1.1)),mu,6000))!;
        if (hyperbolic["bound"]!.GetValue<bool>() || hyperbolic["apoapsisRadiusKm"]!=null || D(hyperbolic,"eccentricity")<=1) throw new Exception("Escape classification failed");
        var elliptic=new Orbit { Axis=12000,Minor=12000*Math.Sqrt(1-.45*.45),Ecc=.45,Period=15000,Direction=1 };
        var state=elliptic.At(1234);
        var elements=JsonSerializer.SerializeToNode(Elements(new(state.P,state.V),elliptic.Mu,6000))!;
        if (Math.Abs(D(elements,"periapsisRadiusKm")-12000*.55)>1e-7 || Math.Abs(D(elements,"apoapsisRadiusKm")-12000*1.45)>1e-7) throw new Exception("Eccentric elements failed");
        Console.WriteLine("Bound/unbound energy and orbital-element checks passed.");
    }
    static V NativeGravity(V deltaToCenter, double mass) {
        float dx=(float)(deltaToCenter.X/Orbit.AU), dy=(float)(deltaToCenter.Y/Orbit.AU);
        float squared=dx*dx+dy*dy;
        if (squared==0) return default;
        float factor=(float)((double)(float)2e-44*mass*Math.Pow(squared,-1.5));
        return new((float)(dx*factor)*Orbit.AU,(float)(dy*factor)*Orbit.AU);
    }
    static State NativeStep(State state,double t,double dt,Orbit venus,double mu) {
        var planet=venus.At(t); var nextPlanet=venus.At(t+dt);
        V worldPos=planet.P+state.P, worldVel=planet.V+state.V;
        V planetDelta=nextPlanet.P-worldPos;
        V sunDelta=default(V)-worldPos;
        double sunMu=G*venus.Parent!.Mass;
        V accel=mu/Math.Pow(planetDelta.Length,2)>sunMu/Math.Pow(sunDelta.Length,2)?NativeGravity(planetDelta,venus.Mass):NativeGravity(sunDelta,venus.Parent.Mass);
        return new(worldPos+worldVel*dt+accel*(.5*dt*dt)-nextPlanet.P,worldVel+accel*dt-nextPlanet.V);
    }
    static object Forecast(string model,string integrator,State start,double t0,double horizon,double dt,Func<double,V,V> accel,Orbit venus,Orbit station,double radius,double mu,bool native) {
        State state=start;
        double t=t0,minR=state.P.Length,maxR=minR,minEnergy=double.PositiveInfinity,maxEnergy=double.NegativeInfinity;
        double? firstPositive=null;
        string status="completed";
        while (t<t0+horizon) {
            double step=Math.Min(dt,t0+horizon-t);
            state=native?NativeStep(state,t,step,venus,mu):Advance(state,t,step,accel);
            t+=step;
            double r=state.P.Length, energy=state.V.Length*state.V.Length/2-mu/r;
            minR=Math.Min(minR,r);maxR=Math.Max(maxR,r);
            minEnergy=Math.Min(minEnergy,energy);maxEnergy=Math.Max(maxEnergy,energy);
            if (energy>=0 && firstPositive==null) firstPositive=(t-t0)/3600;
            if (r<=radius) { status="surface-crossing";break; }
            if (r>=1000000) { status="reached-1-gigameter";break; }
        }
        var planetEnd=venus.At(t); var stationEnd=station.At(t);
        return new { model, integrator, stepSeconds=dt, elapsedHours=(t-t0)/3600,status,
            minimumAltitudeKm=minR-radius,maximumRadiusKm=maxR,
            minimumSpecificEnergyKm2PerS2=minEnergy,maximumSpecificEnergyKm2PerS2=maxEnergy,
            firstNonnegativeEnergyHours=firstPositive, finalRangeFromVorbKm=(state.P-(stationEnd.P-planetEnd.P)).Length,
            finalOrbit=Elements(state,mu,radius) };
    }
}
