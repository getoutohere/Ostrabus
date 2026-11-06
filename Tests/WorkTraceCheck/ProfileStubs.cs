// Only dependencies of the profiling installation gate, not game method replacements.
namespace Ostrabus.Optimizations {
    internal static class SavedJsonLoading { internal static bool Enabled; }
}
namespace Ostrabus {
    internal static class OstrabusPlugin { internal static QuietLogger Logger = new QuietLogger(); }
    internal sealed class QuietLogger {
        internal void LogInfo(string text) { }
        internal void LogWarning(string text) { }
    }
}
