using System;
using HarmonyLib;
using Ostrabus.NavConsole.Outlines;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Sensors;
using UnityEngine;
using Vectrosity;

static partial class Program {
    static float drawnScale;
    static bool toggled;
    static Rect mapRect=new Rect(-320,-320,640,640);
    static bool PanelRect(ref Rect __result){__result=mapRect;return false;}
    static void ObserveToggle(bool showSilhouette){toggled=showSilhouette;}
    static bool CaptureVectorLine(VectorLine vectorLine,double sx,double sy,float fRot,float fScale) {
        drawnScale=fScale;Check(sx==1&&sy==2&&fRot==3,"Outline scale hook changed map placement/rotation");return false;
    }
    static void Diameter(GUIOrbitDraw nav,float metres,double pixels) {
        double xx=pixels/(2*metres*HullZoom.MetresToAU);
        Field(nav,"dCanvasSolarXX",xx);Field(nav,"dCanvasSolarXY",0d);
        Field(nav,"dOffsetSX",320/xx);Field(nav,"dOffsetSY",320/xx);
    }
    static void PanTo(GUIOrbitDraw nav,double x,double y) {
        double xx=(double)AccessTools.Field(typeof(GUIOrbitDraw),"dCanvasSolarXX").GetValue(nav);
        Field(nav,"dOffsetSX",x/xx);Field(nav,"dOffsetSY",y/xx);
    }
    static void PivotCases(GUIOrbitDraw nav,ShipDraw draw) {
        foreach(var rect in new[]{new Rect(0,0,640,640),new Rect(-320,-320,640,640),
            new Rect(-640,-640,640,640),new Rect(-128,-512,640,640)}) {
            mapRect=rect;
            // InitOrbitArea draws its grid at 0..width/height and DrawSystem
            // centers at width/2,height/2, independently of RectTransform pivot.
            // Pan identical geometry at constant zoom across the entire map.
            foreach(double x in new[]{20d,160,250,320,480,620})foreach(double y in new[]{20d,160,320,400,480,620}) {
                PanTo(nav,x,y);
                Check(HullPresentation.Choose(nav,draw,false),"Viewport pivot hid an onscreen hull at "+x+","+y+" with rect "+rect);
            }
            PanTo(nav,-10,320);Check(HullPresentation.Choose(nav,draw,false),"Partly visible hull lost its outline at left edge");
            PanTo(nav,650,320);Check(HullPresentation.Choose(nav,draw,false),"Partly visible hull lost its outline at right edge");
            PanTo(nav,320,-10);Check(HullPresentation.Choose(nav,draw,false),"Partly visible hull lost its outline at bottom edge");
            PanTo(nav,320,650);Check(HullPresentation.Choose(nav,draw,false),"Partly visible hull lost its outline at top edge");
            PanTo(nav,-16,320);Check(!HullPresentation.Choose(nav,draw,true),"Wholly offscreen hull at left requested geometry");
            PanTo(nav,656,320);Check(!HullPresentation.Choose(nav,draw,true),"Wholly offscreen hull at right requested geometry");
            PanTo(nav,320,-16);Check(!HullPresentation.Choose(nav,draw,true),"Wholly offscreen hull at bottom requested geometry");
            PanTo(nav,320,656);Check(!HullPresentation.Choose(nav,draw,true),"Wholly offscreen hull at top requested geometry");
        }
        mapRect=new Rect(-320,-320,640,640);PanTo(nav,320,320);
    }
    static void PresentationCases() {
        var nav=Empty<GUIOrbitDraw>();Field(nav,"rectDrawPanel",Empty<RectTransform>());
        var ship=Hull(Ship.Loaded.Shallow);var situ=Empty<ShipSitu>();situ.Size=500;Field(ship,"<objSS>k__BackingField",situ);
        var line=NativeLine(8);var symbol=NativeLine(8);
        var draw=new ShipDraw(ship){lineBodySilhouette=line,lineBodySymbol=symbol,lineBody=symbol,
            shipInfo=Empty<ShipInfo>(),SignalVisibility=SignalVisibility.Visible};
        draw.shipInfo.shipSignature=81;
        Diameter(nav,situ.Size,30);
        Check(HullPresentation.Choose(nav,draw,false),"Native zoom cutoff still prevents a useful large outline");
        PivotCases(nav,draw);
        draw.shipInfo.shipSignature=80;
        Check(!HullPresentation.Choose(nav,draw,true),"Screen-size rule revealed an unidentified contact's hull");
        Field(nav,"sdNS",draw);
        Check(HullPresentation.Choose(nav,draw,false),"Own ship lost native identification exemption");
        Field(nav,"sdNS",null);draw.shipInfo.shipSignature=81;
        draw.SignalVisibility=SignalVisibility.Partial;Check(!HullPresentation.Choose(nav,draw,true),"Faint contact gained a precise outline");
        draw.SignalVisibility=SignalVisibility.None;Check(!HullPresentation.Choose(nav,draw,true),"Invisible contact requested hull geometry");
        draw.SignalVisibility=SignalVisibility.Visible;
        Diameter(nav,situ.Size,22);Check(!HullPresentation.Choose(nav,draw,true),"Native true flag bypassed screen-size entry rule");
        draw.lineBody=line;Check(HullPresentation.Choose(nav,draw,false),"Existing outline ignored zoom hysteresis");
        Diameter(nav,situ.Size,19);Check(!HullPresentation.Choose(nav,draw,true),"Tiny outline did not revert to a symbol");
        Diameter(nav,situ.Size,30);situ.vPosx=30000*HullZoom.MetresToAU;
        Check(!HullPresentation.Choose(nav,draw,true),"Offscreen contact requested an outline");situ.vPosx=0;
        Check(HullPresentation.Choose(null,draw,true)&&!HullPresentation.Choose(null,draw,false),"Missing nav context did not preserve native selection");

        // Run the patched native Toggle entry, with only its scene mutation body
        // skipped. The observer sees the actual rewritten bool argument.
        var source=AccessTools.Method(typeof(HullDisplay),"Create").Invoke(null,new object[]{ship});Field(source,"NextCapture",double.MaxValue);
        var table=AccessTools.Field(typeof(HullDisplay),"sources").GetValue(null);table.GetType().GetMethod("Add").Invoke(table,new[]{(object)ship,source});
        GUIOrbitDraw.Instance=nav;
        try {
            draw.ToggleSilhouetteDrawMode(false);Check(toggled,"Actual Toggle prefix retained the vanilla distance gate");
            PanTo(nav,250,400);draw.ToggleSilhouetteDrawMode(false);Check(toggled,"Panning above map center hid the actual native outline selection");
            PanTo(nav,250,160);draw.ToggleSilhouetteDrawMode(false);Check(toggled,"Panning below map center hid the actual native outline selection");
            Diameter(nav,situ.Size,10);draw.ToggleSilhouetteDrawMode(true);Check(!toggled,"Actual Toggle prefix kept an unreadably small hull");
        } finally {GUIOrbitDraw.Instance=null;HullDisplay.Reset();}

        HullDisplay.Assign(draw,Polygon(78),situ.Size,0);
        nav.DrawVectorLine(line,1,2,3,.123f);
        Check(drawnScale==(float)HullZoom.MetresToAU,"Detailed hull retained native minimum-icon padding");
        nav.DrawVectorLine(symbol,1,2,3,.123f);Check(drawnScale==.123f,"Symbol lost native minimum-size padding");
        var unrelated=NativeLine(8);nav.DrawVectorLine(unrelated,1,2,3,.456f);
        Check(drawnScale==.456f,"Weapon/status/orbit line scale was modified");
        HullDisplay.Assign(draw,Polygon(0),situ.Size,0);
        nav.DrawVectorLine(line,1,2,3,.123f);Check(drawnScale==.123f,"Empty-hull marker was treated as detailed geometry");
        HullDisplay.Assign(draw,Polygon(78),situ.Size,0);HullDisplay.Reset();
        nav.DrawVectorLine(line,1,2,3,.123f);Check(drawnScale==.123f,"Old renderer tag survived save/scene reset");
    }
}
