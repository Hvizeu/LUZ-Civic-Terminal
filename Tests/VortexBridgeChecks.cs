using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Luz;

internal static class VortexBridgeChecks
{
    public static void Run(string fixtureRoot, Action<bool, string> check, Action<Action, string> reject)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("SKIP Vortex bridge fixtures: the integration is Windows-only.");
            return;
        }

        string root = Path.Combine(fixtureRoot, "vortex-bridge");
        string game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture; never execute");
        File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        File.WriteAllText(Path.Combine(game, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"), "fixture");

        string setupExchange = Path.Combine(root, "automatic-setup-exchange");
        string setupFolder = Path.Combine(setupExchange, "setup");
        Directory.CreateDirectory(setupFolder);
        string badExchange = Path.Combine(root, "linked-setup-exchange"), externalSetupResults = Path.Combine(root, "external-setup-results");
        Directory.CreateDirectory(Path.Combine(badExchange, "setup")); Directory.CreateDirectory(externalSetupResults);
        string badResults = Path.Combine(badExchange, "setup-results");
        LinkedRootChecks.Link(badResults, externalSetupResults);
        try
        {
            using var unsafeSetup = new Library(Path.Combine(root, "linked-setup-library"));
            unsafeSetup.Save();
            string requestId = Guid.NewGuid().ToString("N"), requestFile = Path.Combine(badExchange, "setup", requestId + ".json");
            JsonFiles.Write(requestFile, new VortexSetupRequest { Schema = 1, RequestId = requestId, GameId = "nivalisnights", GameFolder = game, Reviewed = true, CreatedUtc = DateTime.UtcNow });
            var handler = new PendingLoaderHandler(); using var sources = new Sources(handler);
            byte[] before = File.ReadAllBytes(Path.Combine(unsafeSetup.Root, "registry.json"));
            reject(() => VortexBridge.ConfigureAndPrepare(unsafeSetup, requestFile, Environment.ProcessPath!, sources,
                new CancellationToken(true), badExchange).GetAwaiter().GetResult(), "linked setup output is rejected by the real setup entry point");
            check(unsafeSetup.State.Vortex == null && before.SequenceEqual(File.ReadAllBytes(Path.Combine(unsafeSetup.Root, "registry.json")))
                && !handler.Started.Task.IsCompleted && !Directory.EnumerateFileSystemEntries(externalSetupResults).Any(),
                "unsafe setup output is rejected before connection commit or loader preparation");
        }
        finally { Directory.Delete(badResults); }
        using (var automaticSetup = new Library(Path.Combine(root, "automatic-setup-library")))
        {
            string setupRequestId = Guid.NewGuid().ToString("N");
            string setupRequest = Path.Combine(setupFolder, setupRequestId + ".json");
            JsonFiles.Write(setupRequest, new VortexSetupRequest { Schema = 1, RequestId = setupRequestId, GameId = "nivalisnights", GameFolder = game, CreatedUtc = DateTime.UtcNow });
            VortexSetupResult setupResult = VortexBridge.Configure(automaticSetup, setupRequest, Environment.ProcessPath!, setupExchange);
            check(setupResult.Outcome == VortexSyncOutcome.Applied && GameInstallation.SameLocation(automaticSetup.State.GameFolder, game)
                && automaticSetup.State.Vortex != null && File.Exists(Path.Combine(setupExchange, "connection.json")),
                "Vortex configures a first-run LUZ library without a manual Maintenance step");
            check(VortexBridge.ReadSetupResult(setupRequestId, setupExchange)?.Outcome == VortexSyncOutcome.Applied,
                "automatic Vortex setup persists its terminal result for Vortex");
            check(File.ReadAllText(Path.Combine(setupExchange, "setup-results", setupRequestId + ".json")).Contains("\"Outcome\": \"Applied\""),
                "Vortex setup outcomes use the extension's stable string contract");

            string repeatId = Guid.NewGuid().ToString("N");
            string repeat = Path.Combine(setupFolder, repeatId + ".json");
            JsonFiles.Write(repeat, new VortexSetupRequest { Schema = 1, RequestId = repeatId, GameId = "nivalisnights", GameFolder = game, CreatedUtc = DateTime.UtcNow });
            setupResult = VortexBridge.Configure(automaticSetup, repeat, Environment.ProcessPath!, setupExchange);
            check(setupResult.Outcome == VortexSyncOutcome.NoChanges && automaticSetup.State.Vortex != null,
                "automatic Vortex setup is idempotent");

            string otherGame = Path.Combine(root, "other-game");
            Directory.CreateDirectory(otherGame);
            File.WriteAllText(Path.Combine(otherGame, "Nivalis Nights.exe"), "other fixture; never execute");
            File.WriteAllText(Path.Combine(otherGame, "GameAssembly.dll"), "other fixture");
            string differentId = Guid.NewGuid().ToString("N");
            string different = Path.Combine(setupFolder, differentId + ".json");
            JsonFiles.Write(different, new VortexSetupRequest { Schema = 1, RequestId = differentId, GameId = "nivalisnights", GameFolder = otherGame, CreatedUtc = DateTime.UtcNow });
            string connectionBefore = automaticSetup.State.Vortex!.Id;
            setupResult = VortexBridge.Configure(automaticSetup, different, Environment.ProcessPath!, setupExchange);
            check(setupResult.Outcome == VortexSyncOutcome.NeedsAttention && GameInstallation.SameLocation(automaticSetup.State.GameFolder, game)
                && automaticSetup.State.Vortex!.Id == connectionBefore,
                "automatic Vortex setup refuses a different game installation without mutating the library");
            reject(() => VortexBridge.ValidateSetupMessage(VortexBridge.SetupPrefix + Path.Combine(root, "outside-setup.json"), setupExchange),
                "Vortex setup requests outside the protected exchange are rejected");
        }

        string exchange = Path.Combine(root, "exchange");
        string plugins = Path.Combine(root, "vortex-plugins", "community");
        Directory.CreateDirectory(plugins);
        JsonFiles.Write(Path.Combine(plugins, "package.json"), new { name = "game-nivalisnights-luz-bridge" });
        // Active-extension conflict behavior is covered against Vortex state in the extension suite.
        using var library = new Library(Path.Combine(root, "library"));
        library.State.GameFolder = game;
        library.Save();
        VortexBridge.Connect(library, Environment.ProcessPath ?? throw new IOException("Test host path is unavailable."), exchange, checkConflicts: false);
        VortexConnectionDescriptor descriptor = JsonFiles.Read<VortexConnectionDescriptor>(Path.Combine(exchange, "connection.json"));
        check(File.Exists(Path.Combine(exchange, "connection.json")) && library.State.Vortex?.ExchangeRoot == exchange
            && File.Exists(Path.Combine(descriptor.VortexRoot, "luz-bridge-game.json")) && Directory.Exists(Path.Combine(descriptor.VortexRoot, "deployed")),
            "Vortex connection descriptor uses an isolated virtual game and deployment root");

        var first = Snapshot(exchange, game, library.State.Vortex!.Id, "mod-one", "1.0.0", "one", '1');
        string deployedLink = Path.Combine(descriptor.VortexRoot, "deployed", "Vortex-owned-link");
        LinkedRootChecks.Link(deployedLink, game);
        VortexSyncResult result;
        try
        {
            result = VortexBridge.Synchronize(library, first.Manifest);
            check(result.Outcome == VortexSyncOutcome.Processing && result.Packages.Any(p => p.Action == "Added"), "Vortex first import prepares one profile transaction");
            VortexBridge.RefreshDescriptor(library, Environment.ProcessPath!);
            check(Directory.Exists(deployedLink), "Vortex deployment links do not block imports or descriptor refresh and remain untouched");
        }
        finally { Directory.Delete(deployedLink); }
        ModEntry entry = library.Active.Mods.Single();
        check(entry.Enabled && library.Active.Vortex.Bindings.Single().PackageId == entry.PackageId, "new Vortex mod enters the selected profile enabled and bound");
        string firstPackage = entry.PackageId;
        string importedFingerprint = library.Fingerprint(library.Active);
        string payload = FileSafety.Under(exchange, first.Snapshot.Packages.Single().Payload);
        string originalPayload = Path.Combine(root, "linked-payload-source");
        Directory.Move(payload, originalPayload); LinkedRootChecks.Link(payload, originalPayload);
        try
        {
            result = VortexBridge.Synchronize(library, first.Manifest);
            check(result.Outcome == VortexSyncOutcome.NeedsAttention && library.Fingerprint(library.Active) == importedFingerprint,
                "links inside snapshot payloads are still rejected without changing the profile");
        }
        finally { Directory.Delete(payload); Directory.Move(originalPayload, payload); }
        string manifestAlias = Path.Combine(exchange, "snapshots", "linked-manifest-parent");
        LinkedRootChecks.Link(manifestAlias, Path.GetDirectoryName(first.Manifest)!);
        try { reject(() => VortexBridge.Synchronize(library, Path.Combine(manifestAlias, "snapshot.json")), "linked snapshot manifest ancestors are still rejected"); }
        finally { Directory.Delete(manifestAlias); }
        string results = Path.Combine(exchange, "results"), externalResults = Path.Combine(root, "linked-results-target");
        Directory.CreateDirectory(externalResults); LinkedRootChecks.Link(results, externalResults);
        try
        {
            reject(() => VortexBridge.SaveResult(library, result), "synchronization results cannot be written through a linked control directory");
            check(!Directory.EnumerateFileSystemEntries(externalResults).Any(), "a rejected linked result path leaves its external target intact");
        }
        finally { Directory.Delete(results); }

        entry.Enabled = false;
        entry.Note = "keep this";
        library.Save();
        var second = Snapshot(exchange, game, library.State.Vortex!.Id, "mod-one", "2.0.0", "two", '2');
        result = VortexBridge.Synchronize(library, second.Manifest);
        entry = library.Active.Mods.Single();
        check(!entry.Enabled && entry.Note == "keep this" && entry.PackageId != firstPackage && result.Packages.Any(p => p.Action == "Updated"), "Vortex update preserves disabled state and profile notes");

        string secondPackage = entry.PackageId;
        entry.Pinned = true;
        library.Save();
        var third = Snapshot(exchange, game, library.State.Vortex!.Id, "mod-one", "3.0.0", "three", '3');
        result = VortexBridge.Synchronize(library, third.Manifest);
        check(library.Active.Mods.Single().PackageId == secondPackage && result.Packages.Any(p => p.Action == "Pinned"), "pinned Vortex update is reported without replacing the selected version");
        library.Active.Mods.Single().Pinned = false;
        library.Save();
        result = VortexBridge.Synchronize(library, third.Manifest);
        check(library.Active.Mods.Single().PackageId != secondPackage && result.Packages.Any(p => p.Action == "Updated"), "retrying the same snapshot applies a formerly pinned update after unpinning");

        string updatedPackage = library.Active.Mods.Single().PackageId;
        result = VortexBridge.Synchronize(library, third.Manifest);
        check(result.Outcome == VortexSyncOutcome.NoChanges && library.Active.Mods.Count == 1 && library.Active.Mods.Single().PackageId == updatedPackage, "repeating a completed Vortex snapshot is idempotent");

        string firstProfile = library.Active.Id;
        string secondProfile = library.CreateProfile("Second", false).Id;
        library.State.ActiveProfileId = firstProfile;
        library.Save();
        var fourth = Snapshot(exchange, game, library.State.Vortex!.Id, "mod-one-replacement", "4.0.0", "four", '4');
        result = VortexBridge.Synchronize(library, fourth.Manifest);
        check(library.Active.Mods.Count == 1 && library.Active.Vortex.Bindings.Count == 1
            && library.Active.Vortex.Bindings.Single().BridgeId == "mod-one-replacement" && result.Packages.Any(p => p.Action == "Updated"),
            "one-to-one Vortex replacement lineage updates the existing LUZ entry without a duplicate binding");
        check(library.State.Profiles.Single(p => p.Id == secondProfile).Mods.Count == 0, "Vortex synchronization changes only the captured profile");

        var empty = Snapshot(exchange, game, library.State.Vortex!.Id, null, null, null, '5');
        result = VortexBridge.Synchronize(library, empty.Manifest);
        check(library.Active.Mods.Count == 1 && !library.Active.Vortex.Bindings.Single().Available && result.Packages.Any(p => p.Action == "Unavailable"), "Vortex removal retains the LUZ mod and marks its update source unavailable");

        string removedPackage = library.Active.Mods.Single().PackageId;
        VortexBridge.Exclude(library, removedPackage);
        library.Active.Mods.Clear();
        library.Save();
        var returnSnapshot = Snapshot(exchange, game, library.State.Vortex!.Id, "mod-one-replacement", "5.0.0", "five", '6');
        result = VortexBridge.Synchronize(library, returnSnapshot.Manifest);
        check(library.Active.Mods.Count == 0 && result.Packages.Any(p => p.Action == "Excluded"), "deliberately removed LUZ mod stays excluded on later Vortex sync");

        string profileBefore = library.Fingerprint(library.Active);
        var invalid = Snapshot(exchange, game, library.State.Vortex!.Id, "valid-new", "1.0.0", "valid", '7');
        VortexSnapshot document = JsonFiles.Read<VortexSnapshot>(invalid.Manifest);
        document.Packages.Add(new VortexSnapshotPackage
        {
            BridgeId = "broken", Name = "Broken", Version = "1.0.0", Payload = "../outside", ContentSha256 = new string('a', 64),
            Files = [new() { Path = "../escape.dll", Length = 1, Sha256 = new string('b', 64) }]
        });
        JsonFiles.Write(invalid.Manifest, document);
        result = VortexBridge.Synchronize(library, invalid.Manifest);
        check(result.Outcome == VortexSyncOutcome.NeedsAttention && library.Fingerprint(library.Active) == profileBefore && library.Active.Mods.Count == 0, "one invalid Vortex package prevents a partial profile commit");

        VortexBridge.SaveResult(library, result);
        check(VortexBridge.ReadResult(library, result.RequestId)?.Outcome == VortexSyncOutcome.NeedsAttention, "Vortex completion result persists by request identity");
        reject(() => VortexBridge.Synchronize(library, Path.Combine(root, "outside.json")), "Vortex manifests outside the configured exchange are rejected");

        string autoGame = Path.Combine(root, "auto-game");
        Directory.CreateDirectory(Path.Combine(autoGame, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(autoGame, "Nivalis Nights.exe"), "fixture; never execute");
        File.WriteAllText(Path.Combine(autoGame, "GameAssembly.dll"), "fixture");
        File.WriteAllText(Path.Combine(autoGame, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"), "fixture");
        using (var automatic = new Library(Path.Combine(root, "auto-library")))
        {
            automatic.State.GameFolder = autoGame; automatic.Save();
            new Deployment(automatic).Apply();
            string autoExchange = Path.Combine(root, "auto-exchange");
            VortexBridge.Connect(automatic, Environment.ProcessPath!, autoExchange, checkConflicts: false);
            var autoFirst = Snapshot(autoExchange, autoGame, automatic.State.Vortex!.Id, "auto-one", "1.0.0", "auto-one", '8', managedDll: false);
            result = VortexBridge.SynchronizeAndApply(automatic, autoFirst.Manifest, automatic.Active.Id);
            check(result.Outcome == VortexSyncOutcome.Applied && File.Exists(Path.Combine(autoGame, "BepInEx", "plugins", "Fixture", "value.txt")), "clean applied profile accepts Vortex synchronization through the normal deployment transaction");
            var interrupted = Snapshot(autoExchange, autoGame, automatic.State.Vortex!.Id, "auto-one", "1.1.0", "resume", 'd', managedDll: false);
            string interruptedCapture = automatic.Fingerprint(automatic.Active);
            VortexBridge.Synchronize(automatic, interrupted.Manifest);
            check(automatic.State.AppliedFingerprint != automatic.Fingerprint(automatic.Active), "synchronized candidate remains visibly unapplied across an interrupted handoff");
            result = VortexBridge.SynchronizeAndApply(automatic, interrupted.Manifest, automatic.Active.Id, capturedFingerprint: interruptedCapture);
            check(result.Outcome == VortexSyncOutcome.Applied && File.ReadAllText(Path.Combine(autoGame, "BepInEx", "plugins", "Fixture", "value.txt")) == "resume", "repeated request resumes the exact interrupted bridge Apply instead of treating receipt as completion");
            int restorePoints = new Deployment(automatic).Backups().Count;
            result = VortexBridge.SynchronizeAndApply(automatic, interrupted.Manifest, automatic.Active.Id, capturedFingerprint: interruptedCapture);
            check(result.Outcome == VortexSyncOutcome.Applied && new Deployment(automatic).Backups().Count == restorePoints, "Lost completion receipt recovers the exact committed request without another deployment");
            result = VortexBridge.SynchronizeAndApply(automatic, interrupted.Manifest, automatic.Active.Id);
            check(result.Outcome == VortexSyncOutcome.NoChanges && new Deployment(automatic).Backups().Count == restorePoints, "same Vortex snapshot creates no unnecessary deployment or restore point");

            string installedCompanion = Path.Combine(autoGame, "BepInEx/plugins/Fixture/value.txt");
            File.WriteAllText(installedCompanion, "external change");
            result = VortexBridge.SynchronizeAndApply(automatic, interrupted.Manifest, automatic.Active.Id);
            check(result.Outcome == VortexSyncOutcome.NeedsAttention && File.ReadAllText(installedCompanion) == "external change", "Unchanged snapshot cannot report ready after an external deployment change or purge");
            File.WriteAllText(installedCompanion, "resume");

            automatic.Active.Mods.Single().Enabled = false; automatic.Save();
            var dirtyBase = Snapshot(autoExchange, autoGame, automatic.State.Vortex!.Id, "auto-one", "2.0.0", "auto-two", '9', managedDll: false);
            var addition = Snapshot(autoExchange, autoGame, automatic.State.Vortex!.Id, "auto-two", "1.0.0", "addition", 'a', managedDll: false);
            VortexSnapshot dirtyDocument = JsonFiles.Read<VortexSnapshot>(dirtyBase.Manifest);
            dirtyDocument.Packages.Add(JsonFiles.Read<VortexSnapshot>(addition.Manifest).Packages.Single());
            dirtyDocument.Revision = new string('b', 64); JsonFiles.Write(dirtyBase.Manifest, dirtyDocument);
            result = VortexBridge.SynchronizeAndApply(automatic, dirtyBase.Manifest, automatic.Active.Id);
            check(result.Outcome == VortexSyncOutcome.NeedsAttention && result.Message.Contains("unapplied changes"), "unrelated dirty profile changes stop automatic Apply after synchronization");

            new Deployment(automatic).Apply();
            var recoverySnapshot = Snapshot(autoExchange, autoGame, automatic.State.Vortex!.Id, "auto-two", "2.0.0", "recovery", 'c', managedDll: false);
            JsonFiles.Write(Path.Combine(automatic.Root, "pending.json"), new BackupInfo(Guid.NewGuid().ToString("N"), autoGame, automatic.Active.Id, automatic.Fingerprint(automatic.Active), DateTime.UtcNow));
            result = VortexBridge.SynchronizeAndApply(automatic, recoverySnapshot.Manifest, automatic.Active.Id);
            check(result.Outcome == VortexSyncOutcome.RecoveryRequired && File.Exists(Path.Combine(automatic.Root, "pending.json")), "outstanding recovery journal blocks automatic Apply and remains intact");
            File.Delete(Path.Combine(automatic.Root, "pending.json"));
        }

        using (var adoption = new Library(Path.Combine(root, "pinned-adoption")))
        {
            adoption.State.GameFolder = game; adoption.Save();
            string manual = Path.Combine(root, "manual-pinned.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(manual, System.IO.Compression.ZipArchiveMode.Create))
                archive.CreateEntryFromFile(typeof(TestPlugin).Assembly.Location, "BepInEx/plugins/Fixture/Fixture.dll");
            var selected = adoption.Import(manual, new CatalogMod("42", "Manual pinned", "9.0.0", "", "", "", "", [], "Nexus"));
            adoption.Active.Mods.Single().Pinned = true; adoption.Save();
            string adoptionExchange = Path.Combine(root, "adoption-exchange");
            VortexBridge.Connect(adoption, Environment.ProcessPath!, adoptionExchange, checkConflicts: false);
            var offered = Snapshot(adoptionExchange, game, adoption.State.Vortex!.Id, "new-source", "1.0.0", "offered", 'e');
            result = VortexBridge.Synchronize(adoption, offered.Manifest);
            check(adoption.Active.Mods.Single().PackageId == selected.Id && adoption.Active.Mods.Single().Pinned && result.Packages.Any(p => p.Action == "Pinned"), "First-time adoption retains a manually imported pinned version");
            string captured = adoption.Fingerprint(adoption.Active);
            adoption.Active.Mods.Single().Enabled = false; adoption.Save();
            result = VortexBridge.SynchronizeAndApply(adoption, offered.Manifest, adoption.Active.Id, capturedFingerprint: captured);
            check(result.Outcome == VortexSyncOutcome.NeedsAttention && !adoption.Active.Mods.Single().Enabled, "Queued request rejects a changed captured fingerprint without selecting another profile");
        }

        using (var setupLibrary = new Library(Path.Combine(root, "pending-loader-library")))
        using (var handler = new PendingLoaderHandler())
        using (var sources = new Sources(handler))
        {
            string missingGame = Path.Combine(root, "missing-loader-game"); Directory.CreateDirectory(missingGame);
            File.WriteAllText(Path.Combine(missingGame, "Nivalis Nights.exe"), "fixture; never execute");
            File.WriteAllText(Path.Combine(missingGame, "GameAssembly.dll"), "fixture");
            string setupRoot = Path.Combine(root, "pending-loader-exchange"), requestId = Guid.NewGuid().ToString("N");
            string requestPath = Path.Combine(setupRoot, "setup", requestId + ".json");
            JsonFiles.Write(requestPath, new VortexSetupRequest { Schema = 1, RequestId = requestId, GameId = "nivalisnights", GameFolder = missingGame, Reviewed = true, CreatedUtc = DateTime.UtcNow });
            var pending = VortexBridge.ConfigureAndPrepare(setupLibrary, requestPath, Environment.ProcessPath!, sources, exchangeRoot: setupRoot);
            handler.Started.Task.GetAwaiter().GetResult();
            check(VortexBridge.ReadSetupResult(requestId, setupRoot) == null, "Setup cannot publish terminal success while loader preparation is pending");
            handler.Response.SetResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
            var failed = pending.GetAwaiter().GetResult();
            check(failed.Outcome == VortexSyncOutcome.NeedsAttention && failed.Message.Contains("503")
                && VortexBridge.ReadSetupResult(requestId, setupRoot)?.Outcome == VortexSyncOutcome.NeedsAttention, "Loader network failure persists the first cause instead of connection success");
        }

        var cancellationSnapshot = Snapshot(exchange, game, library.State.Vortex!.Id, "cancelled", "1.0.0", "cancel", 'f');
        string cancellationCapture = library.Fingerprint(library.Active);
        result = VortexBridge.SynchronizeAndApply(library, cancellationSnapshot.Manifest, library.Active.Id, new CancellationToken(true), cancellationCapture);
        check(result.Outcome == VortexSyncOutcome.Cancelled && result.BaseFingerprint == cancellationCapture
            && VortexBridge.ReadResult(library, result.RequestId)?.BaseFingerprint == cancellationCapture, "Cancelled request retains its admitted fingerprint for a valid retry");
        VortexBridge.Disconnect(library);
        check(library.State.Vortex == null && !File.Exists(Path.Combine(exchange, "connection.json")) && library.Active.Mods.Count == 0, "disconnect stops synchronization without changing profile contents");
    }

    private static (string Manifest, VortexSnapshot Snapshot) Snapshot(string exchange, string game, string connection, string? bridgeId, string? version, string? companion, char revision, bool managedDll = true)
    {
        string request = Guid.NewGuid().ToString("N");
        var snapshot = new VortexSnapshot { Schema = 1, RequestId = request, ConnectionId = connection, GameId = "nivalisnights", GameFolder = game, Revision = new string(revision, 64), CreatedUtc = DateTime.UtcNow };
        string generation = Path.Combine(exchange, "snapshots", request);
        Directory.CreateDirectory(generation);
        if (bridgeId != null)
        {
            string payload = Path.Combine(generation, bridgeId);
            string companionPath = "BepInEx/plugins/Fixture/value.txt";
            string text = Path.Combine(payload, companionPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(text)!);
            File.WriteAllText(text, companion ?? "fixture");
            var files = new List<VortexSnapshotFile> { Item(companionPath, text) };
            if (managedDll)
            {
                string relativeDll = "BepInEx/plugins/Fixture/Fixture.dll";
                string dll = Path.Combine(payload, relativeDll.Replace('/', Path.DirectorySeparatorChar));
                File.Copy(typeof(TestPlugin).Assembly.Location, dll);
                files.Add(Item(relativeDll, dll));
            }
            snapshot.Packages.Add(new VortexSnapshotPackage
            {
                BridgeId = bridgeId, Name = "Fixture mod", Version = version!, SourceModId = "42", SourceFileId = version!,
                PageUrl = "https://www.nexusmods.com/nivalisnights/mods/42", Payload = Path.GetRelativePath(exchange, payload).Replace('\\', '/'),
                ContentSha256 = Aggregate(files), Files = files
            });
        }
        string manifest = Path.Combine(generation, "snapshot.json");
        JsonFiles.Write(manifest, snapshot);
        return (manifest, snapshot);
    }

    private static VortexSnapshotFile Item(string relative, string path) => new() { Path = relative, Length = new FileInfo(path).Length, Sha256 = FileSafety.Hash(path) };
    private static string Aggregate(IEnumerable<VortexSnapshotFile> files)
    {
        var text = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            text.Append(file.Path.Replace('\\', '/')).Append('\0').Append(file.Length).Append('\0').Append(file.Sha256.ToLowerInvariant()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
    private sealed class PendingLoaderHandler : System.Net.Http.HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<System.Net.Http.HttpResponseMessage> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellation)
        {
            Started.TrySetResult(true); return Response.Task.WaitAsync(cancellation);
        }
    }

}
