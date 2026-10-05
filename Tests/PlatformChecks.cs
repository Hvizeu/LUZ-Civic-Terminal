using System.IO.Compression;
using Luz;

internal static class PlatformChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        check(GameFiles.LoaderStatus("") == "Loader setup required", "First-run Maintenance has no game folder");
        check(HostPlatform.DataRoot(DesktopPlatform.Windows, root, localAppData: root) == Path.Combine(root, "LUZCivicTerminal"), "Windows library location remains compatible");
        check(HostPlatform.DataRoot(DesktopPlatform.Linux, root, "relative") == Path.Combine(root, ".local", "share", "LUZCivicTerminal"), "Relative XDG override falls back to user data folder");
        check(HostPlatform.DataRoot(DesktopPlatform.Linux, root, root) == Path.Combine(root, "LUZCivicTerminal"), "Absolute XDG override is respected");
        check(HostPlatform.DataRoot(DesktopPlatform.MacOS, root) == Path.Combine(root, "Library", "Application Support", "LUZCivicTerminal"), "macOS Application Support location");
        check(HostPlatform.SteamRoots(DesktopPlatform.Linux, root).Any(p => p.Contains("com.valvesoftware.Steam")), "Flatpak Steam discovery root");
        check(HostPlatform.SteamRoots(DesktopPlatform.MacOS, root).Single() == Path.Combine(root, "Library", "Application Support", "Steam"), "macOS Steam discovery root");
        foreach (var os in new[] { DesktopPlatform.Linux, DesktopPlatform.MacOS })
        {
            string target = Path.Combine(root, "folder with spaces;$(literal)"); var request = HostPlatform.OpenRequest(target, os);
            check(!request.UseShellExecute && request.ArgumentList.SequenceEqual(new[] { target }), os + " opener passes one literal argument");
        }
        var state = new TerminalState { GameFolder = root };
        reject(() => HostPlatform.LaunchRequest(state, DesktopPlatform.MacOS), "macOS requires an explicit compatibility launcher");
        string launcher = Path.Combine(root, "fake launcher;file"); File.WriteAllText(launcher, "fixture; never execute");
        state.LaunchProgram = launcher; state.LaunchArguments = ["--bottle", "Game Folder", "$(not a command)"];
        var launch = HostPlatform.LaunchRequest(state, DesktopPlatform.Linux);
        check(launch.FileName == launcher && !launch.UseShellExecute && launch.ArgumentList.SequenceEqual(state.LaunchArguments), "Custom launch preserves literal argument boundaries without starting a process");
        check(GameFiles.IsGameProcess("Nivalis Nights.", "") && GameFiles.IsGameProcess("wine64", "Z:/game/Nivalis Nights.exe") && !GameFiles.IsGameProcess("steam", "steamwebhelper"), "Windows and Wine game-process matching");
        check(FileSafety.Under(root, @"plugins\Table\a.txt") == Path.Combine(root, "plugins", "Table", "a.txt"), "Backslash package paths normalize on the host");
        reject(() => FileSafety.Under(root, @"\outside"), "Windows-rooted path rejected on every host");
        string zipPath = Path.Combine(root, "windows-paths.zip"), extracted = Path.Combine(root, "windows-paths");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry(@"plugins\Table\a.txt").Open()); writer.Write("portable"); }
        FileSafety.Extract(zipPath, extracted);
        check(File.ReadAllText(Path.Combine(extracted, "plugins", "Table", "a.txt")) == "portable", "Windows ZIP separators extract into real directories");
        check(FileSafety.PortableDestination(extracted, "PLUGINS/table/A.TXT") == Path.Combine(extracted, "plugins", "Table", "a.txt"), "Case variants resolve to the same Windows-game destination");
        string steam = Path.Combine(root, "Steam root"), external = Path.Combine(root, "External library"), game = Path.Combine(external, "steamapps", "common", "Nivalis Nights");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps")); Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"path\" \"" + external.Replace("\\", "\\\\") + "\"");
        File.WriteAllText(Path.Combine(external, "steamapps", "appmanifest_1488490.acf"), "\"installdir\" \"Nivalis Nights\"");
        File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture"); File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        check(GameFiles.Discover([steam]) == game, "Steam VDF discovers an external library with spaces");
        Directory.CreateDirectory(Path.Combine(game, "BEPINEX", "CORE"));
        File.WriteAllText(Path.Combine(game, "BEPINEX", "CORE", "BepInEx.Unity.IL2CPP.dll"), "fixture");
        Directory.CreateDirectory(Path.Combine(game, "BEPINEX", "PLUGINS"));
        File.WriteAllText(Path.Combine(game, "BEPINEX", "PLUGINS", "original.txt"), "original");
        using (var library = new Library(Path.Combine(root, "case-library")))
        {
            library.State.GameFolder = game; library.ImportInstalled(game);
            check(library.Active.Mods.Count == 1, "Adoption finds existing uppercase Windows-game directories");
            var deployment = new Deployment(library); var backup = deployment.Apply(); deployment.Restore(backup);
            check(File.ReadAllText(Path.Combine(game, "BEPINEX", "PLUGINS", "original.txt")) == "original" && Directory.GetDirectories(game).Length == 1, "Apply and restore preserve existing folder casing without parallel BepInEx trees");
        }
        if (!OperatingSystem.IsWindows())
        {
            string link = Path.Combine(root, "linkedSteam"); Directory.CreateSymbolicLink(link, steam);
            check(FileSafety.ResolveFolder(link) == steam, "User-selected Steam link resolves to physical root");
            Directory.Delete(link);
            string alias = Path.Combine(extracted, "plugins", "Table", "A.TXT");
            File.WriteAllText(alias, "second");
            if (Directory.GetFiles(Path.GetDirectoryName(alias)!).Length == 2) reject(() => FileSafety.PortableDestination(extracted, "plugins/Table/a.txt"), "Ambiguous case-sensitive destination is rejected");
            else Console.WriteLine("SKIP case-sensitive alias fixture: volume is case-insensitive.");
        }
    }
}
