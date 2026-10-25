using Luz;

internal static class VortexSnapshotReplay
{
    public static void Run(string root, string manifestPath, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Vortex replay requires Windows.");
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(manifestPath);
        string originalRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, "..", ".."));
        string game = Path.Combine(root, "replay-game"), exchange = Path.Combine(root, "replay-exchange");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture; never execute");
        File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        using var library = new Library(Path.Combine(root, "replay-library"));
        library.State.GameFolder = game; library.Save();
        VortexBridge.Connect(library, Environment.ProcessPath!, exchange, checkConflicts: false);
        snapshot.ConnectionId = library.State.Vortex!.Id;
        snapshot.GameFolder = game;
        snapshot.RequestId = Guid.NewGuid().ToString("N");
        foreach (VortexSnapshotPackage package in snapshot.Packages)
        {
            string original = FileSafety.Under(originalRoot, package.Payload);
            package.Payload = "snapshots/" + snapshot.RequestId + "/packages/" + package.BridgeId;
            FileSafety.CopyTree(original, FileSafety.Under(exchange, package.Payload));
        }
        string localManifest = Path.Combine(exchange, "snapshots", snapshot.RequestId, "snapshot.json");
        JsonFiles.Write(localManifest, snapshot);
        string deployed = Path.Combine(exchange, "vortex-game", "deployed", "ordinary-vortex-link");
        LinkedRootChecks.Link(deployed, game);
        try
        {
        VortexSyncResult result = VortexBridge.Synchronize(library, localManifest);
        check(result.Outcome == VortexSyncOutcome.NeedsAttention && library.Active.Mods.Count == 0,
            "actual snapshot initially stops for its explicit loader review, without a partial import");
        foreach (VortexPackageResult blocked in result.Packages.Where(item => item.Action == "Blocked"))
        {
            if (!blocked.Detail.Contains("BepInEx loader bundle", StringComparison.Ordinal))
                throw new InvalidDataException("Actual package needs investigation: " + blocked.Name + ": " + blocked.Detail);
            VortexBridge.ExcludeSource(library, blocked.BridgeId);
        }
        result = VortexBridge.Synchronize(library, localManifest);
        check(result.Outcome == VortexSyncOutcome.Processing && library.Active.Mods.Count == 3,
            "actual Ambience, Greenhouse and Performance Fix snapshot imports all three mods with Vortex deployment links present");
        foreach (ModEntry entry in library.Active.Mods)
        {
            ModPackage package = library.State.Packages.Single(item => item.Id == entry.PackageId);
            Console.WriteLine("REPLAY IMPORT " + package.Name + " " + package.Version + " enabled=" + entry.Enabled);
        }
        check(VortexBridge.Synchronize(library, localManifest).Outcome == VortexSyncOutcome.NoChanges,
            "actual snapshot replay is idempotent after explicit loader exclusion");
        VortexBridge.RefreshDescriptor(library, Environment.ProcessPath!);
        check(Directory.Exists(deployed), "actual-snapshot import and descriptor refresh preserve Vortex-owned linked deployment");
        Console.WriteLine("Replay used a disposable library and fake game; no Apply or application/game execution occurred.");
        }
        finally { Directory.Delete(deployed); }
    }
}
