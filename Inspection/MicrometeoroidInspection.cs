using Ostrabus.Micrometeoroids;

namespace Ostrabus.Inspection;

internal static class MicrometeoroidInspection {
    internal static InspectionApi.Reply Strike() {
        if (CrewSim.coPlayer == null || CrewSim.coPlayer.ship?.objSS == null)
            return new InspectionApi.Reply(503, SnapshotJson.Map("error", "No loaded player ship."));
        var result = MeteorTest.Trigger();
        return new InspectionApi.Reply(200, SnapshotJson.Map(
            "triggered", result.Triggered,
            "reason", result.Triggered ? null : "BeatManager eligibility rejected the event (station, station docking, torch drive or atmosphere).",
            "shipId", result.ShipId, "angleDegrees", result.Angle,
            "detectionProbability", result.DetectionProbability, "detected", result.Detected,
            "intercepted", result.Intercepted, "bursts", result.Bursts, "rounds", result.Rounds));
    }
}
