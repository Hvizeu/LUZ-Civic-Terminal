using System.Text.Json;

namespace Luz;

public sealed class ModPackage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Imported mod";
    public string Version { get; set; } = "0.0.0";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "Local";
    public string SourceId { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<string> Dependencies { get; set; } = [];
    public List<PluginInfo> Plugins { get; set; } = [];
    public List<PluginDependency> RequiredPlugins { get; set; } = [];
    public List<string> Files { get; set; } = [];
    public string Icon { get; set; } = "";
}
public sealed record PluginDependency(string Guid, string MinimumVersion, bool Optional);
public sealed record PluginInfo(string Guid, string Name, string Version, List<PluginDependency> Dependencies, List<string> Incompatibilities);
public sealed class ModEntry
{
    public string PackageId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Pinned { get; set; }
    public string Note { get; set; } = "";
}
public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Everyday";
    public List<ModEntry> Mods { get; set; } = [];
}
public sealed class TerminalState
{
    public int Schema { get; set; } = 2;
    public string GameFolder { get; set; } = "";
    public List<string> PreviousGameFolders { get; set; } = [];
    public string ActiveProfileId { get; set; } = "";
    public string AppliedProfileId { get; set; } = "";
    public string AppliedFingerprint { get; set; } = "";
    public string LaunchProgram { get; set; } = "";
    public List<string> LaunchArguments { get; set; } = [];
    public string LoaderVersion { get; set; } = "";
    public List<Profile> Profiles { get; set; } = [new()];
    public List<ModPackage> Packages { get; set; } = [];
}
public sealed record Issue(string Severity, string Message);
public sealed record CatalogMod(string Id, string Name, string Version, string Description, string PageUrl, string IconUrl, string DownloadUrl, string[] Dependencies, string Source, long FileId = 0);
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty document: " + Path.GetFileName(path));
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, true);
    }
}
