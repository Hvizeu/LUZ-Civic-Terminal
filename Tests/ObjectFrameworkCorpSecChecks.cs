using System.IO.Compression;
using Luz;
using Luz.Continuity;

public static class ObjectFrameworkCorpSecChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "NivalisFurnishings"));
        string standalone = Path.Combine(sourceRoot, "Loader", "bin", "Release", "net6.0"), recovery = Path.Combine(sourceRoot, "Loader", "bin", "Continuity", "net6.0");
        if (!File.Exists(Path.Combine(standalone, "NivalisFurnishings.dll")) || !File.Exists(Path.Combine(recovery, "NivalisFurnishings.dll"))) throw new FileNotFoundException("Build both Object Framework editions before running the CorpSec workflow fixture.");

        string builds = Path.Combine(root, "object-framework-builds"); Copy(standalone, Path.Combine(builds, "Standalone")); Copy(recovery, Path.Combine(builds, "Recovery"));
        string data = Path.Combine(root, "object-framework-corpsec"); using var library = new Library(data); string profileBefore = library.Fingerprint(library.Active);
        var project = CorpSec.ScanFolder(library.Root, builds).Projects.Single(p => p.PluginGuid == "hvizeu.nivalis.furnishings");
        check(project.BuildChoices.Select(c => c.Edition).Order().SequenceEqual(new[] { "recovery", "standalone" }) && project.BuildEdition.Length == 0, "Object Framework discovery presents its standalone and recovery editions without silently changing products");
        project = CorpSec.SelectBuild(project, "recovery"); project.Author = "Hvizeu"; project.Description = "Runtime framework for custom objects."; project.Website = "https://example.invalid/object-framework"; project.SupportContact = "Object Framework support page"; CorpSec.Save(project);
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

        string next = Path.Combine(builds, "Recovery-next"); Copy(recovery, next); File.SetLastWriteTimeUtc(Path.Combine(next, "NivalisFurnishings.dll"), DateTime.UtcNow.AddMinutes(1));
        project = CorpSec.RefreshFolders(library.Root).Single(p => p.PluginGuid == "hvizeu.nivalis.furnishings");
        check(project.BuildEdition == "recovery" && project.Author == "Hvizeu" && project.SupportContact == "Object Framework support page" && Path.GetFullPath(project.Input) == Path.GetFullPath(next), "Object Framework refresh follows the next recovery build while retaining reviewed author settings");

        project = CorpSec.SelectBuild(project, "standalone"); var normal = CorpSec.Generate(library.Root, project, includeRecovery: false); var normalPackage = CorpSec.Package(library.Root, project, output, "nexus", includeRecovery: false).Single();
        using (var archive = ZipFile.OpenRead(normalPackage.Archive))
            check(normal.PackagePassed && !normal.RecoveryPassed && archive.Entries.Any(e => e.FullName.EndsWith("/NivalisFurnishings.Core.dll", StringComparison.Ordinal)) && !archive.Entries.Any(e => e.FullName.EndsWith("/LUZ/mod-support.json", StringComparison.Ordinal)), "Object Framework standalone edition exports normally without an incomplete recovery declaration");

        string restoreSource = File.ReadAllText(Path.Combine(sourceRoot, "Loader", "ContinuityRestore.cs"));
        check(restoreSource.Contains("CompleteRestore(\"framework\")", StringComparison.Ordinal) && restoreSource.Contains("FailRestore(\"framework\"", StringComparison.Ordinal), "Object Framework restoration acknowledgement and failure reporting retain the framework owner");
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }
}
