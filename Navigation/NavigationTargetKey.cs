using System;
namespace Ostrabus.ShipNavigation;

internal readonly struct NavigationTargetKey {
    internal readonly string Id, Body;
    readonly bool present, orbital, directShip;
    readonly int flags;
    readonly double offsetX, offsetY, x, y, vx, vy;
    internal NavigationTargetKey(Ship ship) {
        ShipSitu target = ship.shipSituTarget;
        Id = ship.shipScanTarget?.strRegID ?? ship.targetRegID;
        present = target != null;
        Body = target?.strBOPORShip;
        flags = target == null ? 0 : (target.bIsBO ? 1 : 0) | (target.bBOLocked ? 2 : 0) |
            (target.bOrbitLocked ? 4 : 0) | (target.IsRegionBorderPoint ? 8 : 0);
        orbital = (flags & 7) != 0 && !string.IsNullOrEmpty(Body);
        directShip = present && !string.IsNullOrEmpty(Id) && ReferenceEquals(ship.shipScanTarget?.objSS, target);
        offsetX = target?.vBOOffsetx ?? 0; offsetY = target?.vBOOffsety ?? 0;
        x = target?.vPosx ?? 0; y = target?.vPosy ?? 0; vx = target?.vVelX ?? 0; vy = target?.vVelY ?? 0;
    }
    internal bool Same(NavigationTargetKey other) {
        if (Id != other.Id || present != other.present) return false;
        if (!present) return true;
        if (flags != other.flags || orbital != other.orbital) return false;
        if (orbital) return Body == other.Body && offsetX == other.offsetX && offsetY == other.offsetY;
        if (directShip && other.directShip) return true;
        return x == other.x && y == other.y && vx == other.vx && vy == other.vy;
    }
}


