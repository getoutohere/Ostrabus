using System;

namespace Ostrabus.NavConsole.Outlines;

// Same map-canvas pixel basis as the native six-pixel icon padding. Work only
// with the unpadded hull footprint, so icon enlargement cannot reveal details.
internal static class HullZoom {
    internal const double MetresToAU = 6.684587E-12;
    internal const double EnterPixels = 26, LeavePixels = 20;
    internal static double RadiusSquared(float metres, double xx, double xy) {
        if (!(metres > 0) || float.IsInfinity(metres)) return 0;
        double radius = metres * MetresToAU;
        double squared = radius * radius * (xx * xx + xy * xy);
        return double.IsNaN(squared) || double.IsInfinity(squared) ? 0 : squared;
    }
    internal static bool Show(double radiusSquared, bool wasOutline) {
        double diameter = wasOutline ? LeavePixels : EnterPixels;
        return radiusSquared > 0 && radiusSquared * 4 >= diameter * diameter;
    }
    internal static bool OnMap(double x, double y, double radiusSquared, double minX, double minY, double maxX, double maxY) {
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y) ||
            !(maxX > minX) || !(maxY > minY) || !(radiusSquared > 0)) return false;
        double dx = x < minX ? minX - x : x > maxX ? x - maxX : 0;
        double dy = y < minY ? minY - y : y > maxY ? y - maxY : 0;
        return dx * dx + dy * dy <= radiusSquared;
    }
}
