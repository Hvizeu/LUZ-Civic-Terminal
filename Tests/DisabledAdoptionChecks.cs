using Luz;

internal static class DisabledAdoptionChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        string game = Path.Combine(root, "disabled-adoption-game");
        Directory.CreateDirectory(Path.Combine(game, "BepInEx/core"));
        File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture, never execute");
        File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        File.WriteAllText(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"), "fixture");
        using var library = new Library(Path.Combine(root, "disabled-adoption-library"));
        library.State.GameFolder = game;
        var deployment = new Deployment(library);
        BackupInfo Apply() => deployment.Apply(Compatibility.Acknowledge(Compatibility.Inspect(library)));
        Apply(); // Existing ownership snapshot does not include the external install.
        string folder = Path.Combine(game, "BepInEx/plugins/Adopted");
        Directory.CreateDirectory(folder);
        string binary = Path.Combine(folder, "Adopted.dll");
        File.WriteAllText(binary, "native fixture");
        File.WriteAllText(Path.Combine(folder, "helper.asi"), "native helper fixture");
        File.WriteAllText(Path.Combine(folder, "content.txt"), "companion content");
        library.ImportInstalled(game);
        library.Active.Mods.Single().Enabled = false;
        library.Save();
        library.ImportInstalled(game);
        check(library.Active.Mods.All(m => !m.Enabled), "Reimport preserves disabled adoption settings");
        check(deployment.UnmanagedBinaries().Count == 0, "Disabled imported DLL and ASI are recognized despite an older ownership snapshot");
        File.WriteAllText(binary, "changed after import");
        check(deployment.UnmanagedBinaries().Contains("plugins/Adopted/Adopted.dll"), "Disabled adoption refuses bytes changed after import");
        reject(() => Apply(), "Changed disabled binary blocks Apply");
        check(File.ReadAllText(binary) == "changed after import", "Rejected Apply preserves changed binary");
        File.WriteAllText(binary, "native fixture");
        string unknown = Path.Combine(folder, "unknown.dll");
        File.WriteAllText(unknown, "unknown");
        reject(() => Apply(), "An unimported binary still blocks Apply");
        File.Delete(unknown);
        var backup = Apply();
        check(!Directory.Exists(folder), "Apply removes the deliberately disabled adopted mod and companions");
        check(File.ReadAllText(Path.Combine(library.Root, "backups", backup.Id, "plugins/Adopted/content.txt")) == "companion content", "Disabled adopted mod remains complete in the restore point");
        deployment.Restore(backup);
        check(File.Exists(binary) && deployment.UnmanagedBinaries().Count == 0, "Restored adopted files remain manageable with disabled entries");
        library.Active.Mods.Single().Enabled = true;
        Apply();
        library.Active.Mods.Single().Enabled = false;
        Apply();
        check(!File.Exists(binary), "Enable Apply then disable Apply works after adoption");
    }
}
