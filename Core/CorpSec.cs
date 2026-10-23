using System.IO.Compression;
using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Luz.Continuity;
using Mono.Cecil;

namespace Luz;

public sealed class CorpSecProject
{
    public int Schema { get; set; } = 2;
    public string Id { get; set; } = "";
    public string Input { get; set; } = "";
    public string ProjectRoot { get; set; } = "";
    public string SourceProject { get; set; } = "";
    public string PackageOutput { get; set; } = "";
    public string Workspace { get; set; } = "";
    public string Owner { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Website { get; set; } = "";
    public string SupportContact { get; set; } = "";
    public string PluginGuid { get; set; } = "";
    public string PluginAssembly { get; set; } = "";
    public string PluginFile { get; set; } = "";
    public string PackageFolder { get; set; } = "";
    public string ThunderstoreName { get; set; } = "";
    public string BuildEdition { get; set; } = "";
    public string PackageMode { get; set; } = "normal";
    public string RecoveryIntent { get; set; } = "unknown";
    public bool RecoveryReviewed { get; set; }
    public string SupportSource { get; set; } = "";
    public List<CorpSecBuildChoice> BuildChoices { get; set; } = new();
    public List<CorpSecFinding> Findings { get; set; } = new();
    public List<CorpSecCompanion> Companions { get; set; } = new();
    public List<CorpSecCapability> Capabilities { get; set; } = new();
    public List<CorpSecAdapter> Adapters { get; set; } = new();
    public List<string> ThunderstoreDependencies { get; set; } = new();
    public string ProjectFile { get; set; } = "";
}

public sealed class CorpSecWorkspaceState
{
    public int Schema { get; set; } = 1;
    public List<string> Roots { get; set; } = new();
    public List<string> IgnoredProjectIds { get; set; } = new();
    public string SelectedProjectId { get; set; } = "";
}

public sealed record CorpSecCompanion(string Root, string Pattern, string Slot, bool Required, string Validation, int SchemaMinimum = 0, int SchemaMaximum = 0, string RevisionProperty = "Revision", string Provenance = "author", bool Confirmed = true);
public sealed record CorpSecCapability(string Name, string Preferred, string[] Fallbacks, string[] Dependencies, string Provenance = "author");
public sealed record CorpSecAdapter(string Id, string Source, string Type, string[] Capabilities, string Provenance = "author");
public sealed record CorpSecBuildChoice(string Edition, string Label, string Version, string Input, string PluginFile, string Evidence);
public sealed record CorpSecFinding(string Id, string Severity, string Title, string Detail, string Source, string Status = "open", string CandidateRoot = "", string CandidatePattern = "");
public sealed record CorpSecAnalysisReport(bool RecoveryReady, string Edition, string SupportSource, IReadOnlyList<CorpSecFinding> Findings);
public sealed record CorpSecMessage(string Severity, string Message, string? Detail = null);
public sealed record CorpSecReport(bool PackagePassed, bool RecoveryPassed, IReadOnlyList<CorpSecMessage> Messages, IReadOnlyList<string> Files, string RecoveryStatus = "incomplete", bool GameplayTested = false);
public sealed record CorpSecPackage(string Archive, string Sha256, string Preset, CorpSecReport Report);
public sealed record CorpSecFolderScan(string Root, IReadOnlyList<CorpSecProject> Projects);
public sealed record CorpSecRelease(CorpSecProject Project, CorpSecReport Report, IReadOnlyList<CorpSecPackage> Packages);

/// <summary>Cold-path authoring service. It never changes profiles, game files or mod DLLs.</summary>
public static partial class CorpSec
{
    private static readonly string[] AdapterMethods = { "Preflight", "Activate", "PrepareSave", "CompleteSave", "Suspend", "Restore", "CleanupFailure" };
    private static readonly HashSet<string> DevelopmentExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdb", ".cs", ".csproj", ".sln", ".py", ".ps1", ".jsonl", ".user", ".cache" };
    private static readonly HashSet<string> DevelopmentFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".github", ".vs", "bin", "obj", "qa", "evidence", "validation" };
    private static readonly HashSet<string> ScanIgnoredFolders = new(StringComparer.OrdinalIgnoreCase) { ".git", ".github", ".vs", "obj", "node_modules", "analysis", "evidence", "backups" };
    private static readonly HashSet<string> RuntimeBinaries = new(StringComparer.OrdinalIgnoreCase) { "GameAssembly.dll", "UnityPlayer.dll", "winhttp.dll", "BepInEx.Core.dll", "BepInEx.Unity.IL2CPP.dll", "0Harmony.dll" };

