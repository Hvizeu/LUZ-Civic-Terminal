using System.IO.Compression;
using System.Text.Json;
using Luz;
using Mono.Cecil;

if (args.Length != 3) throw new ArgumentException("Supply Deployments, bundled Continuity payload and report path.");
const string PatchGuid = "hvizeu.nivalis.unofficialpatch";
const string ExpandedGuid = "hvizeu.nivalis.expanded";
const string ExpandedPatchRange = ">=0.4.12 <0.5.0";

static bool Dependency(PluginInfo plugin, string guid, bool optional, string? requirement = null) =>
    plugin.Dependencies.Any(d => d.Guid == guid && d.Optional == optional &&
        (requirement == null || d.MinimumVersion == requirement));

static Dictionary<string, string> AssemblyMetadata(string assemblyPath)
{
    using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
    return assembly.CustomAttributes
        .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2)
        .ToDictionary(a => a.ConstructorArguments[0].Value as string ?? "",
                      a => a.ConstructorArguments[1].Value as string ?? "", StringComparer.Ordinal);
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
    PluginInfo plugin = source.Plugins.Single(p => p.Guid == PatchGuid);
    return new ModPackage
    {
        Id = Guid.NewGuid().ToString("N"), Name = source.Name, Version = version, Source = source.Source,
        SourceId = source.SourceId, Dependencies = source.Dependencies.ToList(), RequiredPlugins = source.RequiredPlugins.ToList(),
        Files = source.Files.ToList(), Plugins = [plugin with { Version = version }]
    };
}

var expected = new[]
{
    new { Relative = "Nivalis Better Time/Nexus/NivalisBetterTime-2.2.15-Nexus.zip", Guid = "hvizeu.nivalis.slowdays", Version = "2.2.15", Assembly = "NivalisSlowDays" },
    new { Relative = "Nivalis Unofficial Patch/Nexus/NivalisUnofficialPatch-0.4.12-Nexus.zip", Guid = PatchGuid, Version = "0.4.12", Assembly = "NivalisUnofficialPatch" },
    new { Relative = "Nivalis Expanded/Nexus/NivalisExpanded-0.2.21-Nexus.zip", Guid = ExpandedGuid, Version = "0.2.21", Assembly = "NivalisExpanded" },
    new { Relative = "Nivalis City Life/Nexus/NivalisCityLife-1.1.32-Nexus.zip", Guid = "hvizeu.nivalis.citylife", Version = "1.1.32", Assembly = "NivalisCityLife" },
    new { Relative = "Object Framework and Studio/Nexus/Nivalis-Object-Framework-Runtime-0.3.15.zip", Guid = "hvizeu.nivalis.furnishings", Version = "0.3.15", Assembly = "NivalisFurnishings" },
    new { Relative = "Object Framework and Studio/Nexus/Nivalis-Furniture-Lab-0.1.10.zip", Guid = "hvizeu.nivalis.furniturelab", Version = "0.1.10", Assembly = "NivalisFurnitureLab" },
};

