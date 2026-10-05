using System.Runtime.InteropServices;
using Luz;

public static class InstallationChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string source = Path.Combine(root, "install source"), destination = Path.Combine(root, "installed version");
        Directory.CreateDirectory(Path.Combine(source, "Assets"));
        File.WriteAllText(Path.Combine(source, "LUZ Civic Terminal.exe"), "fixture; never execute");
        File.WriteAllText(Path.Combine(source, "Install LUZ.exe"), "installer fixture");
        File.WriteAllText(Path.Combine(source, "Assets/icon.png"), "image fixture");
        DesktopInstallation.InstallFiles(source + Path.DirectorySeparatorChar, destination);
        check(FileSafety.Hash(Path.Combine(source, "Assets/icon.png")) == FileSafety.Hash(Path.Combine(destination, "Assets/icon.png")) && !File.Exists(Path.Combine(destination, "Install LUZ.exe")), "installer copies payload and excludes installer entrypoint");
        check(DesktopInstallation.InstallFiles(source, destination) == destination, "same version install can repair shortcut without replacing binaries");
        reject(() => DesktopInstallation.InstallFiles(source + Path.DirectorySeparatorChar, Path.Combine(source, "nested")), "install cannot recurse into its extracted package");
        File.WriteAllText(Path.Combine(source, "Assets/icon.png"), "changed");
        reject(() => DesktopInstallation.InstallFiles(source, destination), "installer preserves different existing version files");
        check(File.ReadAllText(Path.Combine(destination, "Assets/icon.png")) == "image fixture", "rejected installation preserves previous contents");
        string icon = Path.Combine(root, "Apps with space", "luz-icon.png");
        string entry = DesktopInstallation.LinuxEntry(Path.Combine(root, "Apps with space", "LUZ Civic Terminal"), icon);
        check(entry.Contains("Icon=" + icon.Replace("\\", "\\\\")) && !entry.Contains("--nxm") && !entry.Contains("MimeType=") && entry.Contains("X-LUZ-Managed=true"), "Linux desktop shortcut contains icon and normal launch command");
        if (OperatingSystem.IsWindows())
        {
            string desktop = Path.Combine(root, "fixture desktop");
            string shortcutPath = DesktopInstallation.CreateShortcut(desktop, destination, DesktopPlatform.Windows);
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            try
            {
                check((string)shortcut.TargetPath == Path.Combine(destination, "LUZ Civic Terminal.exe") && (string)shortcut.WorkingDirectory == destination && ((string)shortcut.IconLocation).EndsWith("LUZ Civic Terminal.exe,0") && (string)shortcut.Arguments == "", "Windows shortcut target, working directory and embedded icon verified through native COM");
                shortcut.TargetPath = Path.Combine(root, "Another application.exe"); shortcut.Save();
                reject(() => DesktopInstallation.CreateShortcut(desktop, destination, DesktopPlatform.Windows), "installer preserves unrelated desktop shortcut");
            }
            finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
    }
}
