using System;
using System.Collections.Generic;
using Ostrabus.NavConsole.Outlines;

namespace Ostrabus.ShipNavigation.Collisions;

// Numeric narrow phase. No Unity, world objects, mutable game data or global
// coordinate floats. Work in metres relative to B, subtracting AU positions first.
internal sealed class HullContact {
    internal const double Skin=0.001; // One millimetre, including round trips through heliocentric AU doubles.
    const int MaxVisits=262144,MaxPairs=65536;
    internal readonly struct Vec {
        internal readonly double X,Y;
        internal Vec(double x,double y){X=x;Y=y;}
        internal double Length=>Math.Sqrt(X*X+Y*Y);
        internal double Dot(Vec b)=>X*b.X+Y*b.Y;
        public static Vec operator +(Vec a,Vec b)=>new Vec(a.X+b.X,a.Y+b.Y);
        public static Vec operator -(Vec a,Vec b)=>new Vec(a.X-b.X,a.Y-b.Y);
        public static Vec operator *(Vec a,double b)=>new Vec(a.X*b,a.Y*b);
    }
    internal readonly struct Motion {
        internal readonly Vec Start,End;
        internal readonly double A0,A1,B0,B1,RA,RB;
        internal readonly bool Sweep;
        internal bool Rotating=>A0!=A1||B0!=B1;
        internal Motion(Vec start,Vec end,double a0,double a1,double b0,double b1,double ra,double rb,bool sweep) {
            Start=start;End=end;A0=a0;A1=a1;B0=b0;B1=b1;RA=ra;RB=rb;Sweep=sweep;
        }
    }
    internal readonly struct Contact {
        internal readonly bool Hit,Swept;
        internal readonly Vec Normal;
        internal readonly double Depth,Time;
        internal Contact(Vec normal,double depth,double time,bool swept){Hit=true;Normal=normal;Depth=depth;Time=time;Swept=swept;}
    }
    readonly struct Rect {
        internal readonly Vec Center,U,V;
        internal readonly double X,Y;
        internal Rect(CollisionShape.Box box,Vec position,double radians,double radius) {
            double c=Math.Cos(radians),s=Math.Sin(radians),x=(box.MinX+box.MaxX)*.5*radius,y=(box.MinY+box.MaxY)*.5*radius;
            Center=new Vec(position.X+c*x-s*y,position.Y+s*x+c*y);U=new Vec(c,s);V=new Vec(-s,c);
            X=(box.MaxX-box.MinX)*.5*radius;Y=(box.MaxY-box.MinY)*.5*radius;
        }
        internal double Support(Vec n)=>X*Math.Abs(U.Dot(n))+Y*Math.Abs(V.Dot(n));
    }
    readonly struct Interval {
        internal readonly double Enter,Exit;
        internal Interval(double enter,double exit){Enter=enter;Exit=exit;}
    }
    sealed class Intervals : IComparer<Interval> {
        internal static readonly Intervals Instance=new Intervals();
        public int Compare(Interval a,Interval b)=>a.Enter.CompareTo(b.Enter);
    }
    sealed class Budget : Exception { }
    readonly List<Interval> intervals=new List<Interval>();
    CollisionShape a,b;
    Motion motion;
    int mode; // 0: current overlap, 1: earliest swept hit, 2: separation intervals.
    bool hit;
    Vec normal;
    double depth,time,distance;
    internal int Visits {get;private set;}
    internal int Pairs {get;private set;}
    static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
    static double Gap(Rect a,Rect b,Vec velocity,out Vec normal) {
        Vec delta=a.Center-b.Center;double largest=double.NegativeInfinity;normal=default;
        for(int i=0;i<4;i++) {
            Vec axis=i==0?a.U:i==1?a.V:i==2?b.U:b.V;
            double p=delta.Dot(axis),gap=Math.Abs(p)-a.Support(axis)-b.Support(axis);
            if(gap<=largest)continue;
            largest=gap;
            double side=p!=0?Math.Sign(p):velocity.Dot(axis)>0?-1:1;
            normal=axis*side;
        }
        return largest;
    }
    static bool IntervalFor(Rect a,Rect b,Vec velocity,out double enter,out double exit) {
        enter=double.NegativeInfinity;exit=double.PositiveInfinity;Vec delta=a.Center-b.Center;
        for(int i=0;i<4;i++) {
            Vec n=i==0?a.U:i==1?a.V:i==2?b.U:b.V;
            double p=delta.Dot(n),v=velocity.Dot(n),r=a.Support(n)+b.Support(n)+Skin;
            if(Math.Abs(v)<1e-14){if(Math.Abs(p)>r)return false;continue;}
            double x=(-r-p)/v,y=(r-p)/v;if(x>y){double swap=x;x=y;y=swap;}
            enter=Math.Max(enter,x);exit=Math.Min(exit,y);if(enter>exit)return false;
        }
        return true;
    }
    static CollisionShape.Box World(CollisionShape.Box box,Vec position,double angle,double radius) {
        var rect=new Rect(box,position,angle,radius);
        double x=Math.Abs(rect.U.X)*rect.X+Math.Abs(rect.V.X)*rect.Y,y=Math.Abs(rect.U.Y)*rect.X+Math.Abs(rect.V.Y)*rect.Y;
        return new CollisionShape.Box(rect.Center.X-x,rect.Center.Y-y,rect.Center.X+x,rect.Center.Y+y);
    }
    CollisionShape.Box Bounds(CollisionShape.Box box,bool first) {
        double r=first?motion.RA:motion.RB,angle=first?motion.A1:motion.B1;
        Vec end=first?motion.End:default,start=first?motion.Start:default;
        if(mode==0)return World(box,end,angle,r);
        if(!motion.Rotating) {
            var bounds=World(box,end,angle,r);var delta=start-end;
            return new CollisionShape.Box(bounds.MinX+Math.Min(0,delta.X),bounds.MinY+Math.Min(0,delta.Y),bounds.MaxX+Math.Max(0,delta.X),bounds.MaxY+Math.Max(0,delta.Y));
        }
        // A node's farthest corner bounds all intermediate rotations, rather
        // than assuming the union of endpoint AABBs covers a rotating hull.
        double x=Math.Max(Math.Abs(box.MinX),Math.Abs(box.MaxX)),y=Math.Max(Math.Abs(box.MinY),Math.Abs(box.MaxY)),rad=Math.Sqrt(x*x+y*y)*r;
        return new CollisionShape.Box(Math.Min(start.X,end.X)-rad,Math.Min(start.Y,end.Y)-rad,Math.Max(start.X,end.X)+rad,Math.Max(start.Y,end.Y)+rad);
    }
    static bool Intersects(CollisionShape.Box x,CollisionShape.Box y)=>x.MinX<=y.MaxX+Skin&&x.MaxX+Skin>=y.MinX&&x.MinY<=y.MaxY+Skin&&x.MaxY+Skin>=y.MinY;
    void Visit(int x,int y) {
        if(++Visits>MaxVisits)throw new Budget();
        var na=a.Tree[x];var nb=b.Tree[y];
        if(!Intersects(Bounds(na.Bounds,true),Bounds(nb.Bounds,false)))return;
        if(na.Piece>=0&&nb.Piece>=0){Leaf(a.Pieces[na.Piece],b.Pieces[nb.Piece]);return;}
        if(nb.Piece>=0||(na.Piece<0&&(na.Bounds.MaxX-na.Bounds.MinX+na.Bounds.MaxY-na.Bounds.MinY)*motion.RA>=(nb.Bounds.MaxX-nb.Bounds.MinX+nb.Bounds.MaxY-nb.Bounds.MinY)*motion.RB)) {
            Visit(na.Left,y);Visit(na.Right,y);
        } else {Visit(x,nb.Left);Visit(x,nb.Right);}
    }
    void Leaf(CollisionShape.Box aa,CollisionShape.Box bb) {
        if(++Pairs>MaxPairs)throw new Budget();
        var velocity=motion.End-motion.Start;
        var endA=new Rect(aa,motion.End,motion.A1,motion.RA);var endB=new Rect(bb,default,motion.B1,motion.RB);
        if(mode==0) {
            double gap=Gap(endA,endB,velocity,out var n);
            if(gap<=Skin&&(!hit||-gap>depth)){hit=true;depth=-gap;normal=n;}
            return;
        }
        var startA=new Rect(aa,motion.Start,motion.A0,motion.RA);var startB=new Rect(bb,default,motion.B0,motion.RB);
        if(!motion.Rotating) {
            if(!IntervalFor(startA,startB,velocity,out double enter,out double exit)||exit<0||enter>1)return;
            if(mode==2){intervals.Add(new Interval(Math.Max(0,enter)*distance,Math.Min(1,exit)*distance));return;}
            double t=Math.Max(0,enter);if(t>=time)return;
            var at=new Rect(aa,motion.Start+velocity*t,motion.A0,motion.RA);
            Gap(at,startB,velocity,out var n);hit=true;time=t;normal=n;return;
        }
        // Conservative advancement: separating-axis gap is a lower bound on
        // distance. Relative translation plus angular corner speed is an upper
        // bound on closing speed. Thus no interval that could hide a hit is skipped.
        double speed=velocity.Length+Math.Abs(motion.A1-motion.A0)*motion.RA+Math.Abs(motion.B1-motion.B0)*motion.RB;
        if(!(speed>0))return;
        double tick=0;
        for(int i=0;i<256;i++) {
            if(tick>=time||tick>1)return;
            var pa=new Rect(aa,motion.Start+velocity*tick,motion.A0+(motion.A1-motion.A0)*tick,motion.RA);
            var pb=new Rect(bb,default,motion.B0+(motion.B1-motion.B0)*tick,motion.RB);
            double gap=Gap(pa,pb,velocity,out var n);
            if(gap<=Skin){hit=true;time=tick;normal=n;return;}
            tick+=Math.Max(1e-12,gap/speed);
        }
        throw new Budget(); // Unknown is conservative native fallback, never a fabricated miss.
    }
    internal bool TrySolve(CollisionShape first,CollisionShape second,Motion path,out Contact contact) {
        contact=default;Visits=Pairs=0;a=first;b=second;motion=path;intervals.Clear();
        try {
            if(a==null||b==null||!(path.RA>0)||!(path.RB>0)||!Finite(path.RA+path.RB+path.End.X+path.End.Y+path.Start.X+path.Start.Y+path.A0+path.A1+path.B0+path.B1))return false;
            mode=0;hit=false;depth=0;time=1;Visit(0,0);
            if(hit) {
                Vec n=normal;distance=4*(path.RA+path.RB);
                motion=new Motion(path.End,path.End+n*distance,path.A1,path.A1,path.B1,path.B1,path.RA,path.RB,true);
                mode=2;Visit(0,0);intervals.Sort(Intervals.Instance);double leave=0;
                foreach(var span in intervals){if(span.Enter>leave+Skin)break;leave=Math.Max(leave,span.Exit);}
                contact=new Contact(n,leave+Skin*2,1,false);return true;
            }
            if(!path.Sweep)return true;
            mode=1;hit=false;time=double.PositiveInfinity;Visit(0,0);
            if(hit) {
                var at=path.Start+(path.End-path.Start)*time;
                double correction=Math.Max(0,(at-path.End).Dot(normal))+Skin*2;
                contact=new Contact(normal,correction,time,true);
            }
            return true;
        } catch(Budget){return false;}
        finally {a=b=null;intervals.Clear();}
    }
}
