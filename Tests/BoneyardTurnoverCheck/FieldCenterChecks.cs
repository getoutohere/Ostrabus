using System;
using System.Linq;
using HarmonyLib;
using Ostrabus.BoneyardTurnover;
using SolarSystem;

internal static partial class Program
{
    private static StellarShip SeedCatalogCenter(Ship regional, JsonEnvironmentMap map, JsonSpawnDerelict ring)
    {
        var entity = map.aEntities.Single(x => x.bIsStation && !x.bOrbitBO &&
            x.fDistanceKM == ring.fAltSpawnDistanceKM && x.fAngle == ring.fAltSpawnAngle);
        var reference = string.IsNullOrEmpty(entity.strRegIDRoot) ? regional : CrewSim.system.GetShipByRegID(entity.strRegIDRoot);
        if (reference == null)
        {
            reference = ProductionHull(entity.strRegIDRoot).Hull;
            reference.objSS.bIsBO = true;
            reference.objSS.strBOPORShip = entity.strRegIDRoot;
            reference.objSS.vBOOffsetx = reference.objSS.vBOOffsety = 0;
        }
        reference.objSS.UpdateTime(StarSystem.fEpoch);
        bool ground = reference.Classification == Ship.TypeClassification.GroundStation || reference.Classification == Ship.TypeClassification.GroundStationUnfinished;
        var parent = CrewSim.system.GetNearestBO(reference.objSS, StarSystem.fEpoch, !ground);
        parent.UpdateTime(StarSystem.fEpoch);
        var position = (Ostranauts.Utils.Models.Point)AccessTools.Method(typeof(EnvironmentEntities), "CalculateEntityPosition")
            .Invoke(null, new object[] { parent.vPos, reference.objSS.vPos, entity.fDistanceKM, entity.fAngle });
        string id = (string)AccessTools.Method(typeof(EnvironmentEntities), "GenerateRegID").Invoke(null, new object[] { entity, regional.strRegID });
        var marker = new StellarShip(new JsonStellarShipSave
        {
            strID = id, strLoot = entity.strShipName, strOwner = entity.strOwner ?? CrewSim.system.GetShipOwner(regional.strRegID),
            nMapID = map.MapID, Classification = entity.Classification, strParent = parent.strName,
            vBOOffsetx = position.X - parent.dXReal, vBOOffsety = position.Y - parent.dYReal
        });
        CrewSim.system.dictStellarObjects.Add(id, marker);
        return marker;
    }

    private static void FieldCenterChecks()
    {
        ProductionField();
        var regional = ProductionHull("BT-CENTER").Hull;
        regional.Classification = Ship.TypeClassification.GroundStation;
        regional.objSS.bIsBO = regional.objSS.bIsRegion = regional.objSS.bGrounded = true;
        regional.objSS.vBOOffsetx = 200 / Ostrabus.BoneyardTurnover.Field.AuKm;
        var entity = new JsonEnvironmentEntity { bIsStation = true, strRegIDSuffix = "FD01", strShipName = "study-center",
            strClassification = "Infrastructure", fDistanceKM = 750, fAngle = -5 };
        var ring = new JsonSpawnDerelict { strLootShipType = "RandomDerelictJFTS", strOwner = "study-yard",
            bIsBodyLocked = true, fAltSpawnDistanceKM = 750, fAltSpawnAngle = -5, fSpawnRadiusMinKM = 20, fSpawnRadiusMaxKM = 60 };
        var map = new JsonEnvironmentMap { strName = "1005", aEntities = new[] { entity }, aSpawnDerelictRings = new[] { ring } };
        var marker = SeedCatalogCenter(regional, map, ring);
        var anchor = CrewSim.system.GetBO("study-anchor");
        bool Read() => FieldCenter.TryRead(regional, map, ring, anchor, out _);
        Check(Read(), "Saved deferred native field centre rejected.");
        FieldCenter.TryRead(regional, map, ring, anchor, out var before);
        regional.objSS.vBOOffsetx = -regional.objSS.vBOOffsetx;
        Check(FieldCenter.TryRead(regional, map, ring, anchor, out var rotated) && (rotated - before).magnitude == 0,
            "Rotating ground reference moved the saved field centre.");
        regional.objSS.vBOOffsetx = -regional.objSS.vBOOffsetx;
        map.aEntities = new[] { entity, entity };
        Check(!Read(), "Ambiguous authored centre accepted.");
        map.aEntities = new[] { entity };
        CrewSim.system.dictStellarObjects.Remove(marker.strID);
        Check(!Read(), "Missing centre was reconstructed from current ground bearing.");
        CrewSim.system.dictStellarObjects.Add(marker.strID, marker);
        int oldMap = marker.MapID; marker.MapID++;
        Check(!Read(), "Centre from a different environment accepted."); marker.MapID = oldMap;
        string owner = marker.strOwner; marker.strOwner = "player";
        Check(!Read(), "Claimed centre accepted."); marker.strOwner = owner;
        string loot = marker.strLoot; marker.strLoot = "other-template";
        Check(!Read(), "Wrong template accepted as the centre."); marker.strLoot = loot;
        string parent = marker.objSS.strBOPORShip; marker.objSS.strBOPORShip = "other-body";
        Check(!Read(), "Reattached centre accepted."); marker.objSS.strBOPORShip = parent;
        marker.objSS.fOrbitW = 1e-6;
        Check(!Read(), "Orbiting centre accepted for a fixed offset field."); marker.objSS.fOrbitW = 0;
        marker.objSS.bBOLocked = false;
        Check(!Read(), "Free centre accepted."); marker.objSS.bBOLocked = true;
        marker.objSS.bOrbitLocked = true;
        Check(!Read(), "Alternative orbit lock accepted."); marker.objSS.bOrbitLocked = false;
        double x = marker.objSS.vBOOffsetx; marker.objSS.vBOOffsetx *= 2;
        Check(!Read(), "Radially displaced centre accepted."); marker.objSS.vBOOffsetx = x;
        var physical = ProductionHull(marker.strID).Hull;
        physical.Classification = entity.Classification;
        physical.json.strName = entity.strShipName;
        fixtureConditions[physical.ShipCO.strID]["EnvironmentMapID"] = map.MapID;
        Field(physical, "<objSS>k__BackingField", marker.objSS);
        Check(!Read(), "Duplicate physical/deferred centre accepted.");
        CrewSim.system.dictStellarObjects.Remove(marker.strID);
        Check(Read(), "Physical centre rejected after deferred conversion.");
        physical.json.strName = physical.strRegID;
        Check(Read(), "Native physical save's registry-name substitution rejected.");
        physical.objSS._dockGroup = new DockGroup();
        Check(Read(), "Docking visitor replaced or invalidated a native stationary centre.");
        physical.objSS._dockGroup = null;
        physical.objSS.bIsBO = false;
        Check(!Read(), "Mobile hull accepted as native stationary centre.");
        physical.objSS.bIsBO = true;
        physical.Classification = Ship.TypeClassification.Default;
        Check(!Read(), "Wrong classification accepted as centre.");
        Console.WriteLine("Saved field centres: deferred/physical conversion, rotating references, passive visitors and missing/duplicate/claimed/displaced/wrong-motion refusals pass.");
    }
}