string root = Path.Combine(Path.GetTempPath(), "LuzPackageChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    using var library = new Library(Path.Combine(root, "library"));
    foreach (var item in expected)
    {
        string archive = Path.Combine(args[0], item.Relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(archive)) throw new FileNotFoundException("Universal package is missing.", archive);
        library.Import(archive);
    }

    var bundleManifest = ModContinuity.VerifyBundle(args[1]);
    string runtimeArchive = Path.Combine(root, "runtime.zip");
    using (var zip = ZipFile.Open(runtimeArchive, ZipArchiveMode.Create))
        foreach (string name in bundleManifest.Keys)
            zip.CreateEntryFromFile(Path.Combine(args[1], name), "BepInEx/plugins/LUZContinuity/" + name);
    library.Import(runtimeArchive);

    var plugins = library.State.Packages.SelectMany(p => p.Plugins).ToList();
    foreach (var item in expected)
    {
        PluginInfo plugin = plugins.Single(p => p.Guid == item.Guid);
        if (plugin.Version != item.Version || !Dependency(plugin, ModContinuity.PluginGuid, optional: true))
            throw new InvalidDataException("Universal identity or optional Continuity dependency differs: " + item.Guid +
                $" expected={item.Version} observed={plugin.Version} dependencies=" +
                string.Join(",", plugin.Dependencies.Select(d => $"{d.Guid}:{d.MinimumVersion}:optional={d.Optional}")));

        ModPackage package = library.State.Packages.Single(p => p.Plugins.Any(candidate => candidate.Guid == item.Guid));
        if (package.RequiredPlugins.Any(d => d.Guid == ModContinuity.PluginGuid))
            throw new InvalidDataException("Universal package must not require the optional Continuity host: " + item.Guid);
        string assemblyPath = PluginPath(library, package, item.Assembly);
        var metadata = AssemblyMetadata(assemblyPath);
        if (metadata.GetValueOrDefault("Nivalis.Continuity.Api") != "1")
            throw new InvalidDataException("Embedded Continuity API metadata is missing: " + item.Assembly);
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        if (assembly.MainModule.AssemblyReferences.Any(r => r.Name.Equals("Luz.Continuity.Runtime", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Universal plugin has a hard runtime assembly reference: " + item.Assembly);
    }
    if (plugins.Count != expected.Length + 1 || !plugins.Any(p => p.Guid == ModContinuity.PluginGuid))
        throw new InvalidDataException("Unexpected, duplicate or missing plugin identity.");

    ModPackage expanded = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == ExpandedGuid));
    ModPackage patch = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == PatchGuid));
    ModPackage continuity = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == ModContinuity.PluginGuid));
    PluginInfo expandedPlugin = expanded.Plugins.Single(p => p.Guid == ExpandedGuid);
    if (!Dependency(expandedPlugin, PatchGuid, optional: false, ExpandedPatchRange) || !Dependency(expandedPlugin, ModContinuity.PluginGuid, optional: true))
        throw new InvalidDataException("Expanded universal plugin dependencies differ.");
    if (expanded.RequiredPlugins.Count != 0)
        throw new InvalidDataException("Expanded archive must rely on compiled plugin metadata only.");
    if (expanded.Files.Any(f => Path.GetFileName(f).Equals("NivalisUnofficialPatch.dll", StringComparison.OrdinalIgnoreCase)) ||
        expanded.Files.Count(f => Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase)) != 1)
        throw new InvalidDataException("Expanded must contain only its own plugin DLL.");

    foreach (var item in new[] { (Package: patch, Assembly: "NivalisUnofficialPatch"), (Package: expanded, Assembly: "NivalisExpanded") })
    {
        string assembly = PluginPath(library, item.Package, item.Assembly);
        if (AssemblyMetadata(assembly).GetValueOrDefault("PrivateDiagnosticsBuild") != "false")
            throw new InvalidDataException(item.Assembly + " is not the public build.");
    }

    var enabled = expected.Select(item =>
        (library.State.Packages.Single(p => p.Plugins.Any(plugin => plugin.Guid == item.Guid)), true)).ToList();
    var ordinaryIssues = ModPlanner.Check(Selection(enabled.ToArray()), library.State.Packages);
    if (ordinaryIssues.Any(i => i.Severity == "Error"))
        throw new InvalidDataException("Universal packages do not admit without LUZ: " + string.Join("; ", ordinaryIssues.Select(i => i.Message)));
    enabled.Add((continuity, true));
    var protectedIssues = ModPlanner.Check(Selection(enabled.ToArray()), library.State.Packages);
    if (protectedIssues.Any(i => i.Severity == "Error"))
        throw new InvalidDataException("Universal packages do not admit with LUZ: " + string.Join("; ", protectedIssues.Select(i => i.Message)));

    if (!ModPlanner.Check(Selection((expanded, true), (continuity, true)), [expanded, continuity]).Any(i => i.Severity == "Error") ||
        !ModPlanner.Check(Selection((expanded, true), (patch, false), (continuity, true)), [expanded, patch, continuity]).Any(i => i.Severity == "Error"))
        throw new InvalidDataException("Missing or disabled Unofficial Patch is not rejected.");
    foreach (string version in new[] { "0.4.6", "0.4.11", "0.5.0", "0.6.0" })
    {
        ModPackage wrong = WithPluginVersion(patch, version);
        if (!ModPlanner.Check(Selection((expanded, true), (wrong, true), (continuity, true)), [expanded, wrong, continuity]).Any(i => i.Severity == "Error"))
            throw new InvalidDataException("Wrong Unofficial Patch version is not rejected: " + version);
    }
    var duplicate = new ModPackage { Id = Guid.NewGuid().ToString("N"), Name = "Duplicate Expanded", Plugins = [expandedPlugin], Files = ["plugins/duplicate/NivalisExpanded.dll"] };
    if (!ModPlanner.Check(Selection((expanded, true), (duplicate, true), (patch, true), (continuity, true)), [expanded, duplicate, patch, continuity]).Any(i => i.Message.Contains("Duplicate plugin ID", StringComparison.Ordinal)))
        throw new InvalidDataException("Duplicate Expanded identity is not rejected.");

    var report = new
    {
        ImportedPackages = library.State.Packages.Count,
        Plugins = plugins.Select(p => new { p.Guid, p.Version }),
        OrdinaryProfile = ordinaryIssues,
        ProtectedProfile = protectedIssues,
        Scope = "One universal archive per mod; disposable import library only; no installation, deployment or game execution."
    };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("PASS six universal packages work without LUZ and expose embedded optional Continuity support when LUZ is present.");
}
finally { FileSafety.DeleteOwned(Path.GetTempPath(), root); }
