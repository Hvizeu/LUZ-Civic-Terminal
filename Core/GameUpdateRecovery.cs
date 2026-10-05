using System.Text;
using System.Text.RegularExpressions;

namespace Luz;

public static class GameUpdateRecovery
{
    public static string Inspect(string game)
    {
        GameFiles.Validate(game);
        string manifest = Path.Combine(Directory.GetParent(game)!.Parent!.FullName, "appmanifest_" + GameFiles.AppId + ".acf");
        string build = File.Exists(manifest) ? Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s*\"(?<id>\\d+)\"").Groups["id"].Value : "";
        var text = new StringBuilder("Installed Steam build: " + (build.Length > 0 ? build : "not available") + ".\n");
        string config = Path.Combine(game, "BepInEx/config/BepInEx.cfg");
        if (File.Exists(config) && Regex.IsMatch(File.ReadAllText(config), @"(?im)^\s*UpdateInteropAssemblies\s*=\s*false\s*$"))
            text.AppendLine("BepInEx automatic binding regeneration is disabled. Use Refresh game bindings to re-enable it.");
        string bindings = Path.Combine(game, "BepInEx/interop/Assembly-CSharp.dll");
        if (File.Exists(bindings) && File.GetLastWriteTimeUtc(bindings) < File.GetLastWriteTimeUtc(Path.Combine(game, "GameAssembly.dll")))
            text.AppendLine("Generated game bindings predate the game binary. BepInEx normally refreshes them on the next launch; use recovery if that launch fails.");
        string log = Path.Combine(game, "BepInEx/LogOutput.log");
        if (File.Exists(log)) {
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Max(0, stream.Length - 262144), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n').Where(l => Regex.IsMatch(l, "requires game build|Unsupported GameAssembly|mismatch|Failed to generate|Could not load file or assembly", RegexOptions.IgnoreCase)).TakeLast(8).ToArray();
            if (lines.Length > 0) text.AppendLine("\nLast saved game log (may belong to an earlier launch):\n" + string.Join('\n', lines.Select(l => l.TrimEnd('\r'))));
        }
        text.AppendLine("\nA mod reporting 'requires game build' or 'Unsupported GameAssembly' needs a compatible mod release. Refreshing bindings or updating LUZ cannot override that requirement. Keep object frameworks and packs installed when loading saves that use them.");
        return text.ToString();
    }
    public static string EnableRegeneration(string config)
    {
        string normalized = config.Replace("\r\n", "\n"); var lines = normalized.Split('\n').ToList();
        int section = lines.FindIndex(l => l.Trim().Equals("[IL2CPP]", StringComparison.OrdinalIgnoreCase));
        if (section < 0) { lines.Add("[IL2CPP]"); lines.Add("UpdateInteropAssemblies = true"); return string.Join('\n', lines); }
        int end = lines.FindIndex(section + 1, l => l.TrimStart().StartsWith('[')); if (end < 0) end = lines.Count;
        var settings = Enumerable.Range(section + 1, end - section - 1).Where(i => Regex.IsMatch(lines[i], @"^\s*UpdateInteropAssemblies\s*=", RegexOptions.IgnoreCase)).ToArray();
        if (settings.Length > 1) throw new InvalidDataException("Duplicate BepInEx regeneration settings need manual review.");
        if (settings.Length == 1) lines[settings[0]] = "UpdateInteropAssemblies = true"; else lines.Insert(end, "UpdateInteropAssemblies = true");
        return string.Join('\n', lines);
    }
    public static string Refresh(string game)
    {
        GameFiles.Validate(game); GameFiles.RequireClosed();
        string bep = FileSafety.PortableDestination(game, "BepInEx"); FileSafety.NoLinks(bep);
        if (!File.Exists(Path.Combine(bep, "core/BepInEx.Unity.IL2CPP.dll"))) throw new InvalidDataException("Install BepInEx IL2CPP before refreshing game bindings.");
        string config = Path.Combine(bep, "config/BepInEx.cfg");
        string previous = File.Exists(config) ? File.ReadAllText(config) : "";
        var custom = Regex.Match(previous, @"(?im)^\s*InteropAssemblyPath\s*=\s*(.*?)\s*$");
        if (custom.Success && custom.Groups[1].Value.Replace('\\','/') != "{BepInEx}/interop") throw new InvalidDataException("A custom BepInEx interop path is configured. Review that path before refreshing bindings.");
        string next = EnableRegeneration(previous);
        string backup = Path.Combine(bep, "interop-backups", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
        bool hadConfig = File.Exists(config), moved = false;
        string interop = Path.Combine(bep, "interop");
        if (hadConfig) File.Copy(config, Path.Combine(backup, "BepInEx.cfg"));
        try {
            if (Directory.Exists(interop)) { Directory.Move(interop, Path.Combine(backup,"interop")); moved = true; }
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            File.WriteAllText(config + ".luz-refresh", next); File.Move(config + ".luz-refresh", config, true);
            JsonFiles.Write(Path.Combine(backup, "recovery.json"), new { Game = game, HadConfig = hadConfig, MovedInterop = moved });
            return backup;
        } catch {
            if (moved) Directory.Move(Path.Combine(backup,"interop"), interop);
            if (hadConfig) File.Copy(Path.Combine(backup,"BepInEx.cfg"), config, true); else File.Delete(config);
            File.Delete(config + ".luz-refresh"); throw;
        }
    }
}
