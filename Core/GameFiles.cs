using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Luz;

public static class GameFiles
{
    public const string AppId = "1488490";
    public static void Validate(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !File.Exists(Path.Combine(folder, "Nivalis Nights.exe")) || !File.Exists(Path.Combine(folder, "GameAssembly.dll")))
            throw new InvalidDataException("Choose the Nivalis Nights folder containing Nivalis Nights.exe and GameAssembly.dll.");
        FileSafety.NoLinks(FileSafety.PortableDestination(folder, "BepInEx"));
    }
    public static void RequireClosed()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                string name;
                try { name = p.ProcessName; } catch (InvalidOperationException) { continue; }
                if (IsGameProcess(name, "")) throw new InvalidOperationException("Close Nivalis Nights before installing, updating, applying or restoring files.");
                if (OperatingSystem.IsLinux())
                {
                    try { if (IsGameProcess(name, File.ReadAllText($"/proc/{p.Id}/cmdline").Replace('\0', ' '))) throw new InvalidOperationException("Close Nivalis Nights before changing its files."); }
                    catch (FileNotFoundException) { }
                    catch (DirectoryNotFoundException) { }
                    catch (UnauthorizedAccessException) { if (name.Contains("wine", StringComparison.OrdinalIgnoreCase)) throw new IOException("Cannot inspect a Wine process. Close it before changing game files."); }
                }
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            var request = new ProcessStartInfo("/bin/ps") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in new[] { "-ax", "-o", "command=" }) request.ArgumentList.Add(argument);
            using var ps = Process.Start(request) ?? throw new IOException("Cannot check running game processes.");
            var output = ps.StandardOutput.ReadToEndAsync(); var errors = ps.StandardError.ReadToEndAsync();
            if (!ps.WaitForExit(5000)) throw new IOException("Process inspection timed out; close the game and retry.");
            if (ps.ExitCode != 0) throw new IOException("Cannot inspect running processes. No game files were changed.");
            if (IsGameProcess("", output.GetAwaiter().GetResult())) throw new InvalidOperationException("Close Nivalis Nights before changing its files.");
            _ = errors.GetAwaiter().GetResult();
        }
    }
    public static bool IsGameProcess(string name, string command) => name.StartsWith("Nivalis Nights", StringComparison.OrdinalIgnoreCase) || command.Contains("Nivalis Nights.exe", StringComparison.OrdinalIgnoreCase);
    public static string? Discover(IEnumerable<string> steamRoots)
    {
        var roots = new HashSet<string>(steamRoots.Where(Directory.Exists), HostPlatform.PathComparer);
        foreach (var root in roots.ToArray())
        {
            string libraries = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraries)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s*\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var root in roots)
        {
            string manifest = Path.Combine(root, "steamapps", $"appmanifest_{AppId}.acf"); if (!File.Exists(manifest)) continue;
            var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\""); if (!match.Success) continue;
            string folder = FileSafety.Under(Path.Combine(root, "steamapps", "common"), match.Groups[1].Value);
            if (File.Exists(Path.Combine(folder, "Nivalis Nights.exe")) && File.Exists(Path.Combine(folder, "GameAssembly.dll"))) return FileSafety.ResolveFolder(folder);
        }
        return null;
    }
    public static string LoaderStatus(string game)
    {
        if (string.IsNullOrWhiteSpace(game)) return "Loader setup required";
        string core = FileSafety.PortableDestination(game, "BepInEx/core");
        return File.Exists(Path.Combine(game, "winhttp.dll")) && File.Exists(Path.Combine(core, "BepInEx.Unity.IL2CPP.dll")) ? "IL2CPP loader files found" : "Loader setup required";
    }
}
