using System.IO.Compression;
using System.Text.Json;
using Luz;

if (args.Length != 3) throw new ArgumentException("Supply migration archive directory, bundled payload directory and report path.");
string root = Path.Combine(Path.GetTempPath(), "LuzPackageChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    using var library = new Library(Path.Combine(root, "library"));
    var expected = new Dictionary<string, string> {
        ["hvizeu.nivalis.slowdays"] = "2.3.0", ["hvizeu.nivalis.unofficialpatch"] = "0.5.0",
        ["hvizeu.nivalis.citylife"] = "1.2.0", ["hvizeu.nivalis.furnishings"] = "0.4.0", ["hvizeu.nivalis.furniturelab"] = "0.2.0" };
    foreach (string archive in Directory.GetFiles(args[0], "*-LUZ-Migration.zip").Order()) library.Import(archive);
    var manifest = ModContinuity.VerifyBundle(args[1]);
    string runtime = Path.Combine(root, "runtime.zip");
    using (var zip = ZipFile.Open(runtime, ZipArchiveMode.Create))
        foreach (string name in manifest.Keys) zip.CreateEntryFromFile(Path.Combine(args[1], name), "BepInEx/plugins/LUZContinuity/" + name);
    library.Import(runtime);
    var plugins = library.State.Packages.SelectMany(p => p.Plugins).ToList();
    foreach (var pair in expected)
    {
        var plugin = plugins.Single(p => p.Guid == pair.Key);
        if (plugin.Version != pair.Value || !plugin.Dependencies.Any(d => d.Guid == ModContinuity.PluginGuid && !d.Optional)) throw new InvalidDataException("Migration identity/dependency differs: " + pair.Key);
    }
    if (plugins.Count != expected.Count + 1 || !plugins.Any(p => p.Guid == ModContinuity.PluginGuid)) throw new InvalidDataException("Unexpected or missing plugin identity.");
    var issues = ModPlanner.Check(library.Active, library.State.Packages);
    if (issues.Any(i => i.Severity == "Error")) throw new InvalidDataException(string.Join("; ", issues.Select(i => i.Message)));
    var city = library.State.Packages.Single(p => p.Plugins.Any(i => i.Guid == "hvizeu.nivalis.citylife"));
    if (!city.Files.Any(f => f.EndsWith("/PlayerLocomotion/citylife-player-locomotion", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Complete City Life assets are missing.");
    var report = new { ImportedPackages = library.State.Packages.Count, Plugins = plugins.Select(p => new { p.Guid, p.Version }), CityLifeFiles = city.Files.Count, Issues = issues, Scope = "Disposable import library only; no installation, deployment or game execution." };
    File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("PASS six complete packages imported with exact plugin identities, hard runtime dependencies and retained City Life assets.");
}
finally { FileSafety.DeleteOwned(Path.GetTempPath(), root); }
