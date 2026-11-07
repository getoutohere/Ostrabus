using Ostrabus.Optimizations;

namespace Ostrabus.Inspection;

internal static class TransitProfileInspection {
    internal static bool IsControl(string path) => path == "/profiling/transit/start" || path == "/profiling/transit/stop";
    internal static InspectionApi.Reply Control(string path) {
        if (path.EndsWith("/start")) TransitProfiling.Start();
        else TransitProfiling.Stop();
        return new InspectionApi.Reply(200, TransitProfiling.Snapshot());
    }
}
