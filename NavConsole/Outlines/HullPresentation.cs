using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;
using Vectrosity;

namespace Ostrabus.NavConsole.Outlines;

internal static class HullPresentation {
    sealed class Kind { internal bool Detailed; }
    static ConditionalWeakTable<VectorLine, Kind> kinds = new ConditionalWeakTable<VectorLine, Kind>();
    static readonly AccessTools.FieldRef<GUIOrbitDraw, double> XX = AccessTools.FieldRefAccess<GUIOrbitDraw, double>("dCanvasSolarXX");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, double> XY = AccessTools.FieldRefAccess<GUIOrbitDraw, double>("dCanvasSolarXY");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, RectTransform> Panel = AccessTools.FieldRefAccess<GUIOrbitDraw, RectTransform>("rectDrawPanel");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, ShipDraw> Own = AccessTools.FieldRefAccess<GUIOrbitDraw, ShipDraw>("sdNS");
    static Kind Create(VectorLine _) => new Kind();
    internal static bool Choose(GUIOrbitDraw nav, ShipDraw draw, bool native) {
        if (nav == null) return native;
        if (draw?.ship?.objSS == null || draw.ship.bDestroyed || !draw.IsVisible || draw.lineBodySilhouette == null ||
            (!ReferenceEquals(draw, Own(nav)) && (draw.shipInfo == null || draw.shipInfo.shipSignature <= 80))) return false;
        var panel = Panel(nav);
        if (panel == null) return native;
        double radiusSquared = HullZoom.RadiusSquared(HullDisplay.Radius(draw.ship), XX(nav), XY(nav));
        if (!HullZoom.Show(radiusSquared, ReferenceEquals(draw.lineBody, draw.lineBodySilhouette))) return false;
        nav.SolarToCanvas(draw.ship.objSS.vPosx, draw.ship.objSS.vPosy, out double x, out double y);
        var rect = panel.rect;
        // SolarToCanvas returns the bottom-left map drawing coordinates used by
        // InitOrbitArea (0..width/height), not RectTransform's pivot-local bounds.
        return HullZoom.OnMap(x, y, radiusSquared, 0, 0, rect.width, rect.height);
    }
    internal static void Mark(VectorLine line, bool detailed) => kinds.GetValue(line, Create).Detailed = detailed;
    internal static bool Detailed(VectorLine line) => line != null && kinds.TryGetValue(line, out var kind) && kind.Detailed;
    internal static float Scale(VectorLine line, float native) => Detailed(line) ? (float)HullZoom.MetresToAU : native;
    internal static void Reset() => kinds = new ConditionalWeakTable<VectorLine, Kind>();
}

[HarmonyPatch(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.DrawVectorLine))]
internal static class HullScalePatch {
    static void Prefix(VectorLine vectorLine, ref float fScale) => fScale = HullPresentation.Scale(vectorLine, fScale);
}
