using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Luz;

[JsonConverter(typeof(JsonStringEnumConverter<VortexSyncOutcome>))]
public enum VortexSyncOutcome { Queued, Processing, NeedsAttention, Applied, NoChanges, Cancelled, Failed, RecoveryRequired }

public sealed class VortexSnapshotFile
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class VortexSnapshotPackage
{
    public string BridgeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string SourceModId { get; set; } = "";
    public string SourceFileId { get; set; } = "";
    public string Variant { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public string Payload { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public List<VortexSnapshotFile> Files { get; set; } = [];
}
public sealed class VortexSnapshot
{
    public int Schema { get; set; }
    public string RequestId { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public string GameId { get; set; } = "";
    public string GameFolder { get; set; } = "";
    public string Revision { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public List<VortexSnapshotPackage> Packages { get; set; } = [];
}
public sealed class VortexPackageResult
{
    public string BridgeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}
public sealed class VortexSyncResult
{
    public int Schema { get; set; } = 1;
    public string RequestId { get; set; } = "";
    public string Revision { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string BaseFingerprint { get; set; } = "";
    public string ResultingFingerprint { get; set; } = "";
    public VortexSyncOutcome Outcome { get; set; }
    public string Message { get; set; } = "";
    public DateTime CompletedUtc { get; set; }
    public List<VortexPackageResult> Packages { get; set; } = [];
}
public sealed class VortexConnectionDescriptor
{
    public int Schema { get; set; } = 1;
    public string ConnectionId { get; set; } = "";
    public string GameId { get; set; } = "nivalisnights";
    public string GameFolder { get; set; } = "";
    public string ExchangeRoot { get; set; } = "";
    public string VortexRoot { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
}
public sealed class VortexSetupRequest
{
    public int Schema { get; set; }
    public string RequestId { get; set; } = "";
    public string GameId { get; set; } = "";
    public string GameFolder { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
public sealed class VortexSetupResult
{
    public int Schema { get; set; } = 1;
    public string RequestId { get; set; } = "";
    public VortexSyncOutcome Outcome { get; set; }
    public string Message { get; set; } = "";
    public DateTime CompletedUtc { get; set; }
}

public static class VortexBridge
{
    public const string Prefix = "luz-vortex:";
    public const string SetupPrefix = "luz-vortex-setup:";
    private static readonly Regex Identity = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex32 = new("^[a-f0-9]{32}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant);

    public static string DefaultExchangeRoot(string? localAppData = null) => Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LUZVortexBridge");
    public static string Message(string manifest) { string full = ValidateMessage(Prefix + manifest); return Prefix + full; }
    public static string SetupMessage(string request) { string full = ValidateSetupMessage(SetupPrefix + request); return SetupPrefix + full; }
    public static string ValidateMessage(string message)
    {
        if (!message.StartsWith(Prefix, StringComparison.Ordinal)) throw new InvalidDataException("Invalid Vortex synchronization request.");
        string path = message[Prefix.Length..];
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Use an absolute Vortex snapshot manifest path.");
        if (!File.Exists(path) || new FileInfo(path).Length > 8_388_608) throw new InvalidDataException("The Vortex snapshot manifest is missing or larger than 8 MB.");
        return Path.GetFullPath(path);
    }

    public static string ValidateSetupMessage(string message, string? exchangeRoot = null)
    {
        if (!message.StartsWith(SetupPrefix, StringComparison.Ordinal)) throw new InvalidDataException("Invalid Vortex setup request.");
        string path = message[SetupPrefix.Length..];
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) || !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an absolute Vortex setup request path.");
        string full = Path.GetFullPath(path), setup = Path.GetFullPath(Path.Combine(exchangeRoot ?? DefaultExchangeRoot(), "setup")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(setup, HostPlatform.PathComparison)) throw new InvalidDataException("The Vortex setup request is outside the protected exchange folder.");
        FileSafety.NoLinks(full);
        if (!File.Exists(full) || new FileInfo(full).Length > 65_536) throw new InvalidDataException("The Vortex setup request is missing or larger than 64 KB.");
        return full;
    }

    public static VortexSetupResult Configure(Library library, string requestPath, string executable, string? exchangeRoot = null)
    {
        string root = FileSafety.ResolveFolder(exchangeRoot ?? DefaultExchangeRoot());
        string path = ValidateSetupMessage(SetupPrefix + requestPath, root);
        VortexSetupRequest request = JsonFiles.Read<VortexSetupRequest>(path);
        var result = new VortexSetupResult { RequestId = request.RequestId, Outcome = VortexSyncOutcome.Failed };
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Vortex integration is available on Windows only.");
            if (request.Schema != 1 || request.GameId != "nivalisnights" || !Hex32.IsMatch(request.RequestId))
                throw new InvalidDataException("Unsupported Vortex setup schema, game, or request identity.");
            if (request.CreatedUtc == default || request.CreatedUtc > DateTime.UtcNow.AddMinutes(10) || request.CreatedUtc < DateTime.UtcNow.AddDays(-1))
                throw new InvalidDataException("The Vortex setup request is stale or has an invalid timestamp.");
            if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
                throw new InvalidDataException("Vortex could not locate the LUZ executable it started.");

            string game = FileSafety.ResolveFolder(request.GameFolder);
            GameFiles.ValidateIdentity(game);
            RejectOverlap(root, library.Root, "LUZ library");
            RejectOverlap(root, game, "game folder");
            if (library.State.GameFolder.Length > 0 && !GameInstallation.SameLocation(library.State.GameFolder, game))
            {
                result.Outcome = VortexSyncOutcome.NeedsAttention;
                result.Message = "LUZ is connected to a different Nivalis installation. Open LUZ and review the game location once; no profile or game files were changed.";
                return SaveSetupResult(result, root);
            }
            if (library.State.Vortex is { } existing)
            {
                if (!GameInstallation.SameLocation(existing.GameFolder, game)
                    || !FileSafety.ResolveFolder(existing.ExchangeRoot).Equals(root, HostPlatform.PathComparison))
                {
                    result.Outcome = VortexSyncOutcome.NeedsAttention;
                    result.Message = "LUZ already has a different Vortex connection. Open Maintenance and disconnect that connection before retrying Play in Vortex.";
                    return SaveSetupResult(result, root);
                }
                WriteDescriptor(existing, executable);
                result.Outcome = VortexSyncOutcome.NoChanges;
                result.Message = "Vortex and LUZ are already connected.";
                return SaveSetupResult(result, root);
            }

            Directory.CreateDirectory(root);
            FileSafety.NoLinks(root);
            var connection = new VortexConnection { Id = Guid.NewGuid().ToString("N"), ExchangeRoot = root, GameFolder = game, ConnectedUtc = DateTime.UtcNow };
            WriteDescriptor(connection, executable);
            TerminalState next = Clone(library.State);
            next.GameFolder = game;
            next.Vortex = connection;
            try { library.Commit(next); }
            catch (Exception operation)
            {
                try { File.Delete(FileSafety.Under(root, "connection.json")); }
                catch (Exception cleanup) { throw new AggregateException("Automatic Vortex setup was not committed and its descriptor could not be removed.", operation, cleanup); }
                throw;
            }
            result.Outcome = VortexSyncOutcome.Applied;
            result.Message = "Vortex connected to LUZ. Future updates need only Vortex Play.";
            return SaveSetupResult(result, root);
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            result.Outcome = VortexSyncOutcome.Failed;
            result.Message = "Automatic Vortex setup failed: " + ex.Message;
            return SaveSetupResult(result, root);
        }
    }

    public static VortexSetupResult? ReadSetupResult(string requestId, string? exchangeRoot = null)
    {
        if (!Hex32.IsMatch(requestId)) throw new InvalidDataException("Invalid Vortex setup request identity.");
        string path = FileSafety.Under(exchangeRoot ?? DefaultExchangeRoot(), "setup-results/" + requestId + ".json");
        return File.Exists(path) ? JsonFiles.Read<VortexSetupResult>(path) : null;
    }

    private static VortexSetupResult SaveSetupResult(VortexSetupResult result, string root)
    {
        if (!Hex32.IsMatch(result.RequestId)) throw new InvalidDataException("Invalid Vortex setup request identity.");
        result.CompletedUtc = DateTime.UtcNow;
        JsonFiles.Write(FileSafety.Under(root, "setup-results/" + result.RequestId + ".json"), result);
        return result;
    }

    public static void Connect(Library library, string executable, string? exchangeRoot = null, bool checkConflicts = true)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Vortex bridge is currently available on Windows only.");
        GameFiles.Validate(library.State.GameFolder);
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new InvalidDataException("LUZ cannot advertise a missing application executable to Vortex.");
        if (checkConflicts && ConflictingExtensions() is { Count: > 0 } conflicts)
            throw new InvalidOperationException("Remove the obsolete separate LUZ Bridge extension before using canonical Nivalis Nights support: " + string.Join(", ", conflicts));
        string root = FileSafety.ResolveFolder(exchangeRoot ?? DefaultExchangeRoot());
        RejectOverlap(root, library.Root, "LUZ library"); RejectOverlap(root, library.State.GameFolder, "game folder");
        Directory.CreateDirectory(root); FileSafety.NoLinks(root);
        var connection = new VortexConnection { Id = Guid.NewGuid().ToString("N"), ExchangeRoot = root, GameFolder = FileSafety.ResolveFolder(library.State.GameFolder), ConnectedUtc = DateTime.UtcNow };
        WriteDescriptor(connection, executable);
        TerminalState next = Clone(library.State); next.Vortex = connection;
        try { library.Commit(next); }
        catch (Exception operation)
        {
            try { File.Delete(FileSafety.Under(connection.ExchangeRoot, "connection.json")); }
            catch (Exception cleanup) { throw new AggregateException("The Vortex connection was not committed and its descriptor could not be removed.", operation, cleanup); }
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(operation).Throw(); throw;
        }
    }

    public static void Disconnect(Library library)
    {
        VortexConnection? connection = library.State.Vortex;
        TerminalState next = Clone(library.State); next.Vortex = null; library.Commit(next);
        if (connection != null)
        {
            string descriptor = FileSafety.Under(connection.ExchangeRoot, "connection.json");
            FileSafety.NoLinks(descriptor);
            if (File.Exists(descriptor)) File.Delete(descriptor);
        }
    }

    public static void RefreshDescriptor(Library library, string executable)
    {
        if (library.State.Vortex is not { } connection || !OperatingSystem.IsWindows()) return;
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) return;
        WriteDescriptor(connection, executable);
    }

    public static void Exclude(Library library, string packageId)
    {
        var state = library.Active.Vortex;
        foreach (var binding in state.Bindings.Where(b => b.PackageId == packageId).ToArray())
        {
            if (!state.Exclusions.Contains(binding.BridgeId, StringComparer.OrdinalIgnoreCase)) state.Exclusions.Add(binding.BridgeId);
            state.Bindings.Remove(binding);
        }
    }

    public static IReadOnlyList<string> ConflictingExtensions(string? pluginsRoot = null)
    {
        if (!OperatingSystem.IsWindows()) return [];
        string root = pluginsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vortex", "plugins");
        if (!Directory.Exists(root)) return [];
        var conflicts = new List<string>();
        foreach (string folder in Directory.GetDirectories(root))
        foreach (string filename in new[] { "info.json", "package.json" })
        {
            string file = Path.Combine(folder, filename);
            if (!File.Exists(file) || new FileInfo(file).Length > 1_048_576) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                string id = document.RootElement.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
                string name = document.RootElement.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "" : "";
                if (id.Equals("game-nivalisnights-luz-bridge", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("game-nivalisnights-luz-bridge", StringComparison.OrdinalIgnoreCase))
                    conflicts.Add(Path.GetFileName(folder));
            }
            catch (JsonException) { }
        }
        return conflicts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static VortexSyncResult Synchronize(Library library, string manifestPath, CancellationToken cancellation = default)
    {
        string path = ValidateMessage(Prefix + manifestPath);
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(path);
        VortexConnection connection = ValidateSnapshot(library, path, snapshot);
        Profile current = library.Active;
        string baseFingerprint = library.Fingerprint(current);
        var result = new VortexSyncResult { RequestId = snapshot.RequestId, Revision = snapshot.Revision, ProfileId = current.Id,
            BaseFingerprint = baseFingerprint, Outcome = VortexSyncOutcome.Processing };
        if (current.Vortex.LastRevision.Equals(snapshot.Revision, StringComparison.OrdinalIgnoreCase) && SnapshotSatisfied(current, snapshot))
        {
            result.Outcome = VortexSyncOutcome.NoChanges; result.ResultingFingerprint = baseFingerprint; result.Message = "This Vortex snapshot was already synchronized."; result.CompletedUtc = DateTime.UtcNow; return result;
        }

        var prepared = new Dictionary<string, ModPackage>(StringComparer.OrdinalIgnoreCase);
        var blocked = new List<VortexPackageResult>();
        var lineage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incomingIds = snapshot.Packages.Select(p => p.BridgeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var incoming in snapshot.Packages)
        {
            cancellation.ThrowIfCancellationRequested(); ValidatePackageRecord(incoming);
            if (current.Vortex.Exclusions.Contains(incoming.BridgeId, StringComparer.OrdinalIgnoreCase))
            {
                result.Packages.Add(Item(incoming, "Excluded", "This mod was deliberately removed from this LUZ profile.")); continue;
            }
            VortexBinding? binding = current.Vortex.Bindings.SingleOrDefault(b => b.BridgeId.Equals(incoming.BridgeId, StringComparison.OrdinalIgnoreCase));
            if (binding == null && incoming.SourceModId.Length > 0)
            {
                VortexBinding[] predecessors = current.Vortex.Bindings.Where(b => !incomingIds.Contains(b.BridgeId)
                    && b.SourceModId.Equals(incoming.SourceModId, StringComparison.OrdinalIgnoreCase)
                    && (b.Variant.Length == 0 || incoming.Variant.Length == 0 || b.Variant.Equals(incoming.Variant, StringComparison.OrdinalIgnoreCase))).ToArray();
                int successors = snapshot.Packages.Count(p => p.SourceModId.Equals(incoming.SourceModId, StringComparison.OrdinalIgnoreCase)
                    && (p.Variant.Length == 0 || incoming.Variant.Length == 0 || p.Variant.Equals(incoming.Variant, StringComparison.OrdinalIgnoreCase)));
                if (predecessors.Length > 1 || predecessors.Length == 1 && successors != 1)
                {
                    blocked.Add(Item(incoming, "Blocked", "Vortex replacement lineage is ambiguous. Review the installed variants before synchronizing.")); continue;
                }
                if (predecessors.Length == 1) { binding = predecessors[0]; lineage[incoming.BridgeId] = binding.BridgeId; }
            }
            ModEntry? existingEntry = binding == null ? null : current.Mods.SingleOrDefault(m => m.PackageId == binding.PackageId);
            ModPackage? existingPackage = existingEntry == null ? null : library.State.Packages.SingleOrDefault(p => p.Id == existingEntry.PackageId);
            if (binding != null && binding.SourceModId.Length > 0 && incoming.SourceModId.Length > 0 && !binding.SourceModId.Equals(incoming.SourceModId, StringComparison.OrdinalIgnoreCase))
            {
                blocked.Add(Item(incoming, "Blocked", "The Vortex source identity changed. Review this variant before replacing it.")); continue;
            }
            if (binding != null && binding.Variant.Length > 0 && incoming.Variant.Length > 0 && !binding.Variant.Equals(incoming.Variant, StringComparison.OrdinalIgnoreCase))
            {
                blocked.Add(Item(incoming, "Blocked", "The Vortex installer variant changed. Review it before replacing the selected package.")); continue;
            }
            if (existingEntry?.Pinned == true && !binding!.ContentSha256.Equals(incoming.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                pinned.Add(incoming.BridgeId); result.Packages.Add(Item(incoming, "Pinned", "The selected LUZ version remains pinned.")); continue;
            }
            if (existingPackage != null && !binding!.ContentSha256.Equals(incoming.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                int comparison = ModPlanner.CompareVersions(incoming.Version, existingPackage.Version);
                if (comparison < 0 || comparison == 0 && !incoming.Version.Equals("0.0.0", StringComparison.OrdinalIgnoreCase))
                {
                    blocked.Add(Item(incoming, "Blocked", comparison < 0 ? "Vortex offered an older version." : "The same version has different package contents. Review the variant.")); continue;
                }
            }
            if (binding != null && binding.ContentSha256.Equals(incoming.ContentSha256, StringComparison.OrdinalIgnoreCase) && existingPackage != null)
            {
                prepared[incoming.BridgeId] = existingPackage; result.Packages.Add(Item(incoming, "Unchanged", "Package content matches the bound LUZ version.")); continue;
            }
            try { prepared[incoming.BridgeId] = PreparePackage(library, connection, incoming, cancellation); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                blocked.Add(Item(incoming, "Blocked", ex.Message));
            }
        }
        foreach (var replacement in lineage.Where(pair => !pinned.Contains(pair.Key)))
        {
            VortexBinding predecessor = current.Vortex.Bindings.Single(b => b.BridgeId.Equals(replacement.Value, StringComparison.OrdinalIgnoreCase));
            ModPackage previous = library.State.Packages.Single(p => p.Id == predecessor.PackageId);
            if (!prepared.TryGetValue(replacement.Key, out ModPackage? candidate)) continue;
            string[] previousPlugins = previous.Plugins.Select(p => p.Guid).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] candidatePlugins = candidate.Plugins.Select(p => p.Guid).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!previousPlugins.SequenceEqual(candidatePlugins, StringComparer.OrdinalIgnoreCase))
                blocked.Add(Item(snapshot.Packages.Single(p => p.BridgeId.Equals(replacement.Key, StringComparison.OrdinalIgnoreCase)), "Blocked", "The Vortex replacement changes the complete plugin identity set. Review the variant before replacing it."));
        }
        result.Packages.AddRange(blocked);
        if (blocked.Count > 0)
        {
            result.Outcome = VortexSyncOutcome.NeedsAttention; result.ResultingFingerprint = baseFingerprint;
            result.Message = blocked.Count + " Vortex package(s) need review. The LUZ profile was not changed."; result.CompletedUtc = DateTime.UtcNow; return result;
        }

        TerminalState next = Clone(library.State);
        Profile target = next.Profiles.Single(p => p.Id == current.Id);
        foreach (var binding in target.Vortex.Bindings)
        {
            bool available = incomingIds.Contains(binding.BridgeId);
            if (binding.Available && !available)
            {
                string name = next.Packages.FirstOrDefault(p => p.Id == binding.PackageId)?.Name ?? binding.BridgeId;
                result.Packages.Add(new() { BridgeId = binding.BridgeId, Name = name, Action = "Unavailable", Detail = "The Vortex copy is absent. The imported LUZ version and profile selection were kept." });
            }
            binding.Available = available;
        }
        foreach (var incoming in snapshot.Packages)
        {
            if (pinned.Contains(incoming.BridgeId))
            {
                VortexBinding retained = lineage.TryGetValue(incoming.BridgeId, out string? previousBridgeId)
                    ? target.Vortex.Bindings.Single(b => b.BridgeId.Equals(previousBridgeId, StringComparison.OrdinalIgnoreCase))
                    : target.Vortex.Bindings.Single(b => b.BridgeId.Equals(incoming.BridgeId, StringComparison.OrdinalIgnoreCase));
                retained.BridgeId = incoming.BridgeId; retained.Available = true; continue;
            }
            if (!prepared.TryGetValue(incoming.BridgeId, out ModPackage? package)) continue;
            if (!next.Packages.Any(p => p.Id == package.Id)) next.Packages.Add(package);
            VortexBinding? binding = target.Vortex.Bindings.SingleOrDefault(b => b.BridgeId.Equals(incoming.BridgeId, StringComparison.OrdinalIgnoreCase));
            if (binding == null && lineage.TryGetValue(incoming.BridgeId, out string? lineageBridgeId))
            {
                binding = target.Vortex.Bindings.Single(b => b.BridgeId.Equals(lineageBridgeId, StringComparison.OrdinalIgnoreCase));
                binding.BridgeId = incoming.BridgeId;
            }
            ModEntry? entry;
            try { entry = binding == null ? Adopt(target, next.Packages, package, incoming, result) : target.Mods.SingleOrDefault(m => m.PackageId == binding.PackageId); }
            catch (InvalidDataException ex)
            {
                result.Packages.Add(Item(incoming, "Blocked", ex.Message));
                result.Outcome = VortexSyncOutcome.NeedsAttention; result.ResultingFingerprint = baseFingerprint;
                result.Message = "A Vortex package has an ambiguous LUZ identity. The LUZ profile was not changed."; result.CompletedUtc = DateTime.UtcNow; return result;
            }
            if (entry == null)
            {
                entry = new ModEntry { PackageId = package.Id, Enabled = true }; target.Mods.Add(entry);
                result.Packages.RemoveAll(p => p.BridgeId == incoming.BridgeId); result.Packages.Add(Item(incoming, "Added", "Added enabled at the end of the selected LUZ profile."));
            }
            else if (entry.PackageId != package.Id)
            {
                entry.PackageId = package.Id; result.Packages.RemoveAll(p => p.BridgeId == incoming.BridgeId); result.Packages.Add(Item(incoming, "Updated", "Replaced the bound version while preserving its LUZ settings and order."));
            }
            binding ??= new VortexBinding { BridgeId = incoming.BridgeId };
            if (!target.Vortex.Bindings.Contains(binding)) target.Vortex.Bindings.Add(binding);
            binding.PackageId = package.Id; binding.SourceModId = incoming.SourceModId; binding.SourceFileId = incoming.SourceFileId; binding.Variant = incoming.Variant;
            binding.ContentSha256 = incoming.ContentSha256; binding.Version = incoming.Version; binding.Available = true;
        }
        target.Vortex.LastRevision = snapshot.Revision;
        string candidateFingerprint = library.Fingerprint(target);
        if (candidateFingerprint != baseFingerprint)
        {
            target.Vortex.PendingBaseFingerprint = baseFingerprint;
            target.Vortex.PendingApplyFingerprint = candidateFingerprint;
        }
        library.Commit(next);
        result.ResultingFingerprint = library.Fingerprint(library.Active);
        if (candidateFingerprint == baseFingerprint)
        {
            result.Outcome = VortexSyncOutcome.NoChanges; result.Message = "Vortex source records were refreshed. No LUZ deployment changes were required.";
        }
        else result.Message = "Vortex packages were synchronized. Apply is pending.";
        result.CompletedUtc = DateTime.UtcNow; return result;
    }

    public static VortexSyncResult SynchronizeAndApply(Library library, string manifestPath, string capturedProfileId, CancellationToken cancellation = default)
    {
        VortexSnapshot snapshot = JsonFiles.Read<VortexSnapshot>(ValidateMessage(Prefix + manifestPath));
        var result = new VortexSyncResult { RequestId = snapshot.RequestId, Revision = snapshot.Revision, ProfileId = capturedProfileId, Outcome = VortexSyncOutcome.Processing };
        bool synchronized = false;
        try
        {
            if (library.Active.Id != capturedProfileId)
            {
                result.Outcome = VortexSyncOutcome.NeedsAttention;
                result.Message = "The selected profile changed after this Vortex request was admitted. Return to the captured profile and retry.";
                return result;
            }
            string before = library.Fingerprint(library.Active);
            bool alreadyApplied = library.State.AppliedProfileId == capturedProfileId && library.State.AppliedFingerprint == before;
            bool resumesBridgeApply = library.State.AppliedProfileId == capturedProfileId
                && library.Active.Vortex.PendingBaseFingerprint == library.State.AppliedFingerprint
                && library.Active.Vortex.PendingApplyFingerprint == before;
            bool wasApplied = alreadyApplied || resumesBridgeApply;
            bool hadRecovery = OperationRecovery.Inspect(library).Count > 0;
            result = Synchronize(library, manifestPath, cancellation);
            synchronized = result.Outcome is VortexSyncOutcome.Processing or VortexSyncOutcome.NoChanges;
            if (!synchronized || (result.Outcome == VortexSyncOutcome.NoChanges && !resumesBridgeApply)) return result;
            if (library.Active.Id != capturedProfileId)
            {
                result.Outcome = VortexSyncOutcome.NeedsAttention;
                result.Message = "The selected profile changed during synchronization. Packages were synchronized but not applied.";
                return result;
            }
            if (!wasApplied)
            {
                result.Outcome = VortexSyncOutcome.NeedsAttention;
                result.Message = "Packages were synchronized, but this profile already had unapplied changes or has never been applied. Review and Apply it in LUZ.";
                return result;
            }
            if (hadRecovery || OperationRecovery.Inspect(library).Count > 0)
            {
                result.Outcome = VortexSyncOutcome.RecoveryRequired;
                result.Message = "Packages were synchronized, but an interrupted LUZ operation must be recovered before Apply.";
                return result;
            }
            GameFiles.RequireClosed();
            var deployment = new Deployment(library);
            var issues = deployment.Preview(includeCompatibility: false, cancellation);
            if (issues.Any(issue => issue.Severity == "Error"))
            {
                result.Outcome = VortexSyncOutcome.NeedsAttention;
                result.Message = "Packages were synchronized, but Apply is blocked: " + string.Join(" ", issues.Where(issue => issue.Severity == "Error").Select(issue => issue.Message));
                return result;
            }
            var report = Compatibility.Inspect(library, cancellation: cancellation);
            CompatibilityAcknowledgment? acknowledgment = null;
            if (report.Status != CompatibilityStatus.CheckedContractsPassed)
            {
                if (report.Findings.Any(finding => finding.Severity == "Error" && !finding.CanOverride) || !CompatibilityDecisions.IsRemembered(library, report))
                {
                    result.Outcome = VortexSyncOutcome.NeedsAttention;
                    result.Message = "Packages were synchronized, but this exact setup has a new compatibility finding. Review it in LUZ before Apply.";
                    return result;
                }
                acknowledgment = Compatibility.Acknowledge(report);
            }
            deployment.Apply(acknowledgment, cancellation);
            result.ResultingFingerprint = library.Fingerprint(library.Active);
            if (library.State.AppliedProfileId != capturedProfileId || library.State.AppliedFingerprint != result.ResultingFingerprint)
                throw new IOException("LUZ deployment completed without recording the exact captured profile fingerprint.");
            result.Outcome = VortexSyncOutcome.Applied;
            result.Message = "Vortex packages were synchronized and the captured LUZ profile was applied. LUZ did not launch Nivalis.";
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Outcome = VortexSyncOutcome.Cancelled;
            result.Message = synchronized ? "Synchronization completed, but Apply was cancelled. The synchronized profile remains available in LUZ." : "Vortex synchronization was cancelled before the profile commit.";
            return result;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            bool recovery = OperationRecovery.Inspect(library).Count > 0;
            result.Outcome = recovery ? VortexSyncOutcome.RecoveryRequired : VortexSyncOutcome.Failed;
            result.Message = (synchronized ? "Packages were synchronized, but they were not applied: " : "Vortex synchronization failed: ") + ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            result.Outcome = VortexSyncOutcome.Failed;
            result.Message = (synchronized ? "Packages were synchronized, but an unexpected failure stopped Apply: " : "Vortex synchronization failed unexpectedly: ") + ex.Message;
            return result;
        }
        finally
        {
            result.CompletedUtc = DateTime.UtcNow;
            SaveResult(library, result);
        }
    }

    public static void SaveResult(Library library, VortexSyncResult result)
    {
        VortexConnection connection = library.State.Vortex ?? throw new InvalidOperationException("Let Vortex complete automatic LUZ setup before saving synchronization results.");
        if (!Hex32.IsMatch(result.RequestId)) throw new InvalidDataException("Invalid Vortex request identity.");
        result.CompletedUtc = DateTime.UtcNow;
        JsonFiles.Write(FileSafety.Under(connection.ExchangeRoot, "results/" + result.RequestId + ".json"), result);
    }

    public static VortexSyncResult? ReadResult(Library library, string requestId)
    {
        VortexConnection connection = library.State.Vortex ?? throw new InvalidOperationException("Let Vortex complete automatic LUZ setup before reading synchronization results.");
        if (!Hex32.IsMatch(requestId)) throw new InvalidDataException("Invalid Vortex request identity.");
        string path = FileSafety.Under(connection.ExchangeRoot, "results/" + requestId + ".json");
        return File.Exists(path) ? JsonFiles.Read<VortexSyncResult>(path) : null;
    }

    private static VortexConnection ValidateSnapshot(Library library, string manifestPath, VortexSnapshot snapshot)
    {
        VortexConnection connection = library.State.Vortex ?? throw new InvalidOperationException("Manage Nivalis Nights in Vortex once so it can connect LUZ automatically.");
        if (snapshot.Schema != 1 || snapshot.GameId != "nivalisnights") throw new InvalidDataException("Unsupported Vortex snapshot schema or game identity.");
        if (!Hex32.IsMatch(snapshot.RequestId) || !Hex64.IsMatch(snapshot.Revision) || !Hex32.IsMatch(snapshot.ConnectionId)) throw new InvalidDataException("Invalid Vortex snapshot identity.");
        if (!snapshot.ConnectionId.Equals(connection.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("This Vortex snapshot belongs to another LUZ connection.");
        if (!FileSafety.ResolveFolder(connection.GameFolder).Equals(FileSafety.ResolveFolder(library.State.GameFolder), HostPlatform.PathComparison)) throw new InvalidDataException("The configured Vortex connection belongs to a different Nivalis installation. Disconnect and reconnect it from Maintenance.");
        if (!Hex32.IsMatch(connection.Id)) throw new InvalidDataException("The configured Vortex connection identity is invalid. Disconnect and reconnect it.");
        string root = FileSafety.ResolveFolder(connection.ExchangeRoot), manifest = Path.GetFullPath(manifestPath); FileSafety.NoLinks(root); FileSafety.NoLinks(manifest);
        if (!manifest.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, HostPlatform.PathComparison)) throw new InvalidDataException("The Vortex snapshot is outside the configured exchange folder.");
        if (!FileSafety.ResolveFolder(snapshot.GameFolder).Equals(FileSafety.ResolveFolder(connection.GameFolder), HostPlatform.PathComparison)) throw new InvalidDataException("Vortex and LUZ selected different Nivalis installations.");
        if (snapshot.Packages.Count > 2000 || snapshot.Packages.Select(p => p.BridgeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Packages.Count) throw new InvalidDataException("The Vortex snapshot has too many or duplicate package identities.");
        return connection;
    }

    private static ModPackage PreparePackage(Library library, VortexConnection connection, VortexSnapshotPackage incoming, CancellationToken cancellation)
    {
        ModPackage? registered = library.State.Packages.SingleOrDefault(p => p.Sha256.Equals(incoming.ContentSha256, StringComparison.OrdinalIgnoreCase));
        if (registered != null) return registered;
        string payload = FileSafety.Under(connection.ExchangeRoot, incoming.Payload); FileSafety.NoLinks(payload);
        if (!Directory.Exists(payload)) throw new InvalidDataException("Vortex package payload is missing.");
        var expected = incoming.Files.ToDictionary(f => FileSafety.Under(payload, f.Path), HostPlatform.PathComparer);
        string[] actual = Directory.GetFiles(payload, "*", SearchOption.AllDirectories);
        if (actual.Length != expected.Count || actual.Any(f => !expected.ContainsKey(Path.GetFullPath(f)))) throw new InvalidDataException("Vortex package files do not match its published manifest.");
        long total = 0;
        foreach (var pair in expected.OrderBy(p => p.Key, HostPlatform.PathComparer))
        {
            cancellation.ThrowIfCancellationRequested(); var info = new FileInfo(pair.Key); total += info.Length;
            if (info.Length != pair.Value.Length || !FileSafety.Hash(pair.Key).Equals(pair.Value.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Vortex package changed after its manifest was published: " + pair.Value.Path);
        }
        if (total > 1_073_741_824 || !Aggregate(incoming.Files).Equals(incoming.ContentSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Vortex package exceeds the 1 GB limit or has an invalid content digest.");
        string packageRoot = library.PackageRoot(incoming.ContentSha256[..32]);
        if (Directory.Exists(packageRoot))
        {
            ModPackage orphan = JsonFiles.Read<ModPackage>(Path.Combine(packageRoot, "package.json"));
            if (orphan.Sha256.Equals(incoming.ContentSha256, StringComparison.OrdinalIgnoreCase)) return orphan;
        }
        ModPackage package = library.ImportTree(payload, null, incoming.ContentSha256, incoming.Name, false);
        package.Name = incoming.Name.Length > 0 ? incoming.Name : package.Name;
        if (incoming.Version.Length > 0) package.Version = incoming.Version;
        package.Source = "Vortex"; package.SourceId = incoming.SourceModId; package.PageUrl = incoming.PageUrl;
        JsonFiles.Write(Path.Combine(library.PackageRoot(package.Id), "package.json"), package);
        return package;
    }

    private static ModEntry? Adopt(Profile target, List<ModPackage> packages, ModPackage package, VortexSnapshotPackage incoming, VortexSyncResult result)
    {
        var pluginIds = package.Plugins.Select(p => p.Guid).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = target.Mods.Where(entry =>
        {
            ModPackage existing = packages.Single(p => p.Id == entry.PackageId);
            string[] ids = existing.Plugins.Select(p => p.Guid).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return incoming.SourceModId.Length > 0 && existing.Source == "Nexus" && existing.SourceId.Equals(incoming.SourceModId, StringComparison.OrdinalIgnoreCase)
                && pluginIds.SequenceEqual(ids, StringComparer.OrdinalIgnoreCase);
        }).ToArray();
        if (candidates.Length > 1) throw new InvalidDataException("More than one LUZ entry could represent Vortex mod " + incoming.Name + ". Remove the ambiguity before retrying.");
        if (candidates.Length == 1)
        {
            result.Packages.RemoveAll(p => p.BridgeId == incoming.BridgeId); result.Packages.Add(Item(incoming, "Adopted", "Bound to the matching existing LUZ entry."));
            return candidates[0];
        }
        return null;
    }

    private static void ValidatePackageRecord(VortexSnapshotPackage package)
    {
        if (!Identity.IsMatch(package.BridgeId) || package.Name.Length is < 1 or > 240 || package.Version.Length > 80 || package.SourceModId.Length > 80
            || package.SourceFileId.Length > 80 || package.Variant.Length > 160 || package.Payload.Length is < 1 or > 512 || package.Files.Count is < 1 or > 20000
            || !Hex64.IsMatch(package.ContentSha256)) throw new InvalidDataException("Invalid Vortex package record.");
        if (package.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != package.Files.Count || package.Files.Any(f => !Hex64.IsMatch(f.Sha256) || f.Length < 0 || f.Length > 536_870_912)) throw new InvalidDataException("Invalid or duplicate Vortex package file record.");
        if (package.PageUrl.Length > 0 && (!Uri.TryCreate(package.PageUrl, UriKind.Absolute, out Uri? page) || page.Scheme != "https" || page.UserInfo.Length > 0)) throw new InvalidDataException("Invalid Vortex package page address.");
    }

    private static string Aggregate(IEnumerable<VortexSnapshotFile> files)
    {
        var text = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)) text.Append(file.Path.Replace('\\', '/')).Append('\0').Append(file.Length).Append('\0').Append(file.Sha256.ToLowerInvariant()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
    private static TerminalState Clone(TerminalState state) => JsonSerializer.Deserialize<TerminalState>(JsonSerializer.Serialize(state, JsonFiles.Options), JsonFiles.Options)!;
    private static bool SnapshotSatisfied(Profile profile, VortexSnapshot snapshot)
    {
        var incoming = snapshot.Packages.ToDictionary(p => p.BridgeId, StringComparer.OrdinalIgnoreCase);
        if (profile.Vortex.Bindings.Any(b => b.Available != incoming.ContainsKey(b.BridgeId))) return false;
        return snapshot.Packages.All(package => profile.Vortex.Exclusions.Contains(package.BridgeId, StringComparer.OrdinalIgnoreCase)
            || profile.Vortex.Bindings.Any(binding => binding.BridgeId.Equals(package.BridgeId, StringComparison.OrdinalIgnoreCase)
                && binding.ContentSha256.Equals(package.ContentSha256, StringComparison.OrdinalIgnoreCase)));
    }
    private static void WriteDescriptor(VortexConnection connection, string executable)
    {
        if (!Hex32.IsMatch(connection.Id)) throw new InvalidDataException("Invalid Vortex connection identity.");
        Directory.CreateDirectory(connection.ExchangeRoot); FileSafety.NoLinks(connection.ExchangeRoot);
        string vortexRoot = FileSafety.Under(connection.ExchangeRoot, "vortex-game");
        Directory.CreateDirectory(vortexRoot); FileSafety.NoLinks(vortexRoot);
        Directory.CreateDirectory(FileSafety.Under(vortexRoot, "deployed"));
        File.WriteAllText(FileSafety.Under(vortexRoot, "luz-bridge-game.json"), JsonSerializer.Serialize(new
        {
            Schema = 1,
            ConnectionId = connection.Id,
            GameId = "nivalisnights",
            GameFolder = connection.GameFolder
        }, JsonFiles.Options));
        var descriptor = new VortexConnectionDescriptor { ConnectionId = connection.Id, GameFolder = connection.GameFolder,
            ExchangeRoot = connection.ExchangeRoot, VortexRoot = vortexRoot, Executable = Path.GetFullPath(executable),
            Version = typeof(VortexBridge).Assembly.GetName().Version?.ToString(3) ?? "0.0.0", UpdatedUtc = DateTime.UtcNow };
        JsonFiles.Write(FileSafety.Under(connection.ExchangeRoot, "connection.json"), descriptor);
    }
    private static VortexPackageResult Item(VortexSnapshotPackage package, string action, string detail) => new() { BridgeId = package.BridgeId, Name = package.Name, Action = action, Detail = detail };
    private static void RejectOverlap(string left, string right, string label)
    {
        left = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar); right = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar);
        if (left.Equals(right, HostPlatform.PathComparison) || left.StartsWith(right + Path.DirectorySeparatorChar, HostPlatform.PathComparison) || right.StartsWith(left + Path.DirectorySeparatorChar, HostPlatform.PathComparison)) throw new InvalidDataException("Choose a Vortex exchange folder outside the " + label + ".");
    }
}
