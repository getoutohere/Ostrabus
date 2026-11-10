using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;
using Vectrosity;

static partial class Program {
    // Only Unity objects/mesh upload are absent. The shipped Vectrosity setters,
    // ResetLine, Resize, ZeroVertices, colors, UVs and triangles execute intact.
    static VectorLine NativeLine(int count,Joins joins=Joins.Weld) {
        var line=Empty<VectorLine>();int vertices=Math.Max(0,count-1)*4;
        Field(line,"m_is2D",true);Field(line,"m_points2",Enumerable.Range(0,count).Select(i=>new Vector2(i,0)).ToList());
        Field(line,"m_pointsCount",count);Field(line,"m_vertexCount",vertices);
        Field(line,"m_lineType",LineType.Continuous);Field(line,"m_joins",joins);Field(line,"m_capType",EndCap.None);
        Field(line,"m_lineVertices",new Vector3[vertices]);Field(line,"m_lineUVs",new Vector2[vertices]);
        Field(line,"m_lineColors",new Color32[vertices]);Field(line,"m_lineWidths",new[]{1f});Field(line,"m_lineWidth",2f);
        Field(line,"m_lineTriangles",new List<int>());Field(line,"m_drawEnd",count-1);
        AccessTools.Method(typeof(VectorLine),"SetupTriangles").Invoke(line,new object[]{0});
        return line;
    }
    static void SyncLine(VectorLine line) {
        line.SetColor(new Color32(100,150,200,255)); // Native public API forces lazy Resize, just as Draw does.
        int count=line.points2.Count;
        Check(count%2==0&&count<=EnvelopeBuilder.MaxVertices*2,"Invalid edge-pair count reached native Vectrosity");
        Check((int)AccessTools.Field(typeof(VectorLine),"m_pointsCount").GetValue(line)==count,"Native cached point count is stale");
        Check((int)AccessTools.Field(typeof(VectorLine),"m_vertexCount").GetValue(line)==count*2,"Native mesh vertex count disagrees with discrete segments");
        var triangles=(List<int>)AccessTools.Field(typeof(VectorLine),"m_lineTriangles").GetValue(line);
        Check(triangles.Count==count/2*6&&triangles.All(i=>i>=0&&i<count*2),"Native triangles reference invalid mesh vertices");
    }
    static Envelope Polygon(int count) {
        var points=new HullPoint[count];
        for(int i=0;i<count;i++){double angle=i*Math.PI*2/count;points[i]=new HullPoint(Math.Cos(angle),Math.Sin(angle));}
        return new Envelope(points,count);
    }
    static void NativeRendererCases(Ship ship) {
        var broken=NativeLine(5);bool reproduced=false;
        try { broken.points2=new List<Vector2>();broken.lineType=LineType.Discrete; }
        catch(IndexOutOfRangeException){reproduced=true;}
        Check(reproduced,"Old empty-list transition did not reproduce the live Vectrosity exception");
        foreach(var joins in new[]{Joins.Fill,Joins.Weld,Joins.None})foreach(int initial in new[]{0,5,8,100}) {
            var line=NativeLine(initial,joins);var draw=new ShipDraw(ship){lineBodySilhouette=line,silhouetteDrawPoints=line.points2};
            foreach(int count in new[]{3,78,EnvelopeBuilder.MaxVertices,4,0,3}) {
                var geometry=Polygon(count);HullDisplay.Assign(draw,geometry,250,0);
                Check(line.lineType==LineType.Discrete&&ReferenceEquals(line.points2,draw.silhouetteDrawPoints),"Native renderer lost its shared point buffer");
                Check(count==0?line.points2.Count>0:line.points2.Count==count*2,"Native transition truncated exterior vertices");
                SyncLine(line);
            }
        }
        var separate=new ShipDraw(ship){lineBodySilhouette=NativeLine(5)};
        HullDisplay.Assign(separate,Polygon(78),250,0);SyncLine(separate.lineBodySilhouette);
    }
}
