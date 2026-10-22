using System.IO.Compression;
using System.Text.Json;
using Luz;
using Mono.Cecil;

if (args.Length != 3) throw new ArgumentException("Supply migration archive directory, bundled payload directory and report path.");
const string PatchGuid = "hvizeu.nivalis.unofficialpatch";
const string ExpandedGuid = "hvizeu.nivalis.expanded";
const string ExpandedPatchRange = ">=0.5.4 <0.6.0";

static bool HardDependency(PluginInfo plugin, string guid, string? requirement = null) =>
    plugin.Dependencies.Any(d => d.Guid == guid && !d.Optional && (requirement == null || d.MinimumVersion == requirement));

static string Metadata(string assemblyPath, string key)
{
    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
    return assembly.CustomAttributes
        .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" &&
                    a.ConstructorArguments.Count == 2 && a.ConstructorArguments[0].Value as string == key)
        .Select(a => a.ConstructorArguments[1].Value as string ?? "")
        .SingleOrDefault() ?? "";
}

static string PluginPath(Library library, ModPackage package, string assembly)
{
    string relative = package.Files.Single(f => Path.GetFileName(f).Equals(assembly + ".dll", StringComparison.OrdinalIgnoreCase));
    return Path.Combine(library.PackageRoot(package.Id), "payload", relative.Replace('/', Path.DirectorySeparatorChar));
}

static Profile Selection(params (ModPackage Package, bool Enabled)[] packages) => new()
{
    Mods = packages.Select(p => new ModEntry { PackageId = p.Package.Id, Enabled = p.Enabled }).ToList()
};

static ModPackage WithPluginVersion(ModPackage source, string version)
{
    PluginInfo plugin = source.Plugins.Single(p => p.Guid == "hvizeu.nivalis.unofficialpatch");
    return new ModPackage
    {
        Id = Guid.NewGuid().ToString("N"), Name = source.Name, Version = version, Source = source.Source,
        SourceId = source.SourceId, Dependencies = source.Dependencies.ToList(), RequiredPlugins = source.RequiredPlugins.ToList(),
        Files = source.Files.ToList(), Plugins = [plugin with { Version = version }]
    };
}

