using Luz;

internal static class RelocationChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string data = Path.Combine(root, "relocation-library");
        string oldGame = Path.Combine(root, "relocation-old-game");
        string newGame = Path.Combine(root, "relocation-new-game");
        Directory.CreateDirectory(oldGame);
        File.WriteAllText(Path.Combine(oldGame, "Nivalis Nights.exe"), "fixture, never execute");
        File.WriteAllText(Path.Combine(oldGame, "GameAssembly.dll"), "fixture");
        Directory.CreateDirectory(Path.Combine(oldGame, "BepInEx/core"));

        using (var library = new Library(data))
        {
            GameInstallation.Select(library, oldGame);
            string appliedId = library.Active.Id;
            library.State.AppliedProfileId = appliedId;
            library.State.AppliedFingerprint = "old-fingerprint";
            library.State.LoaderVersion = "old-loader";
            library.Save();
            check(!GameInstallation.RequiresReconnect(library, oldGame + Path.DirectorySeparatorChar), "Equivalent folder spelling does not require reconnect confirmation");

            Directory.Move(oldGame, newGame);
            check(GameInstallation.RequiresReconnect(library, newGame), "A different selected path enters reconnect flow");
            reject(() => GameInstallation.Select(library, newGame), "Reconnect requires explicit confirmation");
            GameInstallation.Select(library, newGame, reconnectConfirmed: true);
            check(library.State.GameFolder == FileSafety.ResolveFolder(newGame) && library.State.PreviousGameFolders.Single() == FileSafety.ResolveFolder(oldGame), "Reconnect records the moved installation path");
            check(library.State.AppliedProfileId == appliedId && library.State.AppliedFingerprint.Length == 0 && library.State.LoaderVersion.Length == 0, "Reconnect preserves profile ownership and invalidates applied and loader status");
            check(GameInstallation.ResolveBackupDestination(library, oldGame) == FileSafety.ResolveFolder(newGame), "Historical backup resolves to the confirmed current folder");
            reject(() => GameInstallation.ResolveBackupDestination(library, Path.Combine(root, "unrelated-game")), "Backup from an unrelated installation is refused");

            string cached = library.ConfigRoot(appliedId);
            string incoming = Path.Combine(root, "incoming-config");
            Directory.CreateDirectory(cached); File.WriteAllText(Path.Combine(cached, "old.cfg"), "cached");
            Directory.CreateDirectory(incoming); File.WriteAllText(Path.Combine(incoming, "new.cfg"), "destination");
            check(FileSafety.CopyTreeReplacing(incoming, cached, library.Root) && File.Exists(Path.Combine(cached, "new.cfg")) && !File.Exists(Path.Combine(cached, "old.cfg")), "Readable destination config replaces cached config after staging");
            check(!FileSafety.CopyTreeReplacing(Path.Combine(root, "missing-config"), cached, library.Root) && File.Exists(Path.Combine(cached, "new.cfg")), "Missing destination config preserves cached configuration");
            string invalidSource = Path.Combine(root, "not-a-config-folder"); File.WriteAllText(invalidSource, "file");
            reject(() => FileSafety.CopyTreeReplacing(invalidSource, cached, library.Root), "Invalid config source fails without replacing cached config");
            check(File.Exists(Path.Combine(cached, "new.cfg")), "Failed config capture leaves cached configuration intact");

            library.State.Schema = 1;
            library.Save();
        }

        using var reopened = new Library(data);
        check(reopened.State.Schema == 2 && reopened.State.PreviousGameFolders.Single() == FileSafety.ResolveFolder(oldGame), "Schema 1 registry migrates with relocation history intact");
        check(File.Exists(Path.Combine(data, "registry.json.previous")), "Registry migration retains the previous registry as a backup");
    }
}
