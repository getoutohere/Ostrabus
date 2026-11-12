using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.ShipNavigation.Towing;
using SolarSystem;

static partial class Program {
    static Dictionary<string, Ship> Links(Ship ship) => (Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship);
    static void AddDepartureMember(DockGroup group, Ship ship, bool mooring = false) {
        ship.objSS._dockGroup = group;
        group.DockedShips.Add(new DockGroupMember(ship, mooring));
    }
    static void ConnectDeparture(Ship a, Ship b, string portA, string portB, bool braceA = false, bool braceB = false) {
        Links(a)[portA] = b; Links(b)[portB] = a;
        if (braceA) Set(a, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = portA });
        if (braceB) Set(b, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = portB });
    }

    static void StationDepartureChecks() {
        int before = checks;
        foreach (string boundary in new[] { "orbit", "substation", "ground", "mooring" }) foreach (bool reverse in new[] { false, true }) {
            var tug = Make("DEPART-TUG", 25000); var tow = Make("DEPART-TOW", 75000);
            var station = Make("DEPART-STATION", 500000); var visitor = Make("DEPART-VISITOR", 60000);
            var group = Join(tug, tow);
            AddDepartureMember(group, station, boundary == "mooring"); AddDepartureMember(group, visitor);
            if (boundary == "orbit") station.objSS.bIsBO = true;
            if (boundary == "substation") Set(station, "_subStation", true);
            if (boundary == "ground") station.Classification = Ship.TypeClassification.GroundStation;
            ConnectDeparture(tug, station, "station-port", "tug-port");
            ConnectDeparture(station, visitor, "visitor-port", "station-port", braceA: true);
            if (reverse) {
                group.DockedShips.Reverse();
                Set(tug, "aSecuredTowBraces", null);
                Set(tow, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = "b" });
            }
            Near(DockedCoursePlayback.InertialMass(tug), 660000, "Physical station group mass was changed");
            Near(DockedCoursePlayback.PredictionMass(tug), 100000, "Station departure included the station/visitors or omitted the tow");
            Near(tug.GetMaxTorchThrust(1), 10d/149597872000, "Native maximum-acceleration preview counted station mass", 1e-17);
            Course(tug); group.Anchor.bIsBO = true;
            Check(DockedCoursePlayback.FindSource(group) == null, "Station preview made its anchored group movable");
            tug.objSS.NavData = null;
            tug.SetThrust(1000000);
            Near(tug.objSS.vAccIn.magnitude, (1000000d/660000)/149597872000, "Manual thrust ignored physically attached station mass", 1e-17);
            var tail = Make("DEPART-TAIL", 50000); AddDepartureMember(group, tail);
            ConnectDeparture(tow, tail, "tail-port", "tow-port", braceB: true);
            Near(DockedCoursePlayback.PredictionMass(tug), 150000, "Braced departure chain omitted a downstream hull");
            Set(tug, "aSecuredTowBraces", null); Set(tow, "aSecuredTowBraces", null);
            Near(DockedCoursePlayback.PredictionMass(tug), 25000, "Released brace still counted in intended departure");
            Near(DockedCoursePlayback.InertialMass(tug), 710000, "Releasing a brace erased physical mass");
            // Even a secured station link cannot make a station/its visitors a tow.
            Set(tug, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = "station-port" });
            Near(DockedCoursePlayback.PredictionMass(tug), 25000, "Secured station link made the station towable");
            for (int i = 0; i < 20; i++) DockedCoursePlayback.PredictionMass(tug);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) DockedCoursePlayback.PredictionMass(tug);
            allocated = GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(allocated == 0, "Repeated station departure previews allocated " + allocated + " bytes");
            var scratch = AccessTools.Field(typeof(DockedCoursePlayback), "departureWalk").GetValue(null);
            Check(!((bool)AccessTools.Field(scratch.GetType(), "Busy").GetValue(scratch)), "Departure scope stayed busy");
            Check(((List<Ship>)AccessTools.Field(scratch.GetType(), "Pending").GetValue(scratch)).Count == 0 &&
                ((HashSet<Ship>)AccessTools.Field(scratch.GetType(), "Seen").GetValue(scratch)).Count == 0,
                "Departure scratch retained live hulls");
        }

        var freeTug = Make("FREE-UNBRACED-TUG", 25000); var freeTow = Make("FREE-UNBRACED-TOW", 75000);
        Join(freeTug, freeTow); Set(freeTug, "aSecuredTowBraces", null);
        Near(DockedCoursePlayback.PredictionMass(freeTug), 100000, "Free unbraced hull stopped contributing to acceleration estimates");
        Near(freeTug.GetMaxTorchThrust(1), 10d/149597872000, "Free unbraced native thrust estimate changed", 1e-17);
        freeTug.SetThrust(1000000);
        Near(freeTug.objSS.vAccIn.magnitude, 10d/149597872000, "Free unbraced manual acceleration changed", 1e-17);
        Check(!freeTug.TowBracesSecured(), "Mass preview granted brace protection");
        Console.WriteLine("Station departure: " + (checks-before) + " native preview/physical-mass, brace, chain, visitor, mooring and allocation checks passed.");
    }
}
