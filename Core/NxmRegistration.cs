using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Luz;

public static class NxmRegistration
{
    public const string ProgId = "LUZ.CivicTerminal.nxm", DesktopId = "luz-civic-terminal.desktop", BundleId = "tools.luz.civicterminal";
    public static string WindowsCommand(string executable) => "\"" + SafeExecutable(executable) + "\" --nxm \"%1\"";
    public static void RefreshExistingWindowsRegistration(string executable)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var command = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command", true);
        if (command == null) return; // Updating LUZ does not opt the user into browser integration.
        command.SetValue("", WindowsCommand(executable));
        using var settings = Registry.CurrentUser.OpenSubKey(@"Software\LUZCivicTerminal\Capabilities", true);
        settings?.SetValue("ApplicationIcon", executable + ",0");
    }
    public static string DesktopEntry(string executable)
    {
        string escaped = SafeExecutable(executable).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("\\", "\\\\").Replace("%", "%%");
        return "[Desktop Entry]\nType=Application\nName=LUZ Civic Terminal\nExec=\"" + escaped + "\" --nxm %u\nTerminal=false\nMimeType=x-scheme-handler/nxm;\nCategories=Game;Utility;\n";
    }
    private static string SafeExecutable(string path) => Path.IsPathFullyQualified(path) && !path.Any(c => char.IsControl(c) || c == '"') ? path : throw new InvalidDataException("Use an installed terminal executable at an absolute path.");
    public static async Task Enable(string executable)
    {
        _ = SafeExecutable(executable);
        if (!File.Exists(executable) || Path.GetFileNameWithoutExtension(executable) != "LUZ Civic Terminal") throw new InvalidOperationException("Enable browser downloads from the installed LUZ executable.");
        if (OperatingSystem.IsWindows())
        {
            using (var protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                protocol.SetValue("", "LUZ Nexus download"); protocol.SetValue("URL Protocol", "");
                using var command = protocol.CreateSubKey(@"shell\open\command"); command.SetValue("", WindowsCommand(executable));
            }
            const string capabilities = @"Software\LUZCivicTerminal\Capabilities";
            using (var settings = Registry.CurrentUser.CreateSubKey(capabilities))
            {
                settings.SetValue("ApplicationName", "LUZ Civic Terminal"); settings.SetValue("ApplicationDescription", "Nivalis Nights Nexus downloads"); settings.SetValue("ApplicationIcon", executable + ",0");
                using var associations = settings.CreateSubKey("URLAssociations"); associations.SetValue("nxm", ProgId);
            }
            using (var applications = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications")) applications.SetValue("LUZ Civic Terminal", capabilities);
            await HostPlatform.Open("ms-settings:defaultapps?registeredAppUser=LUZ%20Civic%20Terminal");
        }
        else if (OperatingSystem.IsLinux())
        {
            string data = Path.GetDirectoryName(HostPlatform.DataRoot())!;
            string folder = Path.Combine(data, "applications"); Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, DesktopId); File.WriteAllText(file, DesktopEntry(executable));
            await Command("xdg-mime", "default", DesktopId, "x-scheme-handler/nxm");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var macos = Directory.GetParent(executable); var bundle = macos?.Parent?.Parent;
            if (macos?.Name != "MacOS" || bundle == null || !bundle.Name.EndsWith(".app", StringComparison.Ordinal)) throw new InvalidOperationException("Open the packaged .app before enabling Nexus links.");
            await Command("/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister", "-f", bundle.FullName);
            IntPtr scheme = CFStringCreateWithCString(IntPtr.Zero, "nxm", 0x08000100), id = CFStringCreateWithCString(IntPtr.Zero, BundleId, 0x08000100);
            try { int result = LSSetDefaultHandlerForURLScheme(scheme, id); if (result != 0) throw new IOException("macOS could not select the Nexus handler (status " + result + ")."); }
            finally { CFRelease(scheme); CFRelease(id); }
        }
    }
    private static async Task Command(string program, params string[] args)
    {
        var request = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in args) request.ArgumentList.Add(arg);
        using var process = Process.Start(request) ?? throw new IOException("The system association tool did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new IOException("The association request has not finished. Check the system's default apps before retrying."); }
        if (process.ExitCode != 0) throw new IOException("The system association tool failed (exit " + process.ExitCode + ").");
    }
    [DllImport("/System/Library/Frameworks/CoreServices.framework/CoreServices")] private static extern int LSSetDefaultHandlerForURLScheme(IntPtr scheme, IntPtr bundle);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr value);
}
