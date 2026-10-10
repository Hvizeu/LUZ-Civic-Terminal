using System.IO.Compression;
using System.Text;
using Luz;

internal static class RepairChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string fixtures = Path.Combine(root, "repairs"); Directory.CreateDirectory(fixtures);
        string Zip(string name, params string[] files)
        {
            string path = Path.Combine(fixtures, name + ".zip");
            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files) { using var writer = new StreamWriter(archive.CreateEntry(file).Open()); writer.Write("native fixture, never execute: " + file); }
            return path;
        }
        using (var library = new Library(Path.Combine(fixtures, "import-library")))
        {
            foreach (var prefix in new[] { "", "Nested/", "BepInEx/plugins/Orders/" })
            {
                var package = library.Import(Zip("layout-" + library.State.Packages.Count, prefix + "Orders.dll", prefix + "Orders.asi"));
                check(package.Files.Count == 2 && package.Files.Select(Path.GetDirectoryName).Distinct().Count() == 1, "DLL/ASI companions remain in the same folder: " + prefix);
            }
            int before = library.State.Packages.Count;
            reject(() => library.Import(Zip("mixed", "BepInEx/plugins/Orders.dll", "Orders.asi")), "Mixed archive never silently drops an ASI");
            check(library.State.Packages.Count == before, "Failed mixed import leaves the library unchanged");
            string game = Path.Combine(fixtures, "game"); Directory.CreateDirectory(Path.Combine(game, "BepInEx/core"));
            File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture, never execute"); File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
            File.WriteAllText(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"), "fixture");
            library.State.GameFolder = game; library.Active.Mods = [new() { PackageId = library.State.Packages[0].Id }]; library.Save();
            var deployment = new Deployment(library); var backup = deployment.Apply(); deployment.Apply();
            var paths = library.State.Packages[0].Files;
            check(paths.All(rel => File.Exists(Path.Combine(game, "BepInEx", rel))), "Repeated Apply preserves both native companion binaries");
            var other = new CatalogMod("fixture.other", "Unrelated mod", "1.0.0", "", "", "", "", [], "Fixture");
            library.Import(Zip("other-first", "plugins/Other/Other.dll"), other); deployment.Apply();
            library.Import(Zip("other-update", "plugins/Other/NewName.dll"), other with { Version = "2.0.0" }); deployment.Apply();
            check(paths.All(rel => File.Exists(Path.Combine(game, "BepInEx", rel))) && File.Exists(Path.Combine(game, "BepInEx/plugins/Other/NewName.dll")) && !File.Exists(Path.Combine(game, "BepInEx/plugins/Other/Other.dll")), "Unrelated mod updates retain DLL/ASI companions and retire only their own former files");
            File.WriteAllText(Path.Combine(game, "BepInEx/plugins/manual.asi"), "manually installed helper");
            reject(() => deployment.Apply(), "Unmanaged ASI blocks replacement before it can disappear");
            check(File.Exists(Path.Combine(game, "BepInEx/plugins/manual.asi")), "Blocked Apply preserves the unmanaged ASI");
            library.ImportInstalled(game); deployment.Apply();
            check(File.Exists(Path.Combine(game, "BepInEx/plugins/manual.asi")), "Explicit adoption preserves the manual companion on Apply");
            var copied = library.CreateProfile("No mods", false); deployment.Apply();
            check(!File.Exists(Path.Combine(game, "BepInEx/plugins/manual.asi")), "Disabled or absent packages do not leak into another profile");
            deployment.Restore(backup);
            check(!File.Exists(Path.Combine(game, "BepInEx/plugins/manual.asi")), "Rollback restores the matching deployment rather than merging manual additions");
        }
        void Failure(string folder, RegistryFailure expected)
        {
            try { using var unexpected = new Library(folder); throw new Exception("Registry failure was accepted"); }
            catch (RegistryRecoveryException error) { check(error.Reason == expected && error.Message.Contains(Path.Combine(folder, "registry.json")), "Specific registry failure and actual path: " + expected); }
        }
        string missing = Path.Combine(fixtures, "missing"); Directory.CreateDirectory(Path.Combine(missing, "library/retained")); File.WriteAllText(Path.Combine(missing, "library/retained/data"), "preserve");
        Failure(missing, RegistryFailure.Missing);
        string malformed = Path.Combine(fixtures, "malformed"); Directory.CreateDirectory(malformed); File.WriteAllText(Path.Combine(malformed, "registry.json"), "{"); Failure(malformed, RegistryFailure.Malformed);
        string schema = Path.Combine(fixtures, "schema"); Directory.CreateDirectory(schema); File.WriteAllText(Path.Combine(schema, "registry.json"), "{\"Schema\":99,\"Profiles\":[]}"); Failure(schema, RegistryFailure.UnsupportedSchema);
        string empty = Path.Combine(fixtures, "empty"); Directory.CreateDirectory(empty); File.WriteAllText(Path.Combine(empty, "registry.json"), "{\"Schema\":2,\"Profiles\":[]}"); Failure(empty, RegistryFailure.EmptyProfiles);
        check(RegistryRecovery.ValidBackup(malformed) == null, "Recovery refuses an absent or invalid backup");
        string recovery = Path.Combine(fixtures, "recovery");
        using (var library = new Library(recovery)) { library.Save(); library.CreateProfile("Second", false); }
        File.WriteAllText(Path.Combine(recovery, "registry.json"), "bad registry");
        check(RegistryRecovery.ValidBackup(recovery) != null, "Recovery offers only a validated previous registry");
        string evidence = RegistryRecovery.Restore(recovery);
        check(File.ReadAllText(evidence) == "bad registry", "Registry restoration preserves original failure evidence");
        using (var library = new Library(recovery)) check(library.Active.Name == "Everyday", "Restored registry opens with its retained profile");
        string diagnostics = RegistryRecovery.ExportDiagnostics(malformed, new InvalidDataException("fixture startup failure"));
        check(File.ReadAllText(Path.Combine(diagnostics, "startup.txt")).Contains(malformed) && File.ReadAllText(Path.Combine(diagnostics, "registry.json")) == "{", "Startup diagnostics work without an open library");
        string fresh = Path.Combine(fixtures, "fresh"); using (var library = new Library(fresh)) check(library.State.Profiles.Count == 1, "Genuine first run still creates its initial profile");
    }
}
