using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Luz;

public sealed class Library : IDisposable
{
    public string Root { get; }
    public TerminalState State { get; private set; }
    private readonly FileStream sessionLock;
    public Profile Active => State.Profiles.First(p => p.Id == State.ActiveProfileId);
    public Library(string root)
    {
        Root = FileSafety.ResolveFolder(root); FileSafety.NoLinks(Root); Directory.CreateDirectory(Root);
        sessionLock = new FileStream(Path.Combine(Root, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            State = RegistryRecovery.Read(Root, allowFirstRun: true);
            State.PreviousGameFolders ??= [];
            State.CompatibilityDecisions ??= [];
            foreach (var profile in State.Profiles)
            {
                profile.Vortex ??= new();
                profile.Vortex.Bindings ??= [];
                profile.Vortex.Exclusions ??= [];
            }
            bool migrate = State.Schema == 1;
            if (migrate) State.Schema = 2;
            if (State.Schema != 2 || State.Profiles.Count == 0) throw new InvalidDataException("Unsupported or empty registry. Restore registry.json from a backup.");
            foreach (var p in State.Profiles) ValidateId(p.Id);
            foreach (var p in State.Packages) ValidateId(p.Id);
            if (!State.Profiles.Any(p => p.Id == State.ActiveProfileId)) State.ActiveProfileId = State.Profiles[0].Id;
            if (migrate) Save();
        }
        catch { sessionLock.Dispose(); throw; }
    }
    public void Dispose() => sessionLock.Dispose();
    public static void ValidateId(string id) { if (id.Length is < 8 or > 64 || id.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid internal record ID."); }
    public string PackageRoot(string id) { ValidateId(id); return FileSafety.Under(Root, "library/" + id); }
    public string ConfigRoot(string id) { ValidateId(id); return FileSafety.Under(Root, "profiles/" + id + "/config"); }
    public void Save()
    {
        string path = Path.Combine(Root, "registry.json");
        if (File.Exists(path)) File.Copy(path, path + ".previous", true);
        JsonFiles.Write(path, State);
    }
    internal void Commit(TerminalState next)
    {
        TerminalState previous = State; State = next;
        try { Save(); }
        catch { State = previous; throw; }
    }
    public Profile CreateProfile(string name, bool duplicate)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("Use a profile name between 1 and 80 characters.");
        var p = duplicate ? JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(Active))! : new Profile();
        var old = p.Id; p.Id = Guid.NewGuid().ToString("N"); p.Name = name.Trim();
        if (duplicate)
        {
            string source = ConfigRoot(old);
            if (old == State.AppliedProfileId) { GameFiles.Validate(State.GameFolder); GameFiles.RequireClosed(); source = FileSafety.PortableDestination(State.GameFolder, "BepInEx/config"); }
            FileSafety.CopyTree(source, ConfigRoot(p.Id));
        }
        State.Profiles.Add(p); State.ActiveProfileId = p.Id; Save(); return p;
    }
    public void DeleteProfile()
    {
        if (State.Profiles.Count == 1) throw new InvalidOperationException("Keep at least one profile.");
        if (Active.Id == State.AppliedProfileId) throw new InvalidOperationException("Apply another profile before deleting the currently deployed profile.");
        State.Profiles.Remove(Active); State.ActiveProfileId = State.Profiles[0].Id; Save();
    }
    public ModPackage Import(string file, CatalogMod? source = null)
    {
        if (!File.Exists(file) || new FileInfo(file).Length > 536_870_912) throw new InvalidDataException("Choose a ZIP or DLL smaller than 512 MB.");
        var hash = FileSafety.Hash(file);
        var existing = State.Packages.FirstOrDefault(p => p.Sha256 == hash && p.PreparationVersion == PackagePreparation.Version);
        if (existing != null) { AddToProfile(existing); return existing; }
        string stage = FileSafety.Under(Root, "staging/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            string raw = Path.Combine(stage, "raw"); Directory.CreateDirectory(raw);
            if (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)) FileSafety.Extract(file, raw);
            else if (Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase)) File.Copy(file, Path.Combine(raw, Path.GetFileName(file)));
            else throw new InvalidDataException("Import a ZIP or a BepInEx DLL.");
            return ImportTree(raw, source, hash, Path.GetFileNameWithoutExtension(file), true);
        }
        finally { FileSafety.DeleteOwned(Root, stage); }
    }
    internal ModPackage ImportTree(string raw, CatalogMod? source, string hash, string fallback, bool attach)
    {
        var all = Directory.GetFiles(raw, "*", SearchOption.AllDirectories);
        var package = new ModPackage { Id = PackagePreparation.Identity(hash), Sha256 = hash, PreparationVersion = PackagePreparation.Version, Name = source?.Name ?? fallback, Version = source?.Version ?? "0.0.0",
            Source = source?.Source ?? "Local", SourceId = source?.Id ?? "", PageUrl = source?.PageUrl ?? "", Description = source?.Description ?? "", Dependencies = source?.Dependencies.ToList() ?? [] };
        var manifestFile = all.Where(f => Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f.Length).FirstOrDefault();
        if (manifestFile != null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile)); var m = doc.RootElement;
            if (source == null)
            {
                package.Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? fallback : fallback;
                package.Version = m.TryGetProperty("version_number", out var v) ? v.GetString() ?? "0.0.0" : "0.0.0";
                package.Description = m.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                package.PageUrl = m.TryGetProperty("website_url", out var w) ? w.GetString() ?? "" : "";
                if (m.TryGetProperty("dependencies", out var deps)) package.Dependencies = deps.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
                if (m.TryGetProperty("luz_project_id", out var projectId))
                {
                    string id = projectId.GetString() ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9._-]{2,95}$")) throw new InvalidDataException("Invalid object project identity.");
                    package.Source = "Object Studio"; package.SourceId = id;
                }
                if (m.TryGetProperty("required_plugins", out var required))
                    foreach (var dep in required.EnumerateArray())
                        package.RequiredPlugins.Add(new(dep.GetProperty("guid").GetString() ?? throw new InvalidDataException("Missing plugin ID."), dep.GetProperty("minimum_version").GetString() ?? "", false));
            }
        }
        if (State.Packages.Any(p => p.Id == package.Id))
            throw new InvalidDataException("A different registered package uses this import identity: " + package.Id);
        string staged = FileSafety.Under(Root, "staging/import-" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(staged, "payload"); Directory.CreateDirectory(payload);
        try
        {
            PreparedContent prepared = PackagePreparation.Prepare(raw, package.Id);
            if (prepared.Kind == "Loader") throw new InvalidDataException("This is a BepInEx loader bundle. Use Maintenance > Loader setup to install the latest official Windows x64 IL2CPP build. Bundled plugins and splash tools require separate review.");
            if (prepared.Kind == "Tool") throw new InvalidDataException("This is a standalone tool or application. It cannot be deployed as a gameplay mod; review or exclude it in LUZ.");
            if (prepared.Kind == "IncompatibleRuntime") throw new InvalidDataException("This package targets MelonLoader. Select the BepInEx 6 IL2CPP version of this mod.");
            foreach (var file in prepared.Files)
            {
                string output = FileSafety.Under(payload, file.Destination); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                string input = FileSafety.Under(raw, file.Source); File.Copy(input, output); package.Files.Add(file.Destination);
                if (file.Destination.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) && input.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    try { package.Plugins.AddRange(PluginMetadata.Read(input)); }
                    catch (BadImageFormatException) { /* Native helpers do not declare managed plugins. */ }
                }
            }
            foreach (string file in prepared.References)
            {
                string output = FileSafety.Under(staged, "reference/" + file); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(FileSafety.Under(raw, file), output); package.ReferenceFiles.Add(file);
            }
            if (source == null && manifestFile == null && package.Plugins.Count == 1) { package.Name = package.Plugins[0].Name; package.Version = package.Plugins[0].Version; }
            var icon = all.FirstOrDefault(f => Path.GetFileName(f).Equals("icon.png", StringComparison.OrdinalIgnoreCase));
            if (icon != null && new FileInfo(icon).Length <= 5_242_880) { File.Copy(icon, Path.Combine(staged, "icon.png")); package.Icon = "icon.png"; }
            JsonFiles.Write(Path.Combine(staged, "package.json"), package);
            PublishImport(package, staged, attach);
            return package;
        }
        finally { FileSafety.DeleteOwned(Root, staged); }
    }
    private void PublishImport(ModPackage package, string staged, bool attach)
    {
        string target = PackageRoot(package.Id);
        FileSafety.NoLinks(target);
        string? preserved = null;
        bool published = false;
        try
        {
            if (FileSafety.DirectoryExistsReadable(target))
            {
                // A crash or registry restore can leave a complete or partial
                // package behind. Rebuild only from this validated import, never
                // trust orphan metadata, and keep its original bytes recoverable.
                preserved = FileSafety.Under(Root, "import-recovery/" + package.Id + "-" + Guid.NewGuid().ToString("N"));
                FileSafety.NoLinks(Path.GetDirectoryName(preserved)!);
                Directory.CreateDirectory(Path.GetDirectoryName(preserved)!);
                Directory.Move(target, preserved);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(staged, target);
            published = true;
            if (attach)
            {
                State.Packages.Add(package);
                AddToProfile(package);
            }
        }
        catch (Exception operation)
        {
            if (attach) State.Packages.Remove(package);
            try
            {
                if (published) FileSafety.DeleteOwned(Root, target);
                if (preserved != null && Directory.Exists(preserved)) Directory.Move(preserved, target);
            }
            catch (Exception rollback)
            {
                throw new IOException("Import failed and its original package could not be restored. Preserved location: " + preserved
                    + ". Original error: " + operation.Message, new AggregateException(operation, rollback));
            }
            throw;
        }
    }
    public void AddToProfile(ModPackage package)
    {
        if (Active.Mods.Any(m => m.PackageId == package.Id)) return;
        var previous = Active.Mods.Select(m => new ModEntry { PackageId = m.PackageId, Enabled = m.Enabled, Pinned = m.Pinned, Note = m.Note }).ToList();
        var replace = Active.Mods.FirstOrDefault(m => State.Packages.Any(p => p.Id == m.PackageId &&
            ((package.SourceId.Length > 0 && p.Source == package.Source && p.SourceId == package.SourceId) || p.Plugins.Any(a => package.Plugins.Any(b => b.Guid == a.Guid)))));
        if (replace != null)
        {
            if (replace.Pinned) throw new InvalidOperationException("This mod is pinned. Unpin it before replacing its version.");
            replace.PackageId = package.Id;
        }
        else Active.Mods.Add(new() { PackageId = package.Id });
        try { Save(); } catch { Active.Mods = previous; throw; }
    }
    public void ImportInstalled(string game)
    {
        GameFiles.Validate(game); GameFiles.RequireClosed();
        string bep = FileSafety.PortableDestination(game, "BepInEx"); FileSafety.NoLinks(bep);
        foreach (var area in new[] { "plugins", "patchers" })
        {
            string folder = FileSafety.PortableDestination(bep, area); if (!Directory.Exists(folder)) continue;
            var groups = Directory.GetDirectories(folder).Select(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories)).ToList();
            groups.Add(Directory.GetFiles(folder));
            foreach (var files in groups.Where(g => g.Length > 0))
            {
                string zip = FileSafety.Under(Root, "staging/" + Guid.NewGuid().ToString("N") + ".zip"); Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
                try
                {
                    using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                        foreach (var file in files) archive.CreateEntryFromFile(file, "BepInEx/" + area + "/" + Path.GetRelativePath(folder, file).Replace('\\', '/'));
                    var imported = Import(zip);
                    if (imported.Plugins.Count != 1 && !files.Any(f=>Path.GetFileName(f).Equals("manifest.json",StringComparison.OrdinalIgnoreCase))) imported.Name = Path.GetDirectoryName(files[0]) == folder ? "Installed " + area + " (loose files)" : new DirectoryInfo(Path.GetDirectoryName(files[0])!).Name;
                }
                finally { File.Delete(zip); }
            }
        }
        FileSafety.CopyTree(FileSafety.PortableDestination(game, "BepInEx/config"), ConfigRoot(Active.Id), true);
        Save();
    }
    public void ExportProfile(string path) => JsonFiles.Write(path, new SharedProfile(1, Active.Name, Active.Mods.Select(e => new SharedMod(State.Packages.Single(p => p.Id == e.PackageId), e.Enabled, e.Pinned, e.Note)).ToList()));
    public void ImportProfile(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Profile file is too large.");
        var share = JsonFiles.Read<SharedProfile>(path); if (share.Schema != 1 || share.Mods.Count > 1000) throw new InvalidDataException("Unsupported profile.");
        var matches = share.Mods.Select(m => (Item: m, Package: State.Packages.FirstOrDefault(p => p.Sha256 == m.Package.Sha256))).ToArray();
        if (matches.Any(m => m.Package == null)) throw new InvalidDataException("Import these exact mod versions first: " + string.Join(", ", matches.Where(m => m.Package == null).Select(m => m.Item.Package.Name + " " + m.Item.Package.Version)));
        var p = CreateProfile(share.Name, false);
        p.Mods = matches.Select(m => new ModEntry { PackageId = m.Package!.Id, Enabled = m.Item.Enabled, Pinned = m.Item.Pinned, Note = m.Item.Note }).ToList(); Save();
    }
    public string Fingerprint(Profile p) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', p.Mods.Where(e => e.Enabled).Select(e => e.PackageId)))));
}
public sealed record SharedMod(ModPackage Package, bool Enabled, bool Pinned, string Note);
public sealed record SharedProfile(int Schema, string Name, List<SharedMod> Mods);
