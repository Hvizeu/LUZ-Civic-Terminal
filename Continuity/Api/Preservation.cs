using System.Text.Json.Nodes;

namespace Luz.Continuity;

public static class Companions
{
    public static IEnumerable<(string Owner, string Relative)> ForSlot(string slot)
    {
        string key = SafeFiles.Key(SafeFiles.Slot(slot));
        yield return ("unofficialpatch", "NivalisUnofficialPatch/Hauling/" + key + ".json");
        yield return ("unofficialpatch", "NivalisUnofficialPatch/ManagerPolicies/" + key + ".json");
        yield return ("citylife", "NivalisCityLife/Saves/" + SafeFiles.Key(SafeFiles.Slot(slot).ToUpperInvariant()) + ".json");
    }
    public static string? Rebase(string relative, string source, string target)
    {
        foreach (var pair in ForSlot(source).Zip(ForSlot(target)))
            if (relative == pair.First.Relative) return pair.Second.Relative;
        return null;
    }
    public static void Validate(string path, string owner, string nativeHash, bool verifyMarker = true)
    {
        var document = SafeFiles.Read<JsonObject>(path, 16 * 1024 * 1024);
        int schema = (int?)(document["Schema"] ?? document["Version"]) ?? -1;
        bool supported = owner == "citylife" ? schema is >= 1 and <= 3 : path.Contains("Hauling") ? schema is >= 1 and <= 4 : schema == 2;
        if (!supported || (string?)document["Revision"] != nativeHash) throw new InvalidDataException("Companion schema or native revision is unsupported: " + Path.GetFileName(path));
        if (verifyMarker && File.Exists(path + ".required") && File.ReadAllText(path + ".required").Trim() != nativeHash) throw new InvalidDataException("Companion required marker differs from its native generation.");
    }
    public static string[] WriteSet(ContinuitySession session, string slot)
    {
        return new[] { ".sav", ".png", ".sav.bak", ".png.bak", ".sav.temp", ".png.temp" }.Select(suffix => "saves/" + SafeFiles.Slot(slot) + suffix).Concat(ForSlot(slot).SelectMany(c => new[] { "", ".required", ".bak", ".tmp", ".required.bak", ".required.tmp" }.Select(suffix => "config/" + c.Relative + suffix))).ToArray();
    }
    public static void Clone(ContinuitySession session)
    {
        var checkpoint = Checkpoints.Verify(session.Checkpoint);
        string source = "saves/" + session.SourceSlot + ".sav";
        if (!checkpoint.Files.TryGetValue(source, out var hash)) throw new InvalidDataException("Selected source save is absent from the checkpoint.");
        string destination = "saves/" + session.BranchSlot + ".sav";
        if (File.Exists(session.File(destination))) throw new IOException("Continuation destination already exists.");
        var paths = WriteSet(session, session.BranchSlot);
        if (paths.Any(p => File.Exists(session.File(p)))) throw new IOException("Continuation companions already exist; they were not overwritten.");
        foreach (var companion in ForSlot(session.SourceSlot))
        {
            string captured = SafeFiles.Under(session.Checkpoint, "config/" + companion.Relative);
            if (File.Exists(captured)) Validate(captured, companion.Owner, hash);
            else if (File.Exists(captured + ".required")) throw new InvalidDataException("Required source companion is missing.");
        }
        // Every required companion for this slot must have a registered owner.
        foreach (string key in checkpoint.Files.Keys.Where(p => p.StartsWith("config/") && p.EndsWith(".required")))
            if ((key.Contains(SafeFiles.Key(session.SourceSlot)) || key.Contains(SafeFiles.Key(session.SourceSlot.ToUpperInvariant()))) && !ForSlot(session.SourceSlot).Any(c => key == "config/" + c.Relative + ".required"))
                throw new InvalidDataException("Unknown required companion; protected continuation refused.");
        Publications.Begin(session, paths);
        try
        {
            File.Copy(SafeFiles.Under(session.Checkpoint, source), session.File(destination));
            foreach (var pair in ForSlot(session.SourceSlot).Zip(ForSlot(session.BranchSlot)))
            foreach (string suffix in new[] { "", ".required" })
            {
                string from = SafeFiles.Under(session.Checkpoint, "config/" + pair.First.Relative + suffix);
                if (!File.Exists(from)) continue;
                string to = session.File("config/" + pair.Second.Relative + suffix); Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(from, to);
            }
            Publications.Commit(session);
        }
        catch (Exception error) { var retained = Publications.RetainFailure(session, error); if (ReferenceEquals(retained, error)) throw; throw retained; }
    }
    public static void VerifyOriginal(ContinuitySession session)
    {
        if (session.OriginalWitness.Count != 0)
        {
            foreach (var expected in session.OriginalWitness)
            {
                string file = session.File(expected.Key); string? observed = File.Exists(file) ? SafeFiles.Hash(file) : null;
                if (observed != expected.Value) throw new IOException("External original file changed after checkpoint restoration: " + expected.Key);
            }
            return;
        }
        var manifest = SafeFiles.Read<CheckpointManifest>(Path.Combine(session.Checkpoint, "checkpoint.json"));
        foreach (string path in new[] { "saves/" + session.SourceSlot + ".sav" }.Concat(ForSlot(session.SourceSlot).SelectMany(c => new[] { "config/" + c.Relative, "config/" + c.Relative + ".required" })))
        {
            bool captured = manifest.Files.TryGetValue(path, out var hash), present = File.Exists(session.File(path));
            if (captured != present || present && SafeFiles.Hash(session.File(path)) != hash) throw new IOException("Original timeline changed externally; protected session paused: " + path);
        }
    }
    public static void VerifyOutputs(ContinuitySession session)
    {
        session.Validate();
        if (Publications.Pending(session)) throw new IOException("Continuity publication is unfinished. Recover it in LUZ.");
        VerifyOriginal(session);
        foreach (var output in session.LastOutputs) if (!File.Exists(session.File(output.Key)) || SafeFiles.Hash(session.File(output.Key)) != output.Value) throw new IOException("Continuation changed externally or through cloud synchronization: " + output.Key);
    }
}

