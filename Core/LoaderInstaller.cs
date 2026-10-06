using System.Text.RegularExpressions;

namespace Luz;

public sealed record LoaderRelease(string Version, string Url);
public sealed record LoaderBackup(string Game, List<string> Files, List<string> Existing);
public static class LoaderInstaller
{
    public static readonly LoaderRelease Recommended = new("6.0.0-be.788", "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip");
    public static async Task<LoaderRelease> Latest(Sources sources, string root, CancellationToken ct)
    {
        string file = FileSafety.Under(root, "downloads/loader-index-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            await sources.Download("https://builds.bepinex.dev/projects/bepinex_be", file, ct);
            string html = await File.ReadAllTextAsync(file, ct);
            var m = Regex.Match(html, "href=[\"'](?<url>[^\"']*BepInEx-Unity\\.IL2CPP-win-x64-(?<version>[^\"'+%]+)(?:%2[Bb]|\\+)[^\"']+\\.zip)[\"']");
            if (!m.Success) throw new InvalidDataException("The official build listing changed. The recommended build remains available.");
            string url = new Uri(new Uri("https://builds.bepinex.dev/projects/bepinex_be/"), System.Net.WebUtility.HtmlDecode(m.Groups["url"].Value)).AbsoluteUri;
            if (new Uri(url).Host != "builds.bepinex.dev") throw new InvalidDataException("Unexpected loader download host.");
            return new(m.Groups["version"].Value, url);
        }
        finally { File.Delete(file); }
    }
    public static string Install(Library library, string archive, LoaderRelease release)
    {
        string game = library.State.GameFolder; GameFiles.Validate(game); GameFiles.RequireClosed(); FileSafety.NoLinks(game);
        string stage = FileSafety.Under(library.Root, "staging/loader-" + Guid.NewGuid().ToString("N"));
        string backup = FileSafety.Under(library.Root, "loader-backups/" + Guid.NewGuid().ToString("N"));
        try
        {
            FileSafety.Extract(archive, stage);
            if (!File.Exists(Path.Combine(stage, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")) || !File.Exists(Path.Combine(stage, "winhttp.dll"))) throw new InvalidDataException("This is not a Windows x64 IL2CPP loader archive.");
            var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(stage, f).Replace('\\', '/')).ToList();
            foreach (var f in files)
                if (!(f.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase) || f.StartsWith("dotnet/", StringComparison.OrdinalIgnoreCase) || f is "winhttp.dll" or "doorstop_config.ini" or ".doorstop_version" or "changelog.txt" || Path.GetFileName(f).StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Unexpected loader archive file: " + f);
            var existing = files.Where(f => File.Exists(FileSafety.PortableDestination(game, f))).ToList();
            foreach (var f in existing) { string dest = FileSafety.Under(backup, "files/" + f); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(FileSafety.PortableDestination(game, f), dest); }
            JsonFiles.Write(Path.Combine(backup, "loader-backup.json"), new LoaderBackup(game, files, existing));
            JsonFiles.Write(Path.Combine(library.Root, "loader-pending.json"), backup);
            try
            {
                foreach (var f in files) { string dest = FileSafety.PortableDestination(game, f); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(FileSafety.Under(stage, f), dest, true); }
                library.State.LoaderVersion = release.Version; library.Save(); File.Delete(Path.Combine(library.Root, "loader-pending.json"));
            }
            catch { Restore(library.Root, backup); throw; }
            return backup;
        }
        finally { FileSafety.DeleteOwned(library.Root, stage); }
    }
    public static void Restore(string root, string backup)
    {
        string checkedPath = FileSafety.Under(root, Path.GetRelativePath(root, backup)); FileSafety.NoLinks(checkedPath);
        var info = JsonFiles.Read<LoaderBackup>(Path.Combine(checkedPath, "loader-backup.json")); GameFiles.Validate(info.Game); GameFiles.RequireClosed(); FileSafety.NoLinks(info.Game);
        ValidateBackup(checkedPath, info);
        foreach (var f in info.Files)
        {
            string dest = FileSafety.PortableDestination(info.Game, f);
            if (info.Existing.Contains(f)) { Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(FileSafety.Under(checkedPath, "files/" + f), dest, true); }
            else File.Delete(dest);
        }
        File.Delete(Path.Combine(root, "loader-pending.json"));
    }

    internal static void ValidateBackup(string backup, LoaderBackup info)
    {
        if (info.Files == null || info.Existing == null || string.IsNullOrWhiteSpace(info.Game)) throw new InvalidDataException("Loader backup metadata is incomplete.");
        foreach (string f in info.Files) _ = FileSafety.Under(info.Game, f);
        // Check every restore source before replacing or deleting any game file.
        foreach (string f in info.Existing)
        {
            if (!info.Files.Contains(f)) throw new InvalidDataException("Loader backup contains an unexpected original file: " + f);
            string source = FileSafety.Under(backup, "files/" + f);
            FileSafety.NoLinks(source);
            if (!File.Exists(source)) throw new FileNotFoundException("Required loader backup file is missing: " + f);
        }
    }
}
