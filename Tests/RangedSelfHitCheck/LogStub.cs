namespace Ostrabus;

internal static class OstrabusPlugin {
    internal static readonly Sink Logger = new();
    internal sealed class Sink {
        internal int Warnings;
        internal void LogWarning(object message) { Warnings++; }
    }
}
