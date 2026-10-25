using System.Security.Cryptography;
using System.Text;

namespace Luz;

public sealed record PreparedFile(string Source, string Destination);
public sealed record PreparedContent(string Kind, IReadOnlyList<PreparedFile> Files, IReadOnlyList<string> References);

public static class PackagePreparation
{
    public const int Version = 2;
    public static string Identity(string inputHash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("LUZ preparation " + Version + "\n" + inputHash))).ToLowerInvariant()[..32];
    private static bool Area(string part) => part.Equals("plugins", StringComparison.OrdinalIgnoreCase) || part.Equals("patchers", StringComparison.OrdinalIgnoreCase) || part.Equals("config", StringComparison.OrdinalIgnoreCase);
    private static bool Reference(string file)
    {
        string name = Path.GetFileName(file), extension = Path.GetExtension(file).ToLowerInvariant();
        return name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("NOTICE", StringComparison.OrdinalIgnoreCase) || name.Equals("icon.png", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("README", StringComparison.OrdinalIgnoreCase) || name.StartsWith("CHANGELOG", StringComparison.OrdinalIgnoreCase)
            || extension is ".cs" or ".csproj" or ".sln" or ".props" or ".targets" or ".fs" or ".fsproj" or ".vb" or ".vbproj";
    }
    public static PreparedContent Prepare(string root, string packageId)
    {
        FileSafety.NoLinks(root);
        string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new InvalidDataException("The installed package is empty.");
        foreach (string file in files) _ = FileSafety.Under(root, file);
        if (files.Any(f => f.Split('/').Any(p => p.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase) || p.Equals("UserLibs", StringComparison.OrdinalIgnoreCase))
            || f.Split('/').SkipLast(1).LastOrDefault()?.Equals("Mods", StringComparison.OrdinalIgnoreCase) == true && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            return new("IncompatibleRuntime", [], files);
        if (files.Any(f => f.EndsWith("BepInEx/core/BepInEx.Unity.IL2CPP.dll", StringComparison.OrdinalIgnoreCase))
            || files.Any(f => Path.GetFileName(f).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase))) return new("Loader", [], files);
        if (files.Any(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) return new("Tool", [], files);

        string[] anchors = files.Select(f => f.Split('/')).Where(p => Array.FindIndex(p, s => s.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) >= 0)
            .Select(p => string.Join('/', p.Take(Array.FindIndex(p, s => s.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)) + 1)) + "/").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (anchors.Length > 1) throw new InvalidDataException("Multiple BepInEx package roots require review.");
        string? prefix = anchors.SingleOrDefault();
        if (prefix == null)
        {
            string[] roots = files.Select(f => f.Split('/')).Where(p => p.Take(p.Length - 1).Any(Area))
                .Select(p => string.Join('/', p.Take(Array.FindIndex(p, Area)))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (roots.Length > 1) throw new InvalidDataException("Multiple plugins/config/patchers roots require review.");
            if (roots.Length == 1) prefix = roots[0].Length == 0 ? "" : roots[0] + "/";
        }
        var mapped = new List<PreparedFile>(); var references = new List<string>();
        if (prefix != null)
        {
            foreach (string file in files)
            {
                string relative = file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? file[prefix.Length..] : "";
                string[] parts = relative.Split('/');
                if (parts.Length > 1 && Area(parts[0])) mapped.Add(new(file, parts[0].ToLowerInvariant() + "/" + string.Join('/', parts.Skip(1))));
                else if (("/" + file).Contains("/LUZ/", StringComparison.OrdinalIgnoreCase))
                {
                    string[] pluginRoots = files.Where(f => f.StartsWith(prefix + "plugins/", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        .Select(f => Path.GetDirectoryName(f[prefix.Length..])!.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (pluginRoots.Length != 1 || pluginRoots[0].Equals("plugins", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The outer LUZ support directory has no unambiguous plugin folder.");
                    int support = ("/" + file).IndexOf("/LUZ/", StringComparison.OrdinalIgnoreCase);
                    mapped.Add(new(file, pluginRoots[0] + ("/" + file)[support..]));
                }
                else if (Reference(file)) references.Add(file);
                else throw new InvalidDataException("Unmapped companion outside the supported mod folders: " + file + ". Keep companions beside their plugin.");
            }
        }
        else
        {
            string[] dlls = files.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (dlls.Length == 0) throw new InvalidDataException("No supported plugin, patcher or configuration folder was found.");
            string shallowest = dlls.OrderBy(f => f.Count(c => c == '/')).First();
            string[] parts = shallowest.Split('/');
            string wrapper = parts.Length > 2 ? string.Join('/', parts.Take(parts.Length - 2)) + "/" : "";
            string destinationRoot = parts.Length == 1 ? "plugins/" + packageId[..12] + "/" : "plugins/";
            foreach (string file in files)
            {
                if (Reference(file) && (!file.StartsWith(wrapper, StringComparison.OrdinalIgnoreCase) || file[wrapper.Length..].Count(c => c == '/') == 0)) { references.Add(file); continue; }
                if (!file.StartsWith(wrapper, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Loose package contains a companion outside its plugin root: " + file);
                mapped.Add(new(file, destinationRoot + file[wrapper.Length..]));
            }
        }
        if (mapped.Count == 0) throw new InvalidDataException("No supported deployment content was found.");
        if (mapped.Select(f => f.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mapped.Count) throw new InvalidDataException("Multiple files map to the same game destination.");
        return new(mapped.All(f => f.Destination.StartsWith("config/", StringComparison.OrdinalIgnoreCase)) ? "Configuration" : "Mod", mapped, references);
    }
}
