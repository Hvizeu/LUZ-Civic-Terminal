using System.IO.Compression;
using Luz;

internal static class ImportRecoveryChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string scope = Path.Combine(root, "import-recovery");
        Directory.CreateDirectory(scope);
        string Archive(string name, string destination, string contents)
        {
            string path = Path.Combine(scope, name + ".zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry(destination).Open());
            writer.Write(contents);
            return path;
        }
        string archive = Archive("incoming", "plugins/Example/Example.dll", "native fixture, never execute");
        var source = new CatalogMod("example", "Example", "1.0.0", "", "", "", "", [], "Fixture");
        using (var library = new Library(Path.Combine(scope, "partial")))
        {
            library.Save();
            string id = PackagePreparation.Identity(FileSafety.Hash(archive)), orphan = library.PackageRoot(id);
            Directory.CreateDirectory(Path.Combine(orphan, "payload/plugins/Example"));
            File.WriteAllText(Path.Combine(orphan, "package.json"), "broken metadata");
            File.WriteAllText(Path.Combine(orphan, "payload/plugins/Example/Example.dll"), "partial original");
            var package = library.Import(archive, source);
            check(package.Id == id && File.ReadAllText(Path.Combine(orphan, "payload/plugins/Example/Example.dll")) == "native fixture, never execute", "Partial orphan is rebuilt from validated incoming bytes");
            string preserved = Directory.GetDirectories(Path.Combine(library.Root, "import-recovery")).Single();
            check(File.ReadAllText(Path.Combine(preserved, "package.json")) == "broken metadata" && File.ReadAllText(Path.Combine(preserved, "payload/plugins/Example/Example.dll")) == "partial original", "Original orphan metadata and payload remain recoverable");
            library.Active.Mods.Single().Enabled = false;
            library.Active.Mods.Single().Pinned = true;
            library.Save();
            library.Import(archive, source);
            check(library.State.Packages.Count == 1 && library.Active.Mods.Count == 1 && !library.Active.Mods.Single().Enabled && library.Active.Mods.Single().Pinned, "Repeated recovered import preserves disabled and pinned settings without duplicates");

            string invalid = Archive("invalid", "BepInEx/core/loader.dll", "invalid loader input");
            string invalidOrphan = library.PackageRoot(PackagePreparation.Identity(FileSafety.Hash(invalid)));
            Directory.CreateDirectory(invalidOrphan);
            File.WriteAllText(Path.Combine(invalidOrphan, "keep.txt"), "preserve invalid import original");
            reject(() => library.Import(invalid), "Invalid source cannot replace an orphan");
            check(File.ReadAllText(Path.Combine(invalidOrphan, "keep.txt")) == "preserve invalid import original" && Directory.GetDirectories(Path.Combine(library.Root, "import-recovery")).Length == 1, "Rejected source leaves orphan at its original location");

            string replacement = Archive("replacement", "plugins/Example/Example.dll", "updated native fixture");
            string replacementOrphan = library.PackageRoot(PackagePreparation.Identity(FileSafety.Hash(replacement)));
            Directory.CreateDirectory(replacementOrphan);
            File.WriteAllText(Path.Combine(replacementOrphan, "keep.txt"), "original replacement folder");
            string before = File.ReadAllText(Path.Combine(library.Root, "registry.json"));
            reject(() => library.Import(replacement, source with { Version = "2.0.0" }), "Pinned replacement is still rejected during orphan repair");
            check(File.ReadAllText(Path.Combine(replacementOrphan, "keep.txt")) == "original replacement folder" && File.ReadAllText(Path.Combine(library.Root, "registry.json")) == before && library.Active.Mods.Single().PackageId == package.Id && library.State.Packages.Count == 1, "Rejected publication restores original folder and profile");
            library.Active.Mods.Single().Pinned = false;
            library.Save();
            if (OperatingSystem.IsWindows())
            {
                using (var locked = new FileStream(Path.Combine(library.Root, "registry.json.previous"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    reject(() => library.Import(replacement, source with { Version = "2.0.0" }), "Registry write failure rejects recovered import");
                check(File.ReadAllText(Path.Combine(replacementOrphan, "keep.txt")) == "original replacement folder" && library.Active.Mods.Single().PackageId == package.Id && library.State.Packages.Count == 1, "Registry write failure restores orphan and in-memory profile");
            }
            library.Import(replacement, source with { Version = "2.0.0" });
            check(!library.Active.Mods.Single().Enabled, "Successful repaired update retains disabled state");
        }

        string data = Path.Combine(scope, "adoption"), game = Path.Combine(scope, "game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx/core"));
        Directory.CreateDirectory(Path.Combine(game, "BepInEx/plugins/Example"));
        File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture, never execute");
        File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        File.WriteAllText(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"), "fixture");
        File.WriteAllText(Path.Combine(game, "BepInEx/plugins/Example/Example.dll"), "native fixture");
        File.WriteAllText(Path.Combine(game, "BepInEx/plugins/Example/content.txt"), "content companion");
        string oldRegistry, orphanId;
        using (var library = new Library(data))
        {
            library.State.GameFolder = game;
            library.Save();
            oldRegistry = File.ReadAllText(Path.Combine(data, "registry.json"));
            library.ImportInstalled(game);
            orphanId = library.Active.Mods.Single().PackageId;
        }
        // Reproduce a registry restore after package creation, as opposed to
        // a fake exception independent of the actual filesystem state.
        File.WriteAllText(Path.Combine(data, "registry.json"), oldRegistry);
        using (var library = new Library(data))
        {
            library.ImportInstalled(game);
            check(library.Active.Mods.Single().PackageId == orphanId && Directory.GetDirectories(Path.Combine(data, "import-recovery")).Length == 1, "Import Existing Installation repairs the exact complete orphan after registry restore");
            library.Active.Mods.Single().Enabled = false;
            library.Save();
        }
        using (var reopened = new Library(data))
        {
            reopened.ImportInstalled(game);
            check(reopened.State.Packages.Count == 1 && !reopened.Active.Mods.Single().Enabled, "Reopen and reimport preserve the repaired disabled selection");
            var deployment = new Deployment(reopened);
            check(deployment.UnmanagedBinaries().Count == 0, "Recovered disabled installation passes ownership checks");
            var backup = deployment.Apply(Compatibility.Acknowledge(Compatibility.Inspect(reopened)));
            check(!Directory.Exists(Path.Combine(game, "BepInEx/plugins/Example")) && File.Exists(Path.Combine(data, "backups", backup.Id, "plugins/Example/content.txt")), "Apply removes disabled recovered files into a complete restore point");
            deployment.Restore(backup);
            reopened.ImportInstalled(game);
            check(reopened.Active.Mods.Count == 1 && !reopened.Active.Mods.Single().Enabled && File.ReadAllText(Path.Combine(game, "BepInEx/plugins/Example/content.txt")) == "content companion", "Restore then reimport completes without duplication or losing companions");
        }
    }
}
