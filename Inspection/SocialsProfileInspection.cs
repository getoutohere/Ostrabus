using Ostrabus.Optimizations;

namespace Ostrabus.Inspection;

internal static class SocialsProfileInspection {
    internal static bool IsControl(string path) => path == "/profiling/socials/start" || path == "/profiling/socials/stop" || path == "/profiling/socials/open";
    internal static InspectionApi.Reply Control(string path) {
        if (path.EndsWith("/open")) {
            var pda = CrewSim.guiPDA;
            if (pda == null) return new InspectionApi.Reply(503, SnapshotJson.Map("error", "PDA is unavailable."));
            if (pda.State == GUIPDA.UIState.Socials) return new InspectionApi.Reply(409, SnapshotJson.Map("error", "Socials is already open; no UI action performed."));
            SocialsProfiling.Start();
            pda.State = GUIPDA.UIState.Socials;
        } else if (path.EndsWith("/start")) SocialsProfiling.Start();
        else SocialsProfiling.Stop();
        return new InspectionApi.Reply(200, SocialsProfiling.Snapshot());
    }
}
