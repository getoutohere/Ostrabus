using System;
using System.IO;

namespace Ostrabus.Development;

internal static class InstallationPaths {
    internal static string Game => Environment.GetEnvironmentVariable("OSTRANAUTS_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "Ostranauts");
    internal static string Managed => Path.Combine(Game, "Ostranauts_Data", "Managed");
    internal static string Data => Path.Combine(Game, "Ostranauts_Data", "StreamingAssets", "data") + Path.DirectorySeparatorChar;
    internal static string Saves => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Blue Bottle Games", "Ostranauts", "Saves");
    internal static string Fixture(string relative) => Path.Combine("Tests", "Fixtures", relative);
}
