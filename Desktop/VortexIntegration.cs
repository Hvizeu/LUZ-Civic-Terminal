using Luz;

namespace LuzDesktop;

public sealed partial class MainWindow
{
    private sealed record VortexRequest(string Manifest, string ProfileId, string RequestId);
    private readonly Queue<VortexRequest> incomingVortex = new();
    private VortexSyncResult? lastVortexResult;
    private string lastVortexManifest = "";

    private void ReceiveVortex(string address)
    {
        string manifest = VortexBridge.ValidateMessage(address);
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(manifest);
        VortexSyncResult? existing = VortexBridge.ReadResult(library, snapshot.RequestId);
        if (existing is { Outcome: not (VortexSyncOutcome.Queued or VortexSyncOutcome.Processing) })
        {
            lastVortexResult = existing; lastVortexManifest = manifest; status.Text = VortexSummary(existing); return;
        }
        string profileId = existing?.ProfileId ?? library.Active.Id;
        if (incomingVortex.Any(request => request.RequestId == snapshot.RequestId)) return;
        if (incomingVortex.Count >= 20) throw new InvalidOperationException("Finish the queued Vortex synchronizations first.");
        var queued = existing ?? new VortexSyncResult
        {
            RequestId = snapshot.RequestId, Revision = snapshot.Revision, ProfileId = profileId,
            BaseFingerprint = library.Fingerprint(library.State.Profiles.Single(p => p.Id == profileId)),
            ResultingFingerprint = "", Outcome = VortexSyncOutcome.Queued,
            Message = "The Vortex snapshot was received and is waiting for LUZ.", CompletedUtc = DateTime.UtcNow
        };
        VortexBridge.SaveResult(library, queued);
        incomingVortex.Enqueue(new(manifest, profileId, snapshot.RequestId));
        lastVortexManifest = manifest; lastVortexResult = queued;
        status.Text = "Vortex synchronization queued for “" + library.State.Profiles.Single(p => p.Id == profileId).Name + "”.";
        _ = ProcessLinks();
    }

    private async Task ProcessVortex(VortexRequest request)
    {
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(request.Manifest);
        VortexSyncResult result = new()
        {
            RequestId = snapshot.RequestId, Revision = snapshot.Revision, ProfileId = request.ProfileId,
            Outcome = VortexSyncOutcome.Processing, Message = "LUZ is validating the Vortex snapshot.", CompletedUtc = DateTime.UtcNow
        };
        VortexBridge.SaveResult(library, result);
        await Busy("Synchronizing Vortex packages…", ct => Task.Run(() =>
        {
            try { result = VortexBridge.SynchronizeAndApply(library, request.Manifest, request.ProfileId, ct); }
            catch (Exception ex)
            {
                result.Outcome = VortexSyncOutcome.Failed;
                result.Message = "LUZ could not persist the synchronization result: " + ex.Message;
                result.CompletedUtc = DateTime.UtcNow;
            }
        }, ct));
        lastVortexResult = result; lastVortexManifest = request.Manifest;
        status.Text = VortexSummary(result);
    }

    private Control VortexIntegrationPanel()
    {
        var content = new StackPanel();
        if (!OperatingSystem.IsWindows())
        {
            content.Children.Add(Label("The Vortex bridge is available on Windows only.", brush: TerminalTheme.Muted));
            return content;
        }
        VortexConnection? connection = library.State.Vortex;
        content.Children.Add(Label("Manage Nivalis Nights in Vortex once. Vortex installs or finds LUZ and connects it automatically. After that, update a mod and press Play; LUZ synchronizes, applies when safe, and opens without launching Nivalis.", brush: TerminalTheme.Muted));
        content.Children.Add(Label(connection == null ? "Waiting for Vortex · no setup is required here" : "Automatic Vortex connection active", 13, connection == null ? TerminalTheme.Muted : TerminalTheme.Teal));
        if (lastVortexResult != null) content.Children.Add(Label(VortexSummary(lastVortexResult), 13, TerminalTheme.Muted));
        var actions = Row();
        if (connection != null)
        {
            actions.Children.Add(Button("Retry Vortex sync", RetryVortex));
            actions.Children.Add(QuietButton("Review profile", () => { page = "Registry"; RefreshPage(); }));
            actions.Children.Add(QuietButton("Disconnect", async () =>
            {
                if (!await Confirm("Disconnect Vortex? Imported packages and every LUZ profile will be kept. Vortex updates will stop until you reconnect.")) return;
                VortexBridge.Disconnect(library); status.Text = "Vortex disconnected. Imported mods and profiles were kept."; RefreshPage();
            }));
        }
        if (actions.Children.Count > 0) content.Children.Add(actions);
        return content;
    }

    private void RetryVortex()
    {
        string manifest = lastVortexManifest;
        if (manifest.Length == 0 && library.State.Vortex is { } connection)
        {
            string snapshots = Path.Combine(connection.ExchangeRoot, "snapshots");
            manifest = Directory.Exists(snapshots) ? Directory.GetFiles(snapshots, "snapshot.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "" : "";
        }
        if (manifest.Length == 0) throw new InvalidOperationException("No Vortex snapshot is available to retry. Use Play in Vortex to create one.");
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(manifest);
        incomingVortex.Enqueue(new(manifest, library.Active.Id, snapshot.RequestId));
        _ = ProcessLinks();
    }

    private static string VortexSummary(VortexSyncResult result)
    {
        string counts = string.Join(" · ", result.Packages.GroupBy(p => p.Action).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()));
        return result.Outcome + ": " + result.Message + (counts.Length > 0 ? "\n" + counts : "");
    }
}
