using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Luz.Continuity;

public enum CapabilityState { Native, Fallback, Dormant, Blocked }
public sealed record Capability(string Owner, string Name, CapabilityState State, string Detail);
public sealed record BuildIdentity(string Native, string Metadata, string Loader);
public sealed record CapabilityRule(string Owner, string Name, string Implementation, string[] Dependencies);
public sealed record ConsumerGrant(string Assembly, string PayloadHash, string SourceNative, string SourceMetadata, string QualificationDigest);
public sealed record AdapterBuild(string Id, BuildIdentity Identity, CapabilityRule[] Capabilities, ConsumerGrant[]? Grants = null);
public sealed class AdapterCatalog
{
    public int Schema { get; set; } = 1;
    public string Version { get; set; } = "0.1.0";
    public AdapterBuild[] Builds { get; set; } = Array.Empty<AdapterBuild>();
    public AdapterBuild Require(BuildIdentity identity)
    {
        if (Schema != 1 || Builds.Length > 64) throw new InvalidDataException("Unsupported continuity catalog.");
        var matches = Builds.Where(b => b.Identity == identity).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("No verified continuity adapter for this game, metadata and loader identity.");
        if (matches[0].Capabilities.Select(c => c.Owner + "/" + c.Name).Distinct().Count() != matches[0].Capabilities.Length)
            throw new InvalidDataException("Ambiguous continuity capabilities.");
        return matches[0];
    }
    public Capability[] Resolve(BuildIdentity identity, IReadOnlySet<string> dormant)
    {
        var build = Require(identity);
        var result = build.Capabilities.Select(c => new Capability(c.Owner, c.Name,
            dormant.Contains(c.Owner + "/" + c.Name) ? CapabilityState.Dormant : c.Implementation == "native" ? CapabilityState.Native : CapabilityState.Fallback, c.Implementation)).ToArray();
        foreach (var entry in build.Capabilities)
        {
            var index = Array.FindIndex(result, c => c.Owner == entry.Owner && c.Name == entry.Name);
            if (entry.Dependencies.Any(d => !result.Any(c => c.Owner + "/" + c.Name == d && c.State is CapabilityState.Native or CapabilityState.Fallback)))
                result[index] = result[index] with { State = CapabilityState.Blocked, Detail = "Required capability is unavailable." };
        }
        return result;
    }
    public bool Authorizes(BuildIdentity identity, string assembly, string payloadHash, string sourceNative, string sourceMetadata)
    {
        var build = Require(identity);
        if (sourceNative == identity.Native && sourceMetadata == identity.Metadata) return true;
        return (build.Grants ?? Array.Empty<ConsumerGrant>()).Any(g => g.Assembly == assembly && g.PayloadHash == payloadHash && g.SourceNative == sourceNative && g.SourceMetadata == sourceMetadata && g.PayloadHash.Length == 64 && g.PayloadHash.All(Uri.IsHexDigit) && g.QualificationDigest.Length == 64 && g.QualificationDigest.All(Uri.IsHexDigit));
    }
}

