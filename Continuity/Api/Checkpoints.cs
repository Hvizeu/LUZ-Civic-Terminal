namespace Luz.Continuity;

public sealed class CheckpointManifest
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class Checkpoints
{
    public static Dictionary<string, string> Inventory(string saves, string config, CancellationToken cancellation = default)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var (prefix, root) in new[] { ("saves", saves), ("config", config) })
        {
            SafeFiles.NoLinks(root); if (!Directory.Exists(root)) continue;
            var directories = new Stack<string>(); directories.Push(root);
            while (directories.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested(); string folder = directories.Pop(); SafeFiles.NoLinks(folder);
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    cancellation.ThrowIfCancellationRequested(); SafeFiles.NoLinks(entry);
                    string relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                    if (prefix == "config" && (relative == "LUZContinuity" || relative.StartsWith("LUZContinuity/", StringComparison.OrdinalIgnoreCase))) continue;
                    if (Directory.Exists(entry)) { directories.Push(entry); if (directories.Count + files.Count > 8192) throw new IOException("Checkpoint tree exceeds its entry limit."); continue; }
                    long length = new FileInfo(entry).Length;
                    if (length > 128L * 1024 * 1024 || (size += length) > 2L * 1024 * 1024 * 1024 || files.Count >= 8192) throw new IOException("Checkpoint exceeds its file or aggregate size limit.");
                    if (!files.TryAdd(prefix + "/" + relative, SafeFiles.Hash(entry))) throw new IOException("Case-conflicting checkpoint paths.");
                }
            }
        }
        return files;
    }
    public static bool Same(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) => left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var hash) && hash == p.Value);
    public static string Capture(string library, string saves, string config, CancellationToken cancellation = default)
    {
        SafeFiles.NoLinks(library);
        string fullRoot = Path.GetFullPath(library).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string root in new[] { saves, config })
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullRoot.StartsWith(full, StringComparison.OrdinalIgnoreCase) || full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("Checkpoints must be outside live save and configuration trees.");
        }
        var before = Inventory(saves, config, cancellation);
        string id = Guid.NewGuid().ToString("N"), area = SafeFiles.Under(library, "continuity/checkpoints");
        Directory.CreateDirectory(area);
        string destination = SafeFiles.Under(area, id), stage = destination + ".incomplete";
        Directory.CreateDirectory(stage);
        var manifest = new CheckpointManifest { Id = id, Label = "Backup Save " + (Directory.EnumerateDirectories(area).Count(d => File.Exists(Path.Combine(d, "checkpoint.json"))) + 1).ToString("D3"), Files = before };
        // Incomplete generations are evidence and are never offered as usable checkpoints.
        foreach (var pair in before)
        {
            cancellation.ThrowIfCancellationRequested(); string source = pair.Key.StartsWith("saves/") ? SafeFiles.Under(saves, pair.Key[6..]) : SafeFiles.Under(config, pair.Key[7..]);
            string target = SafeFiles.Under(stage, pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var input = File.OpenRead(source)) using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
            if (SafeFiles.Hash(target) != pair.Value) throw new IOException("Checkpoint input changed during capture: " + pair.Key);
        }
        if (!Same(before, Inventory(saves, config, cancellation))) throw new IOException("Checkpoint inputs changed during capture; incomplete generation retained.");
        cancellation.ThrowIfCancellationRequested(); SafeFiles.Write(Path.Combine(stage, "checkpoint.json"), manifest); Verify(stage); Directory.Move(stage, destination); return destination;
    }
    public static CheckpointManifest Verify(string folder)
    {
        SafeFiles.NoLinks(folder); var manifest = SafeFiles.Read<CheckpointManifest>(Path.Combine(folder, "checkpoint.json"));
        if (manifest.Schema != 1 || manifest.Id.Length != 32 || !manifest.Id.All(Uri.IsHexDigit) || manifest.Files.Count > 8192) throw new InvalidDataException("Invalid checkpoint manifest.");
        foreach (var pair in manifest.Files) if (SafeFiles.Hash(SafeFiles.Under(folder, pair.Key)) != pair.Value) throw new IOException("Checkpoint integrity failure: " + pair.Key);
        return manifest;
    }
}

public sealed class PublicationJournal
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string State { get; set; } = "Preparing";
    public Dictionary<string, string?> Before { get; set; } = new();
    public Dictionary<string, string> After { get; set; } = new();
    public List<string> Published { get; set; } = new();
    public string FirstFailure { get; set; } = "";
}

