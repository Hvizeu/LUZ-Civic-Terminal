using System.IO.Compression;
using Luz;
using Luz.Continuity;
using Mono.Cecil;

public static class CorpSecChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string input = Path.Combine(root, "corpsec-input"); Directory.CreateDirectory(input);
        string sourceDll = Path.Combine(AppContext.BaseDirectory, "StandaloneFixture.dll"), plugin = Path.Combine(input, "FixtureMod.dll"); File.Copy(sourceDll, plugin);
        File.WriteAllBytes(Path.Combine(input, "icon.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        File.WriteAllText(Path.Combine(input, "README.md"), "Fixture package");

        string data = Path.Combine(root, "corpsec-library"); using var library = new Library(data); string before = library.Fingerprint(library.Active);
        string builds = Path.Combine(root, "corpsec-builds");
        string first10 = Fixture(builds, "Clock/v1", sourceDll, "fixture.clock", "Clock Control", "1.0.0");
        string first11 = Fixture(builds, "Clock/v2", sourceDll, "fixture.clock", "Clock Control", "1.1.0");
        _ = Fixture(builds, "Reports/current", sourceDll, "fixture.reports", "Business Reports", "2.0.0");
        var scan = CorpSec.ScanFolder(library.Root, builds);
        check(scan.Projects.Count == 2 && scan.Projects.Single(p => p.PluginGuid == "fixture.clock").Version == "1.1.0" && scan.Projects.All(p => p.ProjectRoot == Path.GetFullPath(builds)), "CorpSec adds a parent builds folder as a persistent multi-mod workspace and selects the newest version per plugin");
        var kept = scan.Projects.Single(p => p.PluginGuid == "fixture.clock"); kept.Author = "Remembered Author"; CorpSec.Save(kept); CorpSec.RememberSelection(library.Root, kept.Id);
        _ = Fixture(builds, "Clock/v3", sourceDll, "fixture.clock", "Clock Control", "1.2.0");
        var rescanned = CorpSec.RefreshFolders(library.Root);
        check(rescanned.Single(p => p.PluginGuid == "fixture.clock").Version == "1.2.0" && rescanned.Single(p => p.PluginGuid == "fixture.clock").Author == "Remembered Author", "CorpSec rescans saved folders for a new release without losing author settings");
        check(CorpSec.Workspace(library.Root).Roots.Single() == Path.GetFullPath(builds) && CorpSec.Workspace(library.Root).SelectedProjectId == kept.Id, "CorpSec remembers the builds folder and selected mod outside distributable files");
        check(first10 != first11, "CorpSec scan fixture keeps distinct release folders");
        var forgotten = scan.Projects.Single(p => p.PluginGuid == "fixture.reports");
        string forgottenWorkspace = Path.Combine(library.Root, "authoring", "workspaces", forgotten.Id), forgottenIntegration = Path.Combine(library.Root, "authoring", "projects", forgotten.Id + "-integration");
        Directory.CreateDirectory(forgottenWorkspace); File.WriteAllText(Path.Combine(forgottenWorkspace, "private.txt"), "generated"); Directory.CreateDirectory(forgottenIntegration); File.WriteAllText(Path.Combine(forgottenIntegration, "private.txt"), "generated");
        CorpSec.Forget(library.Root, forgotten);
        check(!CorpSec.SavedProjects(library.Root).Any(p => p.Id == forgotten.Id) && !Directory.Exists(forgottenWorkspace) && !Directory.Exists(forgottenIntegration) && Directory.Exists(builds), "Removing a CorpSec project forgets its private authoring files without deleting the watched builds folder");
        check(!CorpSec.RefreshFolders(library.Root).Any(p => p.Id == forgotten.Id) && CorpSec.Workspace(library.Root).IgnoredProjectIds.Contains(forgotten.Id), "Routine build scans keep a removed project out of My mods");
        check(CorpSec.ScanFolder(library.Root, builds).Projects.Any(p => p.Id == forgotten.Id) && !CorpSec.Workspace(library.Root).IgnoredProjectIds.Contains(forgotten.Id), "Explicitly adding the builds folder restores a previously removed project");
        string external = Path.Combine(root, "corpsec-external-records");
        _ = Fixture(external, "old-build", sourceDll, "fixture.clock", "External Clock", "99.0.0");
        string analysisLink = Path.Combine(builds, "analysis"), otherLink = Path.Combine(builds, "linked-records"), cycleLink = Path.Combine(builds, "cycle"), rootAlias = Path.Combine(root, "corpsec-builds-alias");
        try
        {
            LinkedRootChecks.Link(analysisLink, external); LinkedRootChecks.Link(otherLink, external); LinkedRootChecks.Link(cycleLink, builds); LinkedRootChecks.Link(rootAlias, builds);
            var linkedScan = CorpSec.ScanFolder(library.Root, builds);
            check(linkedScan.Projects.Count == 2 && linkedScan.Projects.Single(p => p.PluginGuid == "fixture.clock").Version == "1.2.0", "CorpSec scans a project with linked analysis and other records without importing external builds or following cycles");
            check(CorpSec.RefreshFolders(library.Root).Single(p => p.Id == kept.Id).Author == "Remembered Author", "CorpSec refresh preserves author settings with linked project records");
            check(CorpSec.ScanFolder(library.Root, rootAlias).Projects.All(p => p.ProjectRoot == Path.GetFullPath(builds)), "CorpSec resolves an explicitly selected root link to the same saved folder");
            reject(() => FileSafety.NoLinks(builds), "Discovery does not weaken the managed-payload link guard");
            using var scanCancelled = new CancellationTokenSource(); scanCancelled.Cancel();
            bool scanStopped = false; try { CorpSec.ScanFolder(library.Root, builds, scanCancelled.Token); } catch (OperationCanceledException) { scanStopped = true; }
            check(scanStopped && before == library.Fingerprint(library.Active), "Linked-folder discovery supports cancellation without changing the player profile");
            string payloadLink = Path.Combine(Path.GetDirectoryName(first11)!, "linked-asset");
            try
            {
                LinkedRootChecks.Link(payloadLink, external);
                reject(() => CorpSec.Inspect(library.Root, Path.GetDirectoryName(first11)!), "A selected mod payload still rejects linked assets");
            }
            finally { if (new DirectoryInfo(payloadLink).LinkTarget != null) Directory.Delete(payloadLink); }
        }
        finally
        {
            foreach (string link in new[] { analysisLink, otherLink, cycleLink, rootAlias })
                if (new DirectoryInfo(link).LinkTarget != null) Directory.Delete(link);
        }
        string mixed = Path.Combine(root, "corpsec-mixed-editions");
        _ = Fixture(mixed, "Standalone", sourceDll, "fixture.editions", "Edition Fixture", "9.0.0"); _ = Fixture(mixed, "Recovery", sourceDll, "fixture.editions", "Edition Fixture", "1.0.0", true);
        var editionProject = CorpSec.ScanFolder(library.Root, mixed).Projects.Single();
        check(editionProject.BuildChoices.Count == 2 && editionProject.BuildEdition.Length == 0 && editionProject.Findings.Any(f => f.Id == "choose-edition" && f.Severity == "Action"), "CorpSec refuses to choose silently between standalone and universal builds sharing a plugin GUID");
        editionProject.Author = "Edition Author"; editionProject = CorpSec.SelectBuild(editionProject, "universal"); _ = Fixture(mixed, "Standalone-new", sourceDll, "fixture.editions", "Edition Fixture", "10.0.0");
        editionProject = CorpSec.RefreshFolders(library.Root).Single(p => p.PluginGuid == "fixture.editions");
        check(editionProject.BuildEdition == "universal" && editionProject.Version == "1.0.0" && editionProject.Author == "Edition Author", "CorpSec remembers the selected universal build and author settings instead of switching to a higher standalone version");

        string statelessBuilds = Path.Combine(root, "corpsec-stateless-third-party");
        _ = Fixture(statelessBuilds, "current", sourceDll, "fixture.stateless", "Stateless Third Party", "1.0.0", contracts: false);
        var thirdParty = CorpSec.ScanFolder(library.Root, statelessBuilds).Projects.Single();
        check(thirdParty.Findings.Any(f => f.Id == "support-contact"), "CorpSec tells a new author to provide a support contact");
        thirdParty.SupportContact = "fixture support"; CorpSec.Save(thirdParty);
        check(!thirdParty.Findings.Any(f => f.Id == "support-contact"), "saving a support contact clears its action without requiring another analysis scan");
        thirdParty.RecoveryIntent = "stateless"; thirdParty.RecoveryReviewed = true; thirdParty.SupportSource = "author"; CorpSec.Save(thirdParty);
        var thirdPartyAnalysis = CorpSec.Analyze(library.Root, thirdParty);
        var thirdPartyReport = CorpSec.Generate(library.Root, thirdParty);
        var thirdPartyDescriptor = SafeFiles.Read<ModSupportDescriptor>(Directory.GetFiles(thirdParty.Workspace, "mod-support.json", SearchOption.AllDirectories).Single());
        check(thirdPartyAnalysis.RecoveryReady && !thirdPartyAnalysis.Findings.Any(f => f.Id == "compiled-contracts") && thirdPartyReport.RecoveryPassed && thirdPartyDescriptor.Compatibility.RequirementsSchema == 0 && thirdPartyDescriptor.Compatibility.ContractsSha256.Length == 0 && thirdPartyDescriptor.Capabilities.Length == 0,
            "a stateless third-party mod generates complete exact-hash support without claiming native contracts or recovery capabilities");
        string legacyBuilds = Path.Combine(root, "corpsec-stateless-schema1");
        _ = Fixture(legacyBuilds, "current", sourceDll, "fixture.schema1", "Schema One Stateless", "1.0.0", legacyContracts: true);
        var legacyStateless = CorpSec.ScanFolder(library.Root, legacyBuilds).Projects.Single(); legacyStateless.SupportContact = "fixture support"; legacyStateless.RecoveryIntent = "stateless"; legacyStateless.RecoveryReviewed = true; legacyStateless.SupportSource = "author"; CorpSec.Save(legacyStateless);
        var legacyReport = CorpSec.Generate(library.Root, legacyStateless); var legacyDescriptor = SafeFiles.Read<ModSupportDescriptor>(Directory.GetFiles(legacyStateless.Workspace, "mod-support.json", SearchOption.AllDirectories).Single());
        check(legacyReport.RecoveryPassed && legacyDescriptor.Compatibility.RequirementsSchema == 1 && legacyDescriptor.Compatibility.ContractsSha256.Length == 0 && legacyDescriptor.Capabilities.Length == 0,
            "a stateless schema-one mod keeps its compiled-reference metadata without inventing a resource hash");
        thirdParty.Capabilities = [new("module", "native", Array.Empty<string>(), Array.Empty<string>())]; thirdParty.RecoveryIntent = "embedded"; CorpSec.Save(thirdParty);
        check(CorpSec.Analyze(library.Root, thirdParty).Findings.Any(f => f.Id == "compiled-contracts"), "native recovery claims still require compiled Nivalis contracts");

        var automatic = rescanned.Single(p => p.PluginGuid == "fixture.clock"); automatic.SupportContact = "fixture support"; automatic.RecoveryIntent = "stateless"; automatic.RecoveryReviewed = true; automatic.SupportSource = "author"; CorpSec.Save(automatic); _ = Fixture(builds, "Clock/v4", sourceDll, "fixture.clock", "Clock Control", "1.3.0");
        var automaticRelease = CorpSec.BuildRelease(library.Root, automatic, Path.Combine(root, "corpsec-automatic-package"), "nexus");
        check(automaticRelease.Project.Version == "1.3.0" && automaticRelease.Packages.Count == 1 && Path.GetFileName(automaticRelease.Packages[0].Archive).Contains("1.3.0", StringComparison.Ordinal), "CorpSec one-click release refreshes, generates, validates and packages the newest watched build");
        var project = CorpSec.Inspect(library.Root, input);
        project.Author = "Fixture Author"; project.Description = "CorpSec fixture"; project.Website = "https://example.invalid"; project.SupportContact = "support@example.invalid";
        project.ThunderstoreDependencies = ["hard | Fixture-Hard-2.0.0", "fixture.ranged | Fixture-Ranged-1.0.3"];
        CorpSec.Save(project);
        var unanswered = CorpSec.Analyze(library.Root, project);
        check(!unanswered.RecoveryReady && unanswered.Findings.Any(f => f.Id == "recovery-intent"), "CorpSec keeps recovery incomplete until the author answers where persistent state is stored");
        var normal = CorpSec.Generate(library.Root, project, includeRecovery: false); string normalPackages = Path.Combine(root, "corpsec-normal-package"); var normalBuilt = CorpSec.Package(library.Root, project, normalPackages, "nexus", includeRecovery: false);
        using (var zip = ZipFile.OpenRead(normalBuilt.Single().Archive)) check(normal.PackagePassed && !normal.RecoveryPassed && normal.RecoveryStatus == "not-included" && !zip.Entries.Any(e => e.FullName.EndsWith("/LUZ/mod-support.json", StringComparison.Ordinal)), "CorpSec permits a clearly ordinary package while incomplete recovery declarations stay outside it");
        project.RecoveryIntent = "stateless"; project.RecoveryReviewed = true; project.SupportSource = "author"; CorpSec.Save(project);
        var stateless = CorpSec.Generate(library.Root, project);
        check(stateless.PackagePassed && stateless.RecoveryPassed && before == library.Fingerprint(library.Active), "CorpSec stateless generation leaves the active player profile untouched");
        var descriptorPath = Directory.GetFiles(project.Workspace, "mod-support.json", SearchOption.AllDirectories).Single();
        var descriptor = SafeFiles.Read<ModSupportDescriptor>(descriptorPath);
        string packagedPlugin = Path.Combine(Directory.GetParent(Path.GetDirectoryName(descriptorPath)!)!.FullName, descriptor.Plugin.File.Replace('/', Path.DirectorySeparatorChar));
        check(FileSafety.Hash(packagedPlugin) == FileSafety.Hash(plugin) && descriptor.Payloads.Length >= 2 && !PluginMetadata.Read(packagedPlugin).Single().Dependencies.Any(d => d.Guid.Contains("luz", StringComparison.OrdinalIgnoreCase)), "generated support hashes payload assets, preserves the standalone plugin binary and does not require LUZ");
        string payload = Path.Combine(Directory.GetParent(Path.GetDirectoryName(descriptorPath)!)!.FullName, descriptor.Payloads[0].File.Replace('/', Path.DirectorySeparatorChar)); File.AppendAllText(payload, "changed");
        check(!CorpSec.Validate(project).RecoveryPassed, "CorpSec requires support regeneration after a declared payload changes"); CorpSec.Generate(library.Root, project);

        project.Companions = [new("config", "Fixture/{slot}.json", "raw", false, "json-revision", 1, 2), new("saves", "Fixture/{slot}.bin", "lower", false, "opaque")]; project.RecoveryIntent = "files"; project.RecoveryReviewed = true;
        var ordinary = CorpSec.Generate(library.Root, project);
        check(ordinary.RecoveryPassed && SafeFiles.Read<ModSupportDescriptor>(Directory.GetFiles(project.Workspace, "mod-support.json", SearchOption.AllDirectories).Single()).Companions.Select(c => c.Root).Order().SequenceEqual(new[] { "config", "saves" }), "CorpSec declares ordinary configuration and save companions");

        string adapter = Path.Combine(AppContext.BaseDirectory, "UpdateFixture.dll");
        project.Adapters = [new("fixture-adapter", adapter, "FixtureRecoveryAdapter", ["module"])];
        project.Capabilities = [new("module", "fixture-adapter", ["native"], Array.Empty<string>())];
        project.RecoveryIntent = "custom"; project.RecoveryReviewed = true;
        var custom = CorpSec.Generate(library.Root, project);
        var located = CorpSec.Discover(project.Workspace, true).Single();
        check(custom.RecoveryPassed && located.Selected["module"] == "fixture-adapter", "CorpSec selects a complete author recovery adapter");
        string packagedAdapter = Path.Combine(Path.GetDirectoryName(located.DescriptorPath)!, located.Descriptor.Adapters.Single().File.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(packagedAdapter);
        check(CorpSec.Discover(project.Workspace, true).Single().Selected["module"] == "native" && CorpSec.Validate(project).Messages.Any(m => m.Message.Contains("Using fallback", StringComparison.Ordinal)), "CorpSec resolves and reports a declared fallback before protected effects begin");

        project.Capabilities = [new("module", "fixture-adapter", Array.Empty<string>(), Array.Empty<string>())]; CorpSec.Generate(library.Root, project);
        located = CorpSec.Discover(project.Workspace, true).Single(); packagedAdapter = Path.Combine(Path.GetDirectoryName(located.DescriptorPath)!, located.Descriptor.Adapters.Single().File.Replace('/', Path.DirectorySeparatorChar)); File.Delete(packagedAdapter);
        check(!CorpSec.Validate(project).RecoveryPassed, "CorpSec blocks recovery when no selected adapter implementation is available");
        CorpSec.Generate(library.Root, project); located = CorpSec.Discover(project.Workspace, true).Single();
        File.AppendAllText(Path.Combine(located.PluginRoot, located.Descriptor.Plugin.File.Replace('/', Path.DirectorySeparatorChar)), "changed");
        check(!CorpSec.Validate(project).RecoveryPassed, "CorpSec requires support regeneration after a plugin DLL changes");

        project.Capabilities = [new("module", "fixture-adapter", ["native"], Array.Empty<string>())]; CorpSec.Generate(library.Root, project);
        descriptorPath = Directory.GetFiles(project.Workspace, "mod-support.json", SearchOption.AllDirectories).Single(); descriptor = SafeFiles.Read<ModSupportDescriptor>(descriptorPath); descriptor.Compatibility = descriptor.Compatibility with { RequirementsSchema = 0, ContractsSha256 = "" }; SafeFiles.Write(descriptorPath, descriptor);
        check(!CorpSec.Validate(project).RecoveryPassed, "CorpSec distinguishes generated files from a qualified compiled recovery contract");
        CorpSec.Generate(library.Root, project);

        string packages = Path.Combine(root, "corpsec-packages"); var built = CorpSec.Package(library.Root, project, packages, "all");
        check(built.Count == 2 && built.All(p => File.Exists(p.Archive + ".sha256")) && !Directory.EnumerateFiles(packages).Any(p => p.Contains(".pending-", StringComparison.Ordinal)), "CorpSec atomically builds Nexus and Thunderstore packages with checksums");
        using (var zip = ZipFile.OpenRead(built.Single(p => p.Preset == "nexus").Archive))
            check(zip.Entries.Any(e => e.FullName.EndsWith("/LUZ/mod-support.json", StringComparison.Ordinal)) && zip.Entries.Any(e => e.FullName.EndsWith("/FixtureMod.dll", StringComparison.Ordinal)), "CorpSec package exposes the final deployment tree with LUZ support beside the plugin");
        string importedData = Path.Combine(root, "corpsec-imported-declaration"); using (var importedLibrary = new Library(importedData))
        {
            var importedProject = CorpSec.Inspect(importedLibrary.Root, built.Single(p => p.Preset == "nexus").Archive);
            check(importedProject.SupportSource == "existing-declaration" && importedProject.Adapters.Single().Id == "fixture-adapter" && importedProject.Capabilities.Single().Preferred == "fixture-adapter", "CorpSec imports an existing support declaration and resolves its adapter relative to the original descriptor");
        }
        project.ThunderstoreDependencies.RemoveAll(d => d.StartsWith("fixture.ranged", StringComparison.OrdinalIgnoreCase));
        reject(() => CorpSec.Package(library.Root, project, packages, "thunderstore"), "CorpSec refuses unresolved Thunderstore dependency mappings instead of guessing");
        project.ThunderstoreDependencies.Add("fixture.ranged | Fixture-Ranged-1.0.3");

        var invalid = SafeFiles.Read<ModSupportDescriptor>(Directory.GetFiles(project.Workspace, "mod-support.json", SearchOption.AllDirectories).Single()); invalid.Schema = 2;
        reject(invalid.Validate, "CorpSec rejects incompatible support schemas");
        invalid.Schema = 1; invalid.Companions = [new("config", "../{slot}.json", "raw", false, "opaque")]; reject(invalid.Validate, "CorpSec rejects unsafe declared companion paths");

        string duplicates = Path.Combine(root, "corpsec-duplicate"); Copy(project.Workspace, Path.Combine(duplicates, "one")); Copy(project.Workspace, Path.Combine(duplicates, "two"));
        reject(() => CorpSec.Discover(duplicates, false), "CorpSec rejects duplicate preservation owners");

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool cancellationObserved = false; try { CorpSec.Generate(library.Root, project, cancelled.Token); } catch (OperationCanceledException) { cancellationObserved = true; }
        check(cancellationObserved, "CorpSec generation cancellation is explicit");
        string workspaces = Path.Combine(library.Root, "authoring", "workspaces");
        check(!Directory.EnumerateDirectories(workspaces).Any(p => Path.GetFileName(p).Contains(".pending-", StringComparison.Ordinal)), "cancelled CorpSec generation removes its pending tree and preserves the previous workspace");
        check(CorpSec.SavedProjects(library.Root).Any(p => p.PluginGuid == project.PluginGuid), "CorpSec author settings can be reopened independently of the distributable files");
        string integration = CorpSec.GenerateBuildIntegration(project, Environment.ProcessPath!);
        string adapterTemplate = File.ReadAllText(Path.Combine(Path.GetDirectoryName(integration)!, "LuzRecoveryAdapterV1.cs")), optionalTemplate = File.ReadAllText(Path.Combine(Path.GetDirectoryName(integration)!, "LuzOptionalRuntimeV1.cs"));
        check(File.Exists(integration) && adapterTemplate.Contains("not implemented: validate") && !adapterTemplate.Contains("=> \"ok\"") && optionalTemplate.Contains("RequireProtectedOperation") && File.ReadAllText(integration).Contains("--json generate") && File.Exists(Path.Combine(Path.GetDirectoryName(integration)!, "LuzRecoveryAdapterV1.csproj")) && File.ReadAllText(Path.Combine(Path.GetDirectoryName(integration)!, "README.md")).Contains("`Preflight` validates before effects begin"), "CorpSec generates repeatable automation and a buildable, intentionally incomplete, protected-session-aware source SDK");

        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(library.Root).ToUpperInvariant())));
        using (var gate = new Mutex(false, "LUZ.CorpSec." + key))
        {
            gate.WaitOne(); var save = Task.Run(() => CorpSec.Save(project)); Thread.Sleep(100); bool waited = !save.IsCompleted; gate.ReleaseMutex();
            check(waited && save.Wait(TimeSpan.FromSeconds(5)), "CorpSec serializes concurrent desktop and build-automation writes to one author workspace");
        }
        var originalOut = Console.Out; try
        {
            using var output = new StringWriter(); Console.SetOut(output); int completeExit = CorpSecCommand.Run(["--corpsec", "--json", "analyze", project.ProjectFile], library.Root);
            project.RecoveryIntent = "unknown"; project.RecoveryReviewed = false; CorpSec.Save(project); int unresolvedExit = CorpSecCommand.Run(["--corpsec", "--json", "analyze", project.ProjectFile], library.Root);
            check(completeExit == 0 && unresolvedExit == 2 && output.ToString().Contains("RecoveryReady", StringComparison.Ordinal), "CorpSec CLI exposes machine-readable analysis and a distinct unresolved exit code");
        }
        finally { Console.SetOut(originalOut); project.RecoveryIntent = "custom"; project.RecoveryReviewed = true; CorpSec.Save(project); }
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { string target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); }
    }

    private static string Fixture(string root, string relative, string source, string guid, string name, string version, bool continuity = false, bool contracts = true, bool legacyContracts = false)
    {
        string folder = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(folder); string target = Path.Combine(folder, name.Replace(" ", "") + ".dll"); File.Copy(source, target, true);
        using var assembly = AssemblyDefinition.ReadAssembly(target, new ReaderParameters { InMemory = true });
        var attribute = assembly.MainModule.Types.SelectMany(AllTypes).SelectMany(t => t.CustomAttributes).Single(a => a.AttributeType.Name == "BepInPlugin");
        attribute.ConstructorArguments[0] = new(attribute.ConstructorArguments[0].Type, guid); attribute.ConstructorArguments[1] = new(attribute.ConstructorArguments[1].Type, name); attribute.ConstructorArguments[2] = new(attribute.ConstructorArguments[2].Type, version);
        if (!contracts || legacyContracts)
        {
            for (int index = assembly.CustomAttributes.Count - 1; index >= 0; index--)
                if (assembly.CustomAttributes[index].AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && assembly.CustomAttributes[index].ConstructorArguments.Count == 2 && assembly.CustomAttributes[index].ConstructorArguments[0].Value is string key && key is "Nivalis.Requirements.Schema" or "Nivalis.NativeBuild" or "Nivalis.MetadataBuild") assembly.CustomAttributes.RemoveAt(index);
            for (int index = assembly.MainModule.Resources.Count - 1; index >= 0; index--)
                if (assembly.MainModule.Resources[index].Name == "Nivalis.Contracts.v2.json") assembly.MainModule.Resources.RemoveAt(index);
            if (legacyContracts)
            {
                var constructor = typeof(System.Reflection.AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]) ?? throw new MissingMethodException();
                var schema = new CustomAttribute(assembly.MainModule.ImportReference(constructor)); schema.ConstructorArguments.Add(new(assembly.MainModule.TypeSystem.String, "Nivalis.Requirements.Schema")); schema.ConstructorArguments.Add(new(assembly.MainModule.TypeSystem.String, "1")); assembly.CustomAttributes.Add(schema);
            }
        }
        if (continuity)
        {
            var constructor = typeof(System.Reflection.AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]) ?? throw new MissingMethodException();
            var metadata = new CustomAttribute(assembly.MainModule.ImportReference(constructor)); metadata.ConstructorArguments.Add(new(assembly.MainModule.TypeSystem.String, "Nivalis.Continuity.Api")); metadata.ConstructorArguments.Add(new(assembly.MainModule.TypeSystem.String, "1")); assembly.CustomAttributes.Add(metadata);
        }
        assembly.Write(target); return target;
    }

    private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(AllTypes));
}
