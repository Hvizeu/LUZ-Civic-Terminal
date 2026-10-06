using Luz;
namespace LuzDesktop;
public sealed partial class MainWindow
{
    private readonly NxmQueue incomingLinks = new();
    private readonly Dictionary<string, string> catalogueLinks = [];
    private bool processingLinks;
    private Action removeNxmRegistration = NxmRegistration.RemoveWindowsRegistration;
    private const string NxmWarning = "WARNING: Making LUZ the default NXM manager sends Nexus download links for ALL games to LUZ instead of your current manager. LUZ supports Nivalis Nights only. If you want to keep your current manager, do not register LUZ. Import ZIPs manually or use Paste download link for Nivalis instead.";
    private readonly Queue<string> incomingProjects = new();
    public void ReceiveLink(string address)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (address.StartsWith(LocalImport.Prefix, StringComparison.Ordinal))
        {
            try { var path = LocalImport.Validate(address); if (incomingProjects.Count >= 20) throw new InvalidOperationException("Finish the queued project imports first."); if (!incomingProjects.Contains(path)) incomingProjects.Enqueue(path); _ = ProcessLinks(); }
            catch (Exception ex) { Error(ex); }
            return;
        }
        if (address.Length == 0) { _ = ProcessLinks(); return; }
        try { incomingLinks.Add(NxmLink.Parse(address)); status.Text = incomingLinks.Count + " Nexus download(s) waiting."; _ = ProcessLinks(); }
        catch (Exception ex) { Error(ex); }
    }
    public void ReceiveLinkError(string message) => Error(new IOException(message));
    private async Task ProcessLinks()
    {
        if (preview || processingLinks || operation != null || !IsVisible) return;
        processingLinks = true;
        try
        {
            while (incomingProjects.Count > 0 && operation == null)
            {
                string path = incomingProjects.Peek();
                if (!await Confirm("Import project ZIP “" + Path.GetFileName(path) + "” into “" + library.Active.Name + "”?\n\nIt enters this profile as one mod. Apply profile when you want to deploy it.")) { incomingProjects.Dequeue(); continue; }
                bool imported = false;
                await Busy("Importing object project…", ct => { ct.ThrowIfCancellationRequested(); library.Import(path); imported = true; return Task.CompletedTask; });
                if (!imported) break;
                incomingProjects.Dequeue(); page = "Registry"; RefreshPage(); status.Text = "Project imported. Apply profile when ready.";
            }
            while (incomingLinks.Peek() is { } link && operation == null)
            {
                try { link.RequireFresh(); }
                catch (InvalidDataException ex) { incomingLinks.Remove(); await Notice("Nexus download", ex.Message); continue; }
                string key;
                try { key = await NexusKey(); }
                catch (Exception ex)
                {
                    page = "Maintenance"; RefreshPage(); status.Text = "Nexus link queued. Add your API key, then click Retry queued links.";
                    await Notice("Nexus key required", ex.Message + "\n\nThe link remains in memory until this window closes. Add your key and retry."); break;
                }
                CatalogMod? requested = null; string profile = library.Active.Id;
                await Busy("Looking up " + link + "…", async ct =>
                {
                    var files = await sources.Nexus(link.Address, key, ct);
                    requested = files.SingleOrDefault(p => p.FileId == link.FileId) ?? throw new InvalidDataException("Nexus did not return the requested file. It may have been removed.");
                });
                if (requested == null) break;
                if (profile != library.Active.Id) throw new InvalidOperationException("The active profile changed. Retry the queued Nexus link.");
                if (!await Confirm("Import " + requested.Name + " " + requested.Version + " into “" + library.Active.Name + "”?\n\nNexus mod " + link.ModId + ", file " + link.FileId + ".\n\nThe file enters this profile. Applying it to the game is a separate action.")) { incomingLinks.Remove(); continue; }
                bool imported = false;
                await Busy("Downloading Nexus file…", async ct => { await DownloadMod(requested, ct, link.Address); imported = true; });
                if (!imported) break;
                incomingLinks.Remove(); page = "Registry"; RefreshPage(); status.Text = "Nexus download imported. Apply profile when ready.";
            }
        }
        catch (Exception ex) { Error(ex); }
        finally { processingLinks = false; }
    }
    private Control BrowserDownloadSettings()
    {
        var content = new StackPanel();
        content.Children.Add(Label("Use Nexus’s Mod Manager Download button to send a file here. Downloads require your Nexus API key and your account's download permission.", brush: TerminalTheme.Muted));
        content.Children.Add(Label(NxmWarning, 14, TerminalTheme.Gold, bold: true));
        var removalResult = Label("", 13, TerminalTheme.Muted); removalResult.IsVisible = false;
        var row = Row(); row.Children.Add(Button("Change system-wide NXM handler", async () =>
        {
            if (!await Confirm("Register LUZ for Nexus download links?\n\n" + NxmWarning + "\n\n" + (OperatingSystem.IsWindows() ? "Windows Settings will open so you can choose LUZ for NXM. Your current default remains until you choose it." : "Continuing selects LUZ as the default NXM handler."))) return;
            removalResult.IsVisible = false;
            await NxmRegistration.Enable(Environment.ProcessPath ?? throw new IOException("The installed executable could not be located."));
            status.Text = OperatingSystem.IsWindows() ? "Registered. Choose LUZ for NXM in Windows Default apps, then allow your browser to open it." : "LUZ selected for Nexus links. Allow your browser to open it.";
        }));
        if (OperatingSystem.IsWindows()) row.Children.Add(QuietButton("Remove LUZ as NXM manager", () =>
        {
            removalResult.IsVisible = false;
            removeNxmRegistration();
            status.Text = "LUZ's NXM registration removed. Mods and profiles were kept.";
            removalResult.Text = NxmRegistration.RemovalGuidance; removalResult.IsVisible = true;
        }));
        content.Children.Add(row);
        content.Children.Add(removalResult);
        row = Row();
        row.Children.Add(QuietButton("Paste download link", async () => { var address = await Prompt("Paste a Nivalis NXM download link"); if (address != null) ReceiveLink(address); }));
        row.Children.Add(QuietButton("Retry queued links", ProcessLinks));
        row.Children.Add(QuietButton("Clear queued links", () => { while (incomingLinks.Count > 0) incomingLinks.Remove(); status.Text = "Queued Nexus links cleared."; }));
        content.Children.Add(row); content.Children.Add(Label(incomingLinks.Count + " pending · links are kept in memory and expire on Nexus", 13, TerminalTheme.Muted));
        if (OperatingSystem.IsWindows()) content.Children.Add(QuietButton("Open Windows Default apps", () => HostPlatform.Open("ms-settings:defaultapps")));
        var section = TerminalTheme.Section("BROWSER DOWNLOADS", content); section.Margin = new Thickness(0, 16, 0, 0); return section;
    }
}
