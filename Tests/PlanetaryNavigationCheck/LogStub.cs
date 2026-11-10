namespace Ostrabus;
internal static class OstrabusPlugin {
    internal static Sink Logger = new Sink();
    internal sealed class Sink { internal void LogWarning(object message) => throw new System.Exception("Unexpected patch warning: " + message); }
}
