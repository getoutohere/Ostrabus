using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Venus;

static class OrbitCompatibility {
    static BodyOrbit planet, station;
    static bool Clock(BodyOrbit __instance, double dTime) { __instance.dTimeCalcLast = dTime; return false; }
    static bool Nearest(bool bIncludePlaceholders, ref BodyOrbit __result) {
        __result = bIncludePlaceholders ? station : planet; return false;
    }
    internal static void Run(Action<bool, string> check) {
        var harmony = new Harmony("offline.venus.orbit-compatibility");
        var oldSystem = CrewSim.system;
        double oldEpoch = StarSystem.fEpoch;
        try {
            harmony.Patch(AccessTools.Method(typeof(BodyOrbit), nameof(BodyOrbit.UpdateTime)),
                prefix: new HarmonyMethod(typeof(OrbitCompatibility), nameof(Clock)));
            harmony.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO), new[] { typeof(ShipSitu), typeof(double), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(OrbitCompatibility), nameof(Nearest)));
            CrewSim.system = (StarSystem)FormatterServices.GetUninitializedObject(typeof(StarSystem));
            planet = (BodyOrbit)FormatterServices.GetUninitializedObject(typeof(BodyOrbit));
            planet.strName = "Venus"; planet.fMass = 4.8675e24;
            station = (BodyOrbit)FormatterServices.GetUninitializedObject(typeof(BodyOrbit)); station.strName = "VORB";
            CrewSim.system.aBOs = new Dictionary<string, BodyOrbit> { ["Venus"] = planet, ["VORB"] = station };
            StarSystem.fEpoch = 10000;
            double radius = 9000 / 149597872d, rate = planet.GetOrbitW(radius);
            var situation = new ShipSitu(false) { vPosx = radius, vVelY = rate * radius };
            check(situation.TryRelockToBO(planet) && situation.fOrbitW > 0 && situation.bBOLocked,
                "Native circular-speed relock was lost");
            double circularRate = situation.fOrbitW;
            situation.UnlockFromBO();
            check(!situation.bBOLocked && situation.fOrbitW == 0 && situation.vVelY == circularRate * radius,
                "Native circular release must preserve tangential momentum");
            var wreck = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship)); wreck.DMGStatus = Ship.Damage.Derelict;
            AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(wreck, situation);
            station.dXReal = radius; station.dVelY = situation.vVelY;
            var selected = VorbUndockAnchorPatch.Reference(CrewSim.system, situation, StarSystem.fEpoch, false, wreck);
            check(ReferenceEquals(selected, station) && situation.fOrbitW == 0 && !situation.bBOLocked,
                "VORB selection changed motion before native relocking");
            check(situation.TryRelockToBO(selected) && situation.fOrbitW == 0 && situation.strBOPORShip == "VORB",
                "Eligible slow VORB wreck no longer relocks to its station reference");
            situation.UnlockFromBO(); situation.vVelY += 1000 / 149597872d;
            check(!situation.TryRelockToBO(selected) && !situation.bBOLocked,
                "VORB correction bypassed native speed rejection");
            wreck.DMGStatus = Ship.Damage.Used;
            check(ReferenceEquals(VorbUndockAnchorPatch.Reference(CrewSim.system, situation, StarSystem.fEpoch, false, wreck), planet),
                "Ordinary hull lost its native physical-body reference");
        } finally { harmony.UnpatchSelf(); CrewSim.system = oldSystem; StarSystem.fEpoch = oldEpoch; }
    }
}
