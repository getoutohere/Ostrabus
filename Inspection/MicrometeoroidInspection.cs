using Ostrabus.Micrometeoroids;

namespace Ostrabus.Inspection;

internal static class MicrometeoroidInspection {
    internal static InspectionApi.Reply Strike() {
        if (CrewSim.coPlayer == null || CrewSim.coPlayer.ship?.objSS == null)
            return new InspectionApi.Reply(503, SnapshotJson.Map("error", "No loaded player ship."));
        var result = MeteorTest.Trigger();
        return new InspectionApi.Reply(200, SnapshotJson.Map(
            "triggered", result.Triggered,
            "reason", result.Triggered ? null : "BeatManager eligibility or target selection rejected the event (station, station docking, torch drive, atmosphere or no exposed hull).",
            "shipId", result.ShipId, "angleDegrees", result.Angle,
            "detectionProbability", result.DetectionProbability, "detected", result.Detected,
            "intercepted", result.Intercepted, "bursts", result.Bursts, "rounds", result.Rounds,
            "groupShips", result.GroupShips, "defendingShips", result.DefendingShips, "blockedGuns", result.BlockedGuns));
    }
}