/// <summary>One journal spans native saves and companions. Prepared outputs are never mistaken for commits.</summary>
public static class Publications
{
    public static string Root(ContinuitySession session) => SafeFiles.Under(session.LibraryRoot, "continuity/transactions/" + session.Id);
    public static bool Pending(ContinuitySession session) => File.Exists(Path.Combine(Root(session), "journal.json"));
    public static Exception RetainFailure(ContinuitySession session, Exception first)
    {
        var errors = new List<Exception> { first };
        try { if (Pending(session)) Observe(session); }
        catch (Exception cleanup) { session.CleanupFailures.Add(cleanup.ToString()); errors.Add(cleanup); }
        try { session.Fail(first); }
        catch (Exception cleanup) { session.CleanupFailures.Add(cleanup.ToString()); errors.Add(cleanup); }
        return errors.Count == 1 ? first : new AggregateException("Continuity operation and failure retention were incomplete.", errors);
    }
    public static void Begin(ContinuitySession session, IEnumerable<string> paths)
    {
        session.Validate(); if (Pending(session)) throw new IOException("An unfinished continuity publication requires recovery in LUZ.");
        var allowed = session.Slots.Values.SelectMany(slot => Companions.WriteSet(session, slot)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string root = Root(session); Directory.CreateDirectory(root);
        var journal = new PublicationJournal();
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!allowed.Contains(path)) throw new InvalidDataException("Publication does not own this branch file: " + path);
            string file = session.File(path); journal.Before.Add(path, File.Exists(file) ? SafeFiles.Hash(file) : null);
            if (journal.Before[path] != null)
            {
                string backup = SafeFiles.Under(root, "before/" + path); Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(file, backup, true);
                if (SafeFiles.Hash(backup) != journal.Before[path]) throw new IOException("Continuity publication checkpoint differs from live data.");
            }
        }
        if (journal.Before.Count > 4096) throw new IOException("Publication exceeds its file limit.");
        SafeFiles.Write(Path.Combine(root, "journal.json"), journal);
    }
    public static void Observe(ContinuitySession session)
    {
        string root = Root(session); var journal = Read(session);
        foreach (string relative in journal.Before.Keys) { string file = session.File(relative); journal.After[relative] = File.Exists(file) ? SafeFiles.Hash(file) : ""; }
        journal.State = "Observed"; SafeFiles.Write(Path.Combine(root, "journal.json"), journal);
    }
    public static void Commit(ContinuitySession session)
    {
        Observe(session); var journal = Read(session);
        foreach (var pair in journal.After) { if (pair.Value.Length == 0) session.LastOutputs.Remove(pair.Key); else session.LastOutputs[pair.Key] = pair.Value; }
        session.Save(); journal.State = "Committed";
        string root = Root(session); SafeFiles.Write(Path.Combine(root, "commit-" + journal.Id + ".json"), journal);
        // The session receipt is durable before the pending journal is removed.
        File.Move(Path.Combine(root, "journal.json"), Path.Combine(root, "completed-" + journal.Id + ".json"));
    }
    public static PublicationJournal Read(ContinuitySession session)
    {
        var journal = SafeFiles.Read<PublicationJournal>(Path.Combine(Root(session), "journal.json"));
        if (journal.Schema != 1 || journal.Id.Length != 32 || !journal.Id.All(Uri.IsHexDigit) || journal.Before.Count > 4096 || journal.After.Keys.Any(k => !journal.Before.ContainsKey(k))) throw new InvalidDataException("Invalid publication journal.");
        var allowed = session.Slots.Values.SelectMany(slot => Companions.WriteSet(session, slot)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in journal.Before.Keys) { if (!allowed.Contains(relative)) throw new InvalidDataException("Journal targets an unowned file."); _ = session.File(relative); } return journal;
    }
    public static void Restore(ContinuitySession session)
    {
        var journal = Read(session); string root = Root(session);
        string marker = Path.Combine(root, "commit-" + journal.Id + ".json");
        if (File.Exists(marker))
        {
            var committed = SafeFiles.Read<PublicationJournal>(marker);
            var receipt = SafeFiles.Read<ContinuitySession>(session.Record); receipt.Validate();
            if (committed.Id != journal.Id || committed.State != "Committed" || !Checkpoints.Same(committed.After, journal.After)) throw new IOException("Publication commit marker differs from its pending journal.");
            foreach (var pair in committed.After)
            {
                string file = session.File(pair.Key), actual = File.Exists(file) ? SafeFiles.Hash(file) : "";
                if (actual != pair.Value || receipt.LastOutputs.GetValueOrDefault(pair.Key, "") != pair.Value) throw new IOException("Committed publication changed externally; all evidence retained.");
            }
            File.Move(Path.Combine(root, "journal.json"), Path.Combine(root, "completed-" + journal.Id + ".json")); return;
        }
        // Without an observed outcome, a changed file could be an external/cloud edit.
        foreach (var pair in journal.Before)
        {
            string file = session.File(pair.Key); string? actual = File.Exists(file) ? SafeFiles.Hash(file) : null;
            string? expected = journal.After.TryGetValue(pair.Key, out var after) ? after.Length == 0 ? null : after : pair.Value;
            if (actual != expected && actual != pair.Value) throw new IOException("Externally changed output retained: " + pair.Key);
            if (pair.Value != null && SafeFiles.Hash(SafeFiles.Under(root, "before/" + pair.Key)) != pair.Value) throw new IOException("Publication backup integrity failure.");
        }
        foreach (var pair in journal.Before)
        {
            string file = session.File(pair.Key);
            if (pair.Value == null) { if (File.Exists(file)) { string retained = SafeFiles.Under(root, "retained-" + journal.Id + "/" + pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(retained)!); File.Move(file, retained); } session.LastOutputs.Remove(pair.Key); }
            else { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.Copy(SafeFiles.Under(root, "before/" + pair.Key), file, true); session.LastOutputs[pair.Key] = pair.Value; }
        }
        session.Save(); File.Move(Path.Combine(root, "journal.json"), Path.Combine(root, "restored-" + journal.Id + ".json"));
    }
}