    public static CorpSecProject Inspect(string dataRoot, string input, string sourceProject = "")
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        dataRoot = FileSafety.ResolveFolder(dataRoot); Directory.CreateDirectory(dataRoot);
        input = Path.GetFullPath(input); if (!File.Exists(input) && !Directory.Exists(input)) throw new FileNotFoundException("Choose a mod folder, ZIP or built DLL.", input);
        using var material = Materialized.Open(dataRoot, input);
        var candidates = CandidateFiles(material.Root, input, CancellationToken.None);
        if (candidates.Count != 1) throw new InvalidDataException(candidates.Count == 0 ? "No BepInEx plugin was found." : "This location contains several mods. Add their parent folder to My mods so LUZ can list them separately.");
        var selected = candidates[0]; var choice = Choice(selected); var project = SaveCandidate(dataRoot, selected, "", sourceProject, [choice]); RestoreProject(dataRoot, project.Id); Analyze(dataRoot, project); return project;
    }

    public static CorpSecFolderScan ScanFolder(string dataRoot, string root, CancellationToken cancellation = default, bool restoreForgotten = true)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        dataRoot = FileSafety.ResolveFolder(dataRoot); Directory.CreateDirectory(dataRoot);
        root = Path.TrimEndingDirectorySeparator(FileSafety.ResolveFolder(root));
        if (!FileSafety.DirectoryExistsReadable(root)) throw new DirectoryNotFoundException("This mods folder is unavailable. Choose an existing build or release folder.");
        var raw = CandidateFiles(root, root, cancellation);
        foreach (string zip in ScanFiles(root, ".zip", cancellation).Take(250))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var material = Materialized.Open(dataRoot, zip);
                var archived = CandidateFiles(material.Root, zip, cancellation);
                if (archived.Select(c => c.Plugin.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1) raw.AddRange(archived);
            }
            catch (Exception error) when (error is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or BadImageFormatException) { }
        }
        if (raw.Count == 0) throw new InvalidDataException("No BepInEx mods were found. Choose the folder that receives your built DLLs or release ZIPs.");
        var state = Workspace(dataRoot);
        var projects = new List<CorpSecProject>();
        foreach (var group in raw.GroupBy(c => c.Plugin.Guid, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.First().Plugin.Name, StringComparer.OrdinalIgnoreCase))
        {
            string id = SafeKey(group.Key);
            if (!restoreForgotten && state.IgnoredProjectIds.Contains(id, StringComparer.OrdinalIgnoreCase)) continue;
            if (restoreForgotten) state.IgnoredProjectIds.RemoveAll(value => value.Equals(id, StringComparison.OrdinalIgnoreCase));
            var prepared = group.Select(c => WithCandidateInput(root, c, raw)).ToArray();
            var choices = prepared.GroupBy(c => c.Edition, StringComparer.Ordinal).Select(g => Choice(Preferred(g))).OrderBy(c => c.Edition, StringComparer.Ordinal).ToArray();
            string projectFile = FileSafety.Under(dataRoot, "authoring/projects/" + id + ".json"); CorpSecProject? existing = null;
            if (File.Exists(projectFile)) { existing = JsonFiles.Read<CorpSecProject>(projectFile); UpgradeProject(existing); }
            string edition = existing?.BuildEdition ?? "";
            if (edition.Length == 0 && choices.Length == 1) edition = choices[0].Edition;
            if (edition.Length == 0 && existing != null) edition = choices.FirstOrDefault(c => c.Input.Equals(existing.Input, HostPlatform.PathComparison))?.Edition ?? "";
            var editionCandidates = prepared.Where(c => edition.Length == 0 || c.Edition == edition).ToArray();
            var selected = Preferred(editionCandidates.Length == 0 ? prepared : editionCandidates);
            var project = SaveCandidate(dataRoot, selected, root, "", choices); project.BuildEdition = edition; Save(project); Analyze(dataRoot, project, cancellation); projects.Add(project);
        }
        if (!state.Roots.Contains(root, HostPlatform.PathComparer)) state.Roots.Add(root); SaveWorkspace(dataRoot, state);
        return new(root, projects);
    }

    public static IReadOnlyList<CorpSecProject> RefreshFolders(string dataRoot, CancellationToken cancellation = default)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        var state = Workspace(dataRoot); var projects = new Dictionary<string, CorpSecProject>(StringComparer.Ordinal);
        foreach (string root in state.Roots.ToArray())
        {
            cancellation.ThrowIfCancellationRequested(); if (!Directory.Exists(root)) continue;
            foreach (var project in ScanFolder(dataRoot, root, cancellation, false).Projects) projects[project.Id] = project;
        }
        return projects.Values.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static CorpSecProject Refresh(string dataRoot, CorpSecProject project, CancellationToken cancellation = default)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        ValidateProject(project);
        if (project.ProjectRoot.Length != 0 && Directory.Exists(project.ProjectRoot))
            return ScanFolder(dataRoot, project.ProjectRoot, cancellation, false).Projects.SingleOrDefault(p => p.Id == project.Id) ?? throw new InvalidDataException("The watched folder no longer contains " + project.DisplayName + ". Keep the last project settings, then choose its current build folder.");
        return Inspect(dataRoot, project.Input, project.SourceProject);
    }

    public static CorpSecWorkspaceState Workspace(string dataRoot)
    {
        string file = WorkspaceFile(dataRoot); if (!File.Exists(file)) return new();
        var state = JsonFiles.Read<CorpSecWorkspaceState>(file); state.IgnoredProjectIds ??= new();
        if (state.Schema != 1 || state.Roots.Count > 64 || state.IgnoredProjectIds.Count > 1024 || state.IgnoredProjectIds.Any(id => id.Length != 64 || !id.All(Uri.IsHexDigit))) throw new InvalidDataException("CorpSec workspace settings are invalid.");
        state.Roots = state.Roots.Where(Path.IsPathFullyQualified).Distinct(HostPlatform.PathComparer).ToList(); state.IgnoredProjectIds = state.IgnoredProjectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); return state;
    }

    public static void RememberSelection(string dataRoot, string projectId)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        var state = Workspace(dataRoot); state.SelectedProjectId = projectId; SaveWorkspace(dataRoot, state);
    }

    private static void RestoreProject(string dataRoot, string projectId)
    {
        var state = Workspace(dataRoot);
        if (state.IgnoredProjectIds.RemoveAll(value => value.Equals(projectId, StringComparison.OrdinalIgnoreCase)) > 0) SaveWorkspace(dataRoot, state);
    }

    private static CorpSecProject SaveCandidate(string dataRoot, ScanCandidate candidate, string projectRoot, string sourceProject, IReadOnlyList<CorpSecBuildChoice> choices)
    {
        string owner = Slug(candidate.Plugin.Guid), id = SafeKey(candidate.Plugin.Guid);
        string projects = FileSafety.Under(dataRoot, "authoring/projects"); Directory.CreateDirectory(projects);
        string projectFile = FileSafety.Under(projects, id + ".json");
        CorpSecProject project = File.Exists(projectFile) ? JsonFiles.Read<CorpSecProject>(projectFile) : new(); UpgradeProject(project);
        project.Schema = 2; project.Id = id; project.Input = Path.GetFullPath(candidate.Input); project.ProjectRoot = projectRoot.Length == 0 ? project.ProjectRoot : Path.GetFullPath(projectRoot); project.SourceProject = sourceProject.Length == 0 ? project.SourceProject : Path.GetFullPath(sourceProject);
        project.Owner = project.Owner.Length == 0 ? owner : project.Owner; project.DisplayName = project.DisplayName.Length == 0 ? candidate.Plugin.Name : project.DisplayName;
        project.Version = candidate.Plugin.Version; project.PluginGuid = candidate.Plugin.Guid; project.PluginAssembly = candidate.AssemblyName; project.PluginFile = candidate.Relative;
        project.PackageFolder = project.PackageFolder.Length == 0 ? Slug(candidate.Plugin.Name) : project.PackageFolder; project.ThunderstoreName = project.ThunderstoreName.Length == 0 ? StoreName(candidate.Plugin.Name) : project.ThunderstoreName; project.ProjectFile = projectFile; project.BuildChoices = choices.ToList();
        if (project.BuildEdition.Length == 0 && choices.Count == 1) project.BuildEdition = choices[0].Edition;
        Save(project); return project;
    }

    private static string WorkspaceFile(string dataRoot) => FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring/workspace.json");
    private static void SaveWorkspace(string dataRoot, CorpSecWorkspaceState state) { string file = WorkspaceFile(dataRoot); Directory.CreateDirectory(Path.GetDirectoryName(file)!); JsonFiles.Write(file, state); }

    public static CorpSecProject Load(string path)
    {
        path = Path.GetFullPath(path); using var lease = CorpSecAuthoringLease.Enter(ProjectDataRoot(path));
        var project = JsonFiles.Read<CorpSecProject>(path); UpgradeProject(project); project.ProjectFile = path; if (project.ThunderstoreName.Length == 0) project.ThunderstoreName = StoreName(project.DisplayName); ValidateProject(project); return project;
    }

    public static IReadOnlyList<CorpSecProject> SavedProjects(string dataRoot)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        string projects = FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring/projects");
        if (!Directory.Exists(projects)) return Array.Empty<CorpSecProject>();
        var ignored = Workspace(dataRoot).IgnoredProjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<CorpSecProject>();
        foreach (string file in Directory.EnumerateFiles(projects, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try { var project = Load(file); if (!ignored.Contains(project.Id)) result.Add(project); }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    public static void Save(CorpSecProject project)
    {
        UpgradeProject(project); ReconcileImmediateFindings(project); ValidateProject(project); using var lease = CorpSecAuthoringLease.Enter(ProjectDataRoot(project.ProjectFile)); Directory.CreateDirectory(Path.GetDirectoryName(project.ProjectFile)!); JsonFiles.Write(project.ProjectFile, project);
    }

    public static void Forget(string dataRoot, CorpSecProject project)
    {
        UpgradeProject(project); ValidateProject(project); using var lease = CorpSecAuthoringLease.Enter(dataRoot); dataRoot = FileSafety.ResolveFolder(dataRoot);
        string authoring = FileSafety.Under(dataRoot, "authoring"), projects = FileSafety.Under(authoring, "projects"), expectedProject = FileSafety.Under(projects, project.Id + ".json");
        if (!Path.GetFullPath(project.ProjectFile).Equals(expectedProject, HostPlatform.PathComparison)) throw new InvalidDataException("CorpSec project is outside its author workspace.");
        var state = Workspace(dataRoot);
        if (project.ProjectRoot.Length != 0 && !state.IgnoredProjectIds.Contains(project.Id, StringComparer.OrdinalIgnoreCase)) state.IgnoredProjectIds.Add(project.Id);
        if (state.SelectedProjectId.Equals(project.Id, StringComparison.OrdinalIgnoreCase)) state.SelectedProjectId = "";
        SaveWorkspace(dataRoot, state);
        if (File.Exists(expectedProject)) File.Delete(expectedProject);
        string workspaces = FileSafety.Under(authoring, "workspaces"), generated = FileSafety.Under(workspaces, project.Id);
        if (Directory.Exists(generated)) FileSafety.DeleteOwned(workspaces, generated);
        string integration = FileSafety.Under(projects, project.Id + "-integration");
        if (Directory.Exists(integration)) FileSafety.DeleteOwned(projects, integration);
    }

    public static CorpSecProject SelectBuild(CorpSecProject project, string edition)
    {
        UpgradeProject(project); var choice = project.BuildChoices.SingleOrDefault(c => c.Edition == edition) ?? throw new InvalidDataException("The selected build edition is no longer available.");
        project.BuildEdition = edition; project.Input = choice.Input; project.Version = choice.Version; project.PluginFile = choice.PluginFile; Save(project); return project;
    }

    public static CorpSecReport Generate(string dataRoot, CorpSecProject project, CancellationToken cancellation = default, bool includeRecovery = true)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot); UpgradeProject(project); ValidateProject(project); dataRoot = FileSafety.ResolveFolder(dataRoot);
        var analysis = Analyze(dataRoot, project, cancellation);
        string authoring = FileSafety.Under(dataRoot, "authoring"), workspaces = FileSafety.Under(authoring, "workspaces"); Directory.CreateDirectory(workspaces);
        string destination = FileSafety.Under(workspaces, project.Id), stage = FileSafety.Under(workspaces, project.Id + ".pending-" + Guid.NewGuid().ToString("N"));
        string previous = FileSafety.Under(workspaces, project.Id + ".previous-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage); bool replaced = false, installed = false;
        try
        {
            using var material = Materialized.Open(dataRoot, project.Input);
            CopyPayload(material.Root, stage, project.PackageFolder, cancellation);
            using (var detected = Detect(stage, project.PluginGuid, project.PluginAssembly))
            {
                if (detected.Plugin.Guid != project.PluginGuid || detected.Assembly.Name.Name != project.PluginAssembly) throw new InvalidDataException("The selected build no longer contains the configured plugin. Reopen the project from its final DLL.");
                string pluginRoot = Path.GetDirectoryName(detected.File)!, supportRoot = FileSafety.Under(pluginRoot, "LUZ");
                if (Directory.Exists(supportRoot)) FileSafety.DeleteOwned(pluginRoot, supportRoot);
                if (!includeRecovery && DirectLuzDependency(detected.Assembly, detected.Plugin)) throw new InvalidDataException("This DLL directly depends on the LUZ runtime. Select its standalone build before creating a normal package.");
                if (includeRecovery && analysis.RecoveryReady)
                {
                    Directory.CreateDirectory(supportRoot); var adapters = new List<SupportAdapter>();
                    foreach (var adapter in project.Adapters)
                    {
                        cancellation.ThrowIfCancellationRequested(); ValidateAdapterSource(adapter);
                        string target = FileSafety.Under(supportRoot, "adapters/" + Path.GetFileName(adapter.Source)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(adapter.Source, target, true);
                        adapters.Add(new(adapter.Id, "adapters/" + Path.GetFileName(target), FileSafety.Hash(target), adapter.Type, adapter.Capabilities));
                    }
                    var metadata = Metadata(detected.Assembly); string contracts = ContractHash(detected.Assembly);
                    var ownedFiles = adapters.Select(a => "LUZ/" + a.File).Append(Path.GetRelativePath(pluginRoot, detected.File).Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var payloads = Files(pluginRoot).Select(f => (File: Path.GetRelativePath(pluginRoot, f).Replace('\\', '/'), Full: f)).Where(f => !ownedFiles.Contains(f.File) && !f.File.Equals("LUZ/mod-support.json", StringComparison.OrdinalIgnoreCase)).Select(f => new SupportPayload(f.File, FileSafety.Hash(f.Full))).ToArray();
                    var capabilities = project.Capabilities.Select(c => new SupportCapability(c.Name,
                        new[] { c.Preferred }.Concat(c.Fallbacks).Distinct(StringComparer.Ordinal).Select((id, index) => Implementation(id, index, adapters)).ToArray(), c.Dependencies)).ToArray();
                    var descriptor = new ModSupportDescriptor
                    {
                        Owner = project.Owner, DisplayName = project.DisplayName, SupportContact = project.SupportContact, Provenance = project.SupportSource == "luz-maintained" ? "luz-maintained" : "author",
                        Plugin = new(project.PluginGuid, project.PluginAssembly, project.Version, Path.GetRelativePath(pluginRoot, detected.File).Replace('\\', '/'), FileSafety.Hash(detected.File)),
                        Compatibility = new(metadata.GetValueOrDefault("Nivalis.NativeBuild") ?? "", metadata.GetValueOrDefault("Nivalis.MetadataBuild") ?? "", int.TryParse(metadata.GetValueOrDefault("Nivalis.Requirements.Schema"), out int schema) ? schema : 0, contracts),
                        Payloads = payloads, Capabilities = capabilities,
                        Companions = project.Companions.Select(c => new SupportCompanion(c.Root, c.Pattern, c.Slot, c.Required, c.Validation, c.SchemaMinimum, c.SchemaMaximum, c.RevisionProperty)).ToArray(), Adapters = adapters.ToArray()
                    };
                    descriptor.Validate(); SafeFiles.Write(FileSafety.Under(supportRoot, "mod-support.json"), descriptor);
                }
            }
            string sdk = FileSafety.Under(authoring, "sdk"); WriteSdkFiles(sdk);
            if (Directory.Exists(destination)) { Directory.Move(destination, previous); replaced = true; }
            Directory.Move(stage, destination); installed = true; project.Workspace = destination; project.PackageMode = includeRecovery ? "recovery" : "normal"; Save(project);
            if (replaced) FileSafety.DeleteOwned(workspaces, previous);
            return Validate(project, cancellation, includeRecovery);
        }
        catch
        {
            if (Directory.Exists(stage)) FileSafety.DeleteOwned(workspaces, stage);
            if (replaced && Directory.Exists(previous)) { if (Directory.Exists(destination)) FileSafety.DeleteOwned(workspaces, destination); Directory.Move(previous, destination); }
            else if (installed && Directory.Exists(destination)) FileSafety.DeleteOwned(workspaces, destination);
            throw;
        }
    }

    public static CorpSecReport Validate(CorpSecProject project, CancellationToken cancellation = default, bool includeRecovery = true)
    {
        ValidateProject(project); if (!Directory.Exists(project.Workspace)) throw new InvalidOperationException("Generate support files before validation.");
        var messages = new List<CorpSecMessage>(); var files = Files(project.Workspace).Select(f => Path.GetRelativePath(project.Workspace, f).Replace('\\', '/')).Order().ToArray();
        bool package = true, recovery = true;
        if (files.Length == 0) { package = false; messages.Add(new("Error", "No deployable files were generated.")); }
        if (files.Any(f => DevelopmentExtensions.Contains(Path.GetExtension(f)) || Path.GetFileName(f).Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase))) { package = false; messages.Add(new("Error", "Development files are present in the public package tree.")); }
        if (files.Any(f => Path.GetFileName(f).StartsWith("Luz.Continuity", StringComparison.OrdinalIgnoreCase))) { package = false; messages.Add(new("Error", "Remove the bundled LUZ runtime. LUZ support packages contain declarations and author adapters only.")); }
        if (files.Any(IsRuntimeBinary)) { package = false; messages.Add(new("Error", "Remove bundled game or BepInEx runtime files. A mod package must contain only its own payload and dependencies.")); }
        if (includeRecovery) try
        {
            var descriptors = Discover(project.Workspace, requireAllPlugins: true);
            if (descriptors.Count != 1) throw new InvalidDataException("The author project must produce exactly one LUZ support descriptor.");
            foreach (var support in descriptors)
            {
                cancellation.ThrowIfCancellationRequested();
                string pluginFile = FileSafety.Under(support.PluginRoot, support.Descriptor.Plugin.File);
                using var pluginAssembly = AssemblyDefinition.ReadAssembly(pluginFile, new ReaderParameters { InMemory = true });
                var pluginMetadata = Metadata(pluginAssembly); bool declaresContracts = DeclaresCompiledContracts(support.Descriptor);
                if (RequiresCompiledContracts(support.Descriptor) && !declaresContracts)
                    throw new InvalidDataException("Declare compiled Nivalis contracts in the final mod DLL, then rebuild support files. Native recovery behavior cannot be qualified from an author declaration.");
                if (declaresContracts && (pluginMetadata.GetValueOrDefault("Nivalis.Requirements.Schema") != support.Descriptor.Compatibility.RequirementsSchema.ToString() || support.Descriptor.Compatibility.ContractsSha256.Length != 0 && ContractHash(pluginAssembly) != support.Descriptor.Compatibility.ContractsSha256))
                    throw new InvalidDataException("Rebuild support files after changing the compiled Nivalis contracts.");
                var selected = Select(support.Descriptor, support.PluginRoot, support.DescriptorPath);
                foreach (var capability in support.Descriptor.Capabilities)
                {
                    string preferred = capability.Implementations.OrderByDescending(i => i.Priority).First().Id;
                    if (selected[capability.Name] != preferred)
                        messages.Add(new("Warning", "Using fallback " + selected[capability.Name] + " for " + support.Descriptor.Owner + "/" + capability.Name + ".", "The preferred implementation " + preferred + " is unavailable. LUZ resolved this fallback before any protected effects began."));
                }
            }
            messages.Add(new("Success", "Recovery declaration checks passed.", "Descriptor, payload hashes, companion rules and adapter lifecycles are complete."));
            messages.Add(new("Warning", "In-game recovery has not been tested.", "Run the numbered protected-save gameplay check before describing recovery behavior as verified."));
        }
        catch (Exception error) { recovery = false; messages.Add(new("Error", "Recovery check failed.", error.Message)); }
        else
        {
            recovery = false;
            if (Directory.EnumerateFiles(project.Workspace, "mod-support.json", SearchOption.AllDirectories).Any()) { package = false; messages.Add(new("Error", "Normal packages cannot contain LUZ recovery declarations.", "Build with recovery support or remove the incomplete LUZ folder.")); }
            try { using var ordinary = Detect(project.Workspace, project.PluginGuid, project.PluginAssembly); if (DirectLuzDependency(ordinary.Assembly, ordinary.Plugin)) throw new InvalidDataException("The selected DLL directly depends on the LUZ runtime. Choose its standalone build."); }
            catch (Exception error) when (error is InvalidDataException or BadImageFormatException or IOException) { package = false; messages.Add(new("Error", "Normal package check failed.", error.Message)); }
            messages.Add(new("Warning", "LUZ recovery support is not included.", "This ordinary package can be distributed while recovery setup remains incomplete."));
        }
        try { foreach (string dependency in UnmappedDependencies(project)) messages.Add(new("Warning", "Map required plugin " + dependency + " to a Thunderstore package before using the Thunderstore preset.")); }
        catch (InvalidDataException error) { package = false; messages.Add(new("Error", "Fix the Thunderstore dependency mappings.", error.Message)); }
        if (package) messages.Add(new("Success", "Package checks passed.", files.Length + " files are ready for export."));
        return new(package, recovery, messages, files, includeRecovery ? (recovery ? "declared" : "incomplete") : "not-included", false);
    }

    public static IReadOnlyList<CorpSecPackage> Package(string dataRoot, CorpSecProject project, string output, string preset, CancellationToken cancellation = default, bool includeRecovery = true)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot); var report = Validate(project, cancellation, includeRecovery); if (!report.PackagePassed || includeRecovery && !report.RecoveryPassed) throw new InvalidOperationException(includeRecovery ? "Package and recovery declaration checks must pass before recovery-enabled export." : "Package checks must pass before export.");
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        string[] presets = preset == "all" ? new[] { "nexus", "thunderstore" } : new[] { preset };
        if (presets.Any(p => p is not ("nexus" or "thunderstore"))) throw new ArgumentException("Use nexus, thunderstore or all.");
        if (presets.Contains("thunderstore")) ValidateThunderstore(project);
        var results = new List<CorpSecPackage>();
        foreach (string selected in presets)
        {
            cancellation.ThrowIfCancellationRequested(); string stageRoot = FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring/package-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stageRoot);
            try
            {
                CopyTree(project.Workspace, stageRoot, cancellation);
                if (selected == "thunderstore") WriteManifest(stageRoot, project);
                string name = Slug(project.DisplayName) + "-" + project.Version + (includeRecovery ? "-LUZ-Recovery" : "") + (selected == "thunderstore" ? "-Thunderstore" : "-Nexus") + ".zip";
                string archive = Path.Combine(output, name), pending = Path.Combine(output, "." + Path.GetFileNameWithoutExtension(name) + ".pending-" + Guid.NewGuid().ToString("N") + ".zip");
                ZipFile.CreateFromDirectory(stageRoot, pending, CompressionLevel.Optimal, false);
                ValidateArchive(dataRoot, pending); File.Move(pending, archive, true);
                string hash = FileSafety.Hash(archive), checksum = archive + ".sha256", checksumPending = checksum + ".pending";
                File.WriteAllText(checksumPending, hash + "  " + Path.GetFileName(archive) + Environment.NewLine, new UTF8Encoding(false)); File.Move(checksumPending, checksum, true);
                results.Add(new(archive, hash, selected, report));
            }
            finally { if (Directory.Exists(stageRoot)) FileSafety.DeleteOwned(FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring"), stageRoot); }
        }
        return results;
    }

    public static CorpSecRelease BuildRelease(string dataRoot, CorpSecProject project, string output, string preset, CancellationToken cancellation = default, bool includeRecovery = true)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot); var refreshed = Refresh(dataRoot, project, cancellation); refreshed.PackageMode = includeRecovery ? "recovery" : "normal"; Save(refreshed); var report = Generate(dataRoot, refreshed, cancellation, includeRecovery); var packages = Package(dataRoot, refreshed, output, preset, cancellation, includeRecovery); return new(refreshed, report, packages);
    }

    public static string GenerateBuildIntegration(CorpSecProject project, string executable)
    {
        ValidateProject(project); using var lease = CorpSecAuthoringLease.Enter(ProjectDataRoot(project.ProjectFile)); string root = Path.Combine(Path.GetDirectoryName(project.ProjectFile)!, project.Id + "-integration"); Directory.CreateDirectory(root);
        string target = Path.Combine(root, "CorpSec.targets"), escapedExecutable = SecurityElement.Escape(Path.GetFullPath(executable))!, escapedProject = SecurityElement.Escape(project.ProjectFile)!;
        string text = $"<Project><PropertyGroup><LuzCorpSecExecutable Condition=\"'$(LuzCorpSecExecutable)'==''\">{escapedExecutable}</LuzCorpSecExecutable></PropertyGroup><Target Name=\"GenerateLuzSupport\" AfterTargets=\"Build\"><Exec Command=\"&quot;$(LuzCorpSecExecutable)&quot; --corpsec --json generate &quot;{escapedProject}&quot; {project.PackageMode}\" /></Target></Project>";
        File.WriteAllText(target, text, new UTF8Encoding(false)); WriteSdkFiles(root); return target;
    }

    private static void WriteSdkFiles(string root)
    {
        Directory.CreateDirectory(root); var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(root, "LuzRecoveryAdapterV1.cs"), AdapterSourceTemplate, utf8);
        File.WriteAllText(Path.Combine(root, "LuzOptionalRuntimeV1.cs"), OptionalRuntimeSourceTemplate, utf8);
        File.WriteAllText(Path.Combine(root, "LuzRecoveryAdapterV1.csproj"), AdapterProjectTemplate, utf8);
        File.WriteAllText(Path.Combine(root, "README.md"), SdkGuide, utf8);
    }

    public sealed record LocatedSupport(ModSupportDescriptor Descriptor, string DescriptorPath, string PluginRoot, string DescriptorHash, IReadOnlyDictionary<string, string> Selected);
    public static List<LocatedSupport> Discover(string root, bool requireAllPlugins)
    {
        var result = new List<LocatedSupport>();
        foreach (string path in Directory.EnumerateFiles(root, "mod-support.json", SearchOption.AllDirectories).Where(p => string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "LUZ", StringComparison.OrdinalIgnoreCase)))
        {
            var descriptor = SafeFiles.Read<ModSupportDescriptor>(path); descriptor.Validate(); string pluginRoot = Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
            result.Add(new(descriptor, path, pluginRoot, SafeFiles.Hash(path), Select(descriptor, pluginRoot, path)));
        }
        if (result.Select(r => r.Descriptor.Owner).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new InvalidDataException("Duplicate LUZ support owner.");
        if (requireAllPlugins)
        {
            foreach (var dll in Files(root).Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            foreach (var plugin in ReadPlugins(dll))
                if (!result.Any(r => r.Descriptor.Plugin.Guid == plugin.Guid && Path.GetFullPath(Path.Combine(r.PluginRoot, r.Descriptor.Plugin.File)) == Path.GetFullPath(dll)))
                    throw new InvalidDataException("Generate LUZ support for " + plugin.Name + " before enabling protected recovery.");
        }
        return result;
    }

    private static Dictionary<string, string> Select(ModSupportDescriptor descriptor, string pluginRoot, string descriptorPath)
    {
        string plugin = FileSafety.Under(pluginRoot, descriptor.Plugin.File);
        if (!File.Exists(plugin) || FileSafety.Hash(plugin) != descriptor.Plugin.Sha256) throw new InvalidDataException("Rebuild support files for " + descriptor.DisplayName + "; its plugin DLL changed.");
        var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        var availableAdapters = new HashSet<string>(StringComparer.Ordinal); var unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var adapter in descriptor.Adapters)
        {
            try
            {
                string path = FileSafety.Under(Path.GetDirectoryName(descriptorPath)!, adapter.File);
                if (!File.Exists(path) || FileSafety.Hash(path) != adapter.Sha256) throw new InvalidDataException("missing or changed DLL");
                ValidateAdapter(path, adapter.Type); availableAdapters.Add(adapter.Id);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or BadImageFormatException)
            { unavailable[adapter.Id] = error.Message; }
        }
        foreach (var payload in descriptor.Payloads)
        {
            string path = FileSafety.Under(pluginRoot, payload.File);
            if (!File.Exists(path) || FileSafety.Hash(path) != payload.Sha256) throw new InvalidDataException("Rebuild support files for " + descriptor.DisplayName + "; payload changed: " + payload.File);
        }
        foreach (var capability in descriptor.Capabilities)
        {
            var available = capability.Implementations.OrderByDescending(i => i.Priority).FirstOrDefault(i => i.Kind != "adapter" || availableAdapters.Contains(i.Adapter));
            if (available == null)
            {
                string detail = string.Join("; ", capability.Implementations.Where(i => i.Kind == "adapter" && unavailable.ContainsKey(i.Adapter)).Select(i => i.Adapter + ": " + unavailable[i.Adapter]));
                throw new InvalidDataException("No complete implementation is available for " + descriptor.Owner + "/" + capability.Name + (detail.Length == 0 ? "." : ": " + detail));
            }
            selected.Add(capability.Name, available.Id);
        }
        foreach (var capability in descriptor.Capabilities)
            if (capability.Dependencies.Any(d => !selected.ContainsKey(d))) throw new InvalidDataException("Capability dependency is unavailable: " + descriptor.Owner + "/" + capability.Name + ".");
        return selected;
    }

    private static SupportImplementation Implementation(string id, int order, IReadOnlyList<SupportAdapter> adapters)
    {
        if (id == "native") return new("native", "native", "", 1000 - order);
        if (id == "dormant") return new("dormant", "dormant", "", 1000 - order);
        var adapter = adapters.SingleOrDefault(a => a.Id == id) ?? throw new InvalidDataException("Capability implementation names an adapter that was not supplied: " + id);
        return new(id, "adapter", adapter.Id, 1000 - order);
    }

    private static void UpgradeProject(CorpSecProject project)
    {
        if (project.Schema is not (1 or 2)) throw new InvalidDataException("Unsupported CorpSec author project.");
        project.Schema = 2; project.PackageMode = project.PackageMode is "normal" or "recovery" ? project.PackageMode : "normal";
        project.RecoveryIntent = project.RecoveryIntent is "unknown" or "stateless" or "files" or "embedded" or "custom" ? project.RecoveryIntent : "unknown";
        project.BuildChoices ??= new(); project.Findings ??= new(); project.Companions ??= new(); project.Capabilities ??= new(); project.Adapters ??= new(); project.ThunderstoreDependencies ??= new();
    }

    private static void ReconcileImmediateFindings(CorpSecProject project)
    {
        project.Findings.RemoveAll(finding => finding.Id == "support-contact");
        if (string.IsNullOrWhiteSpace(project.SupportContact))
            project.Findings.Add(new("support-contact", "Action", "Enter a support contact", "Recovery-enabled packages must tell players where to report a failed restore.", "project details"));
    }

    internal static void NormalizeRecoveryCapabilities(CorpSecProject project)
    {
        bool implicitModule = project.Capabilities.Count == 1 && project.Adapters.Count == 0 && project.Capabilities[0] is { Name: "module", Preferred: "native", Fallbacks.Length: 0, Dependencies.Length: 0, Provenance: "author" };
        if (implicitModule && project.RecoveryIntent is "stateless" or "files") project.Capabilities.Clear();
    }

    internal static bool RequiresCompiledContracts(CorpSecProject project) =>
        project.RecoveryIntent == "embedded" || project.Capabilities.Any(capability => capability.Preferred == "native" || capability.Fallbacks.Contains("native", StringComparer.Ordinal));

    internal static bool RequiresCompiledContracts(ModSupportDescriptor descriptor) =>
        descriptor.Capabilities.Any(capability => capability.Implementations.Any(implementation => implementation.Kind == "native"));

    internal static bool DeclaresCompiledContracts(ModSupportDescriptor descriptor) =>
        descriptor.Compatibility.RequirementsSchema == 1 && descriptor.Compatibility.ContractsSha256.Length == 0 ||
        descriptor.Compatibility.RequirementsSchema == 2 && descriptor.Compatibility.ContractsSha256.Length == 64;

    private static string ProjectDataRoot(string projectFile)
    {
        var projects = new FileInfo(Path.GetFullPath(projectFile)).Directory ?? throw new InvalidDataException("CorpSec project path is invalid.");
        if (!projects.Name.Equals("projects", StringComparison.OrdinalIgnoreCase) || projects.Parent?.Name != "authoring" || projects.Parent.Parent == null) throw new InvalidDataException("CorpSec project is outside its author workspace.");
        return projects.Parent.Parent.FullName;
    }

    private static void ValidateProject(CorpSecProject project)
    {
        if (project.Schema != 2 || project.Id.Length != 64 || !project.Id.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(project.Input) || !Path.IsPathFullyQualified(project.Input)) throw new InvalidDataException("Invalid CorpSec project.");
        if (!Regex.IsMatch(project.Owner, "^[a-z0-9][a-z0-9._-]{2,95}$") || string.IsNullOrWhiteSpace(project.PluginGuid) || string.IsNullOrWhiteSpace(project.PluginAssembly) || string.IsNullOrWhiteSpace(project.ProjectFile)) throw new InvalidDataException("CorpSec project identity is incomplete.");
        if (project.ProjectRoot.Length != 0 && !Path.IsPathFullyQualified(project.ProjectRoot) || project.SourceProject.Length != 0 && !Path.IsPathFullyQualified(project.SourceProject) || project.PackageOutput.Length != 0 && !Path.IsPathFullyQualified(project.PackageOutput)) throw new InvalidDataException("CorpSec project folders must use absolute paths.");
        if (project.Capabilities.Count > 128 || project.Companions.Count > 128 || project.Adapters.Count > 32 || project.BuildChoices.Count > 16 || project.Findings.Count > 256) throw new InvalidDataException("CorpSec project exceeds its limits.");
    }

    private static void WriteManifest(string root, CorpSecProject project)
    {
        var manifest = new { name = project.ThunderstoreName, version_number = project.Version, website_url = project.Website, description = project.Description, dependencies = ParsedDependencyMappings(project).Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), author = project.Author, release_channel = "stable" };
        JsonFiles.Write(Path.Combine(root, "manifest.json"), manifest);
    }
    private static Dictionary<string, string> ParsedDependencyMappings(CorpSecProject project)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in project.ThunderstoreDependencies)
        {
            string[] parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !Regex.IsMatch(parts[1], "^[A-Za-z0-9_]+-[A-Za-z0-9_]+-[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$")) throw new InvalidDataException("Map dependencies as: plugin GUID | Author-Package-Version.");
            if (!result.TryAdd(parts[0], parts[1])) throw new InvalidDataException("Duplicate Thunderstore dependency mapping: " + parts[0]);
        }
        return result;
    }
    private static string[] UnmappedDependencies(CorpSecProject project)
    {
        var mappings = ParsedDependencyMappings(project); using var detected = Detect(project.Workspace, project.PluginGuid, project.PluginAssembly);
        return detected.Plugin.Dependencies.Where(d => !d.Optional && !mappings.ContainsKey(d.Guid)).Select(d => d.Guid).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static void ValidateThunderstore(CorpSecProject project)
    {
        if (!Regex.IsMatch(project.ThunderstoreName, "^[A-Za-z0-9_]{1,64}$")) throw new InvalidDataException("Use a Thunderstore package name containing only letters, numbers and underscores.");
        if (string.IsNullOrWhiteSpace(project.Author) || string.IsNullOrWhiteSpace(project.Description) || project.Description.Length > 250) throw new InvalidDataException("Thunderstore requires an author and a description of at most 250 characters.");
        if (!SemanticVersioning.Version.TryParse(project.Version, out _)) throw new InvalidDataException("Thunderstore requires a semantic version such as 1.2.3.");
        string icon = Path.Combine(project.Workspace, "icon.png"); if (!File.Exists(icon)) throw new InvalidDataException("Thunderstore packages require icon.png. Add it to the mod folder before packaging.");
        string[] unresolved = UnmappedDependencies(project); if (unresolved.Length != 0) throw new InvalidDataException("Map these required plugin dependencies before building the Thunderstore preset: " + string.Join(", ", unresolved));
    }

    private static void ValidateArchive(string dataRoot, string archive)
    {
        string fixture = FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring/import-check-" + Guid.NewGuid().ToString("N"));
        using var library = new Library(fixture);
        try
        {
            var package = library.Import(archive); if (package.Plugins.Count == 0) throw new InvalidDataException("Generated package has no BepInEx plugin.");
            foreach (var dependency in package.Plugins.SelectMany(p => p.Dependencies).Where(d => !d.Optional).GroupBy(d => d.Guid, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                string version = DependencyFixtureVersion(dependency.MinimumVersion);
                var external = new ModPackage { Name = "External dependency " + dependency.Guid, Version = version, Plugins = [new(dependency.Guid, dependency.Guid, version, [], [])] };
                library.State.Packages.Add(external); library.Active.Mods.Add(new() { PackageId = external.Id });
            }
            foreach (string dependency in package.Dependencies)
            {
                var parsed = ModPlanner.ParseDependency(dependency); var external = new ModPackage { Name = "External package " + parsed.Id, Version = parsed.Version, Source = "Thunderstore", SourceId = parsed.Id };
                library.State.Packages.Add(external); library.Active.Mods.Add(new() { PackageId = external.Id });
            }
            var issues = ModPlanner.Check(library.Active, library.State.Packages); if (issues.Any(i => i.Severity == "Error")) throw new InvalidDataException("Generated package failed LUZ deployment planning: " + string.Join("; ", issues.Where(i => i.Severity == "Error").Select(i => i.Message)));
        }
        finally { library.Dispose(); FileSafety.DeleteOwned(FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring"), fixture); }
    }
    private static string DependencyFixtureVersion(string requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement)) return "1.0.0";
        var candidates = Regex.Matches(requirement, @"\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.-]+)?").Select(m => m.Value).Concat(new[] { "1.0.0", "1.0.3", "2.0.0", "9.0.0" });
        return candidates.FirstOrDefault(version => { try { return ModPlanner.MatchesPluginDependency(version, requirement); } catch (InvalidDataException) { return false; } }) ?? throw new InvalidDataException("Cannot plan generated package with dependency range " + requirement + ".");
    }

    private sealed record ScanCandidate(string Input, string File, string Relative, PluginInfo Plugin, string AssemblyName, DateTime Stamp, string Edition);
    private sealed record Detected(string File, string Relative, PluginInfo Plugin, AssemblyDefinition Assembly) : IDisposable { public void Dispose() => Assembly.Dispose(); }
    private static Detected Detect(string root, string pluginGuid = "", string pluginAssembly = "")
    {
        var candidates = new List<(string File, PluginInfo Plugin)>();
        foreach (string dll in Files(root).Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) foreach (var plugin in ReadPlugins(dll)) candidates.Add((dll, plugin));
        if (pluginGuid.Length != 0) candidates = candidates.Where(c => c.Plugin.Guid.Equals(pluginGuid, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pluginAssembly.Length != 0) candidates = candidates.Where(c => { try { using var assembly = AssemblyDefinition.ReadAssembly(c.File); return assembly.Name.Name.Equals(pluginAssembly, StringComparison.OrdinalIgnoreCase); } catch (BadImageFormatException) { return false; } }).ToList();
        if (candidates.Count != 1) throw new InvalidDataException(candidates.Count == 0 ? "No BepInEx plugin was found." : "Choose a package containing exactly one BepInEx plugin.");
        var item = candidates[0]; return new(item.File, Path.GetRelativePath(root, item.File).Replace('\\', '/'), item.Plugin, AssemblyDefinition.ReadAssembly(item.File));
    }

    private static List<ScanCandidate> CandidateFiles(string root, string input, CancellationToken cancellation)
    {
        var result = new List<ScanCandidate>();
        foreach (string dll in ScanFiles(root, ".dll", cancellation))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var plugins = ReadPlugins(dll).ToArray(); if (plugins.Length == 0) continue;
                using var assembly = AssemblyDefinition.ReadAssembly(dll);
                string edition = Metadata(assembly).GetValueOrDefault("Nivalis.Continuity.Api") == "1" ? "universal" : "standalone";
                foreach (var plugin in plugins) result.Add(new(input, dll, Path.GetRelativePath(root, dll).Replace('\\', '/'), plugin, assembly.Name.Name, File.GetLastWriteTimeUtc(File.Exists(input) ? input : dll), edition));
            }
            catch (Exception error) when (error is BadImageFormatException or IOException or UnauthorizedAccessException) { }
        }
        return result;
    }

    private static IEnumerable<string> ScanFiles(string root, string extension, CancellationToken cancellation)
    {
        var pending = new Stack<string>(); pending.Push(root); int directories = 0, files = 0;
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested(); string directory = pending.Pop();
            // Discovery skips links; the selected payload is still checked by NoLinks
            // when materialized for generation. A linked records folder is not payload.
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
            if (++directories > 20_000) throw new IOException("The selected mods folder contains too many directories. Choose a narrower release or build folder.");
            IEnumerable<string> children; IEnumerable<string> found;
            try { children = Directory.EnumerateDirectories(directory).ToArray(); found = Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly).ToArray(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (string file in found)
            {
                if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) continue;
                if (++files > 10_000) throw new IOException("The selected mods folder contains too many files. Choose a narrower release or build folder.");
                yield return file;
            }
            foreach (string child in children)
            {
                var info = new DirectoryInfo(child); if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || ScanIgnoredFolders.Contains(info.Name)) continue; pending.Push(child);
            }
        }
    }

    private static ScanCandidate Preferred(IEnumerable<ScanCandidate> candidates) => candidates
        .OrderByDescending(c => VersionKey(c.Plugin.Version))
        .ThenByDescending(c => !c.Plugin.Version.Contains('-', StringComparison.Ordinal))
        .ThenByDescending(c => c.File.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(c => c.Stamp)
        .ThenBy(c => c.Input, HostPlatform.PathComparer)
        .First();

    private static Version VersionKey(string value)
    {
        string core = value.Split('-', 2)[0]; return Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0);
    }

    private static string CandidateInput(string root, ScanCandidate candidate, IReadOnlyList<ScanCandidate> all)
    {
        if (File.Exists(candidate.Input)) return candidate.Input;
        string relative = Path.GetRelativePath(root, candidate.File); string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int bep = Array.FindIndex(parts, p => p.Equals("BepInEx", StringComparison.OrdinalIgnoreCase));
        if (bep >= 0 && bep + 2 < parts.Length && parts[bep + 1].Equals("plugins", StringComparison.OrdinalIgnoreCase) && bep + 3 < parts.Length)
            return Path.GetFullPath(Path.Combine(root, Path.Combine(parts.Take(bep + 3).ToArray())));
        string parent = Path.GetDirectoryName(candidate.File)!;
        int peers = all.Count(c => Path.GetDirectoryName(c.File)!.Equals(parent, HostPlatform.PathComparison) && !c.Plugin.Guid.Equals(candidate.Plugin.Guid, StringComparison.OrdinalIgnoreCase));
        return peers == 0 ? parent : candidate.File;
    }

    private static ScanCandidate WithCandidateInput(string root, ScanCandidate candidate, IReadOnlyList<ScanCandidate> all)
    {
        string input = CandidateInput(root, candidate, all); string relative = File.Exists(input) && input.Equals(candidate.File, HostPlatform.PathComparison) ? Path.GetFileName(candidate.File) : File.Exists(input) ? candidate.Relative : Path.GetRelativePath(input, candidate.File).Replace('\\', '/');
        return candidate with { Input = input, Relative = relative };
    }
    private static CorpSecBuildChoice Choice(ScanCandidate candidate) => new(candidate.Edition,
        candidate.Edition == "universal" ? "Universal build" : "Standalone build", candidate.Plugin.Version, Path.GetFullPath(candidate.Input), candidate.Relative,
        candidate.Edition == "universal" ? "Normal operation with embedded optional LUZ continuity support" : "No compiled LUZ continuity client detected");
    private static IEnumerable<PluginInfo> ReadPlugins(string dll) { try { return PluginMetadata.Read(dll); } catch (BadImageFormatException) { return Array.Empty<PluginInfo>(); } }
    private static Dictionary<string, string> Metadata(AssemblyDefinition assembly) => assembly.CustomAttributes.Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2).ToDictionary(a => (string)a.ConstructorArguments[0].Value, a => (string)a.ConstructorArguments[1].Value);
    private static string ContractHash(AssemblyDefinition assembly)
    {
        var resource = assembly.MainModule.Resources.OfType<EmbeddedResource>().SingleOrDefault(r => r.Name == "Nivalis.Contracts.v2.json"); if (resource == null) return "";
        using var input = resource.GetResourceStream(); return Convert.ToHexString(SHA256.HashData(input));
    }
    private static void ValidateAdapterSource(CorpSecAdapter adapter) { if (!File.Exists(adapter.Source)) throw new FileNotFoundException("Adapter DLL is missing.", adapter.Source); ValidateAdapter(adapter.Source, adapter.Type); }
    private static bool DirectLuzDependency(AssemblyDefinition assembly, PluginInfo plugin) =>
        assembly.MainModule.AssemblyReferences.Any(r => r.Name.StartsWith("Luz.Continuity", StringComparison.OrdinalIgnoreCase)) ||
        plugin.Dependencies.Any(d => d.Guid.Contains("luz", StringComparison.OrdinalIgnoreCase) && !d.Optional);
    private static void ValidateAdapter(string path, string type)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path); var target = assembly.MainModule.GetType(type) ?? assembly.MainModule.Types.SelectMany(AllTypes).FirstOrDefault(t => t.FullName == type) ?? throw new InvalidDataException("Recovery adapter type is missing: " + type);
        if (!target.IsClass || target.IsAbstract || !target.Methods.Any(m => m.IsConstructor && m.IsPublic && !m.HasParameters)) throw new InvalidDataException("Recovery adapter needs a public parameterless constructor.");
        foreach (string name in AdapterMethods)
        {
            var method = target.Methods.SingleOrDefault(m => m.IsPublic && !m.IsStatic && m.Name == name && m.ReturnType.FullName == "System.String" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String") ?? throw new InvalidDataException("Recovery adapter must implement string " + name + "(string context).");
            if (method.HasBody && method.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && i.Operand is string value && value.StartsWith("not implemented", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Recovery adapter still contains the generated incomplete " + name + " implementation. Implement and test this lifecycle step before recovery-enabled export.");
        }
    }
    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(AllTypes));

    private static void CopyPayload(string source, string destination, string packageFolder, CancellationToken cancellation)
    {
        string? bep = Directory.EnumerateDirectories(source, "BepInEx", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault();
        if (bep != null)
        {
            foreach (string area in new[] { "plugins", "patchers", "config" }) { string path = Path.Combine(bep, area); if (Directory.Exists(path)) CopyTree(path, FileSafety.Under(destination, "BepInEx/" + area), cancellation); }
            foreach (string document in Files(source).Where(f => IsRootDocument(f, source))) File.Copy(document, FileSafety.Under(destination, Path.GetFileName(document)), true);
        }
        else
        {
            string plugin = FileSafety.Under(destination, "BepInEx/plugins/" + Slug(packageFolder)); Directory.CreateDirectory(plugin); CopyTree(source, plugin, cancellation);
            foreach (string document in Files(source).Where(f => IsRootDocument(f, source))) File.Copy(document, FileSafety.Under(destination, Path.GetFileName(document)), true);
        }
    }
    private static void CopyTree(string source, string destination, CancellationToken cancellation)
    {
        FileSafety.NoLinks(source); Directory.CreateDirectory(destination); long total = 0; int count = 0;
        foreach (string file in Files(source))
        {
            cancellation.ThrowIfCancellationRequested(); string relative = Path.GetRelativePath(source, file).Replace('\\', '/'); string name = Path.GetFileName(file);
            string[] parts = relative.Split('/');
            string extension = Path.GetExtension(name);
            if (DevelopmentExtensions.Contains(extension) || name.Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || name.Equals(".env", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pem", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".key", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(name, "(?i)(credential|secret|api[-_]?key|access[-_]?token)") || parts.Any(p => p.Equals("saves", StringComparison.OrdinalIgnoreCase) || DevelopmentFolders.Contains(p))) continue;
            if (++count > 10_000 || (total += new FileInfo(file).Length) > 512L * 1024 * 1024) throw new IOException("Mod payload exceeds CorpSec limits.");
            string output = FileSafety.Under(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(file, output, true);
        }
    }
    private static IEnumerable<string> Files(string root) => Directory.Exists(root) ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories) : new[] { root };
    private static bool IsRootDocument(string file, string root)
    {
        if (Path.GetDirectoryName(file) != root) return false; string name = Path.GetFileName(file);
        return name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || name.Equals("icon.png", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(name, "(?i)^(readme|changelog|license|licence|copying|notice|credits)(?:[._-].*)?$");
    }
    private static bool IsRuntimeBinary(string file) => RuntimeBinaries.Contains(Path.GetFileName(file)) || Path.GetFileName(file).StartsWith("Il2CppInterop.", StringComparison.OrdinalIgnoreCase);
    private static string Slug(string value) { string result = new(value.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()); result = string.Join('-', result.Split('-', StringSplitOptions.RemoveEmptyEntries)); return result.Length < 3 ? "mod-" + result : result[..Math.Min(80, result.Length)]; }
    private static string StoreName(string value) { string result = new string(value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_'); return (result.Length == 0 ? "Mod" : result)[..Math.Min(result.Length == 0 ? 3 : result.Length, 64)]; }
    private static string SafeKey(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Materialized : IDisposable
    {
        public string Root { get; } private readonly string? owned; private readonly string dataRoot;
        private Materialized(string root, string? owned, string dataRoot) { Root = root; this.owned = owned; this.dataRoot = dataRoot; }
        public static Materialized Open(string dataRoot, string input)
        {
            if (Directory.Exists(input)) { FileSafety.NoLinks(input); return new(input, null, dataRoot); }
            if (Path.GetExtension(input).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string dllParent = FileSafety.Under(dataRoot, "authoring/materialized"), dllStage = FileSafety.Under(dllParent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dllStage); File.Copy(input, Path.Combine(dllStage, Path.GetFileName(input))); return new(dllStage, dllStage, dataRoot);
            }
            if (!Path.GetExtension(input).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a mod folder, ZIP or built DLL.");
            string parent = FileSafety.Under(dataRoot, "authoring/materialized"), stage = FileSafety.Under(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage); FileSafety.Extract(input, stage); return new(stage, stage, dataRoot);
        }
        public void Dispose() { if (owned != null && Directory.Exists(owned)) FileSafety.DeleteOwned(FileSafety.Under(dataRoot, "authoring/materialized"), owned); }
    }

    public const string AdapterSourceTemplate = """
// Compile this file into a separate adapter DLL stored under your mod's LUZ/adapters folder.
// LUZ calls these methods only in the game runtime and passes a JSON context string.
// Save transformations receive StageRoot plus Files entries containing Root, Relative, Path,
// NativeHash and Existed. Modify only the staged Path values. Return "ok" only after the
// operation is implemented and verified. These deliberate failures prevent an untouched
// template from being packaged as working recovery support.
namespace LuzAuthorSupport;
public sealed class RecoveryAdapterV1
{
    public string Preflight(string context) => "not implemented: validate this mod and its recovery inputs";
    public string Activate(string context) => "not implemented: activate the selected recovery implementation";
    public string PrepareSave(string context) => "not implemented: prepare staged save files";
    public string CompleteSave(string context) => "not implemented: validate completed staged files";
    public string Suspend(string context) => "not implemented: safely suspend unavailable features";
    public string Restore(string context) => "not implemented: restore and validate mod-owned state";
    public string CleanupFailure(string context) => "not implemented: clean up a failed protected operation";
}
""";

    public const string OptionalRuntimeSourceTemplate = """
// This file may be compiled into the normal mod. It has no LUZ assembly dependency.
// Standalone behavior remains authoritative whenever LUZ is absent or not ready.
using System;
using System.Linq;
using System.Reflection;
namespace LuzAuthorSupport;
public static class OptionalLuzRuntimeV1
{
    private static Type? Host => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Luz.Continuity.Runtime")?.GetType("Luz.Continuity.Runtime.Host", false);
    public static bool IsInstalled => Host != null;
    public static bool IsProtectedSession
    {
        get
        {
            try { return Host?.GetProperty("Session", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) != null; }
            catch { return false; }
        }
    }
    public static bool IsReady
    {
        get { try { return Host?.GetProperty("Ready", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is true; } catch { return false; } }
    }
    public static void RequireProtectedOperation()
    {
        if (IsProtectedSession && !IsReady) throw new InvalidOperationException("LUZ protected recovery failed to activate. Saving must remain blocked until LUZ recovery succeeds.");
    }
    public static string Status()
    {
        if (!IsInstalled) return "LUZ absent; ordinary standalone play";
        if (!IsProtectedSession) return "LUZ installed; ordinary standalone play";
        if (!IsReady) return "LUZ protected session failed; saving is blocked";
        try { return Host?.GetMethod("Status", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null) as string ?? "LUZ protected session unavailable"; }
        catch { return "LUZ protected session unavailable"; }
    }
}
""";

    public const string AdapterProjectTemplate = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net6.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><AssemblyName>MyMod.LuzRecovery</AssemblyName><DebugType>none</DebugType></PropertyGroup>
</Project>
""";

    public const string SdkGuide = """
# LUZ recovery adapter SDK

Use a custom adapter only when declarative save/configuration file rules cannot preserve the mod correctly. The adapter is optional during ordinary standalone play and is loaded only for a protected LUZ timeline.

1. Rename the namespace, class and assembly for your mod.
2. Implement every lifecycle method in `LuzRecoveryAdapterV1.cs`. An untouched `not implemented` result is rejected.
3. Run `dotnet build LuzRecoveryAdapterV1.csproj -c Release`.
4. In CorpSec Terminal, open **LUZ Support > Advanced recovery implementations**, add the built DLL, choose its public type and capabilities, then regenerate support from the final mod build.

The JSON context identifies the operation, owner and protected timeline. Save transformations also receive a staging folder and bounded file records. `Preflight` validates before effects begin. `Activate` establishes the selected implementation. `PrepareSave` and `CompleteSave` operate on staged copies. `Suspend` preserves unavailable features. `Restore` must validate restored state before acknowledgement. `CleanupFailure` releases temporary state without hiding the first error. Return `ok` only after successful completion; return a useful error message otherwise.

`LuzOptionalRuntimeV1.cs` adds no LUZ assembly reference. Ordinary play continues when LUZ is absent or no protected timeline exists. Call `RequireProtectedOperation` before any save path controlled by the integration so a failed protected session cannot silently continue with unprotected saving.
""";
}

public static class CorpSecCommand
{
    public static int Run(string[] args, string dataRoot)
    {
        bool json = args.Contains("--json", StringComparer.OrdinalIgnoreCase); args = args.Where(a => !a.Equals("--json", StringComparison.OrdinalIgnoreCase)).ToArray();
        try
        {
            if (args.Length < 2) throw new ArgumentException("Use --corpsec [--json] scan <builds-folder>, inspect <input> [source], analyze <project>, generate <project> [normal|recovery], validate <project> [normal|recovery], package <project> <output> [nexus|thunderstore|all] [normal|recovery], or integration <project>.");
            string action = args[1].ToLowerInvariant();
            if (action == "scan" && args.Length == 3) { var result = CorpSec.ScanFolder(dataRoot, args[2]); Output(result, json); return result.Projects.Any(p => p.Findings.Any(f => f.Severity == "Action")) ? 2 : 0; }
            if (action == "inspect" && args.Length is 3 or 4) { var result = CorpSec.Inspect(dataRoot, args[2], args.Length == 4 ? args[3] : ""); Output(result, json); return result.Findings.Any(f => f.Severity == "Action") ? 2 : 0; }
            var project = CorpSec.Load(args.ElementAtOrDefault(2) ?? "");
            if (action == "analyze" && args.Length == 3) { var result = CorpSec.Analyze(dataRoot, project); Output(result, json); return result.RecoveryReady ? 0 : 2; }
            if (action == "generate" && args.Length is 3 or 4) { bool recovery = Mode(project, args.ElementAtOrDefault(3)); var report = CorpSec.Generate(dataRoot, project, includeRecovery: recovery); Output(report, json); return report.PackagePassed && (!recovery || report.RecoveryPassed) ? 0 : 2; }
            if (action == "validate" && args.Length is 3 or 4) { bool recovery = Mode(project, args.ElementAtOrDefault(3)); var report = CorpSec.Validate(project, includeRecovery: recovery); Output(report, json); return report.PackagePassed && (!recovery || report.RecoveryPassed) ? 0 : 2; }
            if (action == "package" && args.Length is >= 4 and <= 6)
            {
                string preset = args.Skip(4).FirstOrDefault(a => a is "nexus" or "thunderstore" or "all") ?? "nexus"; string? requestedMode = args.Skip(4).FirstOrDefault(a => a is "normal" or "recovery"); bool recovery = Mode(project, requestedMode);
                var result = CorpSec.BuildRelease(dataRoot, project, args[3], preset, includeRecovery: recovery); Output(result, json); return 0;
            }
            if (action == "integration" && args.Length == 3) { Output(new { File = CorpSec.GenerateBuildIntegration(project, Environment.ProcessPath ?? throw new InvalidOperationException("LUZ executable path is unavailable.")) }, json); return 0; }
            throw new ArgumentException("Invalid CorpSec command or argument count.");
        }
        catch (Exception error) { if (json) Console.Error.WriteLine(JsonSerializer.Serialize(new { Error = error.Message, Type = error.GetType().Name })); else Console.Error.WriteLine(error.Message); return 1; }
    }
    private static bool Mode(CorpSecProject project, string? value) => (value ?? project.PackageMode) switch { "recovery" => true, "normal" => false, _ => throw new ArgumentException("Package mode must be normal or recovery.") };
    private static void Output(object value, bool json)
    {
        if (json) { Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); return; }
        if (value is CorpSecReport report) { foreach (var message in report.Messages) Console.WriteLine(message.Severity + ": " + message.Message + (message.Detail == null ? "" : " " + message.Detail)); return; }
        if (value is CorpSecFolderScan scan) { foreach (var project in scan.Projects) Console.WriteLine(project.DisplayName + " " + project.Version + " " + project.ProjectFile); return; }
        if (value is CorpSecProject authoredProject) { Console.WriteLine(authoredProject.ProjectFile); foreach (var finding in authoredProject.Findings.Where(f => f.Severity == "Action")) Console.WriteLine("Action: " + finding.Title); return; }
        if (value is CorpSecAnalysisReport analysis) { foreach (var finding in analysis.Findings) Console.WriteLine(finding.Severity + ": " + finding.Title + " — " + finding.Detail); return; }
        if (value is CorpSecRelease authoredRelease) { foreach (var package in authoredRelease.Packages) Console.WriteLine(package.Archive + " " + package.Sha256); return; }
        Console.WriteLine(JsonSerializer.Serialize(value));
    }
}
