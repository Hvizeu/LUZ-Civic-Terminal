namespace Luz;

public static class LocalImport
{
    public const string Prefix = "luz-import:";
    public static string Message(string path) { Validate(Prefix + path); return Prefix + path; }
    public static string Validate(string message)
    {
        if (!message.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Invalid local import request.");
        string path = message[Prefix.Length..];
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select a local project ZIP at an absolute path.");
        if (!File.Exists(path) || new FileInfo(path).Length > 536_870_912) throw new InvalidDataException("Project ZIP is missing or larger than 512 MB.");
        return path;
    }
    public static void ValidateActivation(string message)
    {
        if (message.StartsWith(Prefix, StringComparison.Ordinal)) _ = Validate(message);
        else if (message.Length > 0) _ = NxmLink.Parse(message);
    }
}
