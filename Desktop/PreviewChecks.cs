#if PREVIEW_TOOL
using Avalonia.Threading;
using Avalonia.LogicalTree;
namespace LuzDesktop;
public sealed partial class MainWindow
{
    private void PreviewRecoveryState(string value)
    {
        if (!preview) throw new InvalidOperationException();
        string profile = Path.Combine(library.Root, "pending.json"), loader = Path.Combine(library.Root, "loader-pending.json");
        File.Delete(profile); File.Delete(loader);
        if (!value.StartsWith("Maintenance-recovery")) return;
        string id = Guid.NewGuid().ToString("N");
        if (value is "Maintenance-recovery-both" or "Maintenance-recovery-profile")
        {
            var info = new Luz.BackupInfo(id, library.State.GameFolder, "", "", DateTime.UtcNow);
            Luz.JsonFiles.Write(Path.Combine(library.Root, "backups", id, "backup.json"), info);
            Luz.JsonFiles.Write(profile, info);
        }
        if (value != "Maintenance-recovery-profile")
        {
            string backup = Path.Combine(library.Root, "loader-backups", id);
            if (value != "Maintenance-recovery-missing")
                Luz.JsonFiles.Write(Path.Combine(backup, "loader-backup.json"), new Luz.LoaderBackup(library.State.GameFolder, ["winhttp.dll"], []));
            Luz.JsonFiles.Write(loader, backup);
        }
        status.Text = Luz.OperationRecovery.CompletionMessage(library);
    }
    public void PreviewMaintenanceBottom() { if (body.Children[1] is ScrollViewer view) view.ScrollToEnd(); }
    public void PreviewCheckInteractions(string output)
    {
        var results = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); results.Add("PASS " + name); }
        PreviewPage("Registry"); Dispatcher.UIThread.RunJobs();
        Check(mods.Items.Count == 3, "Registry renders all fixtures");
        Check(applyButton.IsEnabled && !playButton.IsEnabled, "Unapplied profile allows Apply and blocks Play");
        Check(AutomationProperties.GetName(applyButton) == "Apply profile", "Icon button retains accessible name");
        Check(applyButton.GetVisualDescendants().OfType<CutBorder>().Any(), "Angular button template instantiated");
        PreviewPage("Registry-search"); Check(mods.Items.Count == 1 && selectedId == library.State.Packages[2].Id, "Search selects matching table");
        PreviewPage("Registry-empty"); Check(selectedId == null && mods.Items.OfType<ListBoxItem>().All(i => !i.IsEnabled), "Empty search clears selection and offers no mod actions");
        PreviewPage("Registry-disabled"); Check(library.Active.Mods.Count(m => m.Enabled) == 2 && profileLabel.Text!.StartsWith("2 of 3"), "Disabled row updates profile count");
        PreviewPage("Registry-issues"); Check(!applyButton.IsEnabled && !playButton.IsEnabled, "Missing dependency blocks Apply and Play");
        PreviewPage("Registry-applied"); Check(!applyButton.IsEnabled && playButton.IsEnabled == !OperatingSystem.IsMacOS(), "Applied profile respects platform launch configuration");
        library.State.LaunchProgram = "fixture-only-not-executed"; RefreshProfileStatus();
        Check(playButton.IsEnabled, "Configured launcher enables Play without executing it"); library.State.LaunchProgram = "";
        PreviewPage("Registry"); selectedId = library.Active.Mods[0].PackageId; Move(1);
        Check(library.Active.Mods[1].PackageId == selectedId, "Manual order moves selected mod down"); Move(-1);
        Check(library.Active.Mods[0].PackageId == selectedId, "Manual order moves selected mod up");
        PreviewPage("Maintenance"); Dispatcher.UIThread.RunJobs(); Check(!applyButton.IsEnabled && !playButton.IsEnabled, "Missing game disables actions");
        var maintenanceActions = body.GetLogicalDescendants().OfType<Button>().ToArray();
        Check(maintenanceActions.Length > 0 && maintenanceActions.All(b => b.Content != null), "Authored Maintenance actions have visible content");
        foreach(string action in new[]{"Check LUZ updates","Diagnose game update","Refresh game bindings"})
            Check(body.GetVisualDescendants().OfType<Button>().Any(b=>AutomationProperties.GetName(b)==action),action+" is available in Maintenance");
        PreviewPage("Maintenance-recovery-both"); Dispatcher.UIThread.RunJobs();
        Check(!applyButton.IsEnabled && !playButton.IsEnabled && profileLabel.Text!.Contains("Profile and BepInEx"), "Both journals show specific recovery status and block unsafe actions");
        foreach (string action in new[] { "Recover profile deployment", "Recover BepInEx installation" })
            Check(body.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == action && b.IsEnabled), "Matching direct recovery action: " + action);
        PreviewPage("Maintenance-recovery-missing"); Dispatcher.UIThread.RunJobs();
        Check(body.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Recover BepInEx installation" && !b.IsEnabled), "Missing backup disables impossible recovery instead of a success loop");
        Check(body.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Export recovery diagnostics" && b.IsEnabled), "Missing backup keeps diagnostics reachable");
        PreviewPage("Maintenance"); Dispatcher.UIThread.RunJobs();
        launcherRelease=new("9.8.7","win-x64","",1,new string('a',64));RefreshPage();Dispatcher.UIThread.RunJobs();
        Check(body.GetVisualDescendants().OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Install LUZ 9.8.7"),"Detected update exposes the install action without starting it");
        launcherRelease=null;
        PreviewPage("Catalogue"); Check(page == "Catalogue", "Catalogue navigation succeeds without network requests");
        var secrets = new Credentials(library.Root); secrets.UseForSession("fixture-session-key");
        Check(secrets.Read().GetAwaiter().GetResult() == "fixture-session-key", "Session-only credential retrieval avoids native stores");
        if (OperatingSystem.IsWindows())
        {
            secrets.Save("fixture-encrypted-key").GetAwaiter().GetResult();
            Check(new Credentials(library.Root).Read().GetAwaiter().GetResult() == "fixture-encrypted-key", "DPAPI round trip in disposable library");
            Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(library.Root, "nexus-key.dat"))).Contains("fixture-encrypted-key"), "Saved credential is not plaintext");
            secrets.Remove().GetAwaiter().GetResult(); Check(!File.Exists(Path.Combine(library.Root, "nexus-key.dat")), "Credential removal clears the disposable store");
        }
        ReceiveLink("nxm://nivalisnights/mods/12/files/34?key=fixture&expires=4102444800");
        ReceiveLink("nxm://nivalisnights/mods/12/files/34?key=renewed&expires=4102444800");
        Check(incomingLinks.Count == 1, "Browser links enter one pending UI download without network activity in preview");
        ReceiveLink("nxm://anothergame/mods/12/files/34?key=private-fixture&expires=4102444800");
        Check(incomingLinks.Count == 1 && !lastError.Contains("private-fixture"), "Unsupported browser link leaves queue intact and excludes token from diagnostics");
        PreviewPage("Maintenance"); Dispatcher.UIThread.RunJobs();
        Check(body.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Enable browser downloads"), "Browser association action is available in Maintenance");
        var clear = body.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Clear queued links");
        clear.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Check(incomingLinks.Count == 0, "Clear queue button removes retained links");
        File.WriteAllLines(output, results);
    }
}
#endif
