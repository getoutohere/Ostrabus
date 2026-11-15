using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace ShipExecutivePrototype;

// Represents the native world save accompanying the executive sidecar.
// Deliberately scoped to this fixture: not a replacement game-save writer.
sealed class WorldCheckpoint
{
    public double Epoch, Fuel;
    public double RouteDuration = NativeWorld.RouteSeconds;
    public double PauseUntil, DockingRemaining;
    public JsonShipSitu Motion;
    public string CombatTarget;
    public bool Threat, LowFuel, Damaged, PilotAvailable, RefuseArrival, PlanFails;
    public bool Retired, HullHidden;
    public string CaptainShip;
    public Dictionary<string, double> PhysicalFuel;
    public string FuelDonor;
    public double FuelMinimum;
    public Dictionary<string, Dictionary<string, double>> DialogueConditions;
    public Dictionary<string, string[]> DialogueRelationships;
    public JsonCommData SubjectComms, PlayerComms;
    public JsonShipMessage[] PendingMessages;

    internal static string Capture(NativeWorld world) => JsonConvert.SerializeObject(new WorldCheckpoint
    {
        Epoch = StarSystem.fEpoch,
        Fuel = world.Subject.fShallowRCSRemass,
        RouteDuration = world.RouteDuration,
        PauseUntil = world.Subject.fAIPauseTimer,
        DockingRemaining = world.Subject.fAIDockingExpire,
        Motion = world.Subject.objSS.GetJSON(),
        CombatTarget = world.Subject.shipCombatTarget?.strRegID,
        Threat = world.Threat,
        LowFuel = world.LowFuel,
        Damaged = world.Damaged,
        PilotAvailable = world.PilotAvailable,
        RefuseArrival = world.RefuseArrival,
        PlanFails = world.PlanFails,
        Retired = world.Retired,
        HullHidden = world.Subject.HideFromSystem,
        CaptainShip = world.Captain.ship?.strRegID,
        PhysicalFuel = world.PhysicalFuel,
        FuelDonor = world.FuelDonor,
        FuelMinimum = world.FuelMinimum,
        DialogueConditions = NativeDialogueFixtures.CaptureCrew(world),
        DialogueRelationships = NativeDialogueFixtures.CaptureRelationships(world),
        SubjectComms = world.DialogueConditions == null ? null : world.Subject.Comms.GetJson(),
        PlayerComms = world.DialogueConditions == null ? null : CrewSim.coPlayer.ship.Comms.GetJson(),
        PendingMessages = world.DialogueConditions == null ? null : NativeDialogueFixtures.Queue.Values.Select(m => m.GetJson()).ToArray()
    }, CheckpointJson.Settings);

    internal static NativeWorld Restore(string json)
    {
        var saved = JsonConvert.DeserializeObject<WorldCheckpoint>(json, CheckpointJson.Settings);
        var world = new NativeWorld(saved.Epoch);
        NativeWorld.Back(world.Subject, "objSS", new ShipSitu(saved.Motion));
        world.Subject.fShallowRCSRemass = saved.Fuel;
        world.RouteDuration = saved.RouteDuration;
        world.Subject.fAIPauseTimer = saved.PauseUntil;
        world.Subject.fAIDockingExpire = saved.DockingRemaining;
        world.Subject.shipCombatTarget = world.Resolve(saved.CombatTarget);
        world.Threat = saved.Threat;
        world.LowFuel = saved.LowFuel;
        world.Damaged = saved.Damaged;
        world.PilotAvailable = saved.PilotAvailable;
        world.RefuseArrival = saved.RefuseArrival;
        world.PlanFails = saved.PlanFails;
        world.Retired = saved.Retired;
        world.Subject.HideFromSystem = saved.HullHidden;
        world.Captain.ship = world.Resolve(saved.CaptainShip) ?? world.Subject;
        world.PhysicalFuel = saved.PhysicalFuel;
        world.FuelDonor = saved.FuelDonor;
        world.FuelMinimum = saved.FuelMinimum;
        if (saved.DialogueConditions != null)
        {
            NativeDialogueFixtures.Bind(world, saved.DialogueConditions, saved.DialogueRelationships);
            NativeDialogueFixtures.RestoreComms(world.Subject, saved.SubjectComms);
            NativeDialogueFixtures.RestoreComms(CrewSim.coPlayer.ship, saved.PlayerComms);
            // Use the native comms DTO reconstruction for the fixture's saved
            // transport subset too; do not replay Send/Receive on restoration.
            var carrier = NativeWorld.Empty<Ostranauts.Ships.Comms.Comms>();
            NativeWorld.Set(carrier, "_messageLog", new List<Ostranauts.Ships.Comms.ShipMessage>());
            NativeDialogueFixtures.RestoreComms(carrier, new JsonCommData { aMessages = saved.PendingMessages });
            foreach (var message in carrier.GetMessages())
                NativeDialogueFixtures.Queue.Add(message.ID, message);
        }
        if (world.Retired)
            AIShipManager.dictAIs["HOME"].Remove(world.AI);
        return world;
    }
}
