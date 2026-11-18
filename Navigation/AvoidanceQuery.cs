using System;
using Ostranauts.ShipGUIs.Utilities;

namespace Ostrabus.ShipNavigation;

// Stack scopes only: no actor registry, retained maneuver, timer or save state.
internal static class AvoidanceQuery
{
    internal struct State { internal Ship Owner; internal NavData Route; internal bool Admission; }
    [ThreadStatic] static State flight, query;
    internal static NavData Route => query.Route;
    internal static Ship Owner => query.Owner;
    internal static bool Admission => query.Admission;
    internal static State BeginFlight(Ship ship)
    {
        var previous = flight; flight = new State { Owner = ship, Route = ship?.objSS?.NavData }; return previous;
    }
    internal static void EndFlight(State previous) => flight = previous;
    internal static State BeginQuery(Ship ship)
    {
        var previous = query;
        query = ReferenceEquals(flight.Owner, ship) ? flight : new State { Owner = ship, Route = ship?.objSS?.NavData };
        return previous;
    }
    internal static void EndQuery(State previous) => query = previous;
    internal static bool Candidate(Ship ship, NavData route)
    {
        if (!ReferenceEquals(flight.Owner, ship) || route == null || ReferenceEquals(route, flight.Route)) return false;
        flight.Route = route; flight.Admission = true; return true;
    }
}
