using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Ships.Comms;

namespace Ostrabus.Inspection;

internal static class PlayerInspection {
    const double KmPerAU = 149597870.7;
    static readonly AccessTools.FieldRef<Comms, List<ShipMessage>> MessageLog = AccessTools.FieldRefAccess<Comms, List<ShipMessage>>("_messageLog");
    static readonly AccessTools.FieldRef<PersonSpec, CondOwner> CachedCO = AccessTools.FieldRefAccess<PersonSpec, CondOwner>("co");
    static Dictionary<string, object> Map(params object[] pairs) { return SnapshotJson.Map(pairs); }
    static bool OverBudget(long start) { return Stopwatch.GetTimestamp() - start >= Stopwatch.Frequency / 1000; }
    static CondOwner Selected() {
        var selected = CrewSim.GetSelectedCrew();
        return selected != null ? selected : null;
    }
    internal static Ship SelectedShip() { return Selected()?.ship; }

    internal static object Player() {
        var player = CrewSim.coPlayer;
        var selected = Selected();
        var nav = GUIDockSys.instance != null ? GUIDockSys.instance.COSelf : null;
        return Map("character", Person(player), "selectedCrew", Person(selected),
            "playerShip", Brief(player != null ? player.ship : null), "selectedCrewShip", Brief(selected?.ship),
            "navDevice", Person(nav), "navShip", Brief(nav != null ? nav.ship : null),
            "navTargetId", GUIOrbitDraw.crossHairInfo?._strRegID,
            "nearbyReference", "selectedCrewShip");
    }

    static object Person(CondOwner co) {
        if (co == null) { return null; }
        return Map("id", co.strID, "name", co.strName, "shipId", co.ship?.strRegID, "alive", co.bAlive, "destroyed", co.bDestroyed);
    }

    static Dictionary<string, object> Brief(Ship ship) {
        if (ship == null) { return null; }
        return Map("id", ship.strRegID, "name", ship.publicName, "loadState", ship.LoadState.ToString(),
            "destroyed", ship.bDestroyed, "classification", ship.Classification.ToString(), "damageState", ship.DMGStatus.ToString(),
            "isAI", ship.IsAIShip, "transponder", ship.strXPDR, "transponderAntenna", ship.bXPDRAntenna,
            "hiddenFromSystem", ship.HideFromSystem, "position", Position(ship));
    }

    internal static object Position(Ship ship) {
        var situ = ship?.objSS;
        if (situ == null) { return null; }
        return Map("xAU", situ.vPosx, "yAU", situ.vPosy, "vxAUPerSecond", situ.vVelX, "vyAUPerSecond", situ.vVelY,
            "note", "Stored coordinates; inspection does not call UpdateTime or project positions.");
    }

    internal static Dictionary<string, object> Relative(Ship origin, Ship target) {
        if (origin?.objSS == null || target?.objSS == null) { return null; }
        var a = origin.objSS;
        var b = target.objSS;
        double dx = (b.vPosx - a.vPosx) * KmPerAU, dy = (b.vPosy - a.vPosy) * KmPerAU;
        double vx = (b.vVelX - a.vVelX) * KmPerAU, vy = (b.vVelY - a.vVelY) * KmPerAU;
        return Map("referenceShipId", origin.strRegID, "centerDistanceKm", Math.Sqrt(dx * dx + dy * dy),
            "dxKm", dx, "dyKm", dy, "bearingDegreesFromPositiveX", (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360,
            "relativeVxKmPerSecond", vx, "relativeVyKmPerSecond", vy,
            "note", "Approximate from stored centers; may differ from MFD collision-surface distance and projected positions.");
    }

    internal static InspectionApi.Reply Nearby(int offset, int limit, double radiusKm, long start) {
        Ship origin = SelectedShip();
        if (origin?.objSS == null) { return new InspectionApi.Reply(409, Map("error", "Selected crew has no ship position.")); }
        var items = new List<object>();
        int index = 0, scanned = 0;
        bool more = false;
        foreach (var pair in CrewSim.system.dictShips) {
            if (OverBudget(start)) { more = true; break; }
            if (index < offset) { index++; continue; }
            if (scanned >= limit) { more = true; break; }
            index++; scanned++;
            Ship ship = pair.Value;
            if (ship == null || ship.bDestroyed || ReferenceEquals(ship, origin) || ship.objSS == null) { continue; }
            var relative = Relative(origin, ship);
            double distance = (double)relative["centerDistanceKm"];
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance > radiusKm) { continue; }
            var item = Brief(ship);
            item.Add("relative", relative);
            items.Add(item);
        }
        if (more && index < offset) { return new InspectionApi.Reply(503, Map("error", "Offset scan exceeded budget; use a direct ship ID.")); }
        return new InspectionApi.Reply(200, Map("referenceShipId", origin.strRegID, "radiusKm", radiusKm, "items", items,
            "scanned", scanned, "nextOffset", more ? (object)index : null,
            "note", "Pages scan registry entries, not matches; follow nextOffset even after an empty page. Includes hidden/background objects; does not assert sensor visibility."));
    }

