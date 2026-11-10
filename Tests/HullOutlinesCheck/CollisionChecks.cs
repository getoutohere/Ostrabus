using System;
using System.Collections.Generic;
using System.Linq;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation.Collisions;

static class CollisionChecks {
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static HullPoint[] Box(double x,double y,double width,double height)=>new[]{new HullPoint(x,y),new HullPoint(x+width,y),new HullPoint(x+width,y+height),new HullPoint(x,y+height)};
    static Envelope Build(params HullPoint[][] quads){var points=quads.SelectMany(x=>x).ToArray();return new EnvelopeBuilder().Build(points,points.Length,null);}
    static bool Covers(Envelope hull,double x,double y) {
        var p=hull.Unit(x,y);
        return hull.Collision.Pieces.Any(b=>p.X>b.MinX&&p.X<b.MaxX&&p.Y>b.MinY&&p.Y<b.MaxY);
    }
    static HullContact.Motion Still(double x,double y,double aa=0,double ba=0,double ar=1.4142135623730951,double br=1.4142135623730951)=>
        new HullContact.Motion(new HullContact.Vec(x,y),new HullContact.Vec(x,y),aa,aa,ba,ba,ar,br,false);
    static void Cover() {
        var random=new Random(8317);
        for(int test=0;test<100;test++) {
            var cells=new List<HullPoint[]>();
            for(int x=0;x<8;x++)for(int y=0;y<8;y++)if(random.NextDouble()<.55)cells.Add(Box(x,y,1,1));
            var hull=Build(cells.ToArray());Check(hull.Collision!=null,"Worker did not construct a numeric collision cover: "+hull.CollisionError);
            for(int x=-1;x<9;x++)for(int y=-1;y<9;y++)
                Check(Covers(hull,x+.317,y+.417)==hull.Contains(x+.317,y+.417,false),"Rectangle cover disagrees with the rendered filled exterior");
            Check(hull.Collision.Tree.Length==hull.Collision.Pieces.Length*2-1,"Rectangle BVH is incomplete");
        }
        var ring=Build(Box(0,0,8,1),Box(0,7,8,1),Box(0,1,1,6),Box(7,1,1,6));
        Check(Covers(ring,4,4),"Omitted interior hole became a collision tunnel");
        var detached=Build(Box(0,0,1,1),Box(10,0,1,1));
        Check(!Covers(detached,5,.5),"Disconnected hulls acquired a collision bridge");
    }
    static HullContact.Vec[] Corners(Envelope shape,double radius,double angle,HullContact.Vec position) {
        double c=Math.Cos(angle),s=Math.Sin(angle);
        return Enumerable.Range(0,shape.Count).Select(i=>{var p=shape.Unit(i);return position+new HullContact.Vec((p.X*c-p.Y*s)*radius,(p.X*s+p.Y*c)*radius);}).ToArray();
    }
    static bool Oracle(HullContact.Vec[] a,HullContact.Vec[] b) {
        foreach(var polygon in new[]{a,b})for(int i=0;i<polygon.Length;i++) {
            var d=polygon[(i+1)%polygon.Length]-polygon[i];var axis=new HullContact.Vec(-d.Y,d.X)*(1/d.Length);
            var first=a.Select(p=>p.Dot(axis)).ToArray();var second=b.Select(p=>p.Dot(axis)).ToArray();
            if(first.Max()<second.Min()||second.Max()<first.Min())return false;
        }
        return true;
    }
    static void Intersections() {
        var square=Build(Box(-1,-1,2,2));var longHull=Build(Box(-4,-.5,8,1));var solver=new HullContact();
        var random=new Random(199);
        for(int i=0;i<1000;i++) {
            double aa=random.NextDouble()*Math.PI*2,ba=random.NextDouble()*Math.PI*2,x=random.NextDouble()*20-10,y=random.NextDouble()*20-10;
            var motion=Still(x,y,aa,ba,4.031128874149275,1.4142135623730951);
            bool expected=Oracle(Corners(longHull,motion.RA,aa,motion.End),Corners(square,motion.RB,ba,default));
            Check(solver.TrySolve(longHull.Collision,square.Collision,motion,out var contact),"Rectangle contact exhausted the query budget");
            Check(contact.Hit==expected,"Native-oriented rectangle result differs from independent corner-projection oracle");
            if(contact.Hit) {
                var separated=motion.End+contact.Normal*contact.Depth;
                Check(solver.TrySolve(longHull.Collision,square.Collision,Still(separated.X,separated.Y,aa,ba,motion.RA,motion.RB),out var after)&&!after.Hit,"Returned contact displacement leaves hulls overlapping");
            }
        }
        var u=Build(Box(0,0,8,1),Box(0,1,1,7),Box(7,1,1,7));
        Check(solver.TrySolve(u.Collision,square.Collision,Still(0,-1,0,0,Math.Sqrt(32),.3),out var bay)&&!bay.Hit,"Exterior bay was convexified into a collision obstacle");
        Check(solver.TrySolve(longHull.Collision,longHull.Collision,Still(0,1.2,0,0,4.031128874149275,4.031128874149275),out var clear)&&!clear.Hit,"Circle overlap still collides with visibly clear thin ships");
        for(int i=0;i<100;i++) {
            double x=random.NextDouble()*8-4,y=random.NextDouble()*8-4;
            var motion=Still(x,y,random.NextDouble()*6,random.NextDouble()*6,Math.Sqrt(32),1.4142135623730951);
            Check(solver.TrySolve(u.Collision,square.Collision,motion,out var contact),"Concave query failed");
            if(!contact.Hit)continue;
            var end=motion.End+contact.Normal*contact.Depth;
            Check(solver.TrySolve(u.Collision,square.Collision,Still(end.X,end.Y,motion.A1,motion.B1,motion.RA,motion.RB),out var after)&&!after.Hit,
                "Concave separation stopped inside a different hull piece: x="+x+" y="+y+" angles="+motion.A1+","+motion.B1+" normal="+contact.Normal.X+","+contact.Normal.Y+" depth="+contact.Depth+" after="+after.Depth);
        }
    }
    static void Sweeps() {
        var square=Build(Box(-1,-1,2,2));var beam=Build(Box(-3,-.5,6,1));var solver=new HullContact();
        var path=new HullContact.Motion(new HullContact.Vec(-100,0),new HullContact.Vec(100,0),0,0,0,0,Math.Sqrt(2),Math.Sqrt(2),true);
        Check(solver.TrySolve(square.Collision,square.Collision,path,out var contact)&&contact.Hit&&contact.Swept,"Fast ship tunnelled through a hull between collision samples");
        Check(Math.Abs(contact.Time-.49)<1e-5&&contact.Normal.X<-.99,"Swept contact time/normal is incorrect");
        var end=path.End+contact.Normal*contact.Depth;
        Check(solver.TrySolve(square.Collision,square.Collision,Still(end.X,end.Y),out var after)&&!after.Hit&&end.X<0,"Fast collision did not restore the incoming side of the obstacle");
        foreach(double y in new[]{2.01,10,1000}) {
            var miss=new HullContact.Motion(new HullContact.Vec(-100,y),new HullContact.Vec(100,y),0,0,0,0,Math.Sqrt(2),Math.Sqrt(2),true);
            Check(solver.TrySolve(square.Collision,square.Collision,miss,out var result)&&!result.Hit,"Swept near-miss produced a phantom collision");
        }
        var rotation=new HullContact.Motion(new HullContact.Vec(0,-2.5),new HullContact.Vec(0,-2.5),0,Math.PI,0,0,Math.Sqrt(9.25),.3,true);
        Check(solver.TrySolve(beam.Collision,square.Collision,rotation,out var rotating)&&rotating.Hit&&rotating.Swept,"Rotating hull crossed another hull between clear endpoints");
        var noRotationHit=new HullContact.Motion(new HullContact.Vec(0,-10),new HullContact.Vec(0,-10),0,Math.PI,0,0,Math.Sqrt(9.25),.3,true);
        Check(solver.TrySolve(beam.Collision,square.Collision,noRotationHit,out var far)&&!far.Hit,"Rotational broad phase manufactured a hit");
        Check(!solver.TrySolve(null,square.Collision,path,out _),"Missing geometry was treated as a verified miss");
        var invalid=new HullContact.Motion(default,new HullContact.Vec(double.NaN,0),0,0,0,0,1,1,false);
        Check(!solver.TrySolve(square.Collision,square.Collision,invalid,out _),"Invalid numeric input was treated as a verified miss");
    }
    static void WarmQueries() {
        var shape=Build(Box(-4,-.5,8,1));var solver=new HullContact();var hit=Still(1,.5,0,0,4.031128874149275,4.031128874149275);
        for(int i=0;i<30;i++)solver.TrySolve(shape.Collision,shape.Collision,hit,out _);
        // Bundled Mono's allocation counter is not reliable; measure only on
        // CoreCLR, without presenting it as a live Unity allocation benchmark.
        if(typeof(object).Assembly.GetName().Name=="System.Private.CoreLib") {
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<10000;i++)solver.TrySolve(shape.Collision,shape.Collision,hit,out _);
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"Warmed numeric contact queries allocate on CoreCLR");
        }
    }
    internal static void Run(){Cover();Intersections();Sweeps();WarmQueries();Console.WriteLine(checks+" filled-hull cover/contact/separation/swept-collision checks passed.");}
}
