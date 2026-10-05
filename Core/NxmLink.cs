using System.Globalization;
using System.Text.RegularExpressions;

namespace Luz;

public sealed class NxmLink
{
    public string Address { get; }
    public long ModId { get; }
    public long FileId { get; }
    public string DownloadQuery { get; }
    private long? Expires { get; }
    public string Identity => ModId + "/" + FileId;
    private NxmLink(string address, long mod, long file, string query, long? expires) { Address = address; ModId = mod; FileId = file; DownloadQuery = query; Expires = expires; }
    public override string ToString() => "Nivalis Nexus mod " + ModId + ", file " + FileId;
    public static NxmLink Parse(string address)
    {
        if (address.Length > 8192 || address.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\'')) throw Invalid();
        var match = Regex.Match(address, @"^nxm://nivalisnights/mods/([1-9][0-9]*)/files/([1-9][0-9]*)/?(?:\?([^#]*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out long mod) || !long.TryParse(match.Groups[2].Value, out long file)) throw Invalid();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string field in match.Groups[3].Value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = field.Split('=', 2);
            if (parts.Length != 2 || parts[0] is not ("key" or "expires" or "user_id") || !values.TryAdd(parts[0], Uri.UnescapeDataString(parts[1].Replace('+', ' ')))) throw Invalid();
        }
        long? expires = null; string query = "";
        if (values.ContainsKey("key") != values.ContainsKey("expires")) throw Invalid();
        if (values.TryGetValue("key", out string? key))
        {
            if (key.Length == 0 || key.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) || !long.TryParse(values["expires"], NumberStyles.None, CultureInfo.InvariantCulture, out long expiry) || expiry <= 0) throw Invalid();
            expires = expiry; query = "?key=" + Uri.EscapeDataString(key) + "&expires=" + expiry.ToString(CultureInfo.InvariantCulture);
        }
        if (values.TryGetValue("user_id", out var user) && (!long.TryParse(user, out long userId) || userId <= 0)) throw Invalid();
        return new(address, mod, file, query, expires);
    }
    public void RequireFresh()
    {
        if (Expires is { } expiry && expiry <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) throw new InvalidDataException("This Nexus download link has expired. Click Mod Manager Download on Nexus again.");
    }
    private static InvalidDataException Invalid() => new("Use a Nivalis Nights nxm download link. Other games, collections and malformed links are not supported.");
}

public sealed class NxmQueue
{
    private readonly Queue<string> order = new();
    private readonly Dictionary<string, NxmLink> links = [];
    public int Count => order.Count;
    public void Add(NxmLink link)
    {
        if (!links.ContainsKey(link.Identity))
        {
            if (order.Count >= 20) throw new InvalidOperationException("The Nexus queue is full. Finish the pending downloads before adding more.");
            order.Enqueue(link.Identity);
        }
        links[link.Identity] = link;
    }
    public NxmLink? Peek() => order.TryPeek(out var id) ? links[id] : null;
    public void Remove() { if (order.TryDequeue(out var id)) links.Remove(id); }
}
