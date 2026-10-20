using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

namespace Luz;

public enum CompatibilityStatus { CheckedContractsPassed, Incompatible, Unverified }
public sealed record CompatibilityFinding(string Code, string Severity, string Plugin, string Expected, string Observed, bool CanOverride);
public sealed record CompatibilityIdentity(string File, string Identity, string Sha256);
public sealed record CompatibilityReport(int Schema, string Action, string Fingerprint, CompatibilityStatus Status, IReadOnlyList<CompatibilityFinding> Findings, IReadOnlyList<CompatibilityIdentity>? Identities = null)
{
    public string Describe()
    {
        string summary = Status switch
        {
            CompatibilityStatus.CheckedContractsPassed => "LUZ found no problem in the contracts it can inspect.",
            CompatibilityStatus.Unverified => "LUZ could not verify every enabled mod for this exact setup.",
            _ => "LUZ found a compatibility problem in this exact setup."
        };
        if (Findings.Count == 0) return summary;
        return summary + "\n\nTechnical details:\n" + string.Join("\n\n", Findings.Select(f => $"{f.Severity}: {f.Plugin}\nCode: {f.Code}\nNeeded: {f.Expected}\nFound: {f.Observed}"));
    }
}
public sealed class CompatibilityAcknowledgment
{
    internal readonly string Fingerprint, Action;
    private int consumed;
    internal CompatibilityAcknowledgment(CompatibilityReport report) { Fingerprint = report.Fingerprint; Action = report.Action; }
    internal void Consume(CompatibilityReport report)
    {
        if (report.Fingerprint != Fingerprint || report.Action != Action || Interlocked.Exchange(ref consumed, 1) != 0)
            throw new InvalidOperationException("Compatibility acknowledgment expired or was already used. Review the current report.");
    }
}