public sealed class DormantRecord
{
    public int Schema { get; set; } = 1;
    public string Owner { get; set; } = "";
    public string Generation { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceSlot { get; set; } = "";
    public string SourceNativeHash { get; set; } = "";
    public string State { get; set; } = "Dormant";
    public Dictionary<string, string> Captured { get; set; } = new();
    public List<string> Conflicts { get; set; } = new();
}

/// <summary>State adapters depend only on native serialization and plain companion documents.</summary>
public static class Preservation
{
    public static string StateFile(ContinuitySession session, DormantRecord record, string relative) => SafeFiles.Under(Folder(session, record.Owner), "generations/" + record.Generation + "/state/" + relative);
    public static string Folder(ContinuitySession session, string owner)
    {
        if (owner is not ("citylife" or "bettertime" or "unofficialpatch" or "framework")) throw new InvalidDataException("No preservation adapter for this owner.");
        return SafeFiles.Under(session.LibraryRoot, "continuity/dormant/" + session.Id + "/" + owner);
    }
    public static DormantRecord Capture(ContinuitySession session, string owner)
    {
        if (!session.Dormant.Any(d => d.StartsWith(owner + "/", StringComparison.Ordinal))) throw new InvalidOperationException("State caching is reserved for unavailable capabilities.");
        Companions.VerifyOutputs(session);
        string folder = Folder(session, owner), receipt = Path.Combine(folder, "record.json");
        if (File.Exists(receipt)) { var previous = Read(session, owner); if (previous.State != "Restored") return previous; }
        string sourceSlot = session.ActiveSlot.Length != 0 ? session.ActiveSlot : session.BranchSlot;
        var record = new DormantRecord { Owner = owner, SourceSlot = sourceSlot, SourceNativeHash = SafeFiles.Hash(session.File("saves/" + sourceSlot + ".sav")) };
        foreach (var companion in Companions.ForSlot(sourceSlot).Where(c => c.Owner == owner))
        {
            string file = session.File("config/" + companion.Relative);
            if (!File.Exists(file)) continue;
            Companions.Validate(file, owner, record.SourceNativeHash);
            // Retired greenhouse identities need the dedicated native relationship recovery path.
            if (file.Contains("Hauling") && File.ReadAllText(file).Contains("a11e2026", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Retired worker records require saved-data recovery before continuity.");
            string output = StateFile(session, record, companion.Relative); Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(file, output, false);
            string hash = SafeFiles.Hash(file); if (SafeFiles.Hash(output) != hash) throw new IOException("Dormant capture changed while copying."); record.Captured.Add(companion.Relative, hash);
        }
        SafeFiles.Write(SafeFiles.Under(folder, "generations/" + record.Generation + "/captured.json"), record); SafeFiles.Write(receipt, record); return record;
    }
    public static DormantRecord Read(ContinuitySession session, string owner)
    {
        string folder = Folder(session, owner); var record = SafeFiles.Read<DormantRecord>(Path.Combine(folder, "record.json"));
        if (record.Schema != 1 || record.Owner != owner || record.Generation.Length != 32 || !record.Generation.All(Uri.IsHexDigit) || record.Captured.Count > 16 || record.State is not ("Dormant" or "Restored" or "Conflict")) throw new InvalidDataException("Invalid dormant state.");
        SafeFiles.Slot(record.SourceSlot);
        foreach (var pair in record.Captured) if (SafeFiles.Hash(StateFile(session, record, pair.Key)) != pair.Value) throw new IOException("Dormant state integrity failure.");
        return record;
    }
    public static void CarryForward(ContinuitySession session, string target)
    {
        string native = session.File("saves/" + target + ".sav"), hash = SafeFiles.Hash(native);
        foreach (string owner in session.Dormant.Select(d => d.Split('/')[0]).Distinct())
        {
            var record = Read(session, owner); string folder = Folder(session, owner);
            foreach (var captured in record.Captured)
            {
                string relative = Companions.Rebase(captured.Key, record.SourceSlot, target) ?? throw new InvalidDataException("Unknown dormant companion owner.");
                var data = SafeFiles.Read<JsonObject>(StateFile(session, record, captured.Key), 16 * 1024 * 1024);
                data["Revision"] = hash; SafeFiles.Write(session.File("config/" + relative), data);
                string marker = session.File("config/" + relative + ".required");
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!); File.WriteAllText(marker, hash);
            }
        }
    }
    public static void Restore(ContinuitySession session, string owner, Func<string, IReadOnlyList<string>> validateNativeReferences)
    {
        Companions.VerifyOutputs(session); var record = Read(session, owner); if (record.State == "Restored") { session.Dormant.RemoveAll(d => d.StartsWith(owner + "/", StringComparison.Ordinal)); session.Save(); return; }
        record.Conflicts = validateNativeReferences(owner).ToList();
        if (record.Conflicts.Count != 0) { record.State = "Conflict"; SafeFiles.Write(Path.Combine(Folder(session, owner), "record.json"), record); throw new InvalidDataException("Dormant records retained; native ownership conflicts require resolution."); }
        // Carry-forward already preserves opaque records against each successful native revision.
        // Publication makes reinstatement and receipt recovery one idempotent operation.
        Publications.Begin(session, session.Slots.Values.SelectMany(s => Companions.WriteSet(session, s)));
        try
        {
            foreach (string slot in session.Slots.Values)
            {
                string native = session.File("saves/" + slot + ".sav"); if (!File.Exists(native)) continue;
                foreach (var companion in Companions.ForSlot(slot).Where(c => c.Owner == owner))
                    if (File.Exists(session.File("config/" + companion.Relative))) Companions.Validate(session.File("config/" + companion.Relative), owner, SafeFiles.Hash(native));
            }
            Publications.Commit(session); record.State = "Restored"; SafeFiles.Write(Path.Combine(Folder(session, owner), "record.json"), record);
            session.Dormant.RemoveAll(d => d.StartsWith(owner + "/", StringComparison.Ordinal)); session.Save();
        }
        catch (Exception error) { var retained = Publications.RetainFailure(session, error); if (ReferenceEquals(retained, error)) throw; throw retained; }
    }
}

public static class TimelineRollback
{
    public static ContinuitySession Prepare(ContinuitySession original)
    {
        original.Validate(); Checkpoints.Verify(original.Checkpoint);
        var session = new ContinuitySession { LibraryRoot = original.LibraryRoot, Saves = original.Saves, Config = original.Config, SourceSlot = original.SourceSlot, BranchSlot = "Protected - Backup " + Guid.NewGuid().ToString("N")[..8], Checkpoint = original.Checkpoint, Identity = original.Identity, Inputs = new(original.Inputs, StringComparer.OrdinalIgnoreCase), Fingerprint = original.Fingerprint, CatalogHash = original.CatalogHash, Dormant = original.Dormant.ToList(), SuspendedPackages = original.SuspendedPackages.ToDictionary(p => p.Key, p => p.Value.ToList()), SuspendedPlugins = original.SuspendedPlugins.ToDictionary(p => p.Key, p => p.Value.ToList()) };
        session.Slots.Add(session.SourceSlot, session.BranchSlot);
        foreach (string path in new[] { "saves/" + session.SourceSlot + ".sav" }.Concat(Companions.ForSlot(session.SourceSlot).SelectMany(c => new[] { "config/" + c.Relative, "config/" + c.Relative + ".required" })))
            session.OriginalWitness[path] = File.Exists(session.File(path)) ? SafeFiles.Hash(session.File(path)) : null;
        session.Save(); return session;
    }
    public static void Publish(ContinuitySession session)
    {
        Companions.Clone(session);
        foreach (string owner in session.Dormant.Select(d => d.Split('/')[0]).Distinct()) Preservation.Capture(session, owner);
        Companions.VerifyOutputs(session);
    }
}
