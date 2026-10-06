using Luz;

internal static class LinkedRootChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string physical = Path.Combine(root, "physical-home"), alias = Path.Combine(root, "home-alias");
        Directory.CreateDirectory(physical);
        Link(alias, physical);
        string source = Path.Combine(alias, "Downloads", "LUZ"), destination = Path.Combine(alias, ".local", "share", "LUZ", "application", "fixture");
        string inner = Path.Combine(source, "escape"), libraryLink = Path.Combine(physical, "library", "escape");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "LUZ Civic Terminal"), "fixture executable; never run");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Path.Combine(source, "LUZ Civic Terminal"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            reject(() => FileSafety.NoLinks(source), "Reproduce old installer rejection of a linked home ancestor");
            string installed = DesktopInstallation.InstallFiles(source, destination);
            check(installed == destination.Replace(alias, physical) && File.ReadAllText(Path.Combine(installed, "LUZ Civic Terminal")).StartsWith("fixture"), "Install through home alias resolves source and not-yet-created destination");
            check(DesktopInstallation.InstallFiles(source, destination) == installed, "Repeated installation through alias preserves verified files");
            if (!OperatingSystem.IsWindows()) check(File.GetUnixFileMode(Path.Combine(installed, "LUZ Civic Terminal")).HasFlag(UnixFileMode.UserExecute), "Linked-home installation retains executable permission");
            reject(() => DesktopInstallation.InstallFiles(source, Path.Combine(physical, "Downloads", "LUZ", "nested")), "Aliases cannot bypass source/destination containment guard");
            Link(inner, installed);
            reject(() => DesktopInstallation.InstallFiles(source, Path.Combine(physical, "blocked-install")), "Links within extracted payload remain rejected");
            Directory.Delete(inner);
            using (var library = new Library(Path.Combine(alias, "library")))
            {
                check(library.Root == Path.Combine(physical, "library"), "Library through linked home uses physical root");
                library.Active.Name = "Preserved profile"; library.Save();
                reject(() => { using var other = new Library(Path.Combine(physical, "library")); }, "Physical path and alias share one library lock");
                check(LinkInbox.Name(library.Root) == LinkInbox.Name(Path.Combine(alias, "library")), "Alias and physical path use the same protocol inbox");
            }
            using (var reopened = new Library(Path.Combine(alias, "library"))) check(reopened.Active.Name == "Preserved profile", "Linked-root library persists existing profile");
            Link(libraryLink, installed);
            reject(() => { using var blocked = new Library(Path.Combine(alias, "library")); }, "Library still rejects links inside managed data");
        }
        finally
        {
            foreach (string link in new[] { inner, libraryLink, alias })
                if (new DirectoryInfo(link).LinkTarget != null) Directory.Delete(link);
        }
    }
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        // Junctions exercise the same reparse-point guard without administrator
        // privileges. Unix runs this fixture with actual directory symlinks.
        if ((link + target).IndexOfAny(['"', '&', '|', '<', '>', '^', '%', '\r', '\n']) >= 0)
            throw new IOException("Fixture path contains unsupported command characters.");
        var request = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, target }) request.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(request)!;
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Cannot create fixture junction: " + process.StandardError.ReadToEnd());
    }
}
