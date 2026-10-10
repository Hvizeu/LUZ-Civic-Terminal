using System.Text.Json;
namespace Luz;

public enum RegistryFailure { Missing, Malformed, UnsupportedSchema, EmptyProfiles, InvalidRecords }
public sealed class RegistryRecoveryException(RegistryFailure reason, string path, string message, Exception? inner = null)
    : Exception(message + "\nRegistry: " + path, inner)
{
    public RegistryFailure Reason { get; } = reason;
    public string RegistryPath { get; } = path;
}
public static class RegistryRecovery
{
    public static TerminalState Read(string root, bool allowFirstRun = false)
    {
        root = FileSafety.ResolveFolder(root); FileSafety.NoLinks(root);
        string path = Path.Combine(root, "registry.json");
        if (!File.Exists(path))
        {
            bool existing = File.Exists(path + ".previous") || new[] { "library", "profiles", "backups", "loader-backups" }
                .Any(area => Directory.Exists(Path.Combine(root, area)) && Directory.EnumerateFileSystemEntries(Path.Combine(root, area)).Any())
                || File.Exists(Path.Combine(root, "pending.json")) || File.Exists(Path.Combine(root, "loader-pending.json"));
            if (allowFirstRun && !existing) return new();
            throw new RegistryRecoveryException(RegistryFailure.Missing, path, "The registry is missing. Existing library data has been preserved.");
        }
        return ReadFile(root, path);
    }
    private static TerminalState ReadFile(string root, string path)
    {
        FileSafety.NoLinks(path);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var value = json.RootElement;
            JsonElement Property(string name) => value.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
            if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a registry object.");
            var schema = Property("Schema");
            if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version is not (1 or 2))
                throw new RegistryRecoveryException(RegistryFailure.UnsupportedSchema, path, "This registry schema is not supported by this LUZ version.");
            var profiles = Property("Profiles");
            if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() == 0)
                throw new RegistryRecoveryException(RegistryFailure.EmptyProfiles, path, "The registry has no usable profiles.");
            var state = JsonSerializer.Deserialize<TerminalState>(value.GetRawText(), JsonFiles.Options) ?? throw new JsonException("Empty registry object.");
            Validate(root, path, state);
            return state;
        }
        catch (JsonException error) { throw new RegistryRecoveryException(RegistryFailure.Malformed, path, "The registry contains invalid JSON or invalid field values.", error); }
    }
    private static void Validate(string root, string path, TerminalState state)
    {
        try
        {
            if (state.Profiles == null || state.Packages == null || state.Profiles.Any(p => p == null || p.Mods == null) || state.Packages.Any(p => p == null || p.Files == null))
                throw new InvalidDataException("Missing profile or package records.");
            foreach (var p in state.Profiles) Library.ValidateId(p.Id);
            foreach (var p in state.Packages) Library.ValidateId(p.Id);
            if (state.Profiles.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != state.Profiles.Count || state.Packages.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != state.Packages.Count)
                throw new InvalidDataException("Duplicate profile or package identities.");
            foreach (var profile in state.Profiles)
            foreach (var entry in profile.Mods)
            {
                if (entry == null) throw new InvalidDataException("Missing mod entry.");
                var package = state.Packages.FirstOrDefault(p => p.Id == entry.PackageId) ?? throw new InvalidDataException("A profile references a missing package: " + entry.PackageId);
                foreach (var rel in package.Files)
                {
                    string payload = FileSafety.Under(root, "library/" + package.Id + "/payload/" + rel);
                    FileSafety.NoLinks(payload);
                    if (!File.Exists(payload)) throw new InvalidDataException("A profile package file is missing: " + payload);
                }
            }
        }
        catch (InvalidDataException error) { throw new RegistryRecoveryException(RegistryFailure.InvalidRecords, path, error.Message, error); }
    }
    public static string? ValidBackup(string root)
    {
        string path = Path.Combine(FileSafety.ResolveFolder(root), "registry.json.previous");
        if (!File.Exists(path)) return null;
        try { _ = ReadFile(root, path); return path; }
        catch (Exception error) when (error is RegistryRecoveryException or IOException or UnauthorizedAccessException) { return null; }
    }
    public static string Restore(string root)
    {
        root = FileSafety.ResolveFolder(root); FileSafety.NoLinks(root);
        using var lease = new FileStream(Path.Combine(root, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string backup = ValidBackup(root) ?? throw new InvalidDataException("No valid registry backup is available. Existing files were preserved.");
        string path = Path.Combine(root, "registry.json"), evidence = path + ".failed-" + Guid.NewGuid().ToString("N") + ".bak";
        if (File.Exists(path)) File.Copy(path, evidence);
        var state = ReadFile(root, backup); JsonFiles.Write(path, state);
        return File.Exists(evidence) ? evidence : "Missing registry restored; library data preserved.";
    }
    public static string ExportDiagnostics(string root, Exception error)
    {
        root = FileSafety.ResolveFolder(root);
        string directory = Path.Combine(root, "startup-diagnostics-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "startup.txt"), "LUZ " + typeof(Library).Assembly.GetName().Version + "\nData folder: " + root + "\n" + error);
        foreach (var name in new[] { "registry.json", "registry.json.previous", "pending.json", "loader-pending.json" })
            if (File.Exists(Path.Combine(root, name))) File.Copy(Path.Combine(root, name), Path.Combine(directory, name));
        return directory;
    }
}