public static class Compatibility
{
    private const long MaxAssemblyBytes = 128 * 1024 * 1024;
    public static CompatibilityAcknowledgment Acknowledge(CompatibilityReport report)
    {
        ValidateReport(report);
        if (report.Findings.Any(f => !f.CanOverride && f.Severity == "Error")) throw new InvalidOperationException("This report contains non-overrideable failures.");
        return new(report);
    }
    public static void Authorize(CompatibilityReport report, CompatibilityAcknowledgment? acknowledgment)
    {
        ValidateReport(report);
        if (report.Findings.Any(f => !f.CanOverride && f.Severity == "Error")) throw new InvalidOperationException(report.Describe());
        if (acknowledgment != null) acknowledgment.Consume(report);
        else if (report.Status != CompatibilityStatus.CheckedContractsPassed) throw new InvalidOperationException(report.Describe());
    }
    private static void ValidateReport(CompatibilityReport report)
    {
        if(report.Schema!=1||report.Findings==null||report.Action is not ("Apply" or "Play")||string.IsNullOrWhiteSpace(report.Fingerprint))throw new InvalidDataException("Unrecognized compatibility report; inspect current inputs again.");
    }
    // The same precedence as Deployment.Apply: live config for its owning profile,
    // profile config otherwise, then defaults only where no setting file exists.
    public static SortedDictionary<string, string> EffectiveFiles(Library library, bool installed)
    {
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string game = library.State.GameFolder;
        void Tree(string folder, string prefix)
        {
            if (!Directory.Exists(folder)) return;
            FileSafety.NoLinks(folder);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            { FileSafety.NoLinks(file);string key=prefix + Path.GetRelativePath(folder,file).Replace('\\','/');if(!files.TryAdd(key,file))throw new InvalidDataException("Ambiguous effective file ownership: "+key); }
        }
        if (installed)
            foreach (string area in new[] { "plugins", "patchers", "config" }) Tree(FileSafety.PortableDestination(game, "BepInEx/" + area), area + "/");
        else
        {
            Tree(library.State.AppliedProfileId == library.Active.Id ? FileSafety.PortableDestination(game, "BepInEx/config") : library.ConfigRoot(library.Active.Id), "config/");
            foreach (var entry in library.Active.Mods.Where(e => e.Enabled))
            foreach (string rel in library.State.Packages.Single(p => p.Id == entry.PackageId).Files)
            {
                if (!new[] { "plugins/", "patchers/", "config/" }.Any(p => rel.StartsWith(p, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Unsupported deployment destination: " + rel);
                if (!rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase) || !files.ContainsKey(rel))
                    files[rel] = FileSafety.Under(library.PackageRoot(entry.PackageId), "payload/" + rel);
            }
        }
        if (files.Keys.Count(k=>!ArchivedRecoveryInput(k)) > 20000) throw new InvalidDataException("Compatibility input exceeds 20,000 relevant files.");
        return files;
    }
    public static CompatibilityReport Inspect(Library library, bool installed = false, CancellationToken cancellation = default)
        => CompatibilityWorker.Run(new(library.State.GameFolder, library.Fingerprint(library.Active), installed, new SortedDictionary<string,string>(EffectiveFiles(library, installed).Where(p=>!ArchivedRecoveryInput(p.Key)).ToDictionary(p=>p.Key,p=>p.Value),StringComparer.OrdinalIgnoreCase)), cancellation);
    public static CompatibilityReport InspectSnapshot(CompatibilitySnapshot snapshot, CancellationToken cancellation = default)
    {
        GameFiles.Validate(snapshot.Game);
        var watch = Stopwatch.StartNew();
        void Check() { cancellation.ThrowIfCancellationRequested(); if (watch.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Compatibility inspection exceeded 60 seconds."); }
        var selected = snapshot.Selected;
        if (selected.Count > 20000) throw new InvalidDataException("Too many compatibility inputs.");
        var findings = new List<CompatibilityFinding>();
        string game = snapshot.Game, core = FileSafety.PortableDestination(game, "BepInEx/core"), interop = FileSafety.PortableDestination(game, "BepInEx/interop");
        var inputs = new SortedDictionary<string, string>(selected.Where(p=>!ArchivedRecoveryInput(p.Key)).ToDictionary(p=>p.Key,p=>p.Value), StringComparer.OrdinalIgnoreCase) { ["game/GameAssembly.dll"] = Path.Combine(game, "GameAssembly.dll") };
        string nativeMetadata=Path.Combine(game,"Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat");
        if(File.Exists(nativeMetadata))inputs["game/global-metadata.dat"]=nativeMetadata;
        foreach (var (name, folder) in new[] { ("loader", core), ("bindings", interop), ("unity", FileSafety.PortableDestination(game, "BepInEx/unity-libs")) })
        {
            if (!Directory.Exists(folder)) continue;
            FileSafety.NoLinks(folder);
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f) == "assembly-hash.txt"))
            { FileSafety.NoLinks(file); inputs[name + "/" + Path.GetFileName(file)] = file; }
        }
        if (File.Exists(Path.Combine(game, "BepInEx/DeobfuscationMap.csv.gz"))) inputs["loader/DeobfuscationMap.csv.gz"] = Path.Combine(game, "BepInEx/DeobfuscationMap.csv.gz");
        if (inputs.Count > 22000 || inputs.Where(p => p.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Sum(p => new FileInfo(p.Value).Length) > 512L * 1024 * 1024)
            throw new InvalidDataException("Metadata input exceeds its aggregate limit.");
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var hashes=new Dictionary<string,string>();
        foreach(var recovery in inputs.Where(p=>p.Key.StartsWith("config/NivalisUnofficialPatch/Recovery/",StringComparison.OrdinalIgnoreCase)&&p.Key.EndsWith("/journal.json",StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var generation=NivalisRecovery.RecoveryFiles.Read(recovery.Value);
            if(generation.State=="Complete"&&!string.IsNullOrEmpty(generation.SaveDirectory))
            {
                string native=NivalisRecovery.RecoveryFiles.Under(generation.SaveDirectory,generation.PublishedSlot+".sav");
                if(File.Exists(native))inputs["recovery-native/"+generation.Id+"/"+generation.PublishedSlot]=native;
            }
        }
        foreach (var input in inputs) { Check();hashes[input.Key]=FileSafety.Hash(input.Value);fingerprint.AppendData(Encoding.UTF8.GetBytes(input.Key + "\0" + hashes[input.Key] + "\n")); }
        string nativeHash = hashes["game/GameAssembly.dll"];
        string provenance = BindingProvenance.Verify(game, Check, selected.GetValueOrDefault("config/BepInEx.cfg"));
        if (provenance.Length != 0 && selected.Keys.Any(k => k.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))) findings.Add(new("BindingsUnverified", "Error", "BepInEx bindings", "Current recognized generation fingerprint", provenance, true));
        var recoveries=inputs.Where(p=>p.Key.StartsWith("config/NivalisUnofficialPatch/Recovery/",StringComparison.OrdinalIgnoreCase)&&p.Key.EndsWith("/journal.json",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(recoveries.Length>64)throw new InvalidDataException("Too many recovery generations; inspection stopped.");
        foreach (var recovery in recoveries)
        {
            var generation = NivalisRecovery.RecoveryFiles.Read(recovery.Value);
            string folder=Path.GetDirectoryName(recovery.Value)!,config=Path.GetFullPath(Path.Combine(folder,"../../.."));
            string saves=generation.SaveDirectory;
            if(string.IsNullOrEmpty(saves)&&OperatingSystem.IsWindows())saves=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"../LocalLow/Ion Lands/Nivalis Nights"));
            var publication=NivalisRecovery.RecoveryPublication.Inspect(saves,config,folder);
            if (publication.State==NivalisRecovery.RecoveryPublicationState.Interrupted)
                findings.Add(new("PendingSaveRecovery", "Error", "Save recovery", "Restore or verify the interrupted generation in Maintenance", generation.SourceSlot + ": " + publication.Detail, false));
            if(generation.State!="Abandoned" && generation.Companions.Values.Contains("city") && !selected.Values.Any(p=>Path.GetFileName(p).Equals("NivalisCityLife.dll",StringComparison.OrdinalIgnoreCase)))findings.Add(new("MissingRecoveryOwner","Error","City Life","Required companion owner in this deployment",generation.SourceSlot,false));
            if(generation.State!="Abandoned" && !selected.Values.Any(p=>Path.GetFileName(p).Equals("NivalisUnofficialPatch.dll",StringComparison.OrdinalIgnoreCase)))findings.Add(new("MissingRecoveryOwner","Error","Unofficial Patch","Required recovery owner in this deployment",generation.SourceSlot,false));
        }
        using var resolver = new ExactResolver();
        var modules = new List<(string Name, AssemblyDefinition Assembly)>();
        var identities=new List<CompatibilityIdentity>();
        try
        {
            foreach (var input in inputs.Where(p => p.Value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !p.Key.StartsWith("unity/")))
            {
                Check();
                if (new FileInfo(input.Value).Length > MaxAssemblyBytes) { findings.Add(new("AssemblyTooLarge", "Error", input.Key, "Assembly at most 128 MiB", "Input exceeds parser limit", false)); continue; }
                AssemblyDefinition assembly;
                try { assembly = AssemblyDefinition.ReadAssembly(input.Value, new ReaderParameters { AssemblyResolver = resolver, InMemory = false }); }
                catch (BadImageFormatException)
                { if (input.Key.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || input.Key.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase)) findings.Add(new("NativeContractUnverified", "Error", input.Key, "Inspectable managed contract", "Native or malformed DLL cannot be verified", true)); continue; }
                modules.Add((input.Key, assembly));
                identities.Add(new(input.Key,assembly.Name.FullName,hashes[input.Key]));
                if (!resolver.Add(assembly)) findings.Add(new("AmbiguousAssembly", "Error", input.Key, "One effective assembly identity", assembly.Name.FullName, false));
            }
            foreach (var module in modules.Where(m => m.Name.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase)))
            {
                Check();
                var attributes=module.Assembly.CustomAttributes.Where(a=>a.AttributeType.FullName=="System.Reflection.AssemblyMetadataAttribute").ToArray();
                var metadata=new Dictionary<string,string>();
                foreach(var attribute in attributes)
                    if(attribute.ConstructorArguments.Count!=2 || attribute.ConstructorArguments[0].Value is not string key || attribute.ConstructorArguments[1].Value is not string value || !metadata.TryAdd(key,value))findings.Add(new("MalformedRequirements","Error",module.Name,"Unique versioned metadata keys","Malformed or duplicate assembly metadata",false));
                bool continuityQualified = false;
                if (metadata.GetValueOrDefault("Nivalis.Continuity.Api") == "1")
                {
                    if (!selected.TryGetValue("plugins/LUZContinuity/catalog.json", out var adapterPath)) findings.Add(new("ContinuityBootstrapMissing", "Error", module.Name, "Bundled LUZ continuity runtime and catalog", "Install and configure the LUZ feature", false));
                    else
                    {
                        try
                        {
                            var adapter = Luz.Continuity.SafeFiles.Read<Luz.Continuity.AdapterCatalog>(adapterPath);
                            var target = new Luz.Continuity.BuildIdentity(nativeHash, hashes.GetValueOrDefault("game/global-metadata.dat") ?? "", hashes.GetValueOrDefault("loader/BepInEx.Unity.IL2CPP.dll") ?? "");
                            continuityQualified = adapter.Authorizes(target, module.Assembly.Name.Name, hashes[module.Name], metadata.GetValueOrDefault("Nivalis.NativeBuild") ?? "", metadata.GetValueOrDefault("Nivalis.MetadataBuild") ?? "");
                            if (!continuityQualified) findings.Add(new("ContinuityUnqualifiedConsumer", "Error", module.Name, "Exact current contract or reviewed grant for this frozen payload", "Consumer has not been qualified for this adapter", false));
                        }
                        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException) { findings.Add(new("ContinuityCatalogInvalid", "Error", module.Name, "Verified current continuity catalog", error.Message, false)); }
                    }
                }
                if (metadata.TryGetValue("Nivalis.NativeBuild", out string? supported) && !supported.Equals(nativeHash,StringComparison.OrdinalIgnoreCase) && !continuityQualified)
                    findings.Add(new("UnsupportedNativeBuild", "Error", module.Name, supported, nativeHash, true));
                if(metadata.TryGetValue("Nivalis.MetadataBuild",out var expectedMetadata)&&(!hashes.TryGetValue("game/global-metadata.dat",out var metadataHash)||!metadataHash.Equals(expectedMetadata,StringComparison.OrdinalIgnoreCase))&&!continuityQualified)findings.Add(new("UnsupportedNativeBuild","Error",module.Name,expectedMetadata,hashes.GetValueOrDefault("game/global-metadata.dat")??"IL2CPP metadata missing",true));
                if (metadata.TryGetValue("Nivalis.Requirements.Schema", out string? schema) && schema is not ("1" or "2"))
                    findings.Add(new("RequirementsUnverified", "Error", module.Name, "Requirements schema 1 or 2", schema, true));
                if(schema=="2")
                {
                    var resource=module.Assembly.MainModule.Resources.OfType<EmbeddedResource>().SingleOrDefault(r=>r.Name=="Nivalis.Contracts.v2.json");
                    if(resource==null)findings.Add(new("MalformedRequirements","Error",module.Name,"Embedded versioned compiled contracts","Missing contracts resource",false));
                    else
                    {
                        using var stream=resource.GetResourceStream();if(stream.Length>4*1024*1024)throw new InvalidDataException("Requirements resource exceeds its size limit.");
                        using var document=System.Text.Json.JsonDocument.Parse(stream);
                        if(document.RootElement.GetProperty("Schema").GetInt32()!=2)throw new InvalidDataException("Unsupported compiled contracts schema.");
                        var features=document.RootElement.GetProperty("Features").EnumerateArray().ToArray();
                        if(features.Length>128||features.Select(f=>f.GetProperty("Name").GetString()).Distinct().Count()!=features.Length)throw new InvalidDataException("Ambiguous feature contracts.");
                        foreach(var feature in features)
                        foreach(var member in feature.GetProperty("Members").EnumerateArray())
                        {
                            Check();string assemblyName=member.GetProperty("Assembly").GetString()!,typeName=member.GetProperty("Type").GetString()!,name=member.GetProperty("Name").GetString()!,kind=member.GetProperty("Kind").GetString()!;
                            try
                            {
                                var dependency=resolver.Resolve(new(assemblyName,new Version(0,0)));
                                var type=dependency.MainModule.GetType(typeName);
                                bool resolved=type!=null;
                                if(resolved&&kind=="field")resolved=type!.Fields.Any(f=>f.Name==name&&f.IsStatic==member.GetProperty("Static").GetBoolean()&&MetadataContracts.DeclarationName(f.FieldType)==member.GetProperty("Result").GetString());
                                else if(resolved&&kind=="method")resolved=type!.Methods.Any(m=>m.Name==name&&m.IsStatic==member.GetProperty("Static").GetBoolean()&&m.GenericParameters.Count==member.GetProperty("Arity").GetInt32()&&MetadataContracts.DeclarationName(m.ReturnType)==member.GetProperty("Result").GetString()&&m.Parameters.Select(p=>MetadataContracts.DeclarationName(p.ParameterType)).SequenceEqual(member.GetProperty("Parameters").EnumerateArray().Select(p=>p.GetString())));
                                else if(kind!="type"&&kind!="field"&&kind!="method")throw new InvalidDataException("Unknown member contract kind.");
                                if(!resolved)Missing(feature.GetProperty("Name").GetString()+": "+assemblyName+":"+typeName+"."+name);
                            }
                            catch(AssemblyResolutionException){Missing(assemblyName+":"+typeName+"."+name);}
                        }
                    }
                }
                if(metadata.TryGetValue("Nivalis.BindingRequirements",out var requirements))
                    foreach(string contract in requirements.Split(';').Where(c=>c!="compiled-member-references"))
                    {
                        int colon=contract.IndexOf(':');int open=contract.IndexOf('(');int dot=open<0?-1:contract.LastIndexOf('.',open);
                        if(colon<0||open<0||dot<colon||!contract.EndsWith(')')){findings.Add(new("MalformedRequirements","Error",module.Name,"assembly:type.method(parameters)",contract,false));continue;}
                        try
                        {
                            var assembly=resolver.Resolve(new AssemblyNameReference(contract[..colon],new Version(0,0)));
                            var type=assembly.MainModule.GetType(contract[(colon+1)..dot]);string name=contract[(dot+1)..open];string parameters=contract[(open+1)..^1];
                            if(type==null||!type.Methods.Any(m=>m.Name==name&&string.Join(',',m.Parameters.Select(p=>p.ParameterType.FullName))==parameters))Missing(contract);
                        }
                        catch(AssemblyResolutionException){Missing(contract);}
                    }
                if(metadata.TryGetValue("Nivalis.Capabilities",out var capabilities))foreach(string capability in capabilities.Split(';'))
                    if(capability=="shared-settings-v1" && module.Assembly.MainModule.GetType("NivalisUnofficialPatch.SettingsIntegrationV1")?.Methods.Any(m=>m.Name=="CanHost"&&m.IsPublic&&m.IsStatic&&m.ReturnType.FullName=="System.Boolean"&&string.Join(',',m.Parameters.Select(p=>p.ParameterType.FullName))=="System.String,System.String")!=true || capability=="table-recovery-v1" && module.Assembly.MainModule.GetType("NivalisFurnishings.RecoveryTablesV1")?.Methods.Count(m=>m.IsPublic&&m.IsStatic&&m.Name is "Begin" or "End")!=2 || capability=="city-save-v1" && module.Assembly.MainModule.GetType("NivalisCityLife.SaveIntegrationV1")?.Methods.Any(m=>m.Name=="CanMigrate"&&m.IsPublic&&m.IsStatic&&m.ReturnType.FullName=="System.Boolean"&&m.Parameters.Count==0)!=true)Missing("Declared capability "+capability);
                foreach (var type in module.Assembly.MainModule.GetTypeReferences())
                {
                    Check(); if (!resolver.IsGameScope(type)) continue;
                    try { if (type.Resolve() == null) Missing(type.FullName); } catch (AssemblyResolutionException ex) { Missing(ex.Message); }
                }
                foreach (var member in module.Assembly.MainModule.GetMemberReferences())
                {
                    Check(); if (!resolver.IsGameScope(member.DeclaringType)) continue;
                    try { if (!MetadataContracts.Resolves(member,Check)) Missing(member.FullName); }
                    catch (AssemblyResolutionException ex) { Missing(member.FullName + ": " + ex.Message); }
                }
                if (!metadata.ContainsKey("Nivalis.Requirements.Schema"))
                    findings.Add(new("DynamicContractsUnverified", "Warning", module.Name, "Declared native/reflection capabilities", "No maintained compatibility metadata; only compiled references checked", true));
                void Missing(string member) => findings.Add(new("MissingMember", "Error", module.Name, member, "Not resolved in effective dependencies/current bindings", true));
            }
        }
        finally { foreach (var module in modules) module.Assembly.Dispose(); }
        foreach (var file in selected.Where(p => p.Key.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))) findings.Add(new("NativeContractUnverified", "Error", file.Key, "Declared native contract", "ASI compatibility cannot be inferred", true));
        findings = findings.Distinct().ToList();
        foreach(var input in inputs){Check();string actual=FileSafety.Hash(input.Value);if(actual!=hashes[input.Key])findings.Add(new("ChangedInspectionInput","Error",input.Key,hashes[input.Key],actual,false));fingerprint.AppendData(Encoding.UTF8.GetBytes("recheck/"+input.Key+"\0"+actual+"\n"));}
        var status = findings.Any(f => f.Code is "MissingMember" or "UnsupportedNativeBuild" or "AmbiguousAssembly" or "AssemblyTooLarge" or "MalformedRequirements" or "PendingSaveRecovery" or "MissingRecoveryOwner" or "ChangedInspectionInput") ? CompatibilityStatus.Incompatible
            : findings.Any(f => f.Code.EndsWith("Unverified", StringComparison.Ordinal)) ? CompatibilityStatus.Unverified : CompatibilityStatus.CheckedContractsPassed;
        fingerprint.AppendData(Encoding.UTF8.GetBytes(snapshot.ProfileFingerprint + "\n" + string.Join("\n", findings)));
        return new(1, snapshot.Installed ? "Play" : "Apply", Convert.ToHexString(fingerprint.GetHashAndReset()), status, findings.AsReadOnly(),identities.AsReadOnly());
    }
    internal static bool GameScope(TypeReference type)
    {
        string? name=type.Scope switch{AssemblyNameReference assembly=>assembly.Name,ModuleDefinition module=>module.Assembly?.Name.Name,_=>null};
        return name!=null&&!(name.StartsWith("System",StringComparison.Ordinal)||name.StartsWith("Microsoft.Win32.",StringComparison.Ordinal)||name is "mscorlib" or "netstandard" or "Microsoft.CSharp");
    }
    private static bool ArchivedRecoveryInput(string key)
    {
        if (key.StartsWith("config/LUZContinuity/", StringComparison.OrdinalIgnoreCase)) return true;
        const string prefix="config/NivalisUnofficialPatch/Recovery/";
        if(!key.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return false;
        string relative=key[prefix.Length..];
        return relative.Contains("/source/",StringComparison.OrdinalIgnoreCase)||relative.EndsWith("/source.sav",StringComparison.OrdinalIgnoreCase)||relative.Contains("/checkpoint-",StringComparison.OrdinalIgnoreCase)||relative.Contains("/pending/before/",StringComparison.OrdinalIgnoreCase);
    }
    private sealed class ExactResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, AssemblyDefinition> assemblies = new(StringComparer.OrdinalIgnoreCase);
        public bool Add(AssemblyDefinition value) => assemblies.TryAdd(value.Name.Name, value);
        public bool IsGameScope(TypeReference type) => GameScope(type);
        public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new());
        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters) => assemblies.TryGetValue(name.Name, out var value) ? value : throw new AssemblyResolutionException(name);
        public void Dispose() { }
    }
}

