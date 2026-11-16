using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Trading;

static partial class Program
{
    static int maneuverStops;
    static bool NoCargo(ref double __result)
    {
        __result = 0;
        return false;
    }

    static bool Stop()
    {
        maneuverStops++;
        return false;
    }

    static void Field(object owner, string name, object value) => AccessTools.Field(owner.GetType(), name).SetValue(owner, value);
    static void NativeMovement(JsonShip metadata, Ostrabus.SaveCompatibility.LegacyRcsCaches.Result restored)
    {
        var boundary = new Harmony("offline.save-forward.movement");
        try
        {
            boundary.Patch(AccessTools.Method(typeof(MarketManager), nameof(MarketManager.GetCargoMassForShip)), prefix: new HarmonyMethod(typeof(Program), nameof(NoCargo)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.UpdateDockedShips)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), prefix: new HarmonyMethod(typeof(Program), nameof(Stop)));
            var ship = Empty<Ship>();
            ship.strRegID = metadata.strRegID;
            Field(ship, "nLoadState", Ship.Loaded.Shallow);
            Field(ship, "fShallowMass", 30000d);
            Field(ship, "<objSS>k__BackingField", new ShipSitu(false));
            Field(ship, "aDocked", new Dictionary<string, Ship>());
            Field(ship, "aNavs", new List<CondOwner>());
            Field(ship, "fFuelEfficiencyMod", 1d);
            Field(ship, "nRCSDistroCount", 1);
            ship.RCSCount = metadata.nRCSCount;
            ship.fShallowRCSRemass = 300;
            maneuverStops = 0;
            ship.Maneuver(1, 0, 0, 0, 1);
            Check(maneuverStops > 0 && ship.objSS.vAccRCS.x == 0, "Native missing-cache control unexpectedly steered");
            // Fixture boundary: the two direct assignments made by the native
            // InitShip shallow branch, after the tested load prefix restores JSON.
            Field(ship, "nRCSCountRaw", restored.Count);
            Field(ship, "fRCSExhaustV", restored.Exhaust);
            maneuverStops = 0;
            ship.Maneuver(1, 0, 0, 0, 1);
            Check(maneuverStops == 0 && ship.objSS.vAccRCS.x > 0, "Restored data did not enable actual native maneuvering");
        }
        finally
        {
            boundary.UnpatchSelf();
        }
    }
}
