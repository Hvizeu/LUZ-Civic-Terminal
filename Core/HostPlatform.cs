using System.Diagnostics;
using Microsoft.Win32;

namespace Luz;

public enum DesktopPlatform { Windows, Linux, MacOS }

public static class HostPlatform
{
    public static DesktopPlatform Current => OperatingSystem.IsWindows() ? DesktopPlatform.Windows : OperatingSystem.IsLinux() ? DesktopPlatform.Linux : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOS : throw new PlatformNotSupportedException("This terminal supports Windows, Linux and macOS.");
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string DataRoot(DesktopPlatform platform, string home, string? xdg = null, string? localAppData = null) => platform switch
    {
        DesktopPlatform.Windows => Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUZCivicTerminal"),
        DesktopPlatform.MacOS => Path.Combine(home, "Library", "Application Support", "LUZCivicTerminal"),
        _ => Path.Combine(!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg) ? xdg : Path.Combine(home, ".local", "share"), "LUZCivicTerminal")
    };
    public static string DataRoot() => DataRoot(Current, Home, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
    public static IEnumerable<string> SteamRoots(DesktopPlatform platform, string home, string? xdg = null)
    {
        if (platform == DesktopPlatform.Windows)
        {
            if (OperatingSystem.IsWindows() && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam) yield return steam;
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        }
        else if (platform == DesktopPlatform.MacOS) yield return Path.Combine(home, "Library", "Application Support", "Steam");
        else
        {
            if (!string.IsNullOrWhiteSpace(xdg) && Path.IsPathFullyQualified(xdg)) yield return Path.Combine(xdg, "Steam");
            foreach (string suffix in new[] { ".local/share/Steam", ".steam/steam", ".steam/root", ".var/app/com.valvesoftware.Steam/data/Steam", "snap/steam/common/.local/share/Steam" }) yield return Path.Combine(home, suffix);
        }
    }
    public static IEnumerable<string> SteamRoots() => SteamRoots(Current, Home, Environment.GetEnvironmentVariable("XDG_DATA_HOME")).Concat(new[] { Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH") ?? "" });

    public static ProcessStartInfo OpenRequest(string target, DesktopPlatform platform)
    {
        if (platform == DesktopPlatform.Windows) return new(target) { UseShellExecute = true };
        var request = new ProcessStartInfo(platform == DesktopPlatform.MacOS ? "/usr/bin/open" : "xdg-open") { UseShellExecute = false, CreateNoWindow = true };
        request.ArgumentList.Add(target); return request;
    }
    public static async Task Open(string target)
    {
        using var process = Process.Start(OpenRequest(target, Current));
        if (Current == DesktopPlatform.Windows) return;
        if (process == null) throw new InvalidOperationException("The operating system did not accept the open request.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new IOException("The system opener has not finished. It was left running; whether it opened the destination is unconfirmed."); }
        if (process.ExitCode != 0) throw new IOException("The system opener failed (exit " + process.ExitCode + "). Check that a desktop opener and the requested application are installed.");
    }
    public static ProcessStartInfo LaunchRequest(TerminalState state, DesktopPlatform platform)
    {
        if (state.LaunchProgram.Length == 0)
        {
            if (platform == DesktopPlatform.MacOS) throw new InvalidOperationException("Configure your working Wine/CrossOver launcher in Maintenance. Native macOS Steam cannot launch this Windows game.");
            return OpenRequest("steam://rungameid/" + GameFiles.AppId, platform);
        }
        if (!Path.IsPathFullyQualified(state.LaunchProgram) || !File.Exists(state.LaunchProgram)) throw new InvalidDataException("Choose an existing, absolute launcher executable or executable script.");
        var request = new ProcessStartInfo(state.LaunchProgram) { UseShellExecute = false, WorkingDirectory = state.GameFolder, CreateNoWindow = true };
        foreach (string argument in state.LaunchArguments) request.ArgumentList.Add(argument);
        return request;
    }
    public static async Task Launch(TerminalState state)
    {
        if (state.LaunchProgram.Length == 0) { _ = LaunchRequest(state, Current); await Open("steam://rungameid/" + GameFiles.AppId); return; }
        using var process = Process.Start(LaunchRequest(state, Current)) ?? throw new IOException("The configured launcher did not start.");
        await Task.Delay(250);
        if (process.HasExited && process.ExitCode != 0) throw new IOException("The configured launcher exited with code " + process.ExitCode + ". Check its program and arguments.");
    }
}
