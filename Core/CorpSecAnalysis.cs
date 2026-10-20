using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Luz.Continuity;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Luz;

public static partial class CorpSec
{
    private sealed class MaintainedSupportCatalog
    {
        public int Schema { get; set; }
        public MaintainedSupportModule[] Modules { get; set; } = Array.Empty<MaintainedSupportModule>();
    }

    private sealed record MaintainedSupportModule(string Guid, string Assembly, string Owner, string DisplayName, string[] Capabilities, Luz.Continuity.SupportCompanion[] Companions);

    public static CorpSecAnalysisReport Analyze(string dataRoot, CorpSecProject project, CancellationToken cancellation = default)
    {
        using var lease = CorpSecAuthoringLease.Enter(dataRoot);
        UpgradeProject(project); NormalizeRecoveryCapabilities(project); ValidateProject(project); dataRoot = FileSafety.ResolveFolder(dataRoot);
        var findings = new List<CorpSecFinding>();
        using var material = Materialized.Open(dataRoot, project.Input);
        using var detected = Detect(material.Root, project.PluginGuid, project.PluginAssembly);
        var metadata = Metadata(detected.Assembly); string contracts = ContractHash(detected.Assembly);
        string requirementsSchema = metadata.GetValueOrDefault("Nivalis.Requirements.Schema") ?? "";
        bool compiledContracts = requirementsSchema == "1" || requirementsSchema == "2" && contracts.Length == 64;
        bool continuityClient = metadata.GetValueOrDefault("Nivalis.Continuity.Api") == "1";

        string descriptorPath = Path.Combine(Path.GetDirectoryName(detected.File)!, "LUZ", "mod-support.json");
        bool imported = false;
        if (File.Exists(descriptorPath))
        {
            try
            {
                var descriptor = SafeFiles.Read<Luz.Continuity.ModSupportDescriptor>(descriptorPath); descriptor.Validate();
                if (!descriptor.Plugin.Guid.Equals(project.PluginGuid, StringComparison.OrdinalIgnoreCase) || !descriptor.Plugin.Assembly.Equals(project.PluginAssembly, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The descriptor identifies a different plugin.");
                ImportDescriptor(dataRoot, project, descriptor, descriptorPath, cancellation); imported = true;
                bool current = descriptor.Plugin.Sha256.Equals(FileSafety.Hash(detected.File), StringComparison.OrdinalIgnoreCase);
                findings.Add(new("existing-support", current ? "Success" : "Warning", current ? "Existing LUZ support detected" : "Rebuild support files for this DLL",
                    current ? "CorpSec imported the author declaration and will preserve it." : "The declaration belongs to an older DLL. Its settings were retained, but current compatibility comes from the rebuilt DLL and fresh hashes.",
                    "existing declaration", current ? "resolved" : "review"));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                findings.Add(new("invalid-existing-support", "Action", "Existing LUZ support needs repair", error.Message, "existing declaration"));
            }
        }

        var maintained = MaintainedSupport().Modules.SingleOrDefault(m => m.Guid.Equals(project.PluginGuid, StringComparison.OrdinalIgnoreCase) && m.Assembly.Equals(project.PluginAssembly, StringComparison.OrdinalIgnoreCase));
        if (!imported && maintained != null)
        {
            if (continuityClient && compiledContracts)
            {
                if (CanReplaceDetectedSettings(project))
                {
                    project.Owner = maintained.Owner; project.DisplayName = maintained.DisplayName; project.SupportSource = "luz-maintained";
                    project.Capabilities = maintained.Capabilities.Select(name => new CorpSecCapability(name, "native", Array.Empty<string>(), Array.Empty<string>(), "luz-maintained")).ToList();
                    project.Companions = maintained.Companions.Select(c => new CorpSecCompanion(c.Root, c.Pattern, c.Slot, c.Required, c.Validation, c.SchemaMinimum, c.SchemaMaximum, c.RevisionProperty, "luz-maintained", true)).ToList();
                    project.Adapters.Clear(); project.RecoveryIntent = project.Companions.Count == 0 ? "embedded" : "files"; project.RecoveryReviewed = true;
                }
                findings.Add(new("maintained-support", "Success", "Maintained recovery integration detected",
                    "CorpSec matched the plugin identity, continuity API and compiled contracts. These settings will be regenerated for the selected DLL.", "LUZ maintained integration", "resolved"));
            }
            else
            {
                findings.Add(new("maintained-edition", "Action", "Choose a recovery-ready build or make a normal package",
                    "LUZ recognizes this plugin, but the selected DLL does not contain its continuity client and current compiled contracts.", "LUZ maintained integration"));
            }
        }

        foreach (var candidate in PersistenceCandidates(detected.Assembly, project.SourceProject, cancellation))
        {
            bool confirmed = project.Companions.Any(c => c.Confirmed && c.Root == candidate.Root && (c.Pattern == candidate.Pattern || c.Pattern == SuggestedCompanionPattern(candidate.Pattern)));
            findings.Add(new("persistence-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Root + "|" + candidate.Pattern)))[..12], confirmed ? "Success" : "Suggestion",
                (confirmed ? "Confirmed saved file: " : "Possible saved file: ") + candidate.Pattern, candidate.Evidence + (confirmed ? " The author confirmed its recovery rule." : " Confirm this only if the mod writes data that must follow an individual save."), candidate.Source, confirmed ? "resolved" : "review", candidate.Root, candidate.Pattern));
        }

        if (project.BuildChoices.Select(c => c.Edition).Distinct(StringComparer.Ordinal).Count() > 1 && project.BuildEdition.Length == 0)
            findings.Add(new("choose-edition", "Action", "Choose which build edition to follow", "Standalone and recovery-ready builds share this plugin identity. CorpSec will not switch between them automatically.", "build discovery"));
        if (project.RecoveryIntent == "unknown")
            findings.Add(new("recovery-intent", "Action", "Tell LUZ where this mod keeps persistent state", "Choose no extra state, separate files, game-save state, custom recovery, or I don't know yet.", "author review"));
        if (project.RecoveryIntent == "files" && !project.Companions.Any(c => c.Confirmed))
            findings.Add(new("confirm-files", "Action", "Confirm at least one owned file", "Review a detected suggestion or add the mod's save/configuration rule.", "author review"));
        if (project.RecoveryIntent == "stateless" && project.Companions.Any(c => c.Confirmed))
            findings.Add(new("stateless-files-conflict", "Action", "Resolve the saved-file conflict", "This project says it has no persistent state but still owns confirmed recovery files. Remove the rules or choose separate files.", "author review"));
        if (project.RecoveryIntent == "custom" && project.Adapters.Count == 0)
            findings.Add(new("custom-adapter", "Action", "Add the recovery adapter", "Build the generated adapter template, then register its DLL and public type.", "author review"));
        if (project.RecoveryIntent == "embedded" && !continuityClient)
            findings.Add(new("embedded-client", "Action", "Add the optional LUZ recovery client", "Game-save state needs an acknowledgement path so a protected restore cannot silently continue without the mod.", "compiled DLL"));
        if (RequiresCompiledContracts(project) && !compiledContracts)
            findings.Add(new("compiled-contracts", "Action", "Compile current Nivalis contracts into the final DLL", "Static generation cannot qualify unknown native members or core save behavior.", "compiled DLL"));
        if (string.IsNullOrWhiteSpace(project.SupportContact))
            findings.Add(new("support-contact", "Action", "Enter a support contact", "Recovery-enabled packages must tell players where to report a failed restore.", "project details"));
        foreach (var adapter in project.Adapters.Where(a => !File.Exists(a.Source)))
            findings.Add(new("missing-adapter-" + adapter.Id, "Action", "Recovery adapter is missing: " + adapter.Id, adapter.Source, adapter.Provenance));

        project.Findings = findings.GroupBy(f => f.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
        bool ready = project.RecoveryReviewed && project.RecoveryIntent != "unknown" && project.Findings.All(f => f.Severity != "Action");
        Save(project); return new(ready, project.BuildEdition, project.SupportSource, project.Findings);
    }

    private static bool CanReplaceDetectedSettings(CorpSecProject project) => project.SupportSource.Length == 0 || project.SupportSource is "luz-maintained" or "existing-declaration";

    private static string SuggestedCompanionPattern(string value)
    {
        string path = value.Replace('\\', '/').TrimStart('/'); if (path.Contains("{slot}", StringComparison.Ordinal)) return path;
        string extension = Path.GetExtension(path); return extension.Length == 0 ? path.TrimEnd('/') + "/{slot}.json" : path[..^extension.Length] + "-{slot}" + extension;
    }

    private static void ImportDescriptor(string dataRoot, CorpSecProject project, Luz.Continuity.ModSupportDescriptor descriptor, string descriptorPath, CancellationToken cancellation)
    {
        if (!CanReplaceDetectedSettings(project)) return;
        project.Owner = descriptor.Owner; project.DisplayName = descriptor.DisplayName; project.SupportContact = project.SupportContact.Length == 0 ? descriptor.SupportContact : project.SupportContact;
        project.SupportSource = "existing-declaration"; project.RecoveryReviewed = true;
        project.Companions = descriptor.Companions.Select(c => new CorpSecCompanion(c.Root, c.Pattern, c.Slot, c.Required, c.Validation, c.SchemaMinimum, c.SchemaMaximum, c.RevisionProperty, "existing declaration", true)).ToList();
        string cache = FileSafety.Under(FileSafety.ResolveFolder(dataRoot), "authoring/adapters/" + project.Id); Directory.CreateDirectory(cache);
        project.Adapters = new();
        foreach (var adapter in descriptor.Adapters)
        {
            cancellation.ThrowIfCancellationRequested(); string source = FileSafety.Under(Path.GetDirectoryName(descriptorPath)!, adapter.File);
            if (!File.Exists(source)) continue;
            string target = FileSafety.Under(cache, adapter.Sha256 + "-" + Path.GetFileName(adapter.File));
            if (!File.Exists(target)) File.Copy(source, target); if (!FileSafety.Hash(target).Equals(adapter.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Imported adapter hash changed: " + adapter.Id);
            project.Adapters.Add(new(adapter.Id, target, adapter.Type, adapter.Capabilities, "existing declaration"));
        }
        project.Capabilities = descriptor.Capabilities.Select(c =>
        {
            var ordered = c.Implementations.OrderByDescending(i => i.Priority).Select(i => i.Id).ToArray();
            return new CorpSecCapability(c.Name, ordered[0], ordered.Skip(1).ToArray(), c.Dependencies, "existing declaration");
        }).ToList();
        project.RecoveryIntent = project.Adapters.Count != 0 ? "custom" : project.Companions.Count != 0 ? "files" : "embedded";
    }

    private static MaintainedSupportCatalog MaintainedSupport()
    {
        using Stream input = typeof(CorpSec).Assembly.GetManifestResourceStream("Luz.maintained-support.json") ?? throw new InvalidDataException("LUZ maintained support data is missing.");
        var catalog = JsonSerializer.Deserialize<MaintainedSupportCatalog>(input, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("LUZ maintained support data is invalid.");
        if (catalog.Schema != 1 || catalog.Modules.Length > 64) throw new InvalidDataException("Unsupported maintained support data."); return catalog;
    }

    private sealed record PersistenceCandidate(string Root, string Pattern, string Evidence, string Source);

    private static IReadOnlyList<PersistenceCandidate> PersistenceCandidates(AssemblyDefinition assembly, string sourceProject, CancellationToken cancellation)
    {
        var result = new Dictionary<string, PersistenceCandidate>(StringComparer.OrdinalIgnoreCase); int instructions = 0;
        foreach (var method in assembly.MainModule.Types.SelectMany(AllTypes).SelectMany(t => t.Methods).Where(m => m.HasBody))
        {
            var body = method.Body.Instructions;
            for (int index = 0; index < body.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested(); if (++instructions > 150_000) break;
                if (body[index].OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj) || body[index].Operand is not MethodReference call || !LooksLikeWrite(call)) continue;
                foreach (string text in body.Skip(Math.Max(0, index - 12)).Take(Math.Min(13, index + 1)).Where(i => i.OpCode.Code == Code.Ldstr).Select(i => i.Operand as string).Where(s => s != null).Cast<string>())
                    AddCandidate(result, text, method.FullName + " calls " + call.FullName, "compiled DLL");
            }
        }
        if (sourceProject.Length != 0 && Directory.Exists(sourceProject))
        {
            int files = 0; long bytes = 0;
            foreach (string file in SourceFiles(sourceProject, cancellation))
            {
                if (++files > 250) break; long length = new FileInfo(file).Length; if ((bytes += length) > 2 * 1024 * 1024) break;
                string text; try { text = File.ReadAllText(file); } catch (IOException) { continue; }
                foreach (Match match in Regex.Matches(text, "(?:WriteAllText|WriteAllBytes|FileStream|Serialize)[^;\\r\\n]{0,240}?\\\"(?<path>[^\\\"]{3,180})\\\"", RegexOptions.CultureInvariant))
                    AddCandidate(result, match.Groups["path"].Value, Path.GetRelativePath(sourceProject, file) + " contains a recognized write call", "optional source");
            }
        }
        return result.Values.Take(64).ToArray();
    }

    private static bool LooksLikeWrite(MethodReference call) =>
        (call.DeclaringType.FullName == "System.IO.File" && call.Name.StartsWith("Write", StringComparison.Ordinal)) ||
        (call.DeclaringType.FullName == "System.IO.FileStream" && call.Name == ".ctor") ||
        call.Name is "Serialize" or "SerializeAsync";

    private static void AddCandidate(Dictionary<string, PersistenceCandidate> result, string raw, string evidence, string source)
    {
        string value = raw.Trim().Replace('\\', '/'); if (value.Length > 180 || value.Contains("..", StringComparison.Ordinal) || value.Contains("://", StringComparison.Ordinal)) return;
        string extension = Path.GetExtension(value); if (extension.Length == 0 || (!value.Contains('/') && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))) return;
        string root = value.Contains("save", StringComparison.OrdinalIgnoreCase) ? "saves" : "config";
        string pattern = value.TrimStart('/'); if (Path.IsPathFullyQualified(pattern)) return;
        result.TryAdd(root + "|" + pattern, new(root, pattern, evidence, source));
    }

    private static IEnumerable<string> SourceFiles(string root, CancellationToken cancellation)
    {
        var pending = new Stack<string>(); pending.Push(Path.GetFullPath(root));
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested(); string directory = pending.Pop();
            DirectoryInfo info = new(directory); if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            IEnumerable<string> files; IEnumerable<string> folders; try { files = Directory.EnumerateFiles(directory, "*.cs").ToArray(); folders = Directory.EnumerateDirectories(directory).ToArray(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            foreach (string file in files) yield return file;
            foreach (string folder in folders) if (!DevelopmentFolders.Contains(Path.GetFileName(folder))) pending.Push(folder);
        }
    }
}

internal sealed class CorpSecAuthoringLease : IDisposable
{
    private readonly Mutex mutex; private bool held;
    private CorpSecAuthoringLease(Mutex mutex) { this.mutex = mutex; }
    public static CorpSecAuthoringLease Enter(string dataRoot)
    {
        string path = Path.GetFullPath(dataRoot).ToUpperInvariant(); string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        var lease = new CorpSecAuthoringLease(new Mutex(false, "LUZ.CorpSec." + key));
        try { lease.held = lease.mutex.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { lease.held = true; }
        if (!lease.held) { lease.mutex.Dispose(); throw new IOException("Another CorpSec operation is still updating this author workspace. Wait for it to finish, then retry."); }
        return lease;
    }
    public void Dispose() { if (held) mutex.ReleaseMutex(); held = false; mutex.Dispose(); }
}
