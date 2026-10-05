using System.Runtime.InteropServices;

namespace Luz;

public static class DesktopInstallation
{
    public const string AppName = "LUZ Civic Terminal";

    public static string Destination(string version) => HostPlatform.Current switch {
        DesktopPlatform.Windows => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName, version),
        DesktopPlatform.MacOS => Path.Combine(HostPlatform.Home, "Applications", "LUZ " + version, AppName + ".app"),
        _ => Path.Combine(HostPlatform.DataRoot(), "application", version)
    };

    public static string InstallFiles(string source, string destination)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)); destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        FileSafety.NoLinks(source); FileSafety.NoLinks(destination);
        if (source.Equals(destination, HostPlatform.PathComparison)) return destination;
        if (destination.StartsWith(source + Path.DirectorySeparatorChar, HostPlatform.PathComparison))
            throw new IOException("Choose an installation folder outside the extracted package.");
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) is not ("Install LUZ" or "Install LUZ.exe")).ToArray();
        if (files.Length == 0) throw new IOException("The extracted application files are missing.");
        if (Directory.Exists(destination))
        {
            foreach (var file in files)
            {
                string installed = FileSafety.Under(destination, Path.GetRelativePath(source, file));
                if (!File.Exists(installed) || FileSafety.Hash(file) != FileSafety.Hash(installed))
                    throw new IOException("This version is already installed with different files. Preserve that folder and choose a fresh version before retrying.");
            }
            return destination;
        }
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".luz-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in files)
            {
                string target = FileSafety.Under(stage, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(file));
                if (FileSafety.Hash(file) != FileSafety.Hash(target)) throw new IOException("Copy verification failed.");
            }
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) FileSafety.DeleteOwned(parent, stage); }
        return destination;
    }

    public static string LinuxEntry(string executable, string icon)
    {
        static string Checked(string path) => Path.IsPathFullyQualified(path) && !path.Any(char.IsControl)
            ? path : throw new InvalidDataException("Shortcut paths must be absolute and contain no control characters.");
        string exec = NxmRegistration.DesktopEntry(Checked(executable));
        exec = exec.Replace(" --nxm %u", "").Replace("MimeType=x-scheme-handler/nxm;\n", "");
        string escapedIcon = Checked(icon).Replace("\\", "\\\\");
        return exec + "Icon=" + escapedIcon + "\nX-LUZ-Managed=true\n";
    }

    public static string CreateShortcut(string desktop, string installed, DesktopPlatform platform)
    {
        desktop = FileSafety.ResolveFolder(desktop); installed = Path.GetFullPath(installed);
        FileSafety.NoLinks(installed); Directory.CreateDirectory(desktop);
        if (platform == DesktopPlatform.Windows)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            string exe = Path.Combine(installed, AppName + ".exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Installed launcher is missing.", exe);
            string path = Path.Combine(desktop, AppName + ".lnk");
            Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException("Windows shortcuts are unavailable.");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(path);
            try
            {
                if (File.Exists(path) && Path.GetFileName((string)shortcut.TargetPath) != AppName + ".exe")
                    throw new IOException("A desktop shortcut with this name points to another application.");
                shortcut.TargetPath = exe; shortcut.WorkingDirectory = installed; shortcut.Arguments = "";
                shortcut.IconLocation = exe + ",0"; shortcut.Description = "Nivalis Nights mod registry and launcher";
                shortcut.Save();
            }
            finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
            return path;
        }
        if (platform == DesktopPlatform.MacOS)
        {
            string path = Path.Combine(desktop, AppName + ".app");
            var existing = new DirectoryInfo(path);
            if (existing.LinkTarget != null)
            {
                if (Path.GetFileName(existing.LinkTarget.TrimEnd('/')) != AppName + ".app")
                    throw new IOException("The desktop link points to another application.");
                existing.Delete();
            }
            else if (existing.Exists || File.Exists(path)) throw new IOException("An item with this name already exists on the desktop.");
            Directory.CreateSymbolicLink(path, installed); return path;
        }
        string entry = Path.Combine(desktop, "luz-civic-terminal.desktop");
        if (new FileInfo(entry).LinkTarget != null) throw new IOException("The desktop shortcut is a symbolic link; preserve it before installing.");
        if (File.Exists(entry) && !File.ReadAllText(entry).Contains("X-LUZ-Managed=true"))
            throw new IOException("An unmanaged desktop shortcut already uses this name.");
        File.WriteAllText(entry, LinuxEntry(Path.Combine(installed, AppName), Path.Combine(installed, "luz-icon.png")));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        return entry;
    }
}