string root = Path.Combine(Path.GetTempPath(), "LuzPackageChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    using var library = new Library(Path.Combine(root, "library"));
    var expected = new Dictionary<string, string> {
        ["hvizeu.nivalis.slowdays"] = "2.3.4", [PatchGuid] = "0.5.4", [ExpandedGuid] = "0.3.7",
        ["hvizeu.nivalis.furnishings"] = "0.4.0", ["hvizeu.nivalis.furniturelab"] = "0.2.0" };
    string[] archives = Directory.GetFiles(args[0], "*-LUZ-Migration.zip").Order().ToArray();
    if (archives.Length != expected.Count) throw new InvalidDataException("Missing or unexpected migration package count.");
    foreach (string archive in archives) library.Import(archive);
    var manifest = ModContinuity.VerifyBundle(args[1]);
    string runtime = Path.Combine(root, "runtime.zip");
    using (var zip = ZipFile.Open(runtime, ZipArchiveMode.Create))
        foreach (string name in manifest.Keys) zip.CreateEntryFromFile(Path.Combine(args[1], name), "BepInEx/plugins/LUZContinuity/" + name);
    library.Import(runtime);
    var plugins = library.State.Packages.SelectMany(p => p.Plugins).ToList();
    foreach (var pair in expected)
    {
        var plugin = plugins.Single(p => p.Guid == pair.Key);
        if (plugin.Version != pair.Value || !HardDependency(plugin, ModContinuity.PluginGuid))
            throw new InvalidDataException("Migration identity/dependency differs: " + pair.Key);
    }
    if (plugins.Count != expected.Count + 1 || !plugins.Any(p => p.Guid == ModContinuity.PluginGuid))
        throw new InvalidDataException("Unexpected, duplicate or missing plugin identity.");

    ModPackage expanded = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == ExpandedGuid));
    ModPackage patch = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == PatchGuid));
    ModPackage continuity = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == ModContinuity.PluginGuid));
    PluginInfo expandedPlugin = expanded.Plugins.Single(p => p.Guid == ExpandedGuid);
    if (!HardDependency(expandedPlugin, PatchGuid, ExpandedPatchRange) || !HardDependency(expandedPlugin, ModContinuity.PluginGuid))
        throw new InvalidDataException("Expanded Continuity plugin dependencies differ.");
    if (!expanded.RequiredPlugins.Any(d => d.Guid == PatchGuid && d.MinimumVersion == "0.5.4" && !d.Optional) ||
        !expanded.RequiredPlugins.Any(d => d.Guid == ModContinuity.PluginGuid && d.MinimumVersion.Length == 0 && !d.Optional))
        throw new InvalidDataException("Expanded LUZ required_plugins metadata differs.");
    if (expanded.Files.Any(f => Path.GetFileName(f).Equals("NivalisUnofficialPatch.dll", StringComparison.OrdinalIgnoreCase)) ||
        expanded.Files.Count(f => Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase)) != 1)
        throw new InvalidDataException("Expanded must contain only its own plugin DLL.");
    string[] retired = ["PerformanceCapture", "NivalisGreenhouse", "TrackedQuestsHUD"];
    if (expanded.Files.Concat(patch.Files).Any(f => retired.Any(r => f.Contains(r, StringComparison.OrdinalIgnoreCase))))
        throw new InvalidDataException("A retired system is present in a migration payload.");
    if (expanded.Files.Concat(patch.Files).Any(f => Path.GetExtension(f).Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(f).Equals("AGENTS.md", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException("A private development artifact is present in a migration payload.");
    foreach (var item in new[] { (Package: patch, Assembly: "NivalisUnofficialPatch"), (Package: expanded, Assembly: "NivalisExpanded") })
    {
        string assembly = PluginPath(library, item.Package, item.Assembly);
        if (Metadata(assembly, "PrivateDiagnosticsBuild") != "false") throw new InvalidDataException(item.Assembly + " is not the public build.");
        if (Metadata(assembly, "Nivalis.Continuity.Api") != "1") throw new InvalidDataException(item.Assembly + " is not a Continuity build.");
    }

    using (var support = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1], "maintained-support.json"))))
    {
        var modules = support.RootElement.GetProperty("Modules").EnumerateArray().ToArray();
        if (modules.Select(m => m.GetProperty("Owner").GetString() ?? "").Distinct(StringComparer.Ordinal).Count() != modules.Length)
            throw new InvalidDataException("Maintained descriptor ownership collides.");
        var up = modules.Single(m => m.GetProperty("Guid").GetString() == PatchGuid);
        var ex = modules.Single(m => m.GetProperty("Guid").GetString() == ExpandedGuid);
        string[] upCapabilities = up.GetProperty("Capabilities").EnumerateArray().Select(c => c.GetString() ?? "").ToArray();
        string[] companions = up.GetProperty("Companions").EnumerateArray().Select(c => c.GetProperty("Pattern").GetString() ?? "").ToArray();
        string[] managerOwners = modules.Where(m => m.GetProperty("Companions").EnumerateArray().Any(c => c.GetProperty("Pattern").GetString() == "NivalisUnofficialPatch/ManagerPolicies/{slot}.json"))
            .Select(m => m.GetProperty("Owner").GetString() ?? "").ToArray();
        if (!upCapabilities.Contains("inventory-transactions") ||
            !companions.Contains("NivalisUnofficialPatch/ManagerPolicies/{slot}.json") ||
            ex.GetProperty("Companions").GetArrayLength() != 0 || !managerOwners.SequenceEqual(new[] { "unofficialpatch" }))
            throw new InvalidDataException("Expanded changed Unofficial Patch companion or transaction ownership.");
    }
    using (var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1], "catalog.json"))))
    {
        var capabilities = catalog.RootElement.GetProperty("Builds")[0].GetProperty("Capabilities").EnumerateArray().ToArray();
        var expandedRule = capabilities.Single(c => c.GetProperty("Owner").GetString() == "expanded" && c.GetProperty("Name").GetString() == "module");
        string[] dependencies = expandedRule.GetProperty("Dependencies").EnumerateArray().Select(d => d.GetString() ?? "").Order().ToArray();
        if (!dependencies.SequenceEqual(new[] { "core/save-lifecycle", "unofficialpatch/inventory-transactions", "unofficialpatch/module" }))
            throw new InvalidDataException("Expanded capability dependencies differ.");
    }

    var issues = ModPlanner.Check(library.Active, library.State.Packages);
    if (issues.Any(i => i.Severity == "Error")) throw new InvalidDataException(string.Join("; ", issues.Select(i => i.Message)));
    if (!ModPlanner.Check(Selection((expanded, true), (continuity, true)), [expanded, continuity]).Any(i => i.Severity == "Error") ||
        !ModPlanner.Check(Selection((expanded, true), (patch, false), (continuity, true)), [expanded, patch, continuity]).Any(i => i.Severity == "Error"))
        throw new InvalidDataException("Missing or disabled Unofficial Patch is not rejected.");
    foreach (string version in new[] { "0.4.6", "0.4.7", "0.5.0", "0.6.0" })
    {
        ModPackage wrong = WithPluginVersion(patch, version);
        if (!ModPlanner.Check(Selection((expanded, true), (wrong, true), (continuity, true)), [expanded, wrong, continuity]).Any(i => i.Severity == "Error"))
            throw new InvalidDataException("Wrong Unofficial Patch edition is not rejected: " + version);
    }
    var duplicate = new ModPackage { Id = Guid.NewGuid().ToString("N"), Name = "Duplicate Expanded", Plugins = [expandedPlugin], Files = ["plugins/duplicate/NivalisExpanded.dll"] };
    if (!ModPlanner.Check(Selection((expanded, true), (duplicate, true), (patch, true), (continuity, true)), [expanded, duplicate, patch, continuity]).Any(i => i.Message.Contains("Duplicate plugin ID", StringComparison.Ordinal)))
        throw new InvalidDataException("Duplicate Expanded identity is not rejected.");

    if (library.State.Packages.Any(p => p.Plugins.Any(i => i.Guid == "hvizeu.nivalis.citylife")))
        throw new InvalidDataException("City Life Continuity must remain withheld until its complete schema 2 Humanoid payload passes native acceptance.");
    var report = new { ImportedPackages = library.State.Packages.Count, Plugins = plugins.Select(p => new { p.Guid, p.Version }), CityLifePackage = "Withheld pending complete schema 2 Humanoid payload qualification.", Issues = issues, Scope = "Disposable import library only; no installation, deployment or game execution." };
    File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("PASS six complete packages imported with exact plugin identities, dependency ranges, public editions and retained ownership; unqualified City Life Continuity remains absent.");
}
finally { FileSafety.DeleteOwned(Path.GetTempPath(), root); }
