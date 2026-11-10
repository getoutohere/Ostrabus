using System;
using System.Collections.Generic;

namespace Ostrabus.NavConsole.Outlines;

// Immutable numeric rectangle cover of the SAME filled exterior drawn on nav.
// Built on the hull worker, never by the collision loop. Interior holes stay
// filled; exterior bays and separate components remain empty space.
internal sealed class CollisionShape {
    internal readonly struct Box {
        internal readonly double MinX,MinY,MaxX,MaxY;
        internal Box(double x,double y,double xx,double yy){MinX=x;MinY=y;MaxX=xx;MaxY=yy;}
    }
    internal readonly struct Node {
        internal readonly Box Bounds;
        internal readonly int Left,Right,Piece;
        internal Node(Box bounds,int left,int right,int piece){Bounds=bounds;Left=left;Right=right;Piece=piece;}
    }
    internal readonly Box[] Pieces;
    internal readonly Node[] Tree;
    CollisionShape(Box[] pieces,Node[] tree){Pieces=pieces;Tree=tree;}
    internal sealed class Builder {
        const int MaxPieces=8192,MaxSweepWork=1048576;
        readonly struct Event {
            internal readonly double Y,X;
            internal Event(double y,double x){Y=y;X=x;}
        }
        sealed class Events : IComparer<Event> {
            internal static readonly Events Instance=new Events();
            public int Compare(Event a,Event b){int c=a.Y.CompareTo(b.Y);return c!=0?c:a.X.CompareTo(b.X);}
        }
        readonly struct Run : IEquatable<Run> {
            internal readonly double X,End;
            internal Run(double x,double end){X=x;End=end;}
            public bool Equals(Run b)=>X==b.X&&End==b.End;
            public override bool Equals(object b)=>b is Run r&&Equals(r);
            public override int GetHashCode()=>unchecked(X.GetHashCode()*397^End.GetHashCode());
        }
        sealed class Centers : IComparer<int> {
            internal Box[] Boxes;
            internal bool Y;
            public int Compare(int a,int b){var x=Boxes[a];var y=Boxes[b];return (Y?x.MinY+x.MaxY:x.MinX+x.MaxX).CompareTo(Y?y.MinY+y.MaxY:y.MinX+y.MaxX);}
        }
        List<Event> events=new List<Event>();
        List<Box> boxes=new List<Box>();
        SortedSet<double> active=new SortedSet<double>();
        Dictionary<Run,int> previous=new Dictionary<Run,int>(),current=new Dictionary<Run,int>();
        int[] indices=Array.Empty<int>();
        readonly Centers centers=new Centers();
        int used;
        int Tree(Node[] nodes,int begin,int count) {
            int at=used++;double x=double.PositiveInfinity,y=x,xx=double.NegativeInfinity,yy=xx;
            for(int i=begin;i<begin+count;i++){var b=centers.Boxes[indices[i]];x=Math.Min(x,b.MinX);y=Math.Min(y,b.MinY);xx=Math.Max(xx,b.MaxX);yy=Math.Max(yy,b.MaxY);}
            var bounds=new Box(x,y,xx,yy);
            if(count==1)nodes[at]=new Node(bounds,-1,-1,indices[begin]);
            else {
                centers.Y=yy-y>xx-x;Array.Sort(indices,begin,count,centers);
                int half=count/2,left=Tree(nodes,begin,half),right=Tree(nodes,begin+half,count-half);
                nodes[at]=new Node(bounds,left,right,-1);
            }
            return at;
        }
        internal CollisionShape Build(Envelope hull) {
            events.Clear();boxes.Clear();active.Clear();previous.Clear();current.Clear();
            if(hull.Count<3)return null;
            for(int loop=0;loop<hull.Loops;loop++)for(int i=hull.Start(loop);i<hull.End(loop);i++) {
                var a=hull.Raw(i);var b=hull.Raw(i+1==hull.End(loop)?hull.Start(loop):i+1);
                if(a.X==b.X){events.Add(new Event(a.Y,a.X));events.Add(new Event(b.Y,b.X));}
                else if(a.Y!=b.Y)throw new InvalidOperationException("Collision cover requires orthogonal socket contours.");
            }
            events.Sort(Events.Instance);int e=0,work=0;
            while(e<events.Count) {
                double y=events[e].Y;
                do {double x=events[e++].X;if(!active.Remove(x))active.Add(x);}while(e<events.Count&&events[e].Y==y);
                if(e==events.Count)break;
                double top=events[e].Y,start=0;bool opening=true;current.Clear();
                foreach(double x in active) {
                    if(++work>MaxSweepWork)throw new InvalidOperationException("Collision cover exceeded its bounded sweep budget.");
                    if(opening)start=x;
                    else {
                        var run=new Run(start,x);int index;
                        if(previous.TryGetValue(run,out index)&&boxes[index].MaxY==y){var b=boxes[index];boxes[index]=new Box(b.MinX,b.MinY,b.MaxX,top);}
                        else {if(boxes.Count==MaxPieces)throw new InvalidOperationException("Collision cover exceeded its piece limit.");index=boxes.Count;boxes.Add(new Box(start,y,x,top));}
                        current.Add(run,index);
                    }
                    opening=!opening;
                }
                if(!opening)throw new InvalidOperationException("Collision cover has unmatched perimeter edges.");
                var swap=previous;previous=current;current=swap;
            }
            if(active.Count!=0||boxes.Count==0)throw new InvalidOperationException("Collision cover did not close.");
            var pieces=new Box[boxes.Count];
            for(int i=0;i<pieces.Length;i++){var b=boxes[i];var a=hull.Unit(b.MinX,b.MinY);var z=hull.Unit(b.MaxX,b.MaxY);pieces[i]=new Box(a.X,a.Y,z.X,z.Y);}
            var nodes=new Node[pieces.Length*2-1];if(indices.Length<pieces.Length)indices=new int[pieces.Length];
            for(int i=0;i<pieces.Length;i++)indices[i]=i;
            centers.Boxes=pieces;used=0;Tree(nodes,0,pieces.Length);centers.Boxes=null;
            return new CollisionShape(pieces,nodes);
        }
        internal void Release(){events=new List<Event>();boxes=new List<Box>();active=new SortedSet<double>();previous=new Dictionary<Run,int>();current=new Dictionary<Run,int>();indices=Array.Empty<int>();centers.Boxes=null;}
    }
}
