using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.Utils.Models;
using SolarSystem;

namespace Ostrabus.BoneyardTurnover
{
    internal sealed class Field
    {
        internal const double AuKm = 149597872d;
        internal string Key, Region, Definition;
        internal int Map;
        internal JsonSpawnDerelict Ring;
        internal BodyOrbit Anchor;
        internal Point Center;
        internal double InnerKm, OuterKm;

        // Explicit reviewed map/pool pairs keep active yards and Earth out of
        // scope. Definitions are still read from the game's merged JSON assets.
        private static readonly Dictionary<int, string> Pools = new Dictionary<int, string>
        {
            [100] = "RandomDerelictHQCH", [1000] = "RandomDerelictEJDR_GEO",
            [1002] = "RandomDerelictMTRS_Phobos", [10020] = "RandomDerelictMTRS_Phobos",
            [1005] = "RandomDerelictJFTS", [1008] = "RandomDerelictJFTS",
            [1006] = "RandomDerelictSVIR", [1007] = "RandomDerelictSVIR",
            [102] = "RandomDerelictMSUZ", [1020] = "RandomDerelictMSUZ", [103] = "RandomDerelictJPTN"
        };

        internal static List<Field> ForRegion(string region)
        {
            var fields = new List<Field>();
            if (string.IsNullOrEmpty(region)) return fields;
            var world = CrewSim.system;
            var stationIds = AIShipManager.GetAllStationsInATCRegion(region).Distinct().ToArray();
            // Authored station capabilities matter even when their hauler fleet
            // is temporarily missing. Never supply a second replenishment model.
            foreach (string id in stationIds)
            {
                var station = world.GetShipByRegID(id);
                if (station?.ShipCO != null && (station.ShipCO.HasCond("StationIsDerelictCollector") ||
                    station.ShipCO.HasCond("StationIsDerelictSpawner"))) return fields;
            }
            foreach (string id in stationIds)
            {
                var station = world.GetShipByRegID(id);
                // Native SpawnATCEnvironment is called only for regional
                // references. Its generated buoys/outposts inherit the map ID
                // but do not each own another copy of the derelict ring.
                if (station?.ShipCO == null || station.objSS == null || !station.objSS.bIsRegion) continue;
                int map = EnvironmentEntities.GetMapID(station);
                if (!Pools.TryGetValue(map, out var pool)) continue;
                var definition = DataHandler.GetEnvironmentMap(map);
                if (definition?.aSpawnDerelictRings?.Length != 1) continue;
                var ring = definition.aSpawnDerelictRings[0];
                if (ring == null || ring.strLootShipType != pool || !ring.bIsBodyLocked || ring.bOrbitBO ||
                    !Curve.Finite(ring.fAltSpawnDistanceKM) || !Curve.Finite(ring.fAltSpawnAngle) ||
                    !Curve.Finite(ring.fSpawnRadiusMinKM) || !Curve.Finite(ring.fSpawnRadiusMaxKM) ||
                    ring.fSpawnRadiusMinKM < 0 || ring.fSpawnRadiusMaxKM <= ring.fSpawnRadiusMinKM) continue;
                station.objSS.UpdateTime(StarSystem.fEpoch);
                bool ground = station.Classification == Ship.TypeClassification.GroundStation ||
                    station.Classification == Ship.TypeClassification.GroundStationUnfinished;
                var anchor = string.IsNullOrEmpty(ring.strSpawnAroundBody)
                    ? world.GetNearestBO(station.objSS, StarSystem.fEpoch, !ground) : world.GetBO(ring.strSpawnAroundBody);
                if (anchor == null) continue;
                anchor.UpdateTime(StarSystem.fEpoch);
                if (!FieldCenter.TryRead(station, definition, ring, anchor, out var center)) continue;
                double bodyRadius = ring.fAltSpawnDistanceKM > 0 ? 0 : anchor.fRadiusKM;
                if (!Curve.Finite(center.X) || !Curve.Finite(center.Y) || !Curve.Finite(bodyRadius) || bodyRadius < 0) continue;
                string key = region + "|" + id + "|" + pool + "|" + anchor.strName;
                fields.Add(new Field
                {
                    Key = key, Region = region, Map = map, Ring = ring, Anchor = anchor, Center = center,
                    InnerKm = bodyRadius + ring.fSpawnRadiusMinKM, OuterKm = bodyRadius + ring.fSpawnRadiusMaxKM,
                    Definition = Hash(map + ":" + anchor.strName + ":" + JsonConvert.SerializeObject(ring))
                });
            }
            return fields.GroupBy(x => x.Key).Select(x => x.First()).ToList();
        }

        internal bool Contains(ShipSitu situ)
        {
            if (situ == null || situ.bIsBO || !situ.bBOLocked || situ._dockGroup != null ||
                situ.HasNavData() || situ.strBOPORShip != Anchor.strName) return false;
            situ.UpdateTime(StarSystem.fEpoch);
            double km = (situ.vPos - Center).magnitude * AuKm;
            return Curve.Finite(km) && km >= InnerKm - .001 && km <= OuterKm + .001;
        }

        internal static string Hash(string value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }
    }
}
