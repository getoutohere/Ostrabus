using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.ShipNavigation.Towing;
using SolarSystem;

static partial class Program {
    static int catastrophicTowCalls;
    static float catastrophicTowModifier;
    static bool TowDamageRandom(float minInclusive, float maxInclusive, ref float __result) {
        __result = (minInclusive+maxInclusive)*.5f;
        return false;
    }
    static bool CaptureCatastrophicTow(float fDmgModifier, ref float maneuverWear) {
        Check(maneuverWear > 30, "Native tow damage ran below its stress threshold");
        catastrophicTowCalls++; catastrophicTowModifier = fDmgModifier;
        maneuverWear = 0;
        return false;
    }
    static void BraceAndAssetChecks(Harmony engine) {
        var definitions = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "condowners", "condowners.json")));
        var secured = definitions.Single(item => (string)item["strName"] == "ItmTowingBrace01Secured");
        Check(secured["aStartingConds"].Any(item => ((string)item).StartsWith("IsTowingSecured=", StringComparison.Ordinal)), "Native secured brace lost its authored marker.");
        Check(secured["aInteractions"].Any(item => (string)item == "ACTTowingRelease"), "Native brace release policy changed.");
        var triggers = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "condtrigs", "condtrigs.json")));
        var trigger = triggers.Single(item => (string)item["strName"] == "TIsTowingBrace01InstalledSecure");
        foreach (string required in new[] { "IsEquipmentTowing", "IsInstalled", "IsTowingSecured" })
            Check(trigger["aReqs"].Any(item => (string)item == required), "Native brace lookup no longer requires " + required);

        // Native wear/brace methods execute here. The fixture only supplies the
        // reverse-side lookup, which is bypassed by a secured local brace.
        var tug = Make("BRACED-TUG", 25000); var tow = Make("BRACED-TOW", 75000);
        Join(tug, tow);
        Set(tug, "aSecuredTowBraces", null);
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship> { [tug.strRegID] = tug, [tow.strRegID] = tow };
        engine.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), HarmonyPatchType.Prefix, engine.Id);
        try {
            Check(!tug.TowBracesSecured(), "Unsecured fixture unexpectedly has a brace.");
            tug.AccrueTowGroupLoad(.2, 10);
            Near((float)AccessTools.Field(typeof(Ship), "fTowWearManeuver").GetValue(tug), 2, "Unbraced towing lost its native wear accumulation", 1e-6);
            Set(tug, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = "a" });
            Check(tug.TowBracesSecured(), "Native secured brace is not recognized.");
            tug.AccrueTowGroupLoad(.2, 10);
            Near((float)AccessTools.Field(typeof(Ship), "fTowWearManeuver").GetValue(tug), 0, "Secured brace lost native wear protection", 1e-6);
            var damageBoundary = new Harmony("offline.torch-tow.damage-boundary");
            try {
                damageBoundary.Patch(AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(float), typeof(float) }),
                    prefix: new HarmonyMethod(typeof(Program), nameof(TowDamageRandom)));
                damageBoundary.Patch(AccessTools.Method(typeof(Ship), "WearManeuver", new[] { typeof(CondTrigger), typeof(bool), typeof(float), typeof(float).MakeByRefType() }),
                    prefix: new HarmonyMethod(typeof(Program), nameof(CaptureCatastrophicTow)));
                catastrophicTowCalls = 0; catastrophicTowModifier = 0;
                Set(tug, "aSecuredTowBraces", null);
                tug.AccrueTowGroupLoad(3, 11);
                Check(catastrophicTowCalls == 1 && catastrophicTowModifier > 100000,
                    "Unbraced thrust no longer reaches native catastrophic tow damage");
                Set(tug, "aSecuredTowBraces", new Dictionary<string, string> { ["brace"] = "a" });
                tug.AccrueTowGroupLoad(3, 11);
                Check(catastrophicTowCalls == 1, "Secured brace lost protection from catastrophic tow damage");
            } finally { damageBoundary.UnpatchSelf(); }
        } finally {
            engine.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), prefix: new HarmonyMethod(typeof(Program), nameof(Wear)));
        }

        var ordinary = Join(Make("MANUAL-A", 25000), Make("MANUAL-B", 75000));
        ordinary.Anchor.vVelX = 1e-8;
        ordinary.Anchor.vAccIn.x = 2e-12f;
        StarSystem.fEpoch = 1050;
        ordinary.TimeAdvance(10);
        Near(ordinary.Anchor.vPosx, 1e-7 + .5 * (double)2e-12f * 100, "No-course group integration changed");
        Check(DockedCoursePlayback.FindSource(ordinary) == null, "Manual group acquired a course.");
        Console.WriteLine("Native brace assets, secured/unsecured wear and ordinary manual group controls passed.");
    }
}
