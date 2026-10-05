using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Luz;

public sealed class Sources : IDisposable
{
    private readonly HttpClient http;
    public Sources(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(5); http.DefaultRequestHeaders.UserAgent.ParseAdd("LUZCivicTerminal/" + LauncherUpdates.CurrentVersion);
    }
    public void Dispose() => http.Dispose();
    public static bool IsLoader(string id) => id.Equals("BepInEx-BepInExPack_IL2CPP", StringComparison.OrdinalIgnoreCase);
    public async Task<List<CatalogMod>> Thunderstore(CancellationToken ct = default)
    {
        using var doc = await Json("https://thunderstore.io/c/nivalis-nights/api/v1/package/", null, ct);
        List<CatalogMod> result = [];
        foreach (var p in doc.RootElement.EnumerateArray())
        foreach (var v in p.GetProperty("versions").EnumerateArray().Where(v => !v.TryGetProperty("is_active", out var a) || a.GetBoolean()).Take(1))
            result.Add(new(p.GetProperty("full_name").GetString()!, p.GetProperty("name").GetString()!.Replace('_', ' '), v.GetProperty("version_number").GetString()!,
                v.GetProperty("description").GetString() ?? "", p.GetProperty("package_url").GetString()!, v.GetProperty("icon").GetString() ?? "", v.GetProperty("download_url").GetString()!,
                v.GetProperty("dependencies").EnumerateArray().Select(d => d.GetString()!).ToArray(), "Thunderstore"));
        return result;
    }
    public static List<CatalogMod> Resolve(CatalogMod selected, IReadOnlyList<CatalogMod> catalog)
    {
        List<CatalogMod> plan = []; HashSet<string> visited = [], visiting = [];
        void Visit(CatalogMod mod)
        {
            if (IsLoader(mod.Id) || visited.Contains(mod.Id)) return;
            if (!visiting.Add(mod.Id)) throw new InvalidDataException("Catalogue dependency cycle: " + mod.Name);
            foreach (var text in mod.Dependencies)
            {
                var dep = ModPlanner.ParseDependency(text); if (IsLoader(dep.Id)) continue;
                var target = catalog.FirstOrDefault(p => p.Id == dep.Id) ?? throw new InvalidDataException("Dependency is missing from this catalogue: " + text);
                if (ModPlanner.CompareVersions(target.Version, dep.Version) < 0) throw new InvalidDataException("Catalogue cannot satisfy " + text);
                Visit(target);
            }
            visiting.Remove(mod.Id); visited.Add(mod.Id); plan.Add(mod);
        }
        Visit(selected); return plan;
    }
    public async Task<string> Download(string url, string destination, CancellationToken ct = default, long maxBytes = 536_870_912)
    {
        using var response = await Send(url, null, ct);
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Download exceeds its size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            byte[] buffer = new byte[81920]; long total = 0; int count;
            while ((count = await input.ReadAsync(buffer, ct)) > 0) { total += count; if (total > maxBytes) throw new InvalidDataException("Download exceeds its size limit."); await output.WriteAsync(buffer.AsMemory(0, count), ct); }
        }
        catch { File.Delete(destination); throw; }
        return destination;
    }
    private async Task<HttpResponseMessage> Send(string url, string? key, CancellationToken ct)
    {
        for (int redirect = 0; redirect < 6; redirect++)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" || uri.IsLoopback || uri.UserInfo.Length > 0) throw new InvalidDataException("Downloads require a public HTTPS address.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (key != null)
            {
                if (uri.Host != "api.nexusmods.com") throw new InvalidDataException("Nexus credentials cannot be sent to another host.");
                request.Headers.Add("apikey", key); request.Headers.Add("Application-Name", "LUZ Civic Terminal"); request.Headers.Add("Application-Version", "0.3.2");
            }
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
            {
                var next = new Uri(uri, response.Headers.Location); response.Dispose();
                if (key != null) throw new InvalidDataException("Unexpected redirect from the Nexus API.");
                url = next.AbsoluteUri; continue;
            }
            if (!response.IsSuccessStatusCode) { int code = (int)response.StatusCode; response.Dispose(); throw new HttpRequestException($"Source returned HTTP {code}. For Nexus, check your API key and download entitlement; manual ZIP imports remain available."); }
            return response;
        }
        throw new HttpRequestException("Too many download redirects.");
    }
    private async Task<JsonDocument> Json(string url, string? key, CancellationToken ct)
    {
        using var response = await Send(url, key, ct);
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream(); byte[] buffer = new byte[81920]; int n;
        while ((n = await input.ReadAsync(buffer, ct)) > 0) { if (memory.Length + n > 33_554_432) throw new InvalidDataException("Source response is too large."); memory.Write(buffer, 0, n); }
        return JsonDocument.Parse(memory.ToArray());
    }
    public static (long Mod, long File, string Query) NexusAddress(string address)
    {
        if (address.Trim().StartsWith("nxm:", StringComparison.OrdinalIgnoreCase)) { var link = NxmLink.Parse(address.Trim()); return (link.ModId, link.FileId, link.DownloadQuery); }
        if (long.TryParse(address.Trim(), out long id) && id > 0) return (id, 0, "");
        var uri = new Uri(address.Trim());
        if (uri.Scheme == "https" && (uri.Host == "www.nexusmods.com" || uri.Host == "nexusmods.com"))
        {
            var m = Regex.Match(uri.AbsolutePath, @"^/(?:games/)?nivalisnights/mods/(\d+)/?$"); if (m.Success) return (long.Parse(m.Groups[1].Value), 0, "");
        }
        throw new InvalidDataException("Use a Nivalis Nights Nexus mod page, mod number, or NXM download link.");
    }
    public async Task<List<CatalogMod>> Nexus(string address, string key, CancellationToken ct = default)
    {
        var parsed = NexusAddress(address);
        string route = $"https://api.nexusmods.com/v1/games/nivalisnights/mods/{parsed.Mod}";
        using var details = await Json(route + ".json", key, ct);
        using var files = await Json(route + "/files.json", key, ct);
        var d = details.RootElement;
        return files.RootElement.GetProperty("files").EnumerateArray().Where(f => parsed.File == 0 ? f.GetProperty("category_id").GetInt32() is 1 or 2 : f.GetProperty("file_id").GetInt64() == parsed.File).Select(f => new CatalogMod(
            parsed.Mod.ToString(), f.GetProperty("name").GetString() ?? d.GetProperty("name").GetString()!, f.GetProperty("version").GetString() ?? "0.0.0",
            d.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "", $"https://www.nexusmods.com/nivalisnights/mods/{parsed.Mod}",
            d.TryGetProperty("picture_url", out var pic) ? pic.GetString() ?? "" : "", "", [], "Nexus", f.GetProperty("file_id").GetInt64())).ToList();
    }
    public async Task<string> NexusDownload(CatalogMod mod, string key, string address, CancellationToken ct = default)
    {
        if (address.StartsWith("nxm:", StringComparison.OrdinalIgnoreCase)) NxmLink.Parse(address).RequireFresh();
        var parsed = NexusAddress(address); string query = "";
        if (parsed.File != 0 && (parsed.Mod.ToString() != mod.Id || parsed.File != mod.FileId)) throw new InvalidDataException("The Nexus link belongs to a different file. Click its download button again.");
        if (parsed.Mod == long.Parse(mod.Id) && parsed.File == mod.FileId && parsed.Query.Length > 0)
        {
            var allowed = parsed.Query.TrimStart('?').Split('&').Where(p => p.StartsWith("key=", StringComparison.Ordinal) || p.StartsWith("expires=", StringComparison.Ordinal));
            query = "?" + string.Join('&', allowed);
        }
        using var doc = await Json($"https://api.nexusmods.com/v1/games/nivalisnights/mods/{mod.Id}/files/{mod.FileId}/download_link.json{query}", key, ct);
        return doc.RootElement.EnumerateArray().First().GetProperty("URI").GetString() ?? throw new InvalidDataException("No download address returned.");
    }
}
