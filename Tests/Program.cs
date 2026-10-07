using System.IO.Compression;
using System.Net;
using System.Text;
using Luz;

if (args.Length == 3 && args[0] == "--forward-nxm") { Environment.Exit(await LinkInbox.Forward(args[1], args[2], 5000) ? 0 : 1); return; }
if (args.SequenceEqual(new[] { "--sleep-for-updater" })) { await Task.Delay(1200); return; }

int passed = 0;
void Check(bool condition, string title) { if (!condition) throw new Exception("FAILED: " + title); Console.WriteLine("PASS " + title); passed++; }
void Reject(Action action, string title) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { Check(true, title); return; } throw new Exception("FAILED accepted: " + title); }
string root = FileSafety.ResolveFolder(Path.Combine(Path.GetTempPath(), "LuzFixtures-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
string Zip(string name, params (string Name, string Text)[] files)
{
    string path = Path.Combine(root, name + ".zip"); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var (entry, text) in files) { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write(text); } return path;
}
try
{
    PlatformChecks.Run(root, Check, Reject);
    InstallationChecks.Run(root, Check, Reject);
    RelocationChecks.Run(root, Check, Reject);
    LinkedRootChecks.Run(root, Check, Reject);
    RegistrationRemovalChecks.Run(Check);
    await UpdateChecks.Run(root, Check, Reject);
    PluginDependencyChecks.Run(root, Check, Reject);
    RecoveryChecks.Run(root, Check, Reject);
    await NxmChecks.Run(root, Check, Reject);
    foreach (var bad in new[] { "../outside", "foo/../../outside", "C:/outside", "foo:bar", "con.dll", "x/../y", "x. /y", "foo./x" }) Reject(() => FileSafety.Under(root, bad), "reject path " + bad);
    var traversal = Zip("traversal", ("../escape.txt", "bad")); Reject(() => FileSafety.Extract(traversal, Path.Combine(root, "extract")), "reject ZIP traversal");
    var duplicate = Zip("duplicates", ("plugins/A.txt", "a"), ("plugins/a.txt", "b")); Reject(() => FileSafety.Extract(duplicate, Path.Combine(root, "duplicate-extract")), "reject case duplicate ZIP paths");
    Check(ModPlanner.CompareVersions("1.0.0", "1.0.0-beta") > 0 && ModPlanner.CompareVersions("1.0.0-rc.10", "1.0.0-rc.2") > 0, "semantic prerelease version comparison");
    var metadata = PluginMetadata.Read(typeof(TestPlugin).Assembly.Location).Single();
    Check(metadata.Guid == "fixture.plugin" && metadata.Dependencies.Any(d => d.Optional) && metadata.Dependencies.Any(d => d.MinimumVersion == "2.0.0"), "managed plugin metadata without executing attributes");
    var dep = new ModPackage { Name = "Dependency", Plugins = [new("dep", "Dependency", "1.0.0", [], [])] };
    var child = new ModPackage { Name = "Child", Plugins = [new("child", "Child", "1.0.0", [new("dep", "1.0.0", false)], [])] };
    var profile = new Profile { Mods = [new() { PackageId = child.Id }, new() { PackageId = dep.Id }] };
    Check(ModPlanner.Sort(profile, [dep, child])[0].PackageId == dep.Id, "dependency sort");
    profile.Mods[1].Enabled = false; Check(ModPlanner.Check(profile, [dep, child]).Any(i => i.Severity == "Error"), "disabled required dependency blocks deployment"); profile.Mods[1].Enabled = true;
    dep.Plugins[0].Dependencies.Add(new("child", "", false)); Reject(() => ModPlanner.Sort(profile, [dep, child]), "dependency cycle"); dep.Plugins[0].Dependencies.Clear();
    dep.Files.Add("plugins/shared.txt"); child.Files.Add("plugins/shared.txt"); Check(ModPlanner.Check(profile, [dep, child]).Any(i => i.Severity == "Warning"), "shared file conflict is visible");
    child.Plugins[0].Incompatibilities.Add("dep"); Check(ModPlanner.Check(profile, [dep, child]).Any(i => i.Message.Contains("conflicts with")), "explicit incompatibility");
    child.Dependencies.Add("malformed"); Check(ModPlanner.Check(profile, [dep, child]).Any(i => i.Message.Contains("Invalid package dependency")), "malformed dependency reports an error without breaking the UI"); child.Dependencies.Clear();
    var duplicatePlugin = new ModPackage { Plugins = dep.Plugins }; profile.Mods.Add(new() { PackageId = duplicatePlugin.Id }); Check(ModPlanner.Check(profile, [dep, child, duplicatePlugin]).Any(i => i.Message.Contains("Duplicate plugin")), "duplicate plugin GUID");
    using var library = new Library(Path.Combine(root, "library"));
    var one = library.Import(Zip("one", ("BepInEx/plugins/First/a.txt", "first"), ("BepInEx/config/demo.cfg", "default")));
    Check(one.Files.Contains("plugins/First/a.txt"), "game-root ZIP routing");
    library.Import(Zip("two", ("plugins/Second/b.txt", "second"))); Check(library.Active.Mods.Count == 2, "plugins-root ZIP routing");
    Reject(() => library.Import(Zip("loader-reject", ("winhttp.dll", "fake"))), "loader archive cannot be imported as a mod");
    string game = Path.Combine(root, "fake-game"); Directory.CreateDirectory(Path.Combine(game, "BepInEx/core")); File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture only; never execute"); File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture"); File.WriteAllText(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"), "fixture");
    Directory.CreateDirectory(Path.Combine(game, "BepInEx/plugins/Old")); File.WriteAllText(Path.Combine(game, "BepInEx/plugins/Old/existing.txt"), "preserve in backup"); library.State.GameFolder = game; library.Save();
    var deployment = new Deployment(library); var backup = deployment.Apply();
    Check(File.ReadAllText(Path.Combine(game, "BepInEx/plugins/First/a.txt")) == "first" && !Directory.Exists(Path.Combine(game, "BepInEx/plugins/Old")), "apply exact profile into disposable game fixture");
    Check(File.ReadAllText(Path.Combine(library.Root, "backups", backup.Id, "plugins/Old/existing.txt")) == "preserve in backup", "preexisting unmanaged file backed up");
    one.Files.Add("GameAssembly.dll"); Reject(() => deployment.Apply(), "package cannot overwrite game-root files"); one.Files.Remove("GameAssembly.dll"); Check(File.ReadAllText(Path.Combine(game, "GameAssembly.dll")) == "fixture", "game assembly remains unchanged after rejected apply");
    Directory.CreateDirectory(Path.Combine(game, "BepInEx/patchers")); string locked = Path.Combine(game, "BepInEx/patchers/locked.txt"); File.WriteAllText(locked, "locked fixture");
    library.Active.Mods.First(m => m.PackageId == one.Id).Enabled = false;
    if (OperatingSystem.IsWindows())
    {
        using var lockedFile = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read);
        Reject(() => deployment.Apply(), "locked destination interrupts apply without erasing backups");
    }
    else Console.WriteLine("SKIP Windows file-sharing failure: Unix permits renaming an open file. Journal recovery is tested separately.");
    if (deployment.Pending != null) deployment.Restore(deployment.Backups().Single(b => b.Id == deployment.Pending));
    Check(File.ReadAllText(Path.Combine(game, "BepInEx/plugins/First/a.txt")) == "first", "partial deployment can recover its original plugin files"); library.Active.Mods.First(m => m.PackageId == one.Id).Enabled = true;
    File.WriteAllText(Path.Combine(game, "BepInEx/config/demo.cfg"), "user edited"); string originalId = library.Active.Id;
    library.CreateProfile("Empty", false); deployment.Apply(); Check(!File.Exists(Path.Combine(game, "BepInEx/plugins/First/a.txt")), "empty profile removes deployed mod files");
    library.State.ActiveProfileId = originalId; deployment.Apply(); Check(File.ReadAllText(Path.Combine(game, "BepInEx/config/demo.cfg")) == "user edited", "configuration isolated and restored per profile");
    library.CreateProfile("Copy", true); Check(library.Active.Mods.Count == 2 && File.Exists(Path.Combine(library.ConfigRoot(library.Active.Id), "demo.cfg")), "profile duplication includes config");
    library.ExportProfile(Path.Combine(root, "share.luzprofile")); library.ImportProfile(Path.Combine(root, "share.luzprofile")); Check(library.Active.Mods.Count == 2, "profile manifest round trip");
    deployment.Restore(backup); Check(File.Exists(Path.Combine(game, "BepInEx/plugins/Old/existing.txt")) && !File.Exists(Path.Combine(game, "BepInEx/plugins/First/a.txt")), "restore original installation");
    JsonFiles.Write(Path.Combine(library.Root, "pending.json"), backup); Check(deployment.Preview().Any(i => i.Message.Contains("interrupted")), "interrupted deployment blocks apply"); deployment.Restore(backup); Check(deployment.Pending == null, "recovery clears deployment journal");
    library.CreateProfile("Adopted", false); library.ImportInstalled(game); Check(library.Active.Mods.SelectMany(m => library.State.Packages.First(p => p.Id == m.PackageId).Files).Contains("plugins/Old/existing.txt"), "adoption preserves original relative paths");
    var source = new CatalogMod("Fixture-Mod", "Fixture", "1.0.0", "", "", "", "", [], "Thunderstore");
    var v1 = library.Import(Zip("pinned1", ("plugins/Pinned/a.txt", "v1")), source); library.Active.Mods.First(m => m.PackageId == v1.Id).Pinned = true;
    Reject(() => library.Import(Zip("pinned2", ("plugins/Pinned/a.txt", "v2")), source with { Version = "2.0.0" }), "pinned mod replacement refused"); Check(library.State.Packages.Any(p => p.Id == v1.Id) && library.Active.Mods.Any(m => m.PackageId == v1.Id) && !library.State.Packages.Any(p => p.SourceId == source.Id && p.Version == "2.0.0"), "failed import leaves profile and library consistent");
    string loaderZip = Zip("loader", ("BepInEx/core/BepInEx.Unity.IL2CPP.dll", "new loader"), ("winhttp.dll", "new doorstop"), ("doorstop_config.ini", "enabled=true"));
    var loaderBackup = LoaderInstaller.Install(library, loaderZip, LoaderInstaller.Recommended); Check(File.ReadAllText(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")) == "new loader" && File.Exists(Path.Combine(game, "BepInEx/plugins/Old/existing.txt")), "loader install preserves plugins");
    LoaderInstaller.Restore(library, loaderBackup); Check(File.ReadAllText(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")) == "fixture" && !File.Exists(Path.Combine(game, "winhttp.dll")), "loader rollback restores and removes introduced files");
    var parent = source with { Dependencies = ["Other-Library-1.0.0", "BepInEx-BepInExPack_IL2CPP-6.0.755"] }; var dependency = source with { Id = "Other-Library" }; Check(Sources.Resolve(parent, [parent, dependency]).Select(p => p.Id).SequenceEqual(new[] { dependency.Id, parent.Id }), "Thunderstore dependency plan routes loader separately");
    Check(Sources.NexusAddress("nxm://nivalisnights/mods/12/files/34?key=abc&expires=123").File == 34, "NXM address parsing"); Reject(() => Sources.NexusAddress("https://example.com/nivalisnights/mods/12"), "reject wrong Nexus host");
    var fake = new FakeHandler(); using var sources = new Sources(fake);
    var nexus = await sources.Nexus("12", "fixture-secret"); Check(nexus.Single().FileId == 34 && fake.Keys.All(k => k == "fixture-secret"), "Nexus API metadata and credentials confined to API requests");
    string download = await sources.NexusDownload(nexus[0], "fixture-secret", "nxm://nivalisnights/mods/12/files/34?key=signed&expires=4102444800&user_id=5"); Check(download == "https://example.com/mod.zip" && fake.LastQuery == "?key=signed&expires=4102444800", "NXM download query passes only signed key and expiry");
    if (args.Contains("--network"))
    {
        using var live = new Sources(); var catalog = await live.Thunderstore(); Check(catalog.Count > 0 && catalog.Any(p => p.Id.Contains("HVizeu")), "live Thunderstore Nivalis catalogue");
        var latest = await LoaderInstaller.Latest(live, library.Root, CancellationToken.None); Check(latest.Url.Contains("Unity.IL2CPP-win-x64"), "live official BepInEx release discovery");
        Console.WriteLine("Official build: " + latest.Version);
        string officialZip = Path.Combine(root, "official-loader.zip"); await live.Download(latest.Url, officialZip);
        string officialBackup = LoaderInstaller.Install(library, officialZip, latest); Check(new FileInfo(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")).Length > 1000, "official loader archive installed into disposable fixture"); LoaderInstaller.Restore(library, officialBackup);
        var published = catalog.First(p => p.Id.Contains("HVizeu") && p.Name.Contains("Time"));
        string publishedZip = Path.Combine(root, "published-mod.zip"); await live.Download(published.DownloadUrl, publishedZip);
        var package = library.Import(publishedZip, published); Check(package.SourceId == published.Id && package.Plugins.Count > 0, "live Thunderstore package download and metadata import");
    }
    if (args.FirstOrDefault(a => a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) is { } real)
    {
        var imported = library.Import(Path.GetFullPath(real)); Check(imported.Plugins.Any(p => p.Guid == "hvizeu.nivalis.slowdays" && p.Version == "2.0.12"), "real Better Time release metadata import");
    }
    Console.WriteLine($"\n{passed} checks passed. All writes confined to disposable fixtures.");
}
finally { FileSafety.DeleteOwned(FileSafety.ResolveFolder(Path.GetTempPath()), root); }

public sealed class FakeHandler : HttpMessageHandler
{
    public List<string> Keys { get; } = []; public string LastQuery { get; private set; } = "";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Keys.Add(request.Headers.GetValues("apikey").Single()); LastQuery = request.RequestUri!.Query;
        string text = request.RequestUri.AbsolutePath.EndsWith("download_link.json") ? "[{\"URI\":\"https://example.com/mod.zip\"}]" : request.RequestUri.AbsolutePath.EndsWith("files.json") ? "{\"files\":[{\"name\":\"Fixture\",\"file_id\":34,\"category_id\":1,\"version\":\"1.0.0\"}]}" : "{\"name\":\"Fixture\",\"summary\":\"Test\",\"picture_url\":\"\"}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") });
    }
}
[AttributeUsage(AttributeTargets.Class)] public sealed class BepInPlugin(string guid, string name, string version) : Attribute { public string Value => guid + name + version; }
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class BepInDependency : Attribute { public BepInDependency(string id, string version) { throw new Exception("Metadata must not execute code"); } public BepInDependency(string id, int flags) { throw new Exception("Metadata must not execute code"); } }
[BepInPlugin("fixture.plugin", "Fixture", "1.0.0"), BepInDependency("hard", "2.0.0"), BepInDependency("soft", 2), BepInDependency("fixture.ranged", ">=1.03")] public sealed class TestPlugin;
