using HarmonyLib;

namespace Ostrabus.ShipNavigation;

internal static class NavigationRoute {
    static readonly AccessTools.FieldRef<Ship, bool> TorchActive = AccessTools.FieldRefAccess<Ship, bool>("bTorchDriveThrusting");
    // Retargeting abandons the old commanded maneuver, never the vessel's momentum.
    internal static void Stop(Ship ship) {
        ship.StopManeuver(ship.LoadState >= Ship.Loaded.Edit, false);
        ship.objSS.ResetNavData();
        if (ship.objSS.vAccIn.x != 0 || ship.objSS.vAccIn.y != 0 || TorchActive(ship)) ship.SetThrust(0);
    }
}
