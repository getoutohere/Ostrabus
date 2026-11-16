using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.ApartmentRefuel;

internal static class Eligibility {
    internal static readonly string[] Templates = { "ResOKLG01", "ResBCER01", "ResBCRS01", "ResBCRS02",
        "ResMSUZ01", "ResMSUZ02", "ResMLAB01", "ResEJDR01", "ResAero01", "ResAero02", "ResRyokka01" };
    static readonly AccessTools.FieldRef<Ship, Dictionary<string, Ship>> Docks = AccessTools.FieldRefAccess<Ship, Dictionary<string, Ship>>("aDocked");

    internal static void ValidateTemplates() {
        foreach (string id in Templates)
            if (DataHandler.GetShip(id) == null) throw new InvalidOperationException("Missing residential template: " + id);
    }

    internal static bool Live(Ship ship) => ship != null && !ship.bDestroyed && !ship.bRemove && ship.objSS != null;

    internal static bool Apartment(Ship ship, out Ship host) {
        host = null;
        var system = CrewSim.system;
        var player = CrewSim.coPlayer;
        if (!Live(ship) || system == null || player == null || ship.objSS.bIsBO != true ||
            system.GetShipOwner(ship.strRegID) != player.strID || ship.json == null ||
            string.IsNullOrEmpty(ship.strRegID)) return false;
        int separator = ship.strRegID.LastIndexOf("|RES_", StringComparison.Ordinal);
        if (separator <= 0 || separator + 5 >= ship.strRegID.Length) return false;
        for (int i = separator + 5; i < ship.strRegID.Length; i++)
            if (ship.strRegID[i] < '0' || ship.strRegID[i] > '9') return false;
        // Native saves replace JsonShip.strName with the hull registry ID.
        // Older broker residences also have no strTemplateName. Use template
        // metadata when present; otherwise accept the broker's saved identity,
        // still requiring registry ownership, body state and a real station host.
        string template = !string.IsNullOrEmpty(ship.strTemplateName) ? ship.strTemplateName : ship.json.strTemplateName;
        if (!string.IsNullOrEmpty(template)) {
            if (Array.IndexOf(Templates, template) < 0) return false;
        } else if (ship.json.strName != ship.strRegID && Array.IndexOf(Templates, ship.json.strName) < 0) return false;
        host = system.GetShipByRegID(ship.strRegID.Substring(0, separator));
        return Live(host) && !ReferenceEquals(host, ship) && host.IsStation(bIgnoreDocks: true);
    }

    internal static bool User(CondOwner user) => user != null && !user.bDestroyed && CrewSim.coPlayer != null &&
        (ReferenceEquals(user, CrewSim.coPlayer) || user.GetCondAmount("IsPlayerCrew") > 0);

    static bool PortLink(Ship from, Ship to) {
        var links = Docks(from);
        if (links != null) foreach (var link in links) {
            if (ReferenceEquals(link.Value, to) && !string.IsNullOrEmpty(link.Key) &&
                !link.Key.StartsWith(Ship.MOORINGID, StringComparison.Ordinal)) return true;
        }
        if (from.LoadState <= Ship.Loaded.Shallow && from.json?.aDocked != null)
            foreach (var link in from.json.aDocked)
                if (link.Value == to.strRegID && !string.IsNullOrEmpty(link.Key) &&
                    !link.Key.StartsWith(Ship.MOORINGID, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static bool Vessel(Ship apartment, Ship target) => Apartment(apartment, out _) && Live(target) &&
        !ReferenceEquals(apartment, target) && !target.objSS.bIsBO && target.LoadState >= Ship.Loaded.Edit &&
        PortLink(apartment, target) && PortLink(target, apartment);

    internal static Ship Provider(Ship apartment) {
        if (!Apartment(apartment, out var host)) return null;
        return CrewSim.system.GetNearestStationRegional(host.objSS.vPosx, host.objSS.vPosy) ?? host;
    }

    internal static bool Placement(CondOwner floor, Vector3 center, Ship reference) {
        if (!Runtime.Ready || floor?.ship == null || !Apartment(floor.ship, out _)) return false;
        var centerTile = reference?.GetTileAtWorldCoords1(center.x, center.y, bAllowDocked: true);
        return centerTile?.coProps != null && ReferenceEquals(centerTile.coProps.ship, floor.ship);
    }

    internal static bool Action(Interaction action, CondOwner us, CondOwner them) {
        if (action.strName == Runtime.Use)
            return Runtime.Ready && User(us) && Runtime.Working(them) && Apartment(them.ship, out _);
        if (action.strName == Runtime.Use + "Allow")
            return Runtime.Ready && User(them) && Runtime.Working(us) && Apartment(us.ship, out _);
        string name = action.strName;
        if (name == null || !(name.StartsWith("ACTOstrabusApartmentRefuel", StringComparison.Ordinal) ||
            name.StartsWith("MSOstrabusApartmentRefuel", StringComparison.Ordinal))) return true;
        if (name.IndexOf("Install", StringComparison.Ordinal) < 0) return true;
        if (!Runtime.Ready) return false;
        if (us != null && Runtime.InstalledDefinition(us.strPlaceholderInstallFinish)) return Apartment(us.ship, out _);
        if (them != null && Runtime.InstalledDefinition(them.strPlaceholderInstallFinish)) return Apartment(them.ship, out _);
        // Initial loose-item action is legal from inventory aboard any ship.
        // The cursor and later placeholder carry the actual destination.
        return !name.StartsWith("MS", StringComparison.Ordinal) ||
            (us != null && Apartment(us.ship, out _));
    }
}
