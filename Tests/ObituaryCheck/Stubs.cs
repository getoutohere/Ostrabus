namespace Ostrabus {
    internal static class OstrabusPlugin { internal static BepInEx.Logging.ManualLogSource Logger = null; }
}
namespace Ostrabus.Inspection {
    internal static class InspectionApi {
        internal readonly struct Reply {
            internal readonly int Status;
            internal readonly object Body;
            internal Reply(int status, object body) { Status = status; Body = body; }
        }
    }
}