public static class CompatibilityDecisions
{
    private const int MaximumRemembered = 32;
    public static bool IsRemembered(Library library, CompatibilityReport report)
        => report.Status != CompatibilityStatus.CheckedContractsPassed
            && library.State.CompatibilityDecisions.Any(d => d.Action == report.Action && d.Fingerprint.Equals(report.Fingerprint, StringComparison.OrdinalIgnoreCase));

    public static void Remember(Library library, CompatibilityReport report)
    {
        if (report.Status == CompatibilityStatus.CheckedContractsPassed) return;
        _ = Compatibility.Acknowledge(report);
        var previous = library.State.CompatibilityDecisions.ToList();
        try
        {
            library.State.CompatibilityDecisions.RemoveAll(d => d.Action == report.Action && d.Fingerprint.Equals(report.Fingerprint, StringComparison.OrdinalIgnoreCase));
            library.State.CompatibilityDecisions.Add(new() { Action = report.Action, Fingerprint = report.Fingerprint, ReviewedUtc = DateTime.UtcNow });
            library.State.CompatibilityDecisions = library.State.CompatibilityDecisions.OrderByDescending(d => d.ReviewedUtc).Take(MaximumRemembered).ToList();
            library.Save();
        }
        catch
        {
            library.State.CompatibilityDecisions = previous;
            throw;
        }
    }
}
