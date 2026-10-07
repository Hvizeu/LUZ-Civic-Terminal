namespace Luz;

/// <summary>Owns the single game-folder binding stored by a LUZ library.</summary>
public static class GameInstallation
{
    public static string Resolve(string folder) => FileSafety.ResolveFolder(folder);

    public static bool SameLocation(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try { return Resolve(first).Equals(Resolve(second), HostPlatform.PathComparison); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The old installation may be unavailable precisely because it was
            // moved or its permissions changed. Compare its lexical path so the
            // user can still explicitly reconnect the replacement location.
            return Path.GetFullPath(first).Equals(Path.GetFullPath(second), HostPlatform.PathComparison);
        }
    }

    public static bool RequiresReconnect(Library library, string folder)
    {
        string current = library.State.GameFolder;
        return current.Length > 0 && !SameLocation(current, folder);
    }

    public static string ReconnectPrompt(Library library, string folder) =>
        "LUZ currently owns this installation:\n" + library.State.GameFolder +
        "\n\nReconnect its existing profiles and restore points to this folder?\n" + Resolve(folder) +
        "\n\nOnly choose this if it is the same Nivalis installation after moving it. This does not copy or change game files. A different installation is not supported by this library.";

    public static void Select(Library library, string folder, bool reconnectConfirmed = false)
    {
        string resolved = Resolve(folder);
        GameFiles.ValidateIdentity(resolved);
        string previous = library.State.GameFolder;
        bool moved = previous.Length > 0 && !SameLocation(previous, resolved);
        if (moved && RequiresReconnect(library, resolved) && !reconnectConfirmed)
            throw new InvalidOperationException("Confirm that this is the same moved installation before reconnecting it.");

        if (!moved)
        {
            string unchangedFingerprint = library.State.AppliedFingerprint;
            string unchangedLoaderVersion = library.State.LoaderVersion;
            library.State.GameFolder = resolved;
            if (!previous.Equals(resolved, HostPlatform.PathComparison))
            {
                library.State.AppliedFingerprint = "";
                library.State.LoaderVersion = "";
            }
            try { library.Save(); }
            catch
            {
                library.State.GameFolder = previous;
                library.State.AppliedFingerprint = unchangedFingerprint;
                library.State.LoaderVersion = unchangedLoaderVersion;
                throw;
            }
            return;
        }

        var oldHistory = library.State.PreviousGameFolders.ToList();
        string oldFingerprint = library.State.AppliedFingerprint;
        string oldLoaderVersion = library.State.LoaderVersion;
        library.State.PreviousGameFolders ??= [];
        if (!oldHistory.Any(path => SameLocation(path, previous))) library.State.PreviousGameFolders.Add(previous);
        library.State.GameFolder = resolved;
        // The last applied profile remains the owner of its cached config, but
        // the destination must be reviewed and applied again after a move.
        library.State.AppliedFingerprint = "";
        library.State.LoaderVersion = "";
        try { library.Save(); }
        catch
        {
            library.State.GameFolder = previous;
            library.State.PreviousGameFolders = oldHistory;
            library.State.AppliedFingerprint = oldFingerprint;
            library.State.LoaderVersion = oldLoaderVersion;
            throw;
        }
    }

    /// <summary>Maps a backup's recorded path to this library's explicitly bound installation.</summary>
    public static string ResolveBackupDestination(Library library, string recordedFolder)
    {
        if (string.IsNullOrWhiteSpace(library.State.GameFolder))
            throw new InvalidOperationException("Select the managed Nivalis installation before restoring this backup.");
        string current = Resolve(library.State.GameFolder);
        if (string.IsNullOrWhiteSpace(recordedFolder)) throw new InvalidDataException("Backup installation location is missing.");
        if (SameLocation(recordedFolder, current)) return current;
        if (library.State.PreviousGameFolders.Any(path => SameLocation(path, recordedFolder))) return current;
        throw new InvalidOperationException("This backup belongs to a different installation and cannot be restored through the current LUZ profile.");
    }

    public static bool CanRestore(Library library, string recordedFolder)
    {
        try { _ = ResolveBackupDestination(library, recordedFolder); return true; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { return false; }
    }
}
