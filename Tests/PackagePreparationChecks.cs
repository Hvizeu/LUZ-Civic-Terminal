using System.IO.Compression;
using Luz;

internal static class PackagePreparationChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string scope = Path.Combine(root, "package-layouts"); Directory.CreateDirectory(scope);
        using var library = new Library(Path.Combine(scope, "library"));
        int sequence = 0;
        string Archive(params (string Path, string Value)[] files)
        {
            string path = Path.Combine(scope, (++sequence) + ".zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files) { using var writer = new StreamWriter(zip.CreateEntry(file.Path).Open()); writer.Write(file.Value); }
            return path;
        }
        foreach (string prefix in new[] { "", "Wrap/", "Wrap/BepInEx/", "BepInEx/" })
        {
            var package = library.Import(Archive((prefix + "plugins/Foo/Foo.dll", "native helper fixture"),
                (prefix + "plugins/Foo/assets/value.json", "selected content"), (prefix + "plugins/Foo/LUZ/mod-support.json", "descriptor fixture"),
                (prefix + "config/foo.cfg", "selected config"), ("manifest.json", "{\"name\":\"Fixture\",\"version_number\":\"2.3.4\",\"dependencies\":[\"Owner-Dependency-1.0.0\"]}")));
            check(package.Files.Order().SequenceEqual(new[] { "config/foo.cfg", "plugins/Foo/Foo.dll", "plugins/Foo/LUZ/mod-support.json", "plugins/Foo/assets/value.json" }.Order()), "Exact wrapped plugin/config/descriptor mapping: " + prefix);
            check(File.ReadAllText(Path.Combine(library.PackageRoot(package.Id), "payload/config/foo.cfg")) == "selected config"
                && package.Version == "2.3.4" && package.Dependencies.Single() == "Owner-Dependency-1.0.0", "Selected content and package declarations survive: " + prefix);
        }
        var outerSupport = library.Import(Archive(("Wrap/BepInEx/plugins/Foo/Foo.dll", "native helper"), ("Wrap/LUZ/mod-support.json", "support"), ("Wrap/LUZ/adapters/adapter.dll", "adapter")));
        check(outerSupport.Files.Contains("plugins/Foo/LUZ/mod-support.json") && outerSupport.Files.Contains("plugins/Foo/LUZ/adapters/adapter.dll"), "An unambiguous outer support directory stays with its plugin and adapters");
        var config = library.Import(Archive(("Wrap/BepInEx/config/foo.cfg", "config only")));
        check(config.Files.SequenceEqual(new[] { "config/foo.cfg" }), "Configuration-only imports use the existing config deployment area");
        var loose = library.Import(Archive(("Wrap/Foo/Foo.dll", "native helper"), ("Wrap/Foo/assets/icon.png", "asset"), ("Wrap/Foo/data.json", "data"), ("Wrap/README.md", "reference")));
        check(loose.Files.Order().SequenceEqual(new[] { "plugins/Foo/Foo.dll", "plugins/Foo/assets/icon.png", "plugins/Foo/data.json" }.Order())
            && loose.ReferenceFiles.Contains("Wrap/README.md"), "Loose wrapper preserves its mod folder and companions, retaining outer reference material");
        reject(() => library.Import(Archive(("Mods/Example.dll", "Melon fixture"))), "MelonLoader package cannot become a BepInEx plugin");
        reject(() => library.Import(Archive(("SaveEditor.exe", "tool"), ("Helper.dll", "helper"))), "Standalone tools cannot deploy through ordinary import");
        reject(() => library.Import(Archive(("Wrap/BepInEx/core/BepInEx.Unity.IL2CPP.dll", "loader"), ("Wrap/winhttp.dll", "loader"))), "Wrapped loader uses loader setup instead of mod import");
        reject(() => library.Import(Archive(("One/plugins/Foo.dll", "one"), ("Two/plugins/Bar.dll", "two"))), "Ambiguous sibling roots require review");
        reject(() => library.Import(Archive(("BepInEx/plugins/Foo.dll", "one"), ("external-data.json", "unmapped companion"))), "Unmapped companions receive an explicit error");
        string archive = Archive(("plugins/Cache/Foo.dll", "immutable fixture"));
        var prepared = library.Import(archive);
        check(prepared.PreparationVersion == PackagePreparation.Version && prepared.Id != prepared.Sha256[..32], "Preparation semantics participate in immutable identity");
        string before = File.ReadAllText(Path.Combine(library.PackageRoot(prepared.Id), "package.json"));
        check(library.Import(archive).Id == prepared.Id && File.ReadAllText(Path.Combine(library.PackageRoot(prepared.Id), "package.json")) == before, "Identical imports reuse immutable prepared packages");
        foreach (string prefix in new[] { "", "Release/" })
        {
            var text = library.Import(Archive((prefix + "TextMod.dll", "native helper fixture"), (prefix + "words.txt", "runtime dictionary")));
            string companion = text.Files.Single(f => f.EndsWith("words.txt", StringComparison.OrdinalIgnoreCase));
            check(File.ReadAllText(Path.Combine(library.PackageRoot(text.Id), "payload", companion)) == "runtime dictionary", "Loose runtime text companion deploys: " + prefix);
        }
        var latest = LoaderInstaller.ParseLatest("<a href='/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2Babc.zip'>older</a>"
            + "<a href='/projects/bepinex_be/790/BepInEx-Unity.Mono-win-x64-6.0.0-be.790%2Bdef.zip'>wrong runtime</a>"
            + "<a href='/projects/bepinex_be/789/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789%2Bghi.zip'>newer</a>");
        check(latest.Version == "6.0.0-be.789" && latest.Url.StartsWith("https://builds.bepinex.dev/"), "Latest loader resolver sorts available matching artifacts and ignores Mono");
        reject(() => LoaderInstaller.ParseLatest("<a href='https://example.com/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.999+abc.zip'>unexpected host</a>"), "Loader resolver rejects a foreign distribution host");
        reject(() => LoaderInstaller.ParseLatest("no matching artifacts"), "Missing official loader is an error rather than a claimed latest fallback");
    }
}
