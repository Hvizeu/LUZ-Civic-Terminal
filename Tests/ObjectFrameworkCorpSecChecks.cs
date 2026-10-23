using System.IO.Compression;
using Luz;
using Luz.Continuity;

public static class ObjectFrameworkCorpSecChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "NivalisFurnishings"));
        string universal = Path.Combine(sourceRoot, "Loader", "bin", "Release", "net6.0");
        if (!File.Exists(Path.Combine(universal, "NivalisFurnishings.dll"))) throw new FileNotFoundException("Build the universal Object Framework before running the CorpSec workflow fixture.");

        string builds = Path.Combine(root, "object-framework-builds"); Copy(universal, Path.Combine(builds, "Universal"));
        string data = Path.Combine(root, "object-framework-corpsec"); using var library = new Library(data); string profileBefore = library.Fingerprint(library.Active);
        var project = CorpSec.ScanFolder(library.Root, builds).Projects.Single(p => p.PluginGuid == "hvizeu.nivalis.furnishings");
        check(project.BuildChoices.Select(c => c.Edition).SequenceEqual(new[] { "universal" }) && project.BuildEdition == "universal", "Object Framework discovery presents one universal build");
        project.Author = "Hvizeu"; project.Description = "Runtime framework for custom objects."; project.Website = "https://example.invalid/object-framework"; project.SupportContact = "Object Framework support page"; CorpSec.Save(project);
        var analysis = CorpSec.Analyze(library.Root, project);
        check(analysis.RecoveryReady && project.SupportSource == "luz-maintained" && project.Owner == "framework" && project.Capabilities.Select(c => c.Name).Order().SequenceEqual(new[] { "behaviours", "catalogue", "module" }), "Object Framework automatically reuses its maintained framework owner and module, catalogue and behaviours capabilities");

        var generated = CorpSec.Generate(library.Root, project, includeRecovery: true); string output = Path.Combine(root, "object-framework-packages"); var package = CorpSec.Package(library.Root, project, output, "nexus", includeRecovery: true).Single();
        check(generated.PackagePassed && generated.RecoveryPassed && profileBefore == library.Fingerprint(library.Active), "Object Framework support generation leaves the selected player profile untouched");
        using (var archive = ZipFile.OpenRead(package.Archive))
        {
            string[] names = archive.Entries.Select(e => e.FullName).ToArray();
            check(names.Any(n => n.EndsWith("/NivalisFurnishings.dll", StringComparison.Ordinal)) && names.Any(n => n.EndsWith("/NivalisFurnishings.Core.dll", StringComparison.Ordinal)) && names.Any(n => n.EndsWith("/LUZ/mod-support.json", StringComparison.Ordinal)), "Object Framework recovery package retains the plugin, framework core and generated support descriptor");
        }
        var descriptor = SafeFiles.Read<ModSupportDescriptor>(Directory.GetFiles(project.Workspace, "mod-support.json", SearchOption.AllDirectories).Single());
        check(descriptor.Owner == "framework" && descriptor.Provenance == "luz-maintained" && descriptor.Capabilities.Select(c => c.Name).Order().SequenceEqual(new[] { "behaviours", "catalogue", "module" }), "Object Framework generated support retains its established owner and maintained provenance");

        string next = Path.Combine(builds, "Universal-next"); Copy(universal, next); File.SetLastWriteTimeUtc(Path.Combine(next, "NivalisFurnishings.dll"), DateTime.UtcNow.AddMinutes(1));
        project = CorpSec.RefreshFolders(library.Root).Single(p => p.PluginGuid == "hvizeu.nivalis.furnishings");
        check(project.BuildEdition == "universal" && project.Author == "Hvizeu" && project.SupportContact == "Object Framework support page" && Path.GetFullPath(project.Input) == Path.GetFullPath(next), "Object Framework refresh follows the next universal build while retaining reviewed author settings");

        var normal = CorpSec.Generate(library.Root, project, includeRecovery: false); var normalPackage = CorpSec.Package(library.Root, project, output, "nexus", includeRecovery: false).Single();
        using (var archive = ZipFile.OpenRead(normalPackage.Archive))
            check(normal.PackagePassed && !normal.RecoveryPassed && archive.Entries.Any(e => e.FullName.EndsWith("/NivalisFurnishings.Core.dll", StringComparison.Ordinal)) && !archive.Entries.Any(e => e.FullName.EndsWith("/LUZ/mod-support.json", StringComparison.Ordinal)), "Object Framework universal build can export an ordinary package without a generated recovery descriptor");

        string restoreSource = File.ReadAllText(Path.Combine(sourceRoot, "Loader", "ContinuityRestore.cs"));
        check(restoreSource.Contains("CompleteRestore(\"framework\")", StringComparison.Ordinal) && restoreSource.Contains("FailRestore(\"framework\"", StringComparison.Ordinal), "Object Framework restoration acknowledgement and failure reporting retain the framework owner");
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }
}
