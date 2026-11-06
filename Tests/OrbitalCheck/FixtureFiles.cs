using System.IO;

static class FixtureFiles {
    internal static string Read(string path) => File.ReadAllText(Ostrabus.Development.InstallationPaths.Fixture(Path.Combine("Orbital", path)));
}
