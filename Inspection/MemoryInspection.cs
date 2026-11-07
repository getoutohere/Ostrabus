namespace Ostrabus.Inspection;

internal static class MemoryInspection {
    internal static bool IsControl(string path) => path == "/memory/start" || path == "/memory/stop" || path == "/memory/snapshot" || path == "/memory/assets";
    internal static InspectionApi.Reply Control(string path) {
        if (path == "/memory/start") MemoryDiagnostics.Start();
        else if (path == "/memory/stop") MemoryDiagnostics.Stop();
        else MemoryDiagnostics.Snapshot(path == "/memory/assets");
        return new InspectionApi.Reply(200, MemoryDiagnostics.Read(0, 1));
    }
}
