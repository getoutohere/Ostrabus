using System;
using System.Linq;
using HarmonyLib;
using Ostranauts.Utils.Models;
using SolarSystem;

namespace Ostrabus.BoneyardTurnover
{
    // Offset yards were placed at generation time. A rotating ground station's
    // present bearing is not that historical centre. Read the unique native
    // facility authored at the same placement, without moving any object.
    internal static class FieldCenter
    {
        private delegate string EntityId(JsonEnvironmentEntity entity, string region);
        private delegate Point EntityPosition(Point body, Point station, double distance, float angle);
        private static readonly EntityId Id = AccessTools.MethodDelegate<EntityId>(AccessTools.Method(typeof(EnvironmentEntities), "GenerateRegID"));
        private static readonly EntityPosition Position = AccessTools.MethodDelegate<EntityPosition>(AccessTools.Method(typeof(EnvironmentEntities), "CalculateEntityPosition"));

        internal static bool TryRead(Ship regional, JsonEnvironmentMap map, JsonSpawnDerelict ring, BodyOrbit anchor, out Point center)
        {
            center = anchor.vPos;
            if (ring.fAltSpawnDistanceKM <= 0) return true;
            var markers = map.aEntities?.Where(x => x != null && x.bIsStation && !x.bOrbitBO &&
                x.fDistanceKM == ring.fAltSpawnDistanceKM && x.fAngle == ring.fAltSpawnAngle).ToArray();
            if (markers == null || markers.Length != 1) return false;
            var marker = markers[0];
            string id = Id(marker, regional.strRegID);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(marker.strShipName)) return false;
            var world = CrewSim.system;
            world.dictShips.TryGetValue(id, out var physical);
            world.dictStellarObjects.TryGetValue(id, out var deferred);
            if ((physical == null) == (deferred == null)) return false;
            string owner = string.IsNullOrEmpty(marker.strOwner) ? world.GetShipOwner(regional.strRegID) : marker.strOwner;
            if (string.IsNullOrEmpty(owner) || owner == "UNREGISTERED") return false;
            ShipSitu pose;
            if (physical != null)
            {
                if (physical.bDestroyed || physical.ShipCO == null || physical.json == null ||
                    physical.Classification != marker.Classification || EnvironmentEntities.GetMapID(physical) != map.MapID ||
                    !physical.ShipCO.HasCond("IsEnvironmentEntity") || world.GetShipOwner(id) != owner) return false;
                // Native physical saves commonly replace the template name
                // with the generated registry ID. Deferred saves retain it.
                string template = physical.strTemplateName ?? physical.json.strName;
                if (template != marker.strShipName && template != id) return false;
                pose = physical.objSS;
            }
            else
            {
                if (!(deferred is StellarShip saved) || saved.MapID != map.MapID || saved.strOwner != owner ||
                    saved.Classification != marker.Classification || saved.strLoot != marker.strShipName ||
                    world.GetShipOwner(id) != "UNREGISTERED" && world.GetShipOwner(id) != owner) return false;
                pose = saved.objSS;
            }
            if (pose == null || !pose.bIsBO || !pose.bBOLocked || pose.bOrbitLocked || pose.fOrbitW != 0 ||
                pose.strBOPORShip != anchor.strName || pose.HasNavData()) return false;

            // Reject a radially displaced facility as well as altered attachment
            // or motion. Rotation of the original ground reference changes its
            // bearing, but not the authored centre's radius about the parent.
            var reference = string.IsNullOrEmpty(marker.strRegIDRoot) ? regional : world.GetShipByRegID(marker.strRegIDRoot);
            if (reference?.objSS == null) return false;
            reference.objSS.UpdateTime(StarSystem.fEpoch);
            bool ground = reference.Classification == Ship.TypeClassification.GroundStation ||
                reference.Classification == Ship.TypeClassification.GroundStationUnfinished;
            var parent = world.GetNearestBO(reference.objSS, StarSystem.fEpoch, !ground);
            if (parent == null || parent.strName != anchor.strName) return false;
            parent.UpdateTime(StarSystem.fEpoch);
            Point authored = Position(parent.vPos, reference.objSS.vPos, marker.fDistanceKM, marker.fAngle);
            double radius = (authored - parent.vPos).magnitude * Field.AuKm;
            pose.UpdateTime(StarSystem.fEpoch);
            double actual = (pose.vPos - parent.vPos).magnitude * Field.AuKm;
            if (!Curve.Finite(actual) || !Curve.Finite(radius) || Math.Abs(actual - radius) > .001) return false;
            center = pose.vPos;
            return Curve.Finite(center.X) && Curve.Finite(center.Y);
        }
    }
}
