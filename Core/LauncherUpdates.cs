using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace Luz;

public sealed record LauncherRelease(string Version, string Rid, string Url, long Size, string Sha256);
public sealed record UpdatePreferences(bool CheckOnStartup = true, DateTimeOffset? LastCheckedUtc = null);

// Only this application's public release feed is accepted. Mod sources/credentials are never used here.
public sealed class LauncherUpdates(HttpClient http)
{
    public const string Repository = "Hvizeu/LUZ-Civic-Terminal";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    public const long MaxDownload = 512L * 1024 * 1024;
    public static string CurrentVersion => typeof(LauncherUpdates).Assembly.GetName().Version!.ToString(3);
    public static string RuntimeId => (HostPlatform.Current, RuntimeInformation.ProcessArchitecture) switch {
        (DesktopPlatform.Windows, Architecture.X64) => "win-x64",
        (DesktopPlatform.Linux, Architecture.X64) => "linux-x64",
        (DesktopPlatform.MacOS, Architecture.X64) => "osx-x64",
        (DesktopPlatform.MacOS, Architecture.Arm64) => "osx-arm64",
        _ => throw new PlatformNotSupportedException("No LUZ update package is available for this architecture.")
    };
    public static bool Due(UpdatePreferences preferences, DateTimeOffset now) => preferences.CheckOnStartup &&
        (preferences.LastCheckedUtc == null || now < preferences.LastCheckedUtc || now - preferences.LastCheckedUtc >= TimeSpan.FromHours(24));
    public async Task<LauncherRelease?> Check(string current, string rid, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + Repository + "/releases?per_page=20");
        request.Headers.UserAgent.ParseAdd("LUZ-Civic-Terminal/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new HttpRequestException("The LUZ release feed is not available yet. Your installed version remains usable.");
        response.EnsureSuccessStatusCode();
        using var output = new MemoryStream();
        await CopyBounded(response, output, 1024 * 1024, ct).ConfigureAwait(false);
        return Select(output.ToArray(), current, rid);
    }
    public static LauncherRelease? Select(byte[] json, string current, string rid)
    {
        try { return SelectRelease(json, current, rid); }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException) { throw new InvalidDataException("The GitHub release response has an unexpected format. No update was installed.", ex); }
    }
    private static LauncherRelease? SelectRelease(byte[] json, string current, string rid)
    {
        if (!new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" }.Contains(rid)) throw new InvalidDataException("Unsupported update platform.");
        using var doc = JsonDocument.Parse(json);
        LauncherRelease? selected = null;
        foreach (var release in doc.RootElement.EnumerateArray()) {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$")) continue;
            string version = tag[1..];
            if (!Version.TryParse(version, out var parsed) || parsed <= Version.Parse(current) || (selected != null && parsed <= Version.Parse(selected.Version))) continue;
            string name = $"LUZ-Civic-Terminal-{version}-{rid}.zip";
            var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
            if (assets.Length != 1) throw new InvalidDataException($"LUZ {version} is available, but its {rid} update package is missing or duplicated. Check {ReleasesUrl}.");
            var asset = assets[0];
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            string expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
            string digest = asset.TryGetProperty("digest", out var value) ? value.GetString() ?? "" : "";
            long size = asset.GetProperty("size").GetInt64();
            if (url != expected || !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$") || size <= 0 || size > MaxDownload)
                throw new InvalidDataException("The LUZ release package has an invalid source, size or SHA-256 digest. Nothing was installed.");
            selected = new(version, rid, url, size, digest[7..].ToLowerInvariant());
        }
        return selected;
    }
    public async Task<string> Download(LauncherRelease release, string root, CancellationToken ct)
    {
        string expected = $"https://github.com/{Repository}/releases/download/v{release.Version}/LUZ-Civic-Terminal-{release.Version}-{release.Rid}.zip";
        if (release.Url != expected || release.Size is <= 0 or > MaxDownload || !Regex.IsMatch(release.Sha256, "^[0-9a-f]{64}$")) throw new InvalidDataException("Invalid LUZ update request.");
        string folder = FileSafety.Under(root, "launcher-updates/" + Guid.NewGuid().ToString("N")); FileSafety.NoLinks(folder); Directory.CreateDirectory(folder);
        string archive = Path.Combine(folder, "update.zip");
        try {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyBounded(response, output, release.Size, ct).ConfigureAwait(false);
            if (new FileInfo(archive).Length != release.Size || FileSafety.Hash(archive) != release.Sha256) throw new InvalidDataException("LUZ update checksum mismatch. The download was discarded; your current installation is unchanged.");
            return archive;
        } catch { FileSafety.DeleteOwned(root, folder); throw; }
    }
    private static async Task CopyBounded(HttpResponseMessage response, Stream output, long limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Update response exceeds its size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        byte[] buffer = new byte[81920]; long total = 0;
        while (true) {
            int read = await input.ReadAsync(buffer, ct).ConfigureAwait(false); if (read == 0) break;
            total += read; if (total > limit) throw new InvalidDataException("Update response exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }
    public static string Prepare(LauncherRelease release, string archive)
    {
        if (new FileInfo(archive).Length != release.Size || FileSafety.Hash(archive) != release.Sha256) throw new InvalidDataException("LUZ update archive changed after download.");
        string stage = Path.Combine(Path.GetDirectoryName(archive)!, "unpacked");
        FileSafety.NoLinks(stage); if (Directory.Exists(stage)) throw new IOException("Update staging folder already exists.");
        try {
            FileSafety.Extract(archive, stage);
            string folderName = $"LUZ Civic Terminal {release.Version} {release.Rid}";
            string root = FileSafety.Under(stage, folderName);
            if (Directory.GetFileSystemEntries(stage).Length != 1 || !Directory.Exists(root)) throw new InvalidDataException("Unexpected LUZ package layout.");
            bool mac = release.Rid.StartsWith("osx-");
            string application = mac ? Path.Combine(root, "LUZ Civic Terminal.app") : root;
            string binaries = mac ? Path.Combine(application, "Contents/MacOS") : application;
            string exe = Path.Combine(binaries, "LUZ Civic Terminal" + (release.Rid == "win-x64" ? ".exe" : ""));
            var identity = AssemblyName.GetAssemblyName(Path.Combine(binaries, "LUZ Civic Terminal.dll"));
            if (!File.Exists(exe) || !File.Exists(Path.Combine(binaries, "Core.dll")) || identity.Name != "LUZ Civic Terminal" || identity.Version?.ToString(3) != release.Version)
                throw new InvalidDataException("The downloaded application does not match the release version.");
            using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(binaries, "LUZ Civic Terminal.deps.json")));
            if (!deps.RootElement.GetProperty("runtimeTarget").GetProperty("name").GetString()!.EndsWith("/" + release.Rid, StringComparison.Ordinal)) throw new InvalidDataException("LUZ update platform mismatch.");
            if (!OperatingSystem.IsWindows()) {
                using var zip = ZipFile.OpenRead(archive);
                foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/'))) {
                    var mode = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF);
                    File.SetUnixFileMode(FileSafety.Under(stage, entry.FullName), mode | UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                if ((File.GetUnixFileMode(exe) & UnixFileMode.UserExecute) == 0) throw new InvalidDataException("The LUZ update entrypoint is not executable.");
            }
            return application;
        } catch { FileSafety.DeleteOwned(Path.GetDirectoryName(archive)!, stage); throw; }
    }
    public static async Task PrepareMacSignature(string application, CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS()) return;
        async Task Sign(params string[] arguments) {
            var request = new ProcessStartInfo("/usr/bin/codesign") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string argument in arguments) request.ArgumentList.Add(argument);
            using var process = Process.Start(request) ?? throw new IOException("macOS signing could not start.");
            var output = process.StandardOutput.ReadToEndAsync(ct); var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            _ = await output.ConfigureAwait(false); string details = await error.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Local macOS signature failed: " + details);
        }
        foreach (string path in Directory.GetFiles(Path.Combine(application, "Contents/MacOS"))) {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".dylib", StringComparison.Ordinal) || name is "createdump" or "LUZ Civic Terminal")
                await Sign("--force", "--sign", "-", path).ConfigureAwait(false);
        }
        await Sign("--force", "--sign", "-", application).ConfigureAwait(false);
        await Sign("--verify", "--deep", "--strict", application).ConfigureAwait(false);
    }
    public static ProcessStartInfo RestartRequest(string installed, int oldPid, DesktopPlatform platform)
    {
        if (oldPid <= 0) throw new ArgumentOutOfRangeException(nameof(oldPid));
        string exe = platform == DesktopPlatform.MacOS ? Path.Combine(installed, "Contents/MacOS/LUZ Civic Terminal") : Path.Combine(installed, "LUZ Civic Terminal" + (platform == DesktopPlatform.Windows ? ".exe" : ""));
        var request = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)!, CreateNoWindow = true };
        request.ArgumentList.Add("--update-restart"); request.ArgumentList.Add(oldPid.ToString(System.Globalization.CultureInfo.InvariantCulture)); return request;
    }
    public static void WaitForPreviousProcess(int pid, int timeoutMs = 30000)
    {
        if (pid <= 0 || pid == Environment.ProcessId) throw new InvalidDataException("Invalid update restart process.");
        try { using var previous = Process.GetProcessById(pid); if (!previous.WaitForExit(timeoutMs)) throw new IOException("The previous LUZ window did not close. Close it normally and reopen the updated launcher."); }
        catch (ArgumentException) { } // The previous launcher already exited.
    }
}
