using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Luz;

namespace LuzDesktop;
public sealed partial class MainWindow
{
    private Control MaintenancePage()
    {
        var panel = new StackPanel { Margin = new Thickness(26, 24, 26, 18) };
        panel.Children.Add(Label("Maintenance", 28, TerminalTheme.Text, bold: true)); panel.Children.Add(Label("Game setup, source accounts and recovery.", brush: TerminalTheme.Muted));
        StackPanel Group(string title)
        {
            var content = new StackPanel(); var frame = (Control)TerminalTheme.Section(title, content);
            frame.Margin = new Thickness(0, 16, 0, 0); panel.Children.Add(frame); return content;
        }
        var pending = OperationRecovery.Inspect(library.Root);
        if (pending.Count > 0)
        {
            var required = Group("RECOVERY REQUIRED");
            required.Children.Add(Label("Apply and Play stay unavailable until each interrupted operation is recovered.", brush: TerminalTheme.Muted));
            foreach (var item in pending)
            {
                required.Children.Add(Label(item.Title, 17, bold: true));
                required.Children.Add(Label(item.Problem != null
                    ? "LUZ cannot read a complete backup for this operation. Export recovery diagnostics for support. Your recovery record has been kept."
                    : (item.Kind == "loader"
                    ? "Restore the BepInEx files from this installation's backup. Profile backups cannot complete this recovery."
                    : "Restore the plugins, patchers and settings from this deployment's backup."), brush: TerminalTheme.Muted));
                var action = Button(item.Action, () => RecoverOperation(item.Kind), primary: true);
                action.IsEnabled = item.Problem == null;
                required.Children.Add(action);
            }
            required.Children.Add(QuietButton("Export recovery diagnostics", Diagnostics));
        }
        var section = Group("LUZ UPDATES"); section.Children.Add(LauncherUpdatesPanel());
        section = Group("GAME LOCATION");
        section.Children.Add(Label(library.State.GameFolder.Length > 0 ? library.State.GameFolder : "No game folder selected", 15));
        var location = Row(); location.Children.Add(Button("Detect Steam install", DetectGame)); location.Children.Add(QuietButton("Choose game folder", ChooseGame));
        location.Children.Add(Button("Import existing installation", async () => { if (await Confirm("Copy the current plugins, patchers and configuration into this profile? Nothing in the game folder will be changed.")) _ = Busy("Importing installed mods…", ct => Task.Run(() => library.ImportInstalled(library.State.GameFolder), ct)); })); section.Children.Add(location);
        section = Group("MOD LOADER");
        section.Children.Add(Label(GameFiles.LoaderStatus(library.State.GameFolder) + (library.State.LoaderVersion.Length > 0 ? " · installed by this terminal: " + library.State.LoaderVersion : "")));
        section.Children.Add(Label("Recommended: BepInEx 6.0.0-be.788, Windows x64 IL2CPP. Installing preserves mods and settings. Newer builds are experimental.", brush: TerminalTheme.Muted));
        var loader = Row(); loader.Children.Add(Button("Install recommended BepInEx", () => _ = SetupLoader(false), primary: true)); loader.Children.Add(QuietButton("Check latest official build", () => _ = SetupLoader(true))); loader.Children.Add(QuietButton("Restore loader backup", RestoreLoader)); section.Children.Add(loader);
        panel.Children.Add(PlatformSettings());
        section = Group("AFTER A GAME UPDATE");
        section.Children.Add(Label("Check which component reported the problem. LUZ updates, generated game bindings and mod compatibility are separate.", brush: TerminalTheme.Muted));
        var gameUpdate = Row(); gameUpdate.Children.Add(Button("Diagnose game update", DiagnoseGameUpdate)); gameUpdate.Children.Add(QuietButton("Refresh game bindings", RefreshGameBindings)); section.Children.Add(gameUpdate);
        section = Group("RECOVERY & SUPPORT");
        var recovery = Row(); recovery.Children.Add(Button("Restore profile backup", RestoreBackup)); recovery.Children.Add(QuietButton("Export diagnostics", Diagnostics)); recovery.Children.Add(QuietButton("Open profile configuration", async () => { string folder = library.State.AppliedProfileId == library.Active.Id ? FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/config") : library.ConfigRoot(library.Active.Id); FileSafety.NoLinks(folder); Directory.CreateDirectory(folder); await HostPlatform.Open(Path.GetFullPath(folder)); })); section.Children.Add(recovery);
        section.Children.Add(Label("Restore points contain mods and settings. Save games are not included; keep separate backups before removing content mods.", 13, TerminalTheme.Muted));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private void DetectGame()
    {
        if (preview) return;
        string? found = GameFiles.Discover(HostPlatform.SteamRoots());
        if (found == null) { status.Text = "Steam discovery did not find Nivalis. Choose its game folder manually."; return; }
        if (library.State.AppliedProfileId.Length > 0 && !found.Equals(library.State.GameFolder, HostPlatform.PathComparison)) throw new InvalidOperationException("An existing managed installation is already selected.");
        library.State.GameFolder = FileSafety.ResolveFolder(found); library.Save(); status.Text = "Nivalis detected. Import existing mods before applying your first profile."; RefreshPage();
    }
    private async Task SetupLoader(bool latest)
    {
        LoaderRelease release = LoaderInstaller.Recommended;
        if (latest)
        {
            bool fetched = false; await Busy("Checking official BepInEx builds…", async ct => { release = await LoaderInstaller.Latest(sources, library.Root, ct); fetched = true; }); if (!fetched) return;
        }
        if (!await Confirm($"Install BepInEx {release.Version} from builds.bepinex.dev?\n\nClose the game first. Loader files will be backed up before replacement. Plugins and configuration are preserved. This does not launch the game.")) return;
        await Busy("Downloading and installing BepInEx…", async ct =>
        {
            GameFiles.Validate(library.State.GameFolder); GameFiles.RequireClosed();
            if (File.Exists(Path.Combine(library.Root, "loader-pending.json"))) throw new InvalidOperationException("Restore the interrupted loader backup first.");
            string file = FileSafety.Under(library.Root, "downloads/loader-" + Guid.NewGuid().ToString("N") + ".zip");
            try { await sources.Download(release.Url, file, ct); ct.ThrowIfCancellationRequested(); await Task.Run(() => LoaderInstaller.Install(library, file, release), ct); }
            finally { File.Delete(file); }
        });
    }
    private async Task Apply()
    {
        var deployment = new Deployment(library); var issues = deployment.Preview(); if (issues.Any(i => i.Severity == "Error")) throw new InvalidOperationException(string.Join('\n', issues.Select(i => i.Message)));
        string first = library.State.AppliedProfileId.Length == 0 ? "FIRST APPLY: this replaces the current plugins and patchers with this profile. Use Import existing installation first if you want to keep existing mods.\n\n" : "";
        if (!await Confirm(first + "Apply “" + library.Active.Name + "” with " + library.Active.Mods.Count(m => m.Enabled) + " enabled mods?\n\nA restore point is created before changing game files. Save games are not altered.\n\n" + string.Join('\n', issues.Select(i => i.Message)))) return;
        _ = Busy("Creating a restore point and applying profile…", ct => Task.Run(() => deployment.Apply(), ct));
    }
    private async Task Play()
    {
        GameFiles.Validate(library.State.GameFolder);
        if (new Deployment(library).Pending != null || File.Exists(Path.Combine(library.Root, "loader-pending.json"))) throw new InvalidOperationException("Restore the interrupted installation before playing.");
        if (library.State.AppliedProfileId != library.Active.Id || library.State.AppliedFingerprint != library.Fingerprint(library.Active)) throw new InvalidOperationException("This profile has unapplied changes. Apply it first, then press Play.");
        await HostPlatform.Launch(library.State); status.Text = "Launch requested. Check the game window for the result.";
    }
    private async Task RestoreBackup()
    {
        if (OperationRecovery.Inspect(library.Root).Any(x => x.Kind == "profile")) { await RecoverOperation("profile"); return; }
        var deployment = new Deployment(library); var backups = deployment.Backups();
        if (backups.Count == 0) throw new InvalidOperationException("No profile restore points exist yet.");
        var picked = await Pick("Choose a profile restore point", backups.Select(b => new Choice(b.Id, b.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " · " + (library.State.Profiles.FirstOrDefault(p => p.Id == b.ProfileId)?.Name ?? "Original installation") + (b.Id == deployment.Pending ? " · INTERRUPTED APPLY" : ""))).ToList());
        if (picked != null && await Confirm("Restore this backup? The current plugins, patchers and configuration will be saved as another restore point first.")) _ = Busy("Restoring profile files…", ct => Task.Run(() => deployment.Restore(backups.First(b => b.Id == picked)), ct));
    }
    private async Task RestoreLoader()
    {
        if (OperationRecovery.Inspect(library.Root).Any(x => x.Kind == "loader")) { await RecoverOperation("loader"); return; }
        string folder = Path.Combine(library.Root, "loader-backups"); if (!Directory.Exists(folder)) throw new InvalidOperationException("No loader backups exist yet.");
        var paths = Directory.GetFiles(folder, "loader-backup.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        var selected = await Pick("Choose a loader backup", paths.Select(p => new Choice(Path.GetDirectoryName(p)!, File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm:ss"))).ToList());
        if (selected != null && await Confirm("Restore the loader files from this backup? Plugins and settings are preserved.")) _ = Busy("Restoring loader files…", ct => Task.Run(() => { LoaderInstaller.Restore(library.Root, selected); library.State.LoaderVersion = ""; library.Save(); }, ct));
    }
    private async Task RecoverOperation(string kind)
    {
        var item = OperationRecovery.Inspect(library.Root).SingleOrDefault(x => x.Kind == kind);
        if (item == null) { RefreshPage(); return; }
        if (item.Problem != null) throw new InvalidOperationException(item.Problem);
        string scope = kind == "loader" ? "BepInEx loader files. Mods and settings are preserved."
            : "Plugins, patchers and configuration. The current files are saved as another restore point first.";
        if (!await Confirm(item.Action + "?\n\n" + scope + "\n\nGame folder: " + item.GameFolder + "\n\nClose the game first. Save games are not changed.")) return;
        await Busy("Recovering " + (kind == "loader" ? "BepInEx installation…" : "profile deployment…"),
            ct => Task.Run(() => OperationRecovery.Restore(library, kind), ct));
    }
    private async Task Diagnostics()
    {
        var destination = await SaveFile("Export diagnostics", "LUZ-diagnostics.zip", "*.zip"); if (destination == null) return;
        using var stream = File.Create(destination); using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        void Add(string name, string text) { using var writer = new StreamWriter(archive.CreateEntry(name).Open()); writer.Write(Redact(text)); }
        Add("recovery.txt", string.Join("\n\n", OperationRecovery.Inspect(library.Root).Select(x => x.Title + "\n" + x.Action + "\n" + (x.Problem ?? "Matching backup available.") + "\nBackup: " + x.BackupPath + "\nGame: " + x.GameFolder)));
        Add("terminal.txt", "LUZ Civic Terminal " + LauncherUpdates.CurrentVersion + "\n" + GameFiles.LoaderStatus(library.State.GameFolder) + "\n" + string.Join('\n', library.Active.Mods.Select(e => { var p = library.State.Packages.First(p => p.Id == e.PackageId); return p.Name + " " + p.Version + " enabled=" + e.Enabled; })) + "\n\nLast error:\n" + lastError);
        if (library.State.GameFolder.Length > 0) Add("game-update.txt", GameUpdateRecovery.Inspect(library.State.GameFolder));
        string log = library.State.GameFolder.Length == 0 ? "" : FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/LogOutput.log"); if (File.Exists(log)) { var lines = File.ReadLines(log).TakeLast(2000); Add("BepInEx-log.txt", string.Join('\n', lines)); }
        status.Text = "Diagnostics exported. Review the archive before sharing; mod logs may contain custom data.";
    }
    private string Redact(string text)
    {
        foreach (var path in new[] { library.Root, library.State.GameFolder, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }.Where(p => p.Length > 0).OrderByDescending(p => p.Length)) text = text.Replace(path, "[local folder]", HostPlatform.PathComparison);
        if (credentials.SessionKey.Length > 0) text = text.Replace(credentials.SessionKey, "[redacted]", StringComparison.Ordinal);
        return Regex.Replace(text, @"(?i)(apikey|api_key|token|key|authorization)(\s*[=:]\s*)[^\s&]+", "$1$2[redacted]");
    }
    private static void Detach(Control element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
        else if (element.Parent is ContentControl content) content.Content = null;
        else if (element.Parent is Decorator decorator) decorator.Child = null;
    }
}
