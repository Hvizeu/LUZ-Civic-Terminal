namespace Luz;

public sealed record RecoveryOperation(string Kind, string Title, string Action, string? BackupPath, string? GameFolder, string? Problem);

// One read-only interpretation of the two transaction journals for status,
// recovery actions and completion feedback. Invalid journals remain blocking.
public static class OperationRecovery
{
    public static List<RecoveryOperation> Inspect(string root)
    {
        var result = new List<RecoveryOperation>();
        foreach (string kind in new[] { "profile", "loader" })
        {
            string journal = Path.Combine(root, kind == "profile" ? "pending.json" : "loader-pending.json");
            if (!File.Exists(journal)) continue;
            string title = kind == "profile" ? "Interrupted profile deployment" : "Interrupted BepInEx installation";
            string action = kind == "profile" ? "Recover profile deployment" : "Recover BepInEx installation";
            string? backup = null, game = null, problem = null;
            try
            {
                if (kind == "profile")
                {
                    var info = JsonFiles.Read<BackupInfo>(journal);
                    if (string.IsNullOrWhiteSpace(info.Id)) throw new InvalidDataException("The profile recovery ID is missing.");
                    Library.ValidateId(info.Id);
                    backup = FileSafety.Under(root, "backups/" + info.Id);
                    FileSafety.NoLinks(backup);
                    var saved = JsonFiles.Read<BackupInfo>(Path.Combine(backup, "backup.json"));
                    if (saved != info) throw new InvalidDataException("The profile backup metadata does not match the interrupted operation.");
                    game = info.GameFolder;
                }
                else
                {
                    string folder = Path.Combine(root, "loader-backups");
                    backup = FileSafety.Under(folder, Path.GetRelativePath(folder, JsonFiles.Read<string>(journal)));
                    FileSafety.NoLinks(backup);
                    var info = JsonFiles.Read<LoaderBackup>(Path.Combine(backup, "loader-backup.json"));
                    LoaderInstaller.ValidateBackup(backup, info);
                    game = info.Game;
                }
                if (string.IsNullOrWhiteSpace(game)) throw new InvalidDataException("The recovery destination is missing.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            {
                problem = "The required recovery backup cannot be used: " + ex.Message + " Export diagnostics for support. The recovery record has been kept; game files have not been changed.";
            }
            result.Add(new(kind, title, action, backup, game, problem));
        }
        return result;
    }

    public static string Summary(IReadOnlyCollection<RecoveryOperation> pending) => pending.Count == 0 ? "No recovery needed." : pending.Count > 1
        ? "Profile and BepInEx recovery needed."
        : pending.FirstOrDefault()?.Kind == "loader" ? "BepInEx recovery needed." : "Profile recovery needed.";

    public static string CompletionMessage(Library library)
    {
        var pending = Inspect(library.Root);
        if (pending.Count > 0) return "Operation finished. " + Summary(pending) + " Open Maintenance to finish recovery. Apply and Play remain unavailable.";
        if (string.IsNullOrWhiteSpace(library.State.GameFolder)) return "Completed. Choose your game folder in Maintenance.";
        if (!File.Exists(FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")))
            return "Completed. Install BepInEx in Maintenance before applying your profile.";
        if (ModPlanner.Check(library.Active, library.State.Packages).Any(x => x.Severity == "Error"))
            return "Completed. Review the blocking issues in mod details before applying.";
        return library.State.AppliedProfileId == library.Active.Id && library.State.AppliedFingerprint == library.Fingerprint(library.Active)
            ? "Completed. Your current profile is applied."
            : "Completed. Review your profile, then Apply.";
    }

    public static void Restore(Library library, string kind)
    {
        // Re-read after confirmation; do not restore a stale UI snapshot.
        var item = Inspect(library.Root).SingleOrDefault(x => x.Kind == kind)
            ?? throw new InvalidOperationException("This operation no longer needs recovery. Refresh Maintenance.");
        if (item.Problem != null) throw new InvalidOperationException(item.Problem);
        if (kind == "profile") new Deployment(library).Restore(JsonFiles.Read<BackupInfo>(Path.Combine(item.BackupPath!, "backup.json")));
        else
        {
            LoaderInstaller.Restore(library.Root, item.BackupPath!);
            library.State.LoaderVersion = "";
            library.Save();
        }
        if (Inspect(library.Root).Any(x => x.Kind == kind)) throw new IOException("Recovery is still pending. Export diagnostics from Maintenance.");
    }
}
