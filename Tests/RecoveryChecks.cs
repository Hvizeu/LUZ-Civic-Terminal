using Luz;

internal static class RecoveryChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string game = Path.Combine(root, "recovery-game"), data = Path.Combine(root, "recovery-library");
        const string dll = "BepInEx/core/BepInEx.Unity.IL2CPP.dll";
        Directory.CreateDirectory(Path.Combine(game, "BepInEx/core"));
        File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture, never execute");
        File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        File.WriteAllText(Path.Combine(game, dll), "original loader");
        using var library = new Library(data);
        library.State.GameFolder = game; library.Save();
        var deployment = new Deployment(library);
        var backup = deployment.Apply();
        string loaderBackup = Path.Combine(data, "loader-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(loaderBackup, "files/BepInEx/core"));
        File.WriteAllText(Path.Combine(loaderBackup, "files", dll), "original loader");
        var loader = new LoaderBackup(game, [dll, "winhttp.dll"], [dll]);
        JsonFiles.Write(Path.Combine(loaderBackup, "loader-backup.json"), loader);
        string oldGame = game;
        game = Path.Combine(root, "recovery-game-moved");
        Directory.Move(oldGame, game);
        GameInstallation.Select(library, game, reconnectConfirmed: true);
        string profileJournal = Path.Combine(data, "pending.json"), loaderJournal = Path.Combine(data, "loader-pending.json");
        void Pending() { JsonFiles.Write(profileJournal, backup); JsonFiles.Write(loaderJournal, loaderBackup); }
        Pending();
        check(OperationRecovery.Inspect(library).Count == 2 && OperationRecovery.Inspect(library).All(x => x.Problem == null && x.GameFolder == game), "Both pending operations route to the confirmed moved installation");
        check(OperationRecovery.Summary(OperationRecovery.Inspect(library)).Contains("Profile and BepInEx"), "Sidebar distinguishes simultaneous recoveries");
        reject(() => deployment.Restore(backup with { GameFolder = Path.Combine(root, "unrelated-game") }), "Profile restore refuses an unrelated installation");
        // Both journals retain their original metadata while recovery targets the confirmed moved folder.
        for (int i = 0; i < 3; i++)
        {
            deployment.Restore(backup);
            check(OperationRecovery.Inspect(library).Single().Kind == "loader", "Profile restore leaves loader recovery pending");
            check(OperationRecovery.CompletionMessage(library).Contains("BepInEx recovery needed"), "Completion names remaining loader blocker instead of instructing Apply");
        }
        OperationRecovery.Restore(library, "loader");
        check(OperationRecovery.Inspect(library).Count == 0 && !deployment.Preview().Any(x => x.Severity == "Error"), "Matching loader recovery unblocks deployment after relocation");
        check(OperationRecovery.CompletionMessage(library).Contains("then Apply"), "Recovered original installation correctly requests Apply");
        deployment.Apply();
        check(OperationRecovery.CompletionMessage(library).Contains("current profile is applied"), "Already applied profile does not request Apply again");
        Pending(); OperationRecovery.Restore(library, "loader");
        check(OperationRecovery.Inspect(library).Single().Kind == "profile", "Loader recovery preserves pending profile operation");
        OperationRecovery.Restore(library, "profile");
        check(OperationRecovery.Inspect(library).Count == 0, "Both operations recover in reverse order too");
        reject(() => OperationRecovery.Restore(library, "profile"), "Stale recovery action cannot replay an old restore");
        JsonFiles.Write(loaderJournal, loaderBackup);
        File.Delete(Path.Combine(loaderBackup, "files", dll));
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "preserve on failed recovery");
        check(OperationRecovery.Inspect(library).Single().Problem!.Contains("missing"), "Missing loader payload has actionable diagnostic");
        reject(() => OperationRecovery.Restore(library, "loader"), "Incomplete backup cannot restore");
        check(File.Exists(loaderJournal) && File.ReadAllText(Path.Combine(game, "winhttp.dll")) == "preserve on failed recovery", "Failed recovery keeps journal and introduced files intact");
        // Validate all sources before touching the first destination, even through
        // the historical manual restore route.
        File.WriteAllText(Path.Combine(loaderBackup, "files", dll), "backup value");
        JsonFiles.Write(Path.Combine(loaderBackup, "loader-backup.json"), new LoaderBackup(game, [dll, "winhttp.dll"], [dll, "winhttp.dll"]));
        reject(() => LoaderInstaller.Restore(library, loaderBackup), "Later missing source stops manual restore before first write");
        check(File.ReadAllText(Path.Combine(game, dll)) == "original loader", "Failed preflight preserves first destination");
        File.WriteAllText(loaderJournal, "{broken");
        check(OperationRecovery.Inspect(library).Single().Problem != null, "Corrupt loader journal remains visible without breaking UI inspection");
        File.Delete(loaderJournal); File.WriteAllText(profileJournal, "{}");
        check(OperationRecovery.Inspect(library).Single().Problem != null, "Missing profile metadata fields remain a visible blocker");
        JsonFiles.Write(profileJournal, backup with { Id = new string('f', 32) });
        check(OperationRecovery.Inspect(library).Single().Problem != null, "Missing referenced profile backup is reported");
        reject(() => OperationRecovery.Restore(library, "profile"), "Missing backup never clears recovery flag");
        File.Delete(profileJournal);
        JsonFiles.Write(Path.Combine(loaderBackup, "loader-backup.json"), new LoaderBackup(Path.Combine(root, "unrelated-game"), [dll, "winhttp.dll"], [dll]));
        JsonFiles.Write(loaderJournal, loaderBackup);
        check(OperationRecovery.Inspect(library).Single().Problem!.Contains("different installation"), "Loader recovery refuses a valid backup from an unrelated installation");
        string loaderBeforeRejectedRestore = File.ReadAllText(Path.Combine(game, dll));
        reject(() => OperationRecovery.Restore(library, "loader"), "Unrelated loader backup cannot be restored");
        check(File.Exists(loaderJournal) && File.ReadAllText(Path.Combine(game, dll)) == loaderBeforeRejectedRestore, "Rejected unrelated recovery preserves its journal and current files");
        File.Delete(loaderJournal);
        check(OperationRecovery.Inspect(library).Count == 0, "Clean library has no recovery banner");
    }
}
