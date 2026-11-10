using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Ostrabus.NavConsole.Outlines;

static class CoreChecks {
    static int checks;
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static HullPoint[] Box(double x, double y, double width, double height) => new[] { new HullPoint(x,y), new HullPoint(x+width,y), new HullPoint(x+width,y+height), new HullPoint(x,y+height) };
    readonly struct Segment : IEquatable<Segment> {
        readonly int x,y,dx,dy;
        internal Segment(int x,int y,int dx,int dy){this.x=x;this.y=y;this.dx=dx;this.dy=dy;}
        public bool Equals(Segment b)=>x==b.x&&y==b.y&&dx==b.dx&&dy==b.dy;
        public override bool Equals(object b)=>b is Segment s&&Equals(s);
        public override int GetHashCode()=>unchecked(x*73856093^y*19349663^dx*397^dy);
    }
    // Independent oracle: flood-fill outside air on small padded fixtures.
    // Production uses directed loops and a containment BVH, not a flood fill.
    static HashSet<Segment> Oracle(bool[,] occupied) {
        int width=occupied.GetLength(0),height=occupied.GetLength(1);
        var outside=new bool[width+2,height+2];var queue=new Queue<(int x,int y)>();queue.Enqueue((0,0));outside[0,0]=true;
        while(queue.Count>0) {
            var cell=queue.Dequeue();
            foreach(var delta in new[]{(1,0),(-1,0),(0,1),(0,-1)}) {
                int x=cell.x+delta.Item1,y=cell.y+delta.Item2;
                if(x<0||x>=width+2||y<0||y>=height+2||outside[x,y]||(x>0&&x<=width&&y>0&&y<=height&&occupied[x-1,y-1]))continue;
                outside[x,y]=true;queue.Enqueue((x,y));
            }
        }
        var edges=new HashSet<Segment>();
        for(int x=0;x<width;x++)for(int y=0;y<height;y++)if(occupied[x,y]) {
            if(outside[x+1,y])edges.Add(new Segment(x,y,1,0));
            if(outside[x+2,y+1])edges.Add(new Segment(x+1,y,0,1));
            if(outside[x+1,y+2])edges.Add(new Segment(x+1,y+1,-1,0));
            if(outside[x,y+1])edges.Add(new Segment(x,y+1,0,-1));
        }
        return edges;
    }
    static HashSet<Segment> Expand(Envelope hull) {
        var result=new HashSet<Segment>();
        for(int loop=0;loop<hull.Loops;loop++)for(int i=hull.Start(loop);i<hull.End(loop);i++) {
            var a=hull.Raw(i);var b=hull.Raw(i+1==hull.End(loop)?hull.Start(loop):i+1);
            Check(a.X==Math.Round(a.X)&&a.Y==Math.Round(a.Y),"Integer fixture perimeter left the authored grid");
            int x=(int)a.X,y=(int)a.Y,endX=(int)b.X,endY=(int)b.Y,dx=Math.Sign(endX-x),dy=Math.Sign(endY-y);
            Check((dx==0)!=(dy==0),"Perimeter was smoothed, diagonalized or duplicated");
            int steps=0;
            while(x!=endX||y!=endY){Check(++steps<1000,"Perimeter edge did not terminate");result.Add(new Segment(x,y,dx,dy));x+=dx;y+=dy;}
        }
        return result;
    }
    static void Geometry() {
        var builder=new EnvelopeBuilder();var random=new Random(6261);
        for(int sample=0;sample<200;sample++) {
            var occupied=new bool[12,10];var quads=new List<HullPoint[]>();
            for(int x=0;x<12;x++)for(int y=0;y<10;y++)if(random.NextDouble()<.55){occupied[x,y]=true;quads.Add(Box(x,y,1,1));}
            var input=quads.SelectMany(p=>p).ToArray();
            var envelope=builder.Build(input,input.Length,null);
            Check(Expand(envelope).SetEquals(Oracle(occupied)),"Exterior perimeter disagrees with independent outside-air oracle");
            foreach(var quad in quads)Check(envelope.Contains(quad[0].X+.5,quad[0].Y+.5,false),"Exterior excludes occupied structure");
            double maximum=0;
            for(int i=0;i<envelope.Count;i++){var p=envelope.Unit(i);maximum=Math.Max(maximum,p.X*p.X+p.Y*p.Y);}
            Check(Math.Abs(maximum-1)<1e-12,"Native-radius normalization failed");
            var shuffled=quads.OrderBy(_=>random.Next()).SelectMany(p=>p).Concat(input).ToArray();
            Check(ReferenceEquals(envelope,builder.Build(shuffled,shuffled.Length,envelope)),"Input order or duplicates changed cached result");
        }
        var outer=Box(-10,-10,20,20);var original=builder.Build((HullPoint[])outer.Clone(),outer.Length,null);
        var withInterior=outer.Concat(Box(-2,-2,4,4)).ToArray();
        Check(ReferenceEquals(original,builder.Build(withInterior,withInterior.Length,original)),"Overlapping interior patch changed exterior");
        Check(original.Contains(0,0,true)&&!original.Contains(-10,0,true)&&original.Contains(-10,0,false),"Strict interior classification failed");
        Check(!original.Contains(-10.001,0,false),"Outside point was classified interior");
        var separated=Box(-20,0,2,2).Concat(Box(20,0,2,2)).ToArray();var combined=builder.Build(separated,separated.Length,null);
        Check(combined.Count==8&&combined.Loops==2&&!combined.Contains(0,1,false),"Disconnected sections lost their own exterior boundaries");
        Check(builder.Build(Array.Empty<HullPoint>(),0,null).Count==0,"Empty hull failed");
        var single=Box(0,0,1,1);Check(builder.Build(single,4,null).Count==4,"Single cell failed");
        bool rejected=false;try{builder.Build(new[]{new HullPoint(1,2)},1,null);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Incomplete socket quad accepted");
        var invalid=Box(0,0,1,1);invalid[0]=new HullPoint(double.NaN,0);
        rejected=false;try{builder.Build(invalid,4,null);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Invalid coordinates accepted");
        var repeatedCorner=Box(0,0,1,1);repeatedCorner[3]=repeatedCorner[1];
        rejected=false;try{builder.Build(repeatedCorner,4,null);}catch(ArgumentException){rejected=true;}
        Check(rejected,"Malformed quad was inflated to an invented rectangle");
        var excessive=new List<HullPoint>();
        for(int x=0;x<9000;x++){excessive.AddRange(Box(x,0,1,1));if(x%2==0)excessive.AddRange(Box(x,1,1,1));}
        rejected=false;try{builder.Build(excessive.ToArray(),excessive.Count,null);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Oversized result was allowed through to the native line mesh");
        var l=Box(0,0,4,1).Concat(Box(0,1,1,3)).ToArray();var angle=builder.Build(l,l.Length,null);
        Check(angle.Count==6&&!angle.Contains(2,2,false)&&angle.Contains(.5,2,false),"L-shaped exterior was convexified");
        var u=Box(0,0,1,4).Concat(Box(3,0,1,4)).Concat(Box(0,0,4,1)).ToArray();var bay=builder.Build(u,u.Length,null);
        Check(bay.Count==8&&!bay.Contains(2,2,false),"U-shaped recess was filled");
        Check(bay.Contains(.25,2,false)&&bay.Contains(3.75,3,false)&&!bay.ContainsBox(.25,2,3.75,3,false),"Four corners incorrectly classified a box spanning an exterior recess as interior");
        var frame=new List<HullPoint>();
        for(int x=0;x<11;x++)for(int y=0;y<11;y++)if(x==0||x==10||y==0||y==10||(x==5&&y==5))frame.AddRange(Box(x,y,1,1));
        var filled=builder.Build(frame.ToArray(),frame.Count,null);
        Check(filled.Count==4&&filled.Loops==1&&filled.Contains(5,5,true),"Internal hole/island boundaries were drawn");
        var overlap=Box(0,0,1,1).Concat(Box(.5,0,1,1)).ToArray();var joined=builder.Build(overlap,overlap.Length,null);
        Check(joined.Count==4&&joined.Loops==1&&joined.Contains(1.49,.5,false),"Overlapping half-grid sockets produced internal edges");
        var shifted=Box(.123,-.241,2,1);var translated=builder.Build(shifted,4,null);
        Check(translated.Count==4&&translated.Contains(.124,-.24,false),"Common transform phase was lost");
    }
    sealed class Manual {
        internal WaitCallback Callback;internal object State;internal int Starts;
        internal void Queue(WaitCallback callback,object state){Check(Callback==null,"Multiple workers were scheduled concurrently");Callback=callback;State=state;Starts++;}
        internal void Drain(){var callback=Callback;var state=State;Callback=null;State=null;callback(state);}
    }
    static void Submit(HullWorker worker,HullWorker.Entry entry,HullPoint[] geometry) {
        Check(worker.TryReserve(entry,out var ticket),"Request reservation failed");
        var buffer=worker.Rent(geometry.Length);Array.Copy(geometry,buffer,geometry.Length);worker.Submit(ticket,buffer,geometry.Length);
    }
    static bool Ready(HullWorker worker,HullWorker.Entry entry)=>worker.Read(entry,out _,out _,out bool clean,out _)&&clean;
    static void QueueAndOwnership() {
        var manual=new Manual();var worker=new HullWorker(manual.Queue);var entry=worker.Create();
        var geometry=Box(0,0,20,10);Submit(worker,entry,geometry);
        Check(!worker.TryReserve(entry,out _),"Duplicate work for one ship was allowed");
        manual.Drain();Check(Ready(worker,entry),"Completed result not published");
        Check(!worker.TryReserve(entry,out _),"Clean geometry recalculated without a change");
        worker.Read(entry,out var first,out long version,out _,out _);
        worker.Dirty(entry);Submit(worker,entry,geometry);manual.Drain();
        worker.Read(entry,out var second,out long unchanged,out _,out _);
        Check(ReferenceEquals(first,second)&&version==unchanged,"Identical hull allocated/reuploaded a replacement");
        worker.Dirty(entry);Submit(worker,entry,Box(0,0,10,20));worker.Dirty(entry);manual.Drain();
        Check(!Ready(worker,entry),"Queued stale result was accepted");
        worker.Read(entry,out var retained,out _,out _,out _);Check(ReferenceEquals(first,retained),"Pending replacement discarded last usable outline");
        Submit(worker,entry,Box(0,0,10,20));manual.Drain();Check(Ready(worker,entry),"Latest changed hull was not recovered");
        var pending=new List<HullWorker.Entry>();
        for(int i=0;i<HullWorker.MaxJobs;i++){var e=worker.Create();pending.Add(e);Submit(worker,e,geometry);}
        Check(!worker.TryReserve(worker.Create(),out _),"Queue capacity was exceeded");
        Check(manual.Starts==5,"Burst did not share one worker");
        worker.Retire(pending[0]);manual.Drain();Check(!Ready(worker,pending[0]),"Retired ship published a result");
        Check(pending.Skip(1).All(e=>Ready(worker,e)),"Retiring one ship lost unrelated jobs");
        Check(worker.TryReserve(entry,out _)==false,"Clean entry acquired unnecessary reservation");
        worker.Dirty(entry);Check(worker.TryReserve(entry,out var unfinished),"Snapshot reservation unavailable");
        worker.Reset();Check(!worker.Valid(unfinished),"Reload kept an in-progress snapshot valid");
        var afterReload=worker.Create();Submit(worker,afterReload,geometry);manual.Drain();Check(Ready(worker,afterReload),"Reload left queue reservations stuck");
        worker.Reset(true);Check(!worker.TryReserve(worker.Create(),out _),"Shutdown accepted more jobs");
        Check(!worker.Read(afterReload,out _,out _,out _,out _),"Shutdown exposed old results");
    }
    static void InFlight(Action<HullWorker,HullWorker.Entry> change,string reason) {
        using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var done=new ManualResetEventSlim()) {
            var builder=new EnvelopeBuilder();
            var worker=new HullWorker((callback,state)=>ThreadPool.QueueUserWorkItem(_=>{try{callback(state);}finally{done.Set();}}),
                (points,count,previous)=>{entered.Set();if(!release.Wait(5000))throw new Exception("Build gate timeout");return builder.Build(points,count,previous);});
            var entry=worker.Create();Submit(worker,entry,Box(0,0,10,20));
            Check(entered.Wait(5000),"Worker did not start");
            try {
                var timer=System.Diagnostics.Stopwatch.StartNew();change(worker,entry);timer.Stop();
                Check(timer.Elapsed.TotalMilliseconds<500,"Cache operation waited for hull calculation: "+reason);
                Check(!Ready(worker,entry),"Main thread unexpectedly blocked or got a stale result: "+reason);
            }
            finally { release.Set(); }
            Check(done.Wait(5000),"Worker did not finish after cancellation");
            Check(!worker.Read(entry,out _,out _,out bool clean,out _)||!clean,"In-flight stale result published: "+reason);
            worker.Reset(true);
        }
    }
    static void FailuresAndTickets() {
        var manual=new Manual();bool fail=true;var builder=new EnvelopeBuilder();
        var worker=new HullWorker(manual.Queue,(p,n,old)=>{if(fail)throw new InvalidOperationException("fixture failure");return builder.Build(p,n,old);});
        var entry=worker.Create();Submit(worker,entry,Box(0,0,1,1));manual.Drain();
        Check(!worker.Read(entry,out _,out _,out _,out string error)&&error=="fixture failure","Worker failure lost diagnostic or published partial geometry");
        fail=false;Submit(worker,entry,Box(0,0,2,1));manual.Drain();Check(Ready(worker,entry),"Worker did not recover after calculation failure");
        worker.Dirty(entry);Check(worker.TryReserve(entry,out var oldTicket),"Old ticket reservation failed");worker.Cancel(oldTicket);
        Check(worker.TryReserve(entry,out var latest),"New ticket reservation failed");
        worker.Submit(oldTicket,worker.Rent(4),4);Check(worker.Valid(latest),"Stale submission cancelled the replacement reservation");
        var buffer=worker.Rent(4);Array.Copy(Box(0,0,3,1),buffer,4);worker.Submit(latest,buffer,4);manual.Drain();
        Check(Ready(worker,entry),"Replacement ticket did not complete");worker.Reset(true);
        var broken=new HullWorker((_,__)=>throw new InvalidOperationException("scheduler failure"));var rejected=broken.Create();
        Submit(broken,rejected,Box(0,0,1,1));
        Check(broken.TryReserve(rejected,out var retry),"Scheduler failure leaked a reservation");broken.Cancel(retry);broken.Reset(true);
    }
    static void Zoom() {
        // A 500 m radius hull on a 600-pixel-wide map remains useful at 10 km
        // range (30 pixels across), well outside the old 2.5 km cutoff.
        double mapScale=300/(10000*HullZoom.MetresToAU);
        double radius=HullZoom.RadiusSquared(500,mapScale,0);
        Check(Math.Abs(radius-225)<1e-8&&HullZoom.Show(radius,false),"Large hull was hidden by the old fixed zoom cutoff");
        Check(!HullZoom.Show(HullZoom.RadiusSquared(100,mapScale,0),false),"Tiny distant hull requested detailed geometry");
        Check(HullZoom.Show(13*13,false)&&!HullZoom.Show(12.99*12.99,false),"Outline entry threshold is wrong");
        Check(HullZoom.Show(11*11,true)&&!HullZoom.Show(11*11,false),"Zoom hysteresis flickers between symbol and hull");
        Check(HullZoom.Show(10*10,true)&&!HullZoom.Show(9.99*9.99,true),"Outline exit threshold is wrong");
        Check(Math.Abs(HullZoom.RadiusSquared(500,mapScale*.6,mapScale*.8)-radius)<1e-8,"Map rotation changed outline visibility");
        foreach(double bad in new[]{0d,double.NaN,double.PositiveInfinity})
            Check(!HullZoom.Show(HullZoom.RadiusSquared(500,bad,0),false),"Invalid projection enabled a hull");
        foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            Check(!HullZoom.Show(HullZoom.RadiusSquared(bad,mapScale,0),false),"Invalid radius enabled a hull");
        Check(HullZoom.OnMap(320,320,radius,0,0,640,640),"Onscreen hull was culled");
        Check(HullZoom.OnMap(-10,320,radius,0,0,640,640),"Partly visible hull was culled by its center");
        Check(!HullZoom.OnMap(-16,320,radius,0,0,640,640),"Offscreen hull requested geometry");
        Check(!HullZoom.OnMap(-14,-14,radius,0,0,640,640),"Offscreen corner missed circle/viewport rejection");
        Check(!HullZoom.OnMap(double.NaN,320,radius,0,0,640,640)&&!HullZoom.OnMap(320,320,radius,0,0,0,0),"Invalid viewport enabled a hull");
    }
    internal static void Run() {
        CollisionChecks.Run();
        Geometry();Zoom();QueueAndOwnership();FailuresAndTickets();
        InFlight((w,e)=>w.Dirty(e),"mutation");InFlight((w,e)=>w.Retire(e),"destruction");
        InFlight((w,e)=>w.Reset(),"reload");InFlight((w,e)=>w.Reset(true),"shutdown");
        Console.WriteLine(checks+" hull geometry/ownership/queue/concurrency checks passed.");
    }
}
