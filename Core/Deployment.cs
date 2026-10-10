namespace Luz;

internal sealed record AppliedFiles(string Fingerprint, List<string> Paths);
public sealed record BackupInfo(string Id, string GameFolder, string ProfileId, string Fingerprint, DateTime CreatedUtc);
public sealed class Deployment(Library library)
{
    private static readonly string[] Areas = ["plugins", "patchers", "config"];
    public string? Pending => File.Exists(Path.Combine(library.Root, "pending.json")) ? JsonFiles.Read<BackupInfo>(Path.Combine(library.Root, "pending.json")).Id : null;
    public List<BackupInfo> Backups() => Directory.Exists(Path.Combine(library.Root, "backups")) ? Directory.GetFiles(Path.Combine(library.Root, "backups"), "backup.json", SearchOption.AllDirectories).Select(JsonFiles.Read<BackupInfo>).OrderByDescending(b => b.CreatedUtc).ToList() : [];
    public List<Issue> Preview()
    {
        GameFiles.Validate(library.State.GameFolder);
        var issues = ModPlanner.Check(library.Active, library.State.Packages);
        if (File.Exists(Path.Combine(library.Root, "loader-pending.json"))) issues.Add(new("Error", "Recover the interrupted loader installation before applying mods."));
        if (Pending != null) issues.Add(new("Error", "A previous deployment was interrupted. Restore its backup before applying again."));
        if (!File.Exists(FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"))) issues.Add(new("Error", "Install the IL2CPP loader before applying mods."));
        var unmanaged = UnmanagedBinaries();
        if (unmanaged.Count > 0) issues.Add(new("Error", "Unmanaged mod binaries would be removed by Apply: " + string.Join(", ", unmanaged) + ". Import the complete mod ZIP or adopt these installed files before applying."));
        return issues;
    }
    public List<string> UnmanagedBinaries()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applied = library.State.Profiles.FirstOrDefault(p => p.Id == library.State.AppliedProfileId);
        string ownership = Path.Combine(library.Root, "applied-files.json");
        if (File.Exists(ownership))
        {
            var snapshot = JsonFiles.Read<AppliedFiles>(ownership);
            if (snapshot.Fingerprint == library.State.AppliedFingerprint)
                foreach (var rel in snapshot.Paths) owned.Add(rel);
        }
        else if (applied != null)
        {
            // Legacy installs have no ownership snapshot. Recognize only bytes
            // from immutable versions of a mod in the applied profile.
            foreach (var entry in applied.Mods)
            {
                var selected = library.State.Packages.Single(p => p.Id == entry.PackageId);
                foreach (var version in library.State.Packages.Where(p => p.Id == selected.Id
                    || selected.SourceId.Length > 0 && p.Source == selected.Source && p.SourceId == selected.SourceId
                    || p.Plugins.Any(a => selected.Plugins.Any(b => a.Guid == b.Guid))))
                foreach (var rel in version.Files)
                {
                    string installed = FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/" + rel);
                    string payload = FileSafety.Under(library.PackageRoot(version.Id), "payload/" + rel);
                    if (File.Exists(installed) && File.Exists(payload) && FileSafety.Hash(installed) == FileSafety.Hash(payload)) owned.Add(rel);
                }
            }
        }
        // The newly selected profile can adopt installed files before its first Apply.
        foreach (var entry in library.Active.Mods.Where(m => m.Enabled))
        foreach (var rel in library.State.Packages.Single(p => p.Id == entry.PackageId).Files)
        {
            string installed = FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/" + rel);
            string payload = FileSafety.Under(library.PackageRoot(entry.PackageId), "payload/" + rel);
            if (File.Exists(installed) && File.Exists(payload) && FileSafety.Hash(installed) == FileSafety.Hash(payload)) owned.Add(rel);
        }
        var result = new List<string>();
        foreach (var area in new[] { "plugins", "patchers" })
        {
            string folder = FileSafety.PortableDestination(library.State.GameFolder, "BepInEx/" + area);
            if (!Directory.Exists(folder)) continue;
            FileSafety.NoLinks(folder);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase) && !Path.GetExtension(file).Equals(".asi", StringComparison.OrdinalIgnoreCase)) continue;
                FileSafety.NoLinks(file);
                string rel = area + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (!owned.Contains(rel)) result.Add(rel);
            }
        }
        return result;
    }
    public BackupInfo Apply()
    {
        GameFiles.RequireClosed(); var issues = Preview();
        if (issues.Any(i => i.Severity == "Error")) throw new InvalidOperationException(string.Join('\n', issues.Where(i => i.Severity == "Error").Select(i => i.Message)));
        string game = library.State.GameFolder; string bep = FileSafety.PortableDestination(game, "BepInEx");
        // Preserve edits made in-game for the profile which actually owns the files.
        if (library.State.AppliedProfileId.Length > 0)
        {
            string oldConfig = library.ConfigRoot(library.State.AppliedProfileId);
            FileSafety.CopyTreeReplacing(FileSafety.PortableDestination(bep, "config"), oldConfig, library.Root);
        }
        string stage = FileSafety.Under(library.Root, "staging/deploy-" + Guid.NewGuid().ToString("N"));
        foreach (var area in Areas) Directory.CreateDirectory(Path.Combine(stage, area));
        try
        {
            FileSafety.CopyTree(library.ConfigRoot(library.Active.Id), Path.Combine(stage, "config"));
            foreach (var entry in library.Active.Mods.Where(m => m.Enabled))
            {
                var package = library.State.Packages.Single(p => p.Id == entry.PackageId);
                foreach (var rel in package.Files)
                {
                    if (!Areas.Any(a => rel.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unsupported deployment destination: " + rel);
                    string source = FileSafety.Under(Path.Combine(library.PackageRoot(package.Id), "payload"), rel);
                    string target = FileSafety.PortableDestination(stage, rel);
                    FileSafety.NoLinks(Path.GetDirectoryName(source)!);
                    if (rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true);
                }
            }
            var backup = Snapshot(game);
            JsonFiles.Write(Path.Combine(library.Root, "pending.json"), backup);
            try { ReplaceAreas(game, stage); }
            catch (Exception operation)
            {
                // The journal is retained until a complete rollback has been confirmed.
                try
                {
                    ReplaceAreas(game, FileSafety.Under(library.Root, "backups/" + backup.Id));
                    File.Delete(Path.Combine(library.Root, "pending.json"));
                }
                catch (Exception rollback) { throw new IOException("Deployment failed and rollback also failed. The recovery record has been kept. Original error: " + operation.Message + "; rollback error: " + rollback.Message, new AggregateException(operation, rollback)); }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operation).Throw();
                throw;
            }
            library.State.AppliedProfileId = library.Active.Id; library.State.AppliedFingerprint = library.Fingerprint(library.Active); library.Save();
            JsonFiles.Write(Path.Combine(library.Root, "applied-files.json"), new AppliedFiles(library.State.AppliedFingerprint,
                library.Active.Mods.Where(m => m.Enabled).SelectMany(m => library.State.Packages.Single(p => p.Id == m.PackageId).Files).Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
            File.Delete(Path.Combine(library.Root, "pending.json")); return backup;
        }
        finally { FileSafety.DeleteOwned(library.Root, stage); }
    }
    private BackupInfo Snapshot(string game)
    {
        string id = Guid.NewGuid().ToString("N"); string backup = FileSafety.Under(library.Root, "backups/" + id);
        Directory.CreateDirectory(backup);
        foreach (var area in Areas) FileSafety.CopyTree(FileSafety.PortableDestination(game, "BepInEx/" + area), Path.Combine(backup, area));
        var info = new BackupInfo(id, game, library.State.AppliedProfileId, library.State.AppliedFingerprint, DateTime.UtcNow);
        JsonFiles.Write(Path.Combine(backup, "backup.json"), info);
        string ownership = Path.Combine(library.Root, "applied-files.json");
        if (File.Exists(ownership)) File.Copy(ownership, Path.Combine(backup, "applied-files.json"));
        return info;
    }
    private static void ReplaceAreas(string game, string source)
    {
        GameFiles.RequireClosed(); string bep = FileSafety.PortableDestination(game, "BepInEx");
        string stage = FileSafety.Under(bep, ".luz-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var changed = new List<string>();
        try
        {
            foreach (var area in Areas)
            {
                string next = Path.Combine(stage, "new", area); Directory.CreateDirectory(next);
                FileSafety.CopyTree(Path.Combine(source, area), next);
            }
            foreach (var area in Areas)
            {
                string current = FileSafety.PortableDestination(bep, area); string old = Path.Combine(stage, "old", area);
                Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                if (Directory.Exists(current)) Directory.Move(current, old);
                try { Directory.Move(Path.Combine(stage, "new", area), current); }
                catch { if (Directory.Exists(old)) Directory.Move(old, current); throw; }
                changed.Add(area);
            }
        }
        catch
        {
            foreach (var area in changed.AsEnumerable().Reverse())
            {
                string current = FileSafety.PortableDestination(bep, area), old = Path.Combine(stage, "old", area);
                FileSafety.DeleteOwned(bep, current);
                if (Directory.Exists(old)) Directory.Move(old, current);
            }
            throw;
        }
        finally { FileSafety.DeleteOwned(bep, stage); }
    }
    public void Restore(BackupInfo info)
    {
        GameFiles.RequireClosed(); Library.ValidateId(info.Id);
        string destination = GameInstallation.ResolveBackupDestination(library, info.GameFolder);
        GameFiles.Validate(destination);
        string source = FileSafety.Under(library.Root, "backups/" + info.Id);
        if (!File.Exists(Path.Combine(source, "backup.json"))) throw new FileNotFoundException("Backup metadata is missing.");
        if (JsonFiles.Read<BackupInfo>(Path.Combine(source, "backup.json")) != info) throw new InvalidDataException("The selected profile backup metadata does not match its restore record.");
        var rescue = Snapshot(destination); // Preserve the state that is being replaced as another restore point.
        JsonFiles.Write(Path.Combine(library.Root, "pending.json"), rescue);
        ReplaceAreas(destination, source);
        library.State.AppliedProfileId = info.ProfileId; library.State.AppliedFingerprint = info.Fingerprint; library.Save();
        string ownership = Path.Combine(library.Root, "applied-files.json"), oldOwnership = Path.Combine(source, "applied-files.json");
        if (File.Exists(oldOwnership)) File.Copy(oldOwnership, ownership, true);
        else if (File.Exists(ownership)) File.Delete(ownership);
        File.Delete(Path.Combine(library.Root, "pending.json"));
    }
}
