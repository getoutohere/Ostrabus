using System;
using System.Globalization;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.Inspection;

// Explicit single-ship RCS repair, executed by InspectionApi's Unity-thread queue.
internal static class ShipRefueling {
    const string Suffix = "/refuel";
    internal static bool IsRoute(string path) => path != null && path.StartsWith("/ships/", StringComparison.Ordinal) && path.EndsWith(Suffix, StringComparison.Ordinal);
    static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    static InspectionApi.Reply Error(int status, string reason) => new InspectionApi.Reply(status, SnapshotJson.Map("error", reason));
    internal static bool Parse(InspectionHttp http, out double? target, out string error) {
        target = null; error = null;
        if (!IsRoute(http.Url.AbsolutePath.TrimEnd('/'))) return true;
        string value = http.QueryString["targetKg"];
        if (value == null) return true;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double kg) || !Finite(kg) || kg < 0) {
            error = "targetKg must be a finite, nonnegative total RCS fuel mass in kilograms."; return false;
        }
        target = kg; return true;
    }

    internal static InspectionApi.Reply Run(string path, bool apply, double? targetKg) {
        string id;
        try { id = Uri.UnescapeDataString(path.Substring(7, path.Length-7-Suffix.Length)); }
        catch (Exception e) when (e is UriFormatException || e is ArgumentOutOfRangeException) { return Error(400, "Invalid registry ID."); }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.IndexOf('/') >= 0 || id.IndexOf('\\') >= 0) return Error(400, "Expected one exact registry ID.");
        foreach (char c in id) if (char.IsControl(c) || char.IsWhiteSpace(c)) return Error(400, "Invalid registry ID.");
        if (targetKg.HasValue && (!Finite(targetKg.Value) || targetKg.Value < 0)) return Error(400, "Invalid targetKg.");
        var system = CrewSim.system;
        if (system?.dictShips == null || CrewSim.coPlayer == null) return Error(503, "Simulation is not ready.");
        if (!system.dictShips.TryGetValue(id, out Ship ship) || ship == null || ship.bDestroyed) return Error(404, "Ship not found.");
        if (!ship.IsAIShip || ship.objSS == null || ship.objSS.bIsBO || ship.LoadState < Ship.Loaded.Shallow)
            return Error(409, "Only initialized AI vessels can be refueled.");
        if (ReferenceEquals(ship, CrewSim.coPlayer.ship) || system.GetShipOwner(id) == CrewSim.coPlayer.strID) return Error(409, "Player-owned ships are outside this AI refueling endpoint.");
        // Reading GetRCSMax with an unknown shallow capacity would assign one.
        double capacity = ship.LoadState <= Ship.Loaded.Shallow ? ship.fShallowRCSRemassMax : ship.GetRCSMax();
        double before = ship.GetRCSRemain();
        if (!Finite(capacity) || capacity <= 0 || capacity > float.MaxValue || !Finite(before) || before < 0) return Error(409, "Usable fuel capacity/current contents cannot be established.");
        double shallowBudget = ship.fShallowRCSRemass;
        if (ship.LoadState > Ship.Loaded.Shallow && (!Finite(shallowBudget) || shallowBudget < 0)) return Error(409, "Loaded fuel reconciliation budget is invalid.");
        double target = Math.Min(capacity, targetKg ?? capacity);
        double requested = Math.Max(0, target-before);
        NavData route = ship.objSS.NavData;
        var points = route?.GetNavDataPoints();
        if (points != null) {
            if (points.Count > 16384) return Error(409, "Stored route is too large to rebase in this request.");
            foreach (var point in points) if (point == null || !Finite(point.FuelLevel) || !Finite(point.FuelLevel+requested)) return Error(409, "Stored route has invalid fuel values; repair the route first.");
        }
        double after = before;
        bool rebased = false;
        if (apply && requested > 0) {
            // Snapshot only for an explicit mutation. Shared refill observers may
            // already shift these points; set from the original values once.
            double[] original = points == null ? null : new double[points.Count];
            if (points != null) for (int i = 0; i < points.Count; i++) original[i] = points[i].FuelLevel;
            if (ship.LoadState <= Ship.Loaded.Shallow && target == capacity) ship.AIRefuel();
            else ship.RefuelRCS((float)requested);
            after = ship.GetRCSRemain();
            double added = after-before;
            if (!Finite(after) || added < 0) return Error(500, "Native refueling produced invalid contents.");
            // Set from the pre-call snapshot to coexist with ordinary refill
            // observers while preserving any pending native SyncFuel debit.
            if (ship.LoadState > Ship.Loaded.Shallow) ship.fShallowRCSRemass = Math.Min(shallowBudget, before) + added;
            if (added > 0 && points != null && ReferenceEquals(ship.objSS.NavData, route)) {
                for (int i = 0; i < points.Count; i++) points[i].FuelLevel = original[i]+added;
                rebased = true;
            }
        }
        return new InspectionApi.Reply(200, SnapshotJson.Map("id", id, "fuelType", "RCS", "applied", apply,
            "fuelBeforeKg", before, "fuelAfterKg", after, "capacityKg", capacity, "targetKg", target,
            "wouldAddKg", requested, "addedKg", after-before, "routeRebased", rebased,
            "note", "Fuel only; no undocking, help-condition removal, controller replacement or navigation order is issued. Materialized tanks use native filling and may accept less than nominal capacity."));
    }
}
