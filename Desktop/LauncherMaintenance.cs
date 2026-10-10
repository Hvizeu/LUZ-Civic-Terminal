using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Luz;

namespace LuzDesktop;

public sealed partial class MainWindow
{
    private readonly HttpClient updateHttp = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource updateLifetime = new();
    private LauncherRelease? launcherRelease;
    private string launcherUpdateStatus = "Checks GitHub for new LUZ releases. Mod updates are separate.";
    private bool checkingLauncher;
    private string UpdatePreferencesPath => Path.Combine(library.Root, "launcher-updates.json");
    private UpdatePreferences UpdatePreferences()
    {
        try { return File.Exists(UpdatePreferencesPath) ? JsonFiles.Read<UpdatePreferences>(UpdatePreferencesPath) : new(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) {
            launcherUpdateStatus = "Could not read update preferences: " + ex.Message + ". Automatic checks are paused; manual checks remain available.";
            return new(false);
        }
    }
    private Control LauncherUpdatesPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Label("LUZ Civic Terminal " + LauncherUpdates.CurrentVersion, 17));
        panel.Children.Add(Label(launcherUpdateStatus, brush: TerminalTheme.Muted));
        var automatic = new CheckBox { Content = "Check for LUZ updates on startup", IsChecked = UpdatePreferences().CheckOnStartup };
        automatic.IsCheckedChanged += (_, _) => Run(() => JsonFiles.Write(UpdatePreferencesPath, UpdatePreferences() with { CheckOnStartup = automatic.IsChecked == true }));
        panel.Children.Add(automatic);
        var actions = Row(); actions.Children.Add(Button("Check LUZ updates", () => _ = CheckLauncherUpdate(true)));
        if (launcherRelease != null) actions.Children.Add(Button("Install LUZ " + launcherRelease.Version, InstallLauncherUpdate, primary: true));
        panel.Children.Add(actions); return panel;
    }
    private async Task CheckLauncherUpdate(bool manual)
    {
        if (preview || checkingLauncher) return;
        checkingLauncher = true;
        try {
            var preferences = UpdatePreferences(); var now = DateTimeOffset.UtcNow;
            if (!manual && !LauncherUpdates.Due(preferences, now)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            launcherRelease = await new LauncherUpdates(updateHttp).Check(LauncherUpdates.CurrentVersion, LauncherUpdates.RuntimeId, timeout.Token);
            JsonFiles.Write(UpdatePreferencesPath, UpdatePreferences() with { LastCheckedUtc = now });
            launcherUpdateStatus = launcherRelease == null ? "No newer stable LUZ release is available for this platform." : "LUZ " + launcherRelease.Version + " is available. Installation preserves your profiles and game files.";
            if (operation == null && (manual || launcherRelease != null)) status.Text = launcherUpdateStatus + (page != "Maintenance" && launcherRelease != null ? " Open Maintenance to install it." : "");
        } catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or PlatformNotSupportedException) {
            launcherUpdateStatus = "LUZ update check could not finish: " + (ex is OperationCanceledException ? "request timed out or was cancelled." : ex.Message);
            if (manual && !updateLifetime.IsCancellationRequested) Error(ex);
        } finally {
            checkingLauncher = false;
            if (!updateLifetime.IsCancellationRequested && page == "Maintenance" && operation == null) RefreshPage();
        }
    }
    private async Task InstallLauncherUpdate()
    {
        var release = launcherRelease;
        if (release == null || operation != null) return;
        if (!await Confirm($"Download and install LUZ {release.Version} from {LauncherUpdates.Repository}?\n\nLUZ will restart after the download and SHA-256 verification. Profiles, mods, settings and game saves are preserved. The game will not be launched.")) return;
        string? installed = null;
        await Busy("Downloading and verifying LUZ " + release.Version + "…", async ct => {
            _ = RegistryRecovery.Read(library.Root, allowFirstRun: true);
            if (!File.Exists(Path.Combine(library.Root, "registry.json"))) library.Save();
            var updater = new LauncherUpdates(updateHttp);
            string archive = await updater.Download(release, library.Root, ct);
            try {
                string source = await Task.Run(() => LauncherUpdates.Prepare(release, archive), ct);
                await LauncherUpdates.PrepareMacSignature(source, ct);
                ct.ThrowIfCancellationRequested();
                string destination = DesktopInstallation.Destination(release.Version);
                await Task.Run(() => DesktopInstallation.InstallFiles(source, destination), ct);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (desktop.Length == 0) desktop = Path.Combine(HostPlatform.Home, "Desktop");
                DesktopInstallation.CreateShortcut(desktop, destination, HostPlatform.Current);
                NxmRegistration.RefreshExistingWindowsRegistration(Path.Combine(destination, "LUZ Civic Terminal.exe"));
                installed = destination;
            } finally { FileSafety.DeleteOwned(library.Root, Path.GetDirectoryName(archive)!); }
        });
        if (installed == null) return;
        try {
            _ = Process.Start(LauncherUpdates.RestartRequest(installed, Environment.ProcessId, HostPlatform.Current)) ?? throw new IOException("The updated LUZ process could not start. Open it from the desktop shortcut.");
            Close();
        } catch (Exception ex) { Error(ex); }
    }
    private async Task DiagnoseGameUpdate()
    {
        string? report = null;
        await Busy("Checking the game build and saved loader log…", ct => Task.Run(() => report = GameUpdateRecovery.Inspect(library.State.GameFolder), ct));
        if (report != null) await Notice("Game update diagnostics", report);
    }
    private async Task RefreshGameBindings()
    {
        if (!await Confirm("Close the game first. This backs up BepInEx's generated game bindings and enables automatic regeneration on your next normal game launch.\n\nMods, content packs and saves are preserved. It does not fix a mod that explicitly requires a different game build. No game will be launched.")) return;
        string? backup = null;
        await Busy("Preparing game binding regeneration…", ct => Task.Run(() => backup = GameUpdateRecovery.Refresh(library.State.GameFolder), ct));
        if (backup != null) await Notice("Bindings ready to regenerate", "Start the game through your usual launcher when ready. The next startup may take longer while BepInEx rebuilds the bindings.\n\nBackup: " + backup + "\n\nIf a mod still reports an unsupported game build, install a compatible version of that mod.");
    }
}