public static class SafeFiles
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string Hash(string path) { NoLinks(path); using var input = File.OpenRead(path); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(input)); }
    public static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static void NoLinks(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Continuity does not follow file or directory links.");
    }
    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') || relative.Split('/').Any(p => p is "" or "." or ".." || p.Any(c => c < 32) || p.IndexOfAny(new[] { '<', '>', '|', '?', '*', '"' }) >= 0 || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException("Unsafe continuity path.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Continuity path escapes its owner.");
        NoLinks(full); return full;
    }
    public static T Read<T>(string path, long limit = 8 * 1024 * 1024)
    {
        NoLinks(path); if (new FileInfo(path).Length > limit) throw new InvalidDataException("Continuity document exceeds its limit.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty continuity document.");
    }
    public static void Write<T>(string path, T value)
    {
        NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".pending-" + Guid.NewGuid().ToString("N");
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(output, value, Json); output.Flush(true); }
        File.Move(temp, path, true);
    }
    public static string Slot(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Select an explicit native save slot.");
        if (value.EndsWith(".sav", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        if (value.Length is < 1 or > 100 || value != Path.GetFileName(value)) throw new InvalidDataException("Invalid native save slot.");
        _ = Under(Path.GetTempPath(), value + ".sav"); return value;
    }
}

public sealed class ContinuitySession
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string LibraryRoot { get; set; } = "";
    public string Saves { get; set; } = "";
    public string Config { get; set; } = "";
    public string SourceSlot { get; set; } = "";
    public string BranchSlot { get; set; } = "";
    public string Checkpoint { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string CatalogHash { get; set; } = "";
    public BuildIdentity Identity { get; set; } = new("", "", "");
    public Dictionary<string, string> Inputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Dormant { get; set; } = new();
    public List<string> PendingRestore { get; set; } = new();
    public Dictionary<string, List<string>> SuspendedPackages { get; set; } = new();
    public Dictionary<string, List<string>> SuspendedPlugins { get; set; } = new();
    public Dictionary<string, string> LastOutputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> OriginalWitness { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string FirstFailure { get; set; } = "";
    public bool RecoveryRequired { get; set; }
    public string ActiveSlot { get; set; } = "";
    public int AutoSaveCursor { get; set; }
    public int FailureCount { get; set; }
    public List<string> CleanupFailures { get; set; } = new();
    public void Validate()
    {
        if (Schema != 1 || Id.Length != 32 || !Id.All(Uri.IsHexDigit) || Inputs.Count > 22000 || Slots.Count > 256 || Dormant.Count > 128 || LastOutputs.Count > 4096) throw new InvalidDataException("Unsupported continuity session.");
        if (!Path.IsPathFullyQualified(LibraryRoot) || !Path.IsPathFullyQualified(Saves) || !Path.IsPathFullyQualified(Config)) throw new InvalidDataException("Continuity roots must be absolute.");
        if (SourceSlot == BranchSlot || !BranchSlot.StartsWith("Protected - ", StringComparison.Ordinal)) throw new InvalidDataException("Continuation must use a separate slot.");
        SafeFiles.Slot(SourceSlot); SafeFiles.Slot(BranchSlot);
        foreach (var pair in Slots) { SafeFiles.Slot(pair.Key); SafeFiles.Slot(pair.Value); if (!pair.Value.StartsWith("Protected - ", StringComparison.Ordinal) || pair.Value == SourceSlot) throw new InvalidDataException("Invalid branch write ownership."); }
        if (Slots.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != Slots.Count) throw new InvalidDataException("Branch slot collision.");
        SafeFiles.NoLinks(LibraryRoot); SafeFiles.NoLinks(Saves); SafeFiles.NoLinks(Config);
        string checkpoint = Path.GetFullPath(Checkpoint);
        if (!checkpoint.StartsWith(Path.GetFullPath(Path.Combine(LibraryRoot, "continuity/checkpoints")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checkpoint is outside its library.");
        foreach (var path in LastOutputs.Keys) _ = File(path);
        var originalPaths = new[] { "saves/" + SourceSlot + ".sav" }.Concat(Companions.ForSlot(SourceSlot).SelectMany(c => new[] { "config/" + c.Relative, "config/" + c.Relative + ".required" })).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (OriginalWitness.Count > originalPaths.Count || OriginalWitness.Any(p => !originalPaths.Contains(p.Key) || p.Value != null && (p.Value.Length != 64 || !p.Value.All(Uri.IsHexDigit)))) throw new InvalidDataException("Invalid original-file witness for restored timeline.");
    }
    public string File(string relative) => relative.StartsWith("saves/", StringComparison.Ordinal) ? SafeFiles.Under(Saves, relative[6..]) : relative.StartsWith("config/", StringComparison.Ordinal) ? SafeFiles.Under(Config, relative[7..]) : throw new InvalidDataException("Unknown continuity file owner.");
    public string Record => SafeFiles.Under(LibraryRoot, "continuity/sessions/" + Id + ".json");
    public string Route(string requested, bool load)
    {
        string slot = SafeFiles.Slot(requested);
        if (Slots.Values.Contains(slot, StringComparer.OrdinalIgnoreCase)) return Slots.Values.Single(s => s.Equals(slot, StringComparison.OrdinalIgnoreCase));
        if (Slots.TryGetValue(slot, out string? existing)) return existing;
        if (load) throw new InvalidDataException("Choose this historical save as the source of a protected session in LUZ.");
        if (Slots.Count >= 256) throw new InvalidDataException("Continuation reached its slot limit; originals were preserved.");
        string destination = "Protected - " + slot[..Math.Min(slot.Length, 42)] + " " + Id[..8] + " " + SafeFiles.Key(slot)[..8];
        if (System.IO.File.Exists(SafeFiles.Under(Saves, destination + ".sav"))) throw new IOException("Unowned continuation slot already exists.");
        Slots.Add(slot, destination); Save(); return destination;
    }
    public void Save() { Validate(); SafeFiles.Write(Record, this); }
    public void Fail(Exception error)
    {
        FailureCount++; RecoveryRequired = true; if (FirstFailure.Length == 0) FirstFailure = error.ToString(); Save();
    }
}
