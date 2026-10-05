using System.IO.Compression;
using System.Security.Cryptography;

namespace Luz;

public static class FileSafety
{
    public static string ResolveFolder(string folder)
    {
        string full = Path.GetFullPath(folder), current = Path.GetPathRoot(full)!;
        foreach (string part in Path.GetRelativePath(current, full).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget != null) current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Cannot resolve the selected folder link.");
        }
        return current;
    }
    public static string PortableDestination(string root, string relative)
    {
        _ = Under(root, relative); string current = root;
        foreach (string part in relative.Replace('\\', '/').Split('/'))
        {
            var matches = Directory.Exists(current) ? Directory.EnumerateFileSystemEntries(current).Where(p => Path.GetFileName(p).Equals(part, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray() : [];
            if (matches.Length > 1) throw new InvalidDataException("Case-conflicting paths cannot be deployed to the Windows game.");
            current = matches.Length == 1 ? matches[0] : Path.Combine(current, part);
        }
        return current;
    }
    public static string Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.StartsWith('\\') || relative.Contains(':')) throw new InvalidDataException("Unsafe package path.");
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Any(p => p is ".." or "." || p.EndsWith(' ') || p.EndsWith('.') || p.Any(c => c < 32) || p.IndexOfAny(['<', '>', '|', '?', '*']) >= 0 ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Unsafe package filename.");
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(basePath, string.Join(Path.DirectorySeparatorChar, parts)));
        if (!result.StartsWith(basePath, HostPlatform.PathComparison)) throw new InvalidDataException("Path escapes its folder.");
        return result;
    }
    public static void NoLinks(string root)
    {
        var info = new DirectoryInfo(root);
        for (var parent = info; parent != null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked folders are not supported for deployment: " + parent.Name);
        if (!Directory.Exists(root)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attr = File.GetAttributes(entry);
            if (attr.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked files or folders cannot be managed: " + Path.GetFileName(entry));
            if (attr.HasFlag(FileAttributes.Directory)) NoLinks(entry);
        }
    }
    public static void CopyTree(string source, string target, bool overwrite = false)
    {
        if (!Directory.Exists(source)) return;
        NoLinks(source); NoLinks(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string dest = PortableDestination(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, overwrite);
        }
    }
    public static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    public static void Extract(string zipPath, string destination)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > 20000 || zip.Entries.Sum(e => e.Length) > 1_073_741_824) throw new InvalidDataException("Package exceeds the 1 GB / 20,000 file limit.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            string path = Under(destination, e.FullName.TrimEnd('/', '\\'));
            if (((e.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Archive links are not supported.");
            if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
            if (!paths.Add(path)) throw new InvalidDataException("Archive contains duplicate paths.");
            if (e.Length > 536_870_912 || (e.CompressedLength > 0 && e.Length / e.CompressedLength > 1000)) throw new InvalidDataException("Archive entry expands beyond the supported limit.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); e.ExtractToFile(path);
        }
    }
    public static void DeleteOwned(string root, string target)
    {
        var relative = Path.GetRelativePath(root, target);
        string checkedPath = Under(root, relative); NoLinks(checkedPath);
        if (Directory.Exists(checkedPath)) Directory.Delete(checkedPath, true);
    }
}
