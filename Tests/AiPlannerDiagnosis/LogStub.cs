namespace Ostrabus {
    internal static class OstrabusPlugin {
        internal static Sink Logger = new Sink();
        internal sealed class Sink {
            internal void LogInfo(object message) {}
            internal void LogError(object message) => throw new System.Exception("Production patch error: " + message);
            internal void LogWarning(object message) => throw new System.Exception("Production patch warning: " + message);
        }
    }
}
