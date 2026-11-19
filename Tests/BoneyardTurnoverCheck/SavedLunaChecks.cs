using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using Ostranauts.Utils.Models;
using SolarSystem;
using BTField = Ostrabus.BoneyardTurnover.Field;

internal static partial class Program
{
    private static void SavedLunaChecks()
    {
        // Only geometry and counts from the coast save; no personal records,
        // inventories, global positions or machine-specific save paths.
        var fixture = JObject.Parse(File.ReadAllText(Path.Combine(
            Path.GetDirectoryName(typeof(Program).Assembly.Location), "LunaGeometry.json")));
        ProductionField();
        StarSystem.fEpoch = (double)fixture["epoch"];
        var orbit = fixture["anchorOrbit"];
        var anchor = CrewSim.system.GetBO("study-anchor");
        CrewSim.system.aBOs.Remove(anchor.strName);
        anchor.strName = "EJDR_GEO";
        anchor.nDrawFlagsBody = (int)orbit["nDrawFlagsBody"];
        CrewSim.system.aBOs[anchor.strName] = anchor;
        anchor.dTimeCalcLast = anchor.dVelCalcLast = double.NegativeInfinity;
        anchor.fPeriod = (double)orbit["fPeriod"];
        anchor.fPeriodShift = (double)orbit["fPeriodShift"];
        anchor.fAxis1 = (double)orbit["fAxis1"];
        anchor.fAxis2 = (double)orbit["fAxis2"];
        anchor.fPerh = (double)orbit["fPerh"];
        anchor.fEcc = (double)orbit["fEcc"];
        anchor.fAngle = (double)orbit["fAngle"];
        anchor.nOrbitDirection = (int)orbit["nOrbitDirection"];
        anchor.UpdateTime(StarSystem.fEpoch);
        var reference = ProductionHull(anchor.strName).Hull;
        reference.objSS.bIsBO = true;
        reference.objSS.vBOOffsetx = reference.objSS.vBOOffsety = 0;
        reference.objSS.strBOPORShip = anchor.strName;
        reference.Classification = Ship.TypeClassification.OrbitalStation;
        reference.objSS.UpdateTime(StarSystem.fEpoch);

        var station = ProductionHull("BT-LUNA").Hull;
        station.objSS.bIsRegion = station.objSS.bIsBO = true;
        station.objSS.strBOPORShip = anchor.strName;
        station.Classification = Ship.TypeClassification.OrbitalStation;
        fixtureConditions[station.ShipCO.strID]["EnvironmentMapID"] = 1000;
        FieldStatic(typeof(AIShipManager), "dictSpawnRegIDs", new Dictionary<string, List<string>>());
        var ring = new JsonSpawnDerelict
        {
            strLootShipType = "RandomDerelictEJDR_GEO", strSpawnAroundBody = anchor.strName,
            strOwner = "study-yard", bIsBodyLocked = true, fAltSpawnDistanceKM = 3500,
            fAltSpawnAngle = 250, fSpawnRadiusMinKM = 100, fSpawnRadiusMaxKM = 150
        };
        DataHandler.dictEnvironmentMaps = new Dictionary<string, JsonEnvironmentMap>
        {
            ["1000"] = new JsonEnvironmentMap
            {
                strName = "1000", aSpawnDerelictRings = new[] { ring },
                aEntities = new[] { new JsonEnvironmentEntity
                {
                    strRegIDSuffix = "FRD2", strRegIDRoot = anchor.strName, bIsStation = true,
                    fDistanceKM = 3500, fAngle = 250, strShipName = "study-center", strClassification = "Infrastructure"
                } }
            }
        };
        SeedCatalogCenter(station, DataHandler.dictEnvironmentMaps["1000"], ring);
        var field = BTField.ForRegion(station.strRegID).Single();
        int index = 0;
        foreach (var saved in fixture["wrecks"])
        {
            var old = ProductionHull("BT-SAVED-" + index++);
            old.Situ.strBOPORShip = anchor.strName;
            old.Situ.vBOOffsetx = (double)saved["offset"][0];
            old.Situ.vBOOffsety = (double)saved["offset"][1];
            fixtureConditions[old.Hull.ShipCO.strID]["EnvironmentMapID"] = 1000;
            old.Hull.json.aShallowPSpecs = saved["spawnDefinitions"].Values<string>().Select((definition, i) =>
            {
                var marker = ProductionMarker(old.Hull, old.Id + "-marker-" + i, definition);
                return new JsonItem { strName = definition, strID = marker.strID };
            }).ToArray();
            Check(new Eligibility().Allows(field, old), "Saved Luna wreck rejected after marker repair: " + old.Id);
        }
        Check(index == 19, "Saved Luna reproduction lost candidates.");
        AIShipManager.strATCLast = station.strRegID;
        CollisionManager.strATCClosest = "BT-AWAY";
        Ostrabus.BoneyardTurnover.Runtime.Loaded(null);
        var departure = Ostrabus.BoneyardTurnover.Runtime.BeforeDeparture();
        Check(departure?.Fields.Count == 1 && departure.Fields[0].Ages.Count == 19,
            "Saved Luna candidates did not become a departure cohort.");
        AIShipManager.strATCLast = "BT-AWAY";
        Ostrabus.BoneyardTurnover.Runtime.AfterDeparture(departure);
        Check(Ostrabus.BoneyardTurnover.Runtime.State.Fields.Single().Ages.Count == 19,
            "Successful native transition lost the saved Luna cohort.");
        // Use the actual native reference resolver, not an assumed natural
        // parent. EJDR_GEO is a placeholder but is not a hyphenated ship orbit.
        var nearest = CrewSim.system.GetNearestBO(reference.objSS, StarSystem.fEpoch, true);
        Check(ReferenceEquals(nearest, anchor) && !anchor.IsShipOrbit(),
            "Native Luna centre reference was replaced by an assumed natural body.");
        var place = AccessTools.MethodDelegate<Func<Point, Point, double, float, Point>>(
            AccessTools.Method(typeof(EnvironmentEntities), "CalculateEntityPosition"));
        var recalculated = place(nearest.vPos, reference.objSS.vPos, 3500, 250);
        var offsetToday = recalculated - anchor.vPos;
        var savedCenter = new Point((double)fixture["centerOffset"][0], (double)fixture["centerOffset"][1]);
        Check((offsetToday - savedCenter).magnitude * BTField.AuKm < .001,
            "Native Luna centre calculation disagrees with the saved yard.");
        Console.WriteLine("Saved Luna reproduction: 19 wrecks with 2-3 native spawn markers admitted and departure committed; actual native reference selection and saved centre agree within 1 metre.");
    }
}
