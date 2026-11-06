namespace Ostrabus {
    internal static class OstrabusPlugin { internal static StubLog Logger = new StubLog(); }
    internal class StubLog { public void LogWarning(string message) { System.Console.WriteLine(message); } }
}
