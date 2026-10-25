using Luz;

namespace LuzDesktop;

public sealed partial class MainWindow
{
    private sealed record VortexRequest(string Manifest, string ProfileId, string RequestId, string Fingerprint);
    private readonly Queue<string> incomingVortexSetup = new();
    private string processingVortexRequest = "";
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
            ShowVortexResult(existing, manifest); return;
        }
        string profileId = existing?.ProfileId ?? library.Active.Id;
        if (processingVortexRequest == snapshot.RequestId || incomingVortex.Any(request => request.RequestId == snapshot.RequestId)) return;
        if (incomingVortex.Count >= 20) throw new InvalidOperationException("Finish the queued Vortex synchronizations first.");
        var queued = existing ?? new VortexSyncResult
        {
            RequestId = snapshot.RequestId, Revision = snapshot.Revision, ProfileId = profileId,
            BaseFingerprint = library.Fingerprint(library.State.Profiles.Single(p => p.Id == profileId)),
            ResultingFingerprint = "", Outcome = VortexSyncOutcome.Queued,
            Message = "The Vortex snapshot was received and is waiting for LUZ.", CompletedUtc = DateTime.UtcNow
        };
        VortexBridge.SaveResult(library, queued);
        incomingVortex.Enqueue(new(manifest, profileId, snapshot.RequestId, queued.BaseFingerprint));
        lastVortexManifest = manifest; lastVortexResult = queued;
        status.Text = "Vortex synchronization queued for “" + library.State.Profiles.Single(p => p.Id == profileId).Name + "”.";
        _ = ProcessLinks();
    }

    private async Task ProcessVortex(VortexRequest request)
    {
        processingVortexRequest = request.RequestId;
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(request.Manifest);
        VortexSyncResult result = new()
        {
            RequestId = snapshot.RequestId, Revision = snapshot.Revision, ProfileId = request.ProfileId, BaseFingerprint = request.Fingerprint,
            Outcome = VortexSyncOutcome.Processing, Message = "LUZ is validating the Vortex snapshot.", CompletedUtc = DateTime.UtcNow
        };
        VortexBridge.SaveResult(library, result);
        await Busy("Synchronizing Vortex packages…", ct => Task.Run(() =>
        {
            try { result = VortexBridge.SynchronizeAndApply(library, request.Manifest, request.ProfileId, ct, request.Fingerprint); }
            catch (Exception ex)
            {
                result.Outcome = VortexSyncOutcome.Failed;
                result.Message = "LUZ could not persist the synchronization result: " + ex.Message;
                result.CompletedUtc = DateTime.UtcNow;
            }
        }, ct));
        processingVortexRequest = "";
        ShowVortexResult(result, request.Manifest);
    }

    private void ShowVortexResult(VortexSyncResult result, string manifest)
    {
        lastVortexResult = result; lastVortexManifest = manifest;
        status.Text = VortexSummary(result);
        RefreshPage();
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
        if (lastVortexResult != null)
        {
            content.Children.Add(Label(VortexSummary(lastVortexResult), 13, TerminalTheme.Muted));
            foreach (var item in lastVortexResult.Packages)
            {
                content.Children.Add(Label(item.Name + " · " + item.Action + " · " + item.Detail, brush: TerminalTheme.Muted));
                if (item.Action == "Blocked")
                {
                    string sourceId = item.BridgeId;
                    string targetProfile = lastVortexResult.ProfileId;
                    string targetManifest = lastVortexManifest;
                    content.Children.Add(Button("Exclude package and retry", async () => {
                        if (library.Active.Id != targetProfile) throw new InvalidOperationException("Return to the profile captured by this synchronization before excluding a package.");
                        if (!await Confirm("Exclude “" + item.Name + "” from this profile's Vortex feed and retry? Its Vortex files and the installed loader are retained.")) return;
                        if (library.Active.Id != targetProfile) throw new InvalidOperationException("The selected profile changed. Return to the captured profile before excluding this package.");
                        VortexBridge.ExcludeSource(library, sourceId); RetryVortex(targetManifest);
                    }));
                }
            }
        }
        var actions = Row();
        if (connection != null)
        {
            actions.Children.Add(Button("Retry Vortex sync", RetryVortex));
            actions.Children.Add(QuietButton("Review", () => { page = lastVortexResult?.Packages.Any(p => p.Detail.Contains("loader", StringComparison.OrdinalIgnoreCase)) == true ? "Maintenance" : "Registry"; RefreshPage(); }));
            actions.Children.Add(QuietButton("Disconnect", async () =>
            {
                if (!await Confirm("Disconnect Vortex? Imported packages and every LUZ profile will be kept. Vortex updates will stop until you reconnect.")) return;
                VortexBridge.Disconnect(library); status.Text = "Vortex disconnected. Imported mods and profiles were kept."; RefreshPage();
            }));
        }
        if (actions.Children.Count > 0) content.Children.Add(actions);
        return content;
    }

    private void RetryVortex() => RetryVortex(lastVortexManifest);

    private void RetryVortex(string manifest)
    {
        if (manifest.Length == 0 && library.State.Vortex is { } connection)
        {
            string snapshots = Path.Combine(connection.ExchangeRoot, "snapshots");
            manifest = Directory.Exists(snapshots) ? Directory.GetFiles(snapshots, "snapshot.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "" : "";
        }
        if (manifest.Length == 0) throw new InvalidOperationException("No Vortex snapshot is available to retry. Use Play in Vortex to create one.");
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(manifest);
        VortexSyncResult previous = VortexBridge.ReadResult(library, snapshot.RequestId) ?? throw new InvalidOperationException("This snapshot has no captured profile. Use Play in Vortex to submit it.");
        if (previous.ProfileId != library.Active.Id) throw new InvalidOperationException("Return to the profile captured by this request before retrying.");
        if (processingVortexRequest == snapshot.RequestId || incomingVortex.Any(r => r.RequestId == snapshot.RequestId)) return;
        if (previous.Outcome is VortexSyncOutcome.Applied or VortexSyncOutcome.NoChanges) { status.Text = VortexSummary(previous); return; }
        string fingerprint = library.Fingerprint(library.Active);
        bool resume = previous.ResultingFingerprint.Length > 0 && previous.ResultingFingerprint == fingerprint;
        if (previous.BaseFingerprint != fingerprint && !resume) throw new InvalidOperationException("The captured profile has changed. Review it and submit a new request with Vortex Play.");
        previous.RetryOf = snapshot.RequestId;
        previous.BaseFingerprint = fingerprint; previous.Outcome = VortexSyncOutcome.Queued;
        VortexBridge.SaveResult(library, previous);
        incomingVortex.Enqueue(new(manifest, previous.ProfileId, snapshot.RequestId, fingerprint));
        _ = ProcessLinks();
    }

    private static string VortexSummary(VortexSyncResult result)
    {
        string counts = string.Join(" · ", result.Packages.GroupBy(p => p.Action).OrderBy(g => g.Key).Select(g => g.Key + " " + g.Count()));
        return result.Outcome + ": " + result.Message + (counts.Length > 0 ? "\n" + counts : "");
    }
}
