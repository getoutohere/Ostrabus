using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipExecutivePrototype;

sealed class NativeArrivalManifest
{
    public string Root, Destination;
    public long Effect;
    public string[] Members;
    public string[] Crew;
    public List<string> DestroyedWitnesses = new List<string>();
}

// The native branch decides whom to transfer/destroy. This port only snapshots
// participants and reads outcomes; missing crew is not evidence of destruction.
sealed class NativeArrivalObservation
{
    readonly NativeReturnActor actor;
    readonly Dictionary<string, CondOwner> before = new Dictionary<string, CondOwner>();
    internal NativeArrivalObservation(NativeReturnActor actor)
    {
        this.actor = actor;
        actor.PrepareArrival = Prepare;
        actor.ObserveArrival = Read;
        if (actor.Arrival != null)
            foreach (string id in actor.Arrival.Crew)
                if (DataHandler.mapCOs.TryGetValue(id, out var person))
                    before[id] = person;
    }
    void Prepare()
    {
        if (actor.Arrival?.Effect == actor.Machine.State.PendingArrival.Id)
            return;
        before.Clear();
        var ships = actor.World.Subject.GetAllDockedShips().Concat(new[] { actor.World.Subject }).Distinct().ToArray();
        var crew = actor.World.Subject.GetPeople(true).ToArray();
        if (ships.Length > 32 || crew.Length > 256 || crew.Any(c => string.IsNullOrWhiteSpace(c.strID)) || crew.Select(c => c.strID).Distinct().Count() != crew.Length)
            throw new NotSupportedException("Arrival participant bounds/identities are not qualified");
        foreach (var person in crew)
            before.Add(person.strID, person);
        actor.Arrival = new NativeArrivalManifest
        {
            Root = actor.World.Subject.strRegID,
            Effect = actor.Machine.State.PendingArrival.Id,
            Destination = actor.Machine.State.PendingArrival.Destination,
            Members = ships.Select(s => s.strRegID).OrderBy(s => s).ToArray(),
            Crew = crew.Select(c => c.strID).OrderBy(c => c).ToArray()
        };
    }
    internal void CaptureWitnesses()
    {
        if (actor.Arrival == null)
            return;
        foreach (var person in before.Values)
            if (person.bDestroyed && !DataHandler.mapCOs.ContainsKey(person.strID) && !actor.Arrival.DestroyedWitnesses.Contains(person.strID))
                actor.Arrival.DestroyedWitnesses.Add(person.strID);
    }
    internal string Read()
    {
        var evidence = actor.Arrival;
        if (evidence == null)
            return null;
        var pending = actor.Machine.State.PendingArrival;
        if (evidence.Effect != (pending?.Id ?? actor.Machine.State.NextEffect) || evidence.Destination != actor.Machine.State.Active?.Goal?.TargetId)
            return null;
        CaptureWitnesses();
        var root = actor.World.Resolve(evidence.Root);
        if (root == null || !root.HideFromSystem || root.strAIDespawnedAt != evidence.Destination || AIShipManager.GetAIShipByRegID(evidence.Root) != null)
            return null;
        if (evidence.Members.Any(id => actor.World.Resolve(id) == null))
            return null;
        foreach (string id in evidence.Crew)
        {
            if (DataHandler.mapCOs.TryGetValue(id, out var person))
            {
                if (person.bDestroyed || person.ship?.strRegID != evidence.Destination || evidence.DestroyedWitnesses.Contains(id))
                    return null;
            }
            else if (!evidence.DestroyedWitnesses.Contains(id))
                return null;
        }
        actor.World.Retired = true; // derived from native retirement and complete readback
        return "actor-retired-with-all-crew-accounted";
    }
}
