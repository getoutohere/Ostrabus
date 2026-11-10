using System;
using System.Collections.Generic;

namespace Ostrabus.NavConsole.Outlines;

// Union the authored socket-cell quads on their native half-tile grid, trace
// their directed boundaries and retain only exterior loops. No convexification
// or curve fitting. Collinear runs alone are collapsed.
internal sealed class EnvelopeBuilder {
    internal const int MaxVertices = 16000, MaxCells = 262144;
    readonly struct Cell : IEquatable<Cell> {
        internal readonly int X, Y;
        internal Cell(int x, int y) { X=x; Y=y; }
        public bool Equals(Cell other) => X==other.X && Y==other.Y;
        public override bool Equals(object other) => other is Cell cell && Equals(cell);
        public override int GetHashCode() => unchecked(X*73856093 ^ Y*19349663);
    }
    sealed class Order : IComparer<Cell> {
        internal static readonly Order Instance = new Order();
        public int Compare(Cell a, Cell b) { int x=a.X.CompareTo(b.X); return x!=0 ? x : a.Y.CompareTo(b.Y); }
    }
    struct Contour {
        internal int Start, End, MinX, MaxX, MinY, MaxY;
        internal double Area;
        internal bool Nested;
    }
    struct Node { internal int MinX,MaxX,MinY,MaxY,Left,Right,Contour; }
    sealed class ContourOrder : IComparer<int> {
        internal List<Contour> Contours;
        public int Compare(int a,int b) { int x=Contours[a].MinX.CompareTo(Contours[b].MinX); return x!=0 ? x : a.CompareTo(b); }
    }
    HashSet<Cell> cells = new HashSet<Cell>();
    Dictionary<Cell,int> edges = new Dictionary<Cell,int>();
    List<Cell> seeds = new List<Cell>(), loop = new List<Cell>(), vertices = new List<Cell>();
    List<Contour> contours = new List<Contour>();
    List<HullPoint> output = new List<HullPoint>();
    List<int> ends = new List<int>();
    readonly ContourOrder contourOrder = new ContourOrder();
    readonly CollisionShape.Builder collision=new CollisionShape.Builder();
    int[] indices = Array.Empty<int>();
    Node[] nodes = Array.Empty<Node>();
    int nodeCount;
    double phaseX,phaseY;
    static Cell Step(Cell p,int direction) => direction==0 ? new Cell(p.X+1,p.Y) : direction==1 ? new Cell(p.X,p.Y+1) : direction==2 ? new Cell(p.X-1,p.Y) : new Cell(p.X,p.Y-1);
    static double Cross(Cell a,Cell b,Cell c) => (double)(b.X-a.X)*(c.Y-b.Y)-(double)(b.Y-a.Y)*(c.X-b.X);
    static int Grid(double value,double phase) {
        double scaled=(value-phase)*2, rounded=Math.Round(scaled);
        // Recover native grid alignment after float Transform subtraction. This
        // is at most .005 tile; no silhouette simplification tolerance is used.
        if (Math.Abs(scaled-rounded)>.01 || rounded<int.MinValue+4d || rounded>int.MaxValue-4d)
            throw new ArgumentException("Hull socket is not aligned to the native half-tile grid.");
        return (int)rounded;
    }
    void Edge(Cell from,int direction) {
        edges.TryGetValue(from,out int mask);edges[from]=mask | (1<<direction);
    }
    void Occupancy(HullPoint[] input,int count) {
        if (count==0) return;
        phaseX=input[0].X-Math.Round(input[0].X*2)*.5;
        phaseY=input[0].Y-Math.Round(input[0].Y*2)*.5;
        for(int i=0;i<count;i+=4) {
            int minX=int.MaxValue,maxX=int.MinValue,minY=int.MaxValue,maxY=int.MinValue;
            for(int j=0;j<4;j++) {
                var p=input[i+j];if(!p.Finite)throw new ArgumentException("Non-finite hull coordinate.");
                int x=Grid(p.X,phaseX),y=Grid(p.Y,phaseY);
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                var next=input[i+(j+1)%4];
                if(Math.Abs(p.X-next.X)>.005 && Math.Abs(p.Y-next.Y)>.005)
                    throw new ArgumentException("Hull socket footprint is not axis aligned.");
            }
            long area=((long)maxX-minX)*(maxY-(long)minY);
            if(area<=0 || area>MaxCells)throw new ArgumentException("Hull socket footprint exceeds the bounded cell grid.");
            int corners=0;
            for(int j=0;j<4;j++) {
                int x=Grid(input[i+j].X,phaseX),y=Grid(input[i+j].Y,phaseY);
                if((x!=minX&&x!=maxX)||(y!=minY&&y!=maxY))throw new ArgumentException("Invalid hull socket rectangle.");
                corners|=1<<((x==maxX?1:0)+(y==maxY?2:0));
            }
            if(corners!=15)throw new ArgumentException("Incomplete hull socket rectangle.");
            for(int x=minX;x<maxX;x++)for(int y=minY;y<maxY;y++) {
                cells.Add(new Cell(x,y));
                if(cells.Count>MaxCells)throw new InvalidOperationException("Hull occupancy exceeds the bounded cell grid.");
            }
        }
    }
    void Trace(Cell start,int direction) {
        loop.Clear();Cell current=start;int walked=0;
        do {
            if(++walked>MaxCells*4)throw new InvalidOperationException("Hull boundary did not close.");
            int mask=edges[current];
            if((mask&(1<<direction))==0)throw new InvalidOperationException("Hull boundary reused an edge.");
            edges[current]=mask&~(1<<direction);loop.Add(current);current=Step(current,direction);
            if(current.Equals(start))break;
            if(!edges.TryGetValue(current,out mask)||mask==0)throw new InvalidOperationException("Hull boundary is open.");
            // Follow the same outside-air face at corner junctions. A zero-width
            // corner contact must not leak into an enclosed interior void.
            int left=(direction+1)%4,right=(direction+3)%4;
            direction=(mask&(1<<right))!=0 ? right : (mask&(1<<direction))!=0 ? direction : (mask&(1<<left))!=0 ? left : (direction+2)%4;
        }while(true);
        double area=0;var origin=loop[0];
        for(int i=0;i<loop.Count;i++) {
            var a=loop[i];var b=loop[(i+1)%loop.Count];
            area+=(double)(a.X-origin.X)*(b.Y-origin.Y)-(double)(a.Y-origin.Y)*(b.X-origin.X);
        }
        if(area<=0)return; // Clockwise loops enclose internal holes.
        var contour=new Contour{Start=vertices.Count,MinX=int.MaxValue,MaxX=int.MinValue,MinY=int.MaxValue,MaxY=int.MinValue,Area=area};
        for(int i=0;i<loop.Count;i++) {
            var p=loop[i];
            if(Cross(loop[(i+loop.Count-1)%loop.Count],p,loop[(i+1)%loop.Count])==0)continue;
            vertices.Add(p);contour.MinX=Math.Min(contour.MinX,p.X);contour.MaxX=Math.Max(contour.MaxX,p.X);
            contour.MinY=Math.Min(contour.MinY,p.Y);contour.MaxY=Math.Max(contour.MaxY,p.Y);
        }
        contour.End=vertices.Count;contours.Add(contour);
    }
    int Tree(int start,int count) {
        int at=nodeCount++;var node=new Node{MinX=int.MaxValue,MaxX=int.MinValue,MinY=int.MaxValue,MaxY=int.MinValue,Contour=-1};
        for(int i=start;i<start+count;i++) {
            var c=contours[indices[i]];node.MinX=Math.Min(node.MinX,c.MinX);node.MaxX=Math.Max(node.MaxX,c.MaxX);
            node.MinY=Math.Min(node.MinY,c.MinY);node.MaxY=Math.Max(node.MaxY,c.MaxY);
        }
        if(count==1)node.Contour=indices[start];
        else {int half=count/2;node.Left=Tree(start,half);node.Right=Tree(start+half,count-half);}
        nodes[at]=node;return at;
    }
    bool Inside(Contour c,double x,double y) {
        bool inside=false;
        for(int i=c.Start;i<c.End;i++) {
            var a=vertices[i];var b=vertices[i+1==c.End ? c.Start : i+1];
            if((a.Y>y)!=(b.Y>y) && x<(b.X-(double)a.X)*(y-a.Y)/(b.Y-(double)a.Y)+a.X)inside=!inside;
        }
        return inside;
    }
    bool HasParent(int nodeIndex,int child,double x,double y) {
        var node=nodes[nodeIndex];
        if(x<node.MinX||x>node.MaxX||y<node.MinY||y>node.MaxY)return false;
        if(node.Contour<0)return HasParent(node.Left,child,x,y)||HasParent(node.Right,child,x,y);
        if(node.Contour==child)return false;
        var p=contours[node.Contour];var c=contours[child];
        return p.Area>c.Area && p.MinX<=c.MinX&&p.MaxX>=c.MaxX&&p.MinY<=c.MinY&&p.MaxY>=c.MaxY&&Inside(p,x,y);
    }
    void RemoveNested() {
        int count=contours.Count;if(count<2)return;
        if(indices.Length<count)indices=new int[count];if(nodes.Length<count*2)nodes=new Node[count*2];
        for(int i=0;i<count;i++)indices[i]=i;
        contourOrder.Contours=contours;Array.Sort(indices,0,count,contourOrder);nodeCount=0;Tree(0,count);
        for(int i=0;i<count;i++) {
            var c=contours[i];var a=vertices[c.Start];var b=vertices[c.Start+1];
            double x=(a.X+(double)b.X)*.5-Math.Sign(b.Y-a.Y)*.125;
            double y=(a.Y+(double)b.Y)*.5+Math.Sign(b.X-a.X)*.125;
            c.Nested=HasParent(0,i,x,y);contours[i]=c;
        }
    }
    internal Envelope Build(HullPoint[] input,int count,Envelope previous) {
        cells.Clear();edges.Clear();seeds.Clear();loop.Clear();vertices.Clear();contours.Clear();output.Clear();ends.Clear();
        if(count<0||count>input.Length||count%4!=0)throw new ArgumentException("Hull input must contain complete socket quads.");
        Occupancy(input,count);
        foreach(var cell in cells) {
            if(!cells.Contains(new Cell(cell.X,cell.Y-1)))Edge(cell,0);
            if(!cells.Contains(new Cell(cell.X+1,cell.Y)))Edge(new Cell(cell.X+1,cell.Y),1);
            if(!cells.Contains(new Cell(cell.X,cell.Y+1)))Edge(new Cell(cell.X+1,cell.Y+1),2);
            if(!cells.Contains(new Cell(cell.X-1,cell.Y)))Edge(new Cell(cell.X,cell.Y+1),3);
        }
        seeds.AddRange(edges.Keys);seeds.Sort(Order.Instance);
        foreach(var seed in seeds)for(int direction=0;direction<4;direction++)
            if((edges[seed]&(1<<direction))!=0)Trace(seed,direction);
        RemoveNested();
        foreach(var c in contours)if(!c.Nested) {
            if(output.Count+c.End-c.Start>MaxVertices)throw new InvalidOperationException("Hull perimeter exceeds the native outline vertex limit.");
            for(int i=c.Start;i<c.End;i++)output.Add(new HullPoint(vertices[i].X*.5+phaseX,vertices[i].Y*.5+phaseY));
            ends.Add(output.Count);
        }
        return previous!=null&&previous.SameRaw(output,ends) ? previous : new Envelope(output,ends,collision);
    }
    internal void Release() {
        cells=new HashSet<Cell>();edges=new Dictionary<Cell,int>();seeds=new List<Cell>();loop=new List<Cell>();vertices=new List<Cell>();
        contours=new List<Contour>();output=new List<HullPoint>();ends=new List<int>();contourOrder.Contours=null;
        indices=Array.Empty<int>();nodes=Array.Empty<Node>();
        collision.Release();
    }
}