    internal static object Comms(Ship ship, Ship peerShip, int offset, int limit, long start) {
        Ship origin = SelectedShip();
        var crew = new List<object>();
        bool truncated = false;
        if (ship.People != null) {
            foreach (var profile in ship.People) {
                if (crew.Count >= 32 || OverBudget(start)) { truncated = true; break; }
                if (profile == null) { crew.Add(null); continue; }
                var co = CachedCO(profile);
                if (co == null && profile.strCO != null && DataHandler.mapCOs != null) { DataHandler.mapCOs.TryGetValue(profile.strCO, out co); }
                crew.Add(Map("profileName", profile.strName, "firstName", profile.strFirstName, "lastName", profile.strLastName,
                    "coId", profile.strCO, "profileAlive", profile.bAlive, "resolvedCO", Person(co)));
            }
        }
        if (peerShip != null) {
            return Map("ship", Brief(ship), "peerShip", Brief(peerShip), "hasComms", ship.Comms != null,
                "shipLog", Messages(ship, peerShip.strRegID, offset, limit, start),
                "peerLog", Messages(peerShip, ship.strRegID, offset, limit, start),
                "note", "Raw messages between these two ships from both ship logs. No interaction eligibility, replies, or status requests are executed.");
        }
        return Map("ship", Brief(ship), "relativeToSelectedCrew", Relative(origin, ship), "hasComms", ship.Comms != null,
            "shipCO", Person(ship.ShipCO), "crewProfileCount", ship.People?.Count, "crew", crew, "crewTruncated", truncated,
            "targetLog", Messages(ship, origin?.strRegID, offset, limit, start),
            "selectedCrewShipLog", origin == null ? null : Messages(origin, ship.strRegID, offset, limit, start),
            "note", "Raw crew/profile references and message logs only. No captain selection, interaction eligibility, replies, or status requests are executed.");
    }

    internal static object Comms(Ship ship, int offset, int limit, long start) { return Comms(ship, null, offset, limit, start); }

    static object Messages(Ship owner, string peerId, int offset, int limit, long start) {
        var messages = owner.Comms == null ? null : MessageLog(owner.Comms);
        var items = new List<object>();
        int scanned = 0;
        if (messages != null) {
            for (int i = messages.Count - 1 - offset; i >= 0 && scanned < limit && !OverBudget(start); i--) {
                scanned++;
                var message = messages[i];
                if (message == null || (peerId != null &&
                    !((message.SenderRegId == owner.strRegID && message.ReceiverRegId == peerId) ||
                      (message.SenderRegId == peerId && message.ReceiverRegId == owner.strRegID)))) { continue; }
                var ia = message.Interaction;
                var inverse = new List<object>();
                if (ia?.aInverse != null) {
                    for (int j = 0; j < Math.Min(ia.aInverse.Length, 16); j++) { inverse.Add(ia.aInverse[j]); }
                }
                items.Add(Map("sender", message.SenderRegId, "receiver", message.ReceiverRegId,
                    "availableTime", message.AvailableTime, "availableNow", message.AvailableTime <= StarSystem.fEpoch,
                    "read", message.Read, "text", message.MessageText,
                    "interaction", ia?.strName, "title", ia?.strTitle, "replyIds", inverse,
                    "replyIdsTruncated", ia?.aInverse != null && ia.aInverse.Length > 16));
            }
        }
        int count = messages?.Count ?? 0;
        return Map("ownerShipId", owner.strRegID, "peerShipId", peerId, "totalLogEntries", count, "items", items,
            "scanned", scanned, "nextOffset", offset + scanned < count ? (object)(offset + scanned) : null,
            "order", "Newest stored entry first. Offset counts raw log entries, not conversation matches.");
    }
}
