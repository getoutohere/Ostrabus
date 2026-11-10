using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;

namespace Ostrabus.ShipNavigation.Collisions;

internal static class PhysicalCollisions {
    internal static ConfigEntry<bool> Flag;
    internal static bool Enabled=>Flag?.Value??true;
    const double MetresPerAU=1/HullZoom.MetresToAU,MaxHistorySeconds=120;
    sealed class Track {
        internal Sample Previous,Latest;
        internal bool Initialized,HasPrevious;
    }
    internal readonly struct Sample {
        internal readonly double Epoch,X,Y,Angle,VX,VY;
        internal readonly float Radius;
        internal readonly int Grid;
        internal readonly bool Docked;
        internal Sample(Ship ship,double epoch) {
            var situ=ship.objSS;Epoch=epoch;X=situ.vPosx;Y=situ.vPosy;VX=situ.vVelX;VY=situ.vVelY;
            Grid=ship.nGridRotation;Radius=situ.Size;Angle=situ.fRot-Grid*Math.PI/180;Docked=ship.bDocked;
        }
    }
    internal readonly struct Prepared {
        internal readonly Ship A,B;
        internal readonly HullContact.Contact Contact;
        internal Prepared(Ship a,Ship b,HullContact.Contact contact){A=a;B=b;Contact=contact;}
    }
    internal struct ScanState {internal Prepared Previous;}
    internal struct ResponseState {internal bool Active;internal HullContact.Vec Normal;}
    static ConditionalWeakTable<Ship,Track> tracks=new ConditionalWeakTable<Ship,Track>();
    static readonly AccessTools.FieldRef<double> Bounce=AccessTools.StaticFieldRefAccess<double>(AccessTools.Field(typeof(CollisionManager),"fBounceRange"));
    [ThreadStatic] static Prepared prepared;
    [ThreadStatic] static ResponseState response;
    [ThreadStatic] static HullContact solver;
    static bool wasJump;
    static long queries,hits,misses,sweeps,fallbacks;
    static Track Create(Ship _)=>new Track();
    internal static void SetEnabled(bool enabled) {
        if(Flag==null)throw new InvalidOperationException("Hull collisions are not initialized.");
        Flag.Value=enabled;if(!enabled)HullDisplay.NavClosed();Reset();
    }
    internal static void Reset(){tracks=new ConditionalWeakTable<Ship,Track>();prepared=default;response=default;solver=null;wasJump=false;queries=hits=misses=sweeps=fallbacks=0;}
    internal static void Begin(out ScanState state) {
        state=new ScanState{Previous=prepared};prepared=default;
        if(!Enabled)return;
        bool jump=GUIFFWD.Active;
        if(jump!=wasJump){tracks=new ConditionalWeakTable<Ship,Track>();wasJump=jump;}
        if(!jump)HullDisplay.Pump();
    }
    internal static void End(ScanState state)=>prepared=state.Previous;
    internal static float Nonzero(float distance,Ship a,Ship b)=>distance==0&&Enabled&&!GUIFFWD.Active&&a!=null&&b!=null&&a.objSS!=null&&b.objSS!=null&&
        a.Classification==Ship.TypeClassification.Default&&b.Classification==Ship.TypeClassification.Default&&
        !StationDockedCollisions.ActualStation(a)&&!StationDockedCollisions.ActualStation(b)?float.Epsilon:distance;
    static double Turn(double value){value%=Math.PI*2;if(value>Math.PI)value-=Math.PI*2;if(value< -Math.PI)value+=Math.PI*2;return value;}
    static Track Observe(Ship ship,double epoch) {
        var track=tracks.GetValue(ship,Create);var now=new Sample(ship,epoch);
        if(!track.Initialized||epoch<track.Latest.Epoch){track.Latest=now;track.Initialized=true;track.HasPrevious=false;}
        else if(epoch>track.Latest.Epoch){track.Previous=track.Latest;track.Latest=now;track.HasPrevious=true;}
        else track.Latest=now;
        if(track.HasPrevious) {
            var before=track.Previous;double dt=epoch-before.Epoch;
            double dx=(now.X-before.X)*MetresPerAU,dy=(now.Y-before.Y)*MetresPerAU;
            double speed=(Math.Sqrt(before.VX*before.VX+before.VY*before.VY)+Math.Sqrt(now.VX*now.VX+now.VY*now.VY))*MetresPerAU;
            // Discontinuities are not a travelled collision segment: save/SFF,
            // native instant docking, changed ship geometry/frame or teleport.
            double reach=Math.Max(1000+now.Radius*16,2*speed*dt);
            if(!(dt>0)||dt>MaxHistorySeconds||now.Grid!=before.Grid||now.Radius!=before.Radius||now.Docked!=before.Docked||dx*dx+dy*dy>reach*reach)track.HasPrevious=false;
        }
        return track;
    }
    static void At(Track track,double epoch,out double x,out double y,out double angle) {
        var end=track.Latest;var start=track.HasPrevious?track.Previous:end;
        double f=end.Epoch>start.Epoch?(epoch-start.Epoch)/(end.Epoch-start.Epoch):1;
        x=start.X+(end.X-start.X)*f;y=start.Y+(end.Y-start.Y)*f;
        angle=end.Angle-Turn(end.Angle-start.Angle)*(1-f);
    }
    static HullContact.Motion Path(Track a,Track b,double epoch) {
        var x=a.Latest;var y=b.Latest;
        double start=a.HasPrevious&&b.HasPrevious?Math.Max(a.Previous.Epoch,b.Previous.Epoch):epoch;
        At(a,start,out double ax,out double ay,out double aa);At(b,start,out double bx,out double by,out double ba);
        return new HullContact.Motion(new HullContact.Vec((ax-bx)*MetresPerAU,(ay-by)*MetresPerAU),
            new HullContact.Vec((x.X-y.X)*MetresPerAU,(x.Y-y.Y)*MetresPerAU),aa,x.Angle,ba,y.Angle,x.Radius,y.Radius,start<epoch);
    }
    static double Closest(HullContact.Vec a,HullContact.Vec b) {
        var d=b-a;double squared=d.Dot(d),t=squared>0?Math.Max(0,Math.Min(1,-a.Dot(d)/squared)):0;
        var p=a+d*t;return p.Dot(p);
    }
    internal static bool Overlap(Ship a,Ship b,float nativeDistance,float nativeRadius) {
        prepared=default;bool native=nativeDistance<nativeRadius;
        if(!Enabled||GUIFFWD.Active||a==null||b==null||a.objSS==null||b.objSS==null||
            a.Classification!=Ship.TypeClassification.Default||b.Classification!=Ship.TypeClassification.Default||a.objSS.bIsBO||b.objSS.bIsBO)return native;
        double epoch=StarSystem.fEpoch;var ta=Observe(a,epoch);var tb=Observe(b,epoch);var path=Path(ta,tb,epoch);
        double radius=path.RA+path.RB;
        var future=path.End+new HullContact.Vec((ta.Latest.VX-tb.Latest.VX)*MetresPerAU,(ta.Latest.VY-tb.Latest.VY)*MetresPerAU)*2;
        bool candidate=Closest(path.Start,path.End)<=radius*radius;
        double warm=radius+5000;
        if(!candidate&&Closest(path.End,future)>warm*warm)return false;
        if(StationDockedCollisions.ActualStation(a)||StationDockedCollisions.ActualStation(b))return native;
        if(!StationDockedCollisions.ShipCollision(a,b))return false;
        bool readyA=HullDisplay.Geometry(a,out var ga),readyB=HullDisplay.Geometry(b,out var gb);
        if(!candidate)return false;
        queries++;
        if(!readyA||!readyB){fallbacks++;return native;}
        if(solver==null)solver=new HullContact();
        if(!solver.TrySolve(ga.Collision,gb.Collision,path,out var contact)){fallbacks++;return native;}
        if(!contact.Hit){misses++;return false;}
        hits++;if(contact.Swept)sweeps++;
        prepared=new Prepared(a,b,contact);return true;
    }
    internal static void EnterResponse(Ship a,Ship b,ref double overlap,out ResponseState state) {
        state=response;response=default;var candidate=prepared;prepared=default;
        if(!Enabled||!ReferenceEquals(candidate.A,a)||!ReferenceEquals(candidate.B,b)||!candidate.Contact.Hit)return;
        response=new ResponseState{Active=true,Normal=candidate.Contact.Normal};
        // Native multiplies overlap by fBounceRange before moving the docked
        // groups. Supply the actual separating displacement, not circle overlap.
        double bounce=Bounce();overlap=candidate.Contact.Depth*HullZoom.MetresToAU/(bounce>0?bounce:1);
    }
    static void Forget(Ship ship) {
        if(ship!=null&&tracks.TryGetValue(ship,out var track)){track.Initialized=false;track.HasPrevious=false;}
    }
    static void Rebase(Ship ship) {
        Forget(ship);
        if(ship==null||ship.bDestroyed||!ship.bDocked)return;
        var group=ship.GetAllDockedShips();for(int i=0;i<group.Count;i++)Forget(group[i]);
    }
    internal static void LeaveResponse(Ship a,Ship b,ResponseState state) {
        try {if(response.Active){Rebase(a);Rebase(b);}}
        finally {response=state;}
    }
    internal static void Normal(ref double x,ref double y,double length) {
        if(response.Active){x=response.Normal.X*length;y=response.Normal.Y*length;}
        else MathUtils.SetLength(ref x,ref y,length);
    }
    internal static object Inspect()=>new Dictionary<string,object>{["enabled"]=Enabled,["queries"]=queries,["hullHits"]=hits,["circleFalsePositivesRejected"]=misses,["sweptHits"]=sweeps,["nativeFallbacks"]=fallbacks};
}
