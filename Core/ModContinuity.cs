using System.IO.Compression;
using System.Text.Json;
using Luz.Continuity;

namespace Luz;

public sealed record ContinuityOptions(bool Enabled = false, string Saves = "", string SourceSlot = "", string SessionId = "");

/// <summary>LUZ owns distribution, launch admission and checkpoints. Only the runtime owns live save routing.</summary>
public static class ModContinuity
{
    public const string PluginGuid = "hvizeu.luz.continuity";
    private static string OptionsPath(Library library) => FileSafety.Under(library.Root, "continuity/options-" + library.Active.Id + ".json");
    public static ContinuityOptions Options(Library library) => File.Exists(OptionsPath(library)) ? JsonFiles.Read<ContinuityOptions>(OptionsPath(library)) : new();
    public static IReadOnlyList<(string Id, string Label)> Timelines(Library library)
    {
        string folder = FileSafety.Under(library.Root, "continuity/sessions");
        if (!Directory.Exists(folder)) return Array.Empty<(string, string)>();
        var files = Directory.GetFiles(folder, "*.json");
        if (files.Length > 4096) throw new InvalidDataException("Too many retained timelines; preserve the library and manage its checkpoints before continuing.");
        string saves = Path.GetFullPath(Options(library).Saves);
        return files.Select(file => Session(library, Path.GetFileNameWithoutExtension(file)))
            .Where(session => Path.GetFullPath(session.Saves).Equals(saves, StringComparison.OrdinalIgnoreCase))
            .Select(session => (session.Id, session.BranchSlot + (session.Dormant.Count == 0 ? "" : " · dormant: " + string.Join(", ", session.Dormant))))
            .OrderBy(value => value.Item2, StringComparer.Ordinal).ToArray();
    }
    public static void SelectTimeline(Library library, string id)
    {
        GameFiles.RequireClosed(); var options = Options(library);
        if (!options.Enabled) throw new InvalidOperationException("Enable protected saves before selecting a timeline.");
        var session = Session(library, id);
        if (!Path.GetFullPath(session.Saves).Equals(Path.GetFullPath(options.Saves), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select a timeline from this profile's configured save directory.");
        if (session.RecoveryRequired || Publications.Pending(session)) throw new IOException("Recover this timeline's unfinished operation before resuming it.");
        Checkpoints.Verify(session.Checkpoint); Companions.VerifyOutputs(session);
        JsonFiles.Write(OptionsPath(library), options with { SourceSlot = session.SourceSlot, SessionId = id });
    }
    public static string Bundle => Path.Combine(AppContext.BaseDirectory, "Continuity");
    public static string Describe(Library library)
    {
        var options = Options(library);
        if (!options.Enabled) return "Protected saves are off for this profile. Enable before migrating to continuity-aware mods.";
        if (options.SessionId.Length == 0) return "Protection enabled. The first Play will verify and capture " + options.SourceSlot + ".";
        var session = Session(library, options.SessionId);
        return session.BranchSlot + "\nCheckpoint: " + Checkpoints.Verify(session.Checkpoint).Label + "\n" +
            (session.RecoveryRequired ? "Recovery required: " + session.FirstFailure : Publications.Pending(session) ? "Publication interrupted. Restore it before playing." : session.PendingRestore.Count != 0 ? "Restoration awaits the owning module's loaded-state acknowledgement." : session.Dormant.Count == 0 ? "Verified adapters selected." : "Dormant: " + string.Join(", ", session.Dormant));
    }
    public static ContinuitySession Session(Library library, string id)
    {
        Library.ValidateId(id); var session = SafeFiles.Read<ContinuitySession>(FileSafety.Under(library.Root, "continuity/sessions/" + id + ".json")); session.Validate();
        if (Path.GetFullPath(session.LibraryRoot) != Path.GetFullPath(library.Root) || Path.GetFullPath(session.Config) != Path.GetFullPath(Path.Combine(library.State.GameFolder, "BepInEx/config"))) throw new InvalidDataException("Continuity session belongs to another library or installation.");
        return session;
    }
    public static void Configure(Library library, string nativeSave)
    {
        GameFiles.Validate(library.State.GameFolder); GameFiles.RequireClosed();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected native saves currently require Windows; Wine/Proton qualification is separate.");
        nativeSave = Path.GetFullPath(nativeSave); FileSafety.NoLinks(nativeSave);
        if (!nativeSave.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || !File.Exists(nativeSave)) throw new InvalidDataException("Choose an existing native .sav file.");
        string source = SafeFiles.Slot(Path.GetFileNameWithoutExtension(nativeSave));
        if (source.StartsWith("Protected - ", StringComparison.Ordinal)) throw new InvalidDataException("Resume the existing protected session instead of branching a branch.");
        _ = VerifyBundle(Bundle);
        JsonFiles.Write(OptionsPath(library), new ContinuityOptions(true, Path.GetDirectoryName(nativeSave)!, source));
        InstallBundle(library);
    }
    public static Dictionary<string, string> VerifyBundle(string folder)
    {
        var manifest = SafeFiles.Read<Dictionary<string, string>>(Path.Combine(folder, "payload.json"));
        string[] required = { "Luz.Continuity.Runtime.dll", "Luz.Continuity.dll", "catalog.json" };
        if (manifest.Count != required.Length || required.Any(f => !manifest.ContainsKey(f))) throw new InvalidDataException("Continuity bundle is incomplete.");
        foreach (var pair in manifest) if (SafeFiles.Hash(SafeFiles.Under(folder, pair.Key)) != pair.Value) throw new IOException("Continuity update payload failed verification: " + pair.Key);
        return manifest;
    }
    public static bool InstallBundle(Library library)
    {
        var expected = VerifyBundle(Bundle);
        var current = library.Active.Mods.Select(m => library.State.Packages.Single(p => p.Id == m.PackageId)).FirstOrDefault(p => p.Plugins.Any(i => i.Guid == PluginGuid));
        if (current != null && expected.All(p => File.Exists(FileSafety.Under(library.PackageRoot(current.Id), "payload/plugins/LUZContinuity/" + p.Key)) && FileSafety.Hash(FileSafety.Under(library.PackageRoot(current.Id), "payload/plugins/LUZContinuity/" + p.Key)) == p.Value)) return false;
        string archive = FileSafety.Under(library.Root, "staging/continuity-" + System.Guid.NewGuid().ToString("N") + ".zip"); Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        try
        {
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) foreach (var pair in expected) zip.CreateEntryFromFile(SafeFiles.Under(Bundle, pair.Key), "BepInEx/plugins/LUZContinuity/" + pair.Key);
            library.Import(archive); return true;
        }
        finally { if (File.Exists(archive)) File.Delete(archive); }
    }
    public static void CheckBeforeApply(Library library, CancellationToken cancellation)
    {
        var options = Options(library); if (!options.Enabled) return;
        GameFiles.RequireClosed();
        if (options.SessionId.Length != 0)
        {
            var session = Session(library, options.SessionId); Companions.VerifyOutputs(session);
        }
        _ = Checkpoints.Capture(library.Root, options.Saves, Path.Combine(library.State.GameFolder, "BepInEx/config"), cancellation);
    }
    public static void CheckpointBeforeAdmission(Library library, CancellationToken cancellation)
    {
        var options = Options(library); if (!options.Enabled) return;
        GameFiles.RequireClosed(); string game = library.State.GameFolder;
        string Stamp(string relative) { string path = FileSafety.Under(game, relative); return File.Exists(path) ? FileSafety.Hash(path) : "missing"; }
        string identity = SafeFiles.Key(options.Saves + options.SourceSlot + library.Fingerprint(library.Active) + Stamp("GameAssembly.dll") + Stamp("Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat") + Stamp("BepInEx/core/BepInEx.Unity.IL2CPP.dll"));
        string marker = FileSafety.Under(library.Root, "continuity/admission-" + library.Active.Id + ".json");
        if (File.Exists(marker))
        {
            var previous = SafeFiles.Read<Dictionary<string, string>>(marker);
            if (previous.GetValueOrDefault("Identity") == identity) { Checkpoints.Verify(previous["Checkpoint"]); return; }
        }
        string checkpoint = Checkpoints.Capture(library.Root, options.Saves, Path.Combine(game, "BepInEx/config"), cancellation);
        SafeFiles.Write(marker, new Dictionary<string, string> { ["Identity"] = identity, ["Checkpoint"] = checkpoint });
    }
    public static void Prepare(Library library, CompatibilityReport report, CancellationToken cancellation = default)
    {
        var options = Options(library);
        bool migrated = report.Identities?.Any(i => i.Identity.StartsWith("Luz.Continuity.Runtime,")) == true;
        if (!options.Enabled) { if (migrated) throw new InvalidOperationException("Enable protected saves in LUZ Maintenance before playing with continuity-aware mods."); return; }
        GameFiles.RequireClosed();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Protected saves are currently qualified only for native Windows.");
        if (options.SessionId.Length != 0 && Session(library, options.SessionId).RecoveryRequired) throw new IOException("Recover the protected timeline's recorded failure in LUZ before launching another session.");
        if (!migrated || report.Status != CompatibilityStatus.CheckedContractsPassed) throw new InvalidOperationException("A verified installed LUZ Continuity runtime is required; Apply the profile first.");
        string game = library.State.GameFolder, config = Path.Combine(game, "BepInEx/config"), installed = Path.Combine(game, "BepInEx/plugins/LUZContinuity");
        var bundle = VerifyBundle(Bundle);
        foreach (var pair in bundle) if (SafeFiles.Hash(Path.Combine(installed, pair.Key)) != pair.Value) throw new IOException("Apply the bundled continuity update before protected play.");
        string catalogPath = Path.Combine(installed, "catalog.json"); var catalog = SafeFiles.Read<AdapterCatalog>(catalogPath);
        var identity = new BuildIdentity(FileSafety.Hash(Path.Combine(game, "GameAssembly.dll")), FileSafety.Hash(Path.Combine(game, "Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat")), FileSafety.Hash(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")));
        _ = catalog.Require(identity);
        var inputs = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Compatibility.EffectiveFiles(library, true).Where(p => (p.Key.StartsWith("plugins/") || p.Key.StartsWith("patchers/")) && !p.Key.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)))
            inputs["BepInEx/" + file.Key] = FileSafety.Hash(file.Value);
        foreach (var area in new[] { "core", "interop", "unity-libs" })
        foreach (string file in Directory.EnumerateFiles(Path.Combine(game, "BepInEx", area), "*", SearchOption.TopDirectoryOnly))
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(file) == "assembly-hash.txt") inputs["BepInEx/" + area + "/" + Path.GetFileName(file)] = FileSafety.Hash(file);
        var supported = new HashSet<string> { PluginGuid, "hvizeu.nivalis.slowdays", "hvizeu.nivalis.citylife", "hvizeu.nivalis.unofficialpatch", "hvizeu.nivalis.furnishings", "hvizeu.nivalis.furniturelab" };
        foreach (var input in inputs.Where(p => p.Key.StartsWith("BepInEx/plugins/") && p.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            string file = SafeFiles.Under(game, input.Key);
            foreach (var plugin in PluginMetadata.Read(file))
            {
                if (!supported.Contains(plugin.Guid)) throw new InvalidDataException("Protected continuation has no preservation contract for " + plugin.Name + ". The preflight checkpoint is retained.");
                if (plugin.Guid != PluginGuid)
                {
                    using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(file);
                    if (!assembly.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2 && a.ConstructorArguments[0].Value as string == "Nivalis.Continuity.Api" && a.ConstructorArguments[1].Value as string == "1")) throw new InvalidDataException("Install the continuity migration build of " + plugin.Name + " before protected play.");
                }
            }
        }
        string catalogHash = SafeFiles.Hash(catalogPath);
        string epoch = SafeFiles.Key(library.Active.Id + library.Fingerprint(library.Active) + JsonSerializer.Serialize(identity) + catalogHash + JsonSerializer.Serialize(inputs));
        ContinuitySession? session = options.SessionId.Length == 0 ? null : Session(library, options.SessionId);
        if (session != null) { Checkpoints.Verify(session.Checkpoint); Companions.VerifyOutputs(session); }
        if (session?.Fingerprint != epoch)
        {
            cancellation.ThrowIfCancellationRequested(); string checkpoint = Checkpoints.Capture(library.Root, options.Saves, config, cancellation);
            string id = System.Guid.NewGuid().ToString("N"), source = session == null ? options.SourceSlot : session.ActiveSlot.Length != 0 ? session.ActiveSlot : session.BranchSlot;
            var dormant = session?.Dormant.ToList() ?? new();
            var restore = dormant.Select(d => d.Split('/')[0]).Distinct().Where(owner => inputs.Keys.Any(k => Path.GetFileName(k) == (owner == "citylife" ? "NivalisCityLife.dll" : owner == "bettertime" ? "NivalisSlowDays.dll" : "NivalisFurnishings.dll"))).ToList();
            foreach (var pair in new[] { ("citylife", "NivalisCityLife.dll"), ("bettertime", "NivalisSlowDays.dll") })
                if (!inputs.Keys.Any(k => Path.GetFileName(k) == pair.Item2) && (session?.Inputs.Keys.Any(k => Path.GetFileName(k) == pair.Item2) == true || Companions.ForSlot(source).Any(c => c.Owner == pair.Item1 && File.Exists(Path.Combine(config, c.Relative)))) && !dormant.Contains(pair.Item1 + "/module")) dormant.Add(pair.Item1 + "/module");
            if (session != null && session.Inputs.Keys.Any(k => Path.GetFileName(k) == "NivalisFurnishings.dll") && !inputs.Keys.Any(k => Path.GetFileName(k) == "NivalisFurnishings.dll")) throw new InvalidDataException("A compatible Object Framework persistence adapter is required for this timeline.");
            var suspendedPackages = session?.SuspendedPackages ?? new(); var suspendedPlugins = session?.SuspendedPlugins ?? new();
            session = new ContinuitySession { Id = id, LibraryRoot = library.Root, Saves = options.Saves, Config = config, SourceSlot = source, BranchSlot = "Protected - " + options.SourceSlot[..Math.Min(options.SourceSlot.Length, 48)] + " " + id[..8], Checkpoint = checkpoint, Fingerprint = epoch, CatalogHash = catalogHash, Identity = identity, Inputs = new(inputs, StringComparer.OrdinalIgnoreCase), Dormant = dormant, PendingRestore = restore, SuspendedPackages = suspendedPackages, SuspendedPlugins = suspendedPlugins };
            session.Slots.Add(session.SourceSlot, session.BranchSlot); session.Save();
            JsonFiles.Write(OptionsPath(library), options with { SessionId = session.Id }); Companions.Clone(session);
            foreach (string owner in dormant.Select(d => d.Split('/')[0]).Distinct()) Preservation.Capture(session, owner);
            JsonFiles.Write(OptionsPath(library), options with { SessionId = session.Id });
        }
        if (session == null) throw new InvalidDataException("Continuity session was not prepared.");
        foreach (var input in inputs) if (SafeFiles.Hash(SafeFiles.Under(game, input.Key)) != input.Value) throw new IOException("Installed inputs changed while preparing continuity.");
        SafeFiles.Write(SafeFiles.Under(config, "LUZContinuity/catalog.json"), catalog);
        if (SafeFiles.Hash(SafeFiles.Under(config, "LUZContinuity/catalog.json")) != catalogHash) File.Copy(catalogPath, SafeFiles.Under(config, "LUZContinuity/catalog.json"), true);
        SafeFiles.Write(SafeFiles.Under(config, "LUZContinuity/session.json"), session);
    }
    public static void Recover(Library library)
    {
        GameFiles.RequireClosed(); var options = Options(library); var session = Session(library, options.SessionId);
        Checkpoints.Verify(session.Checkpoint);
        if (Publications.Pending(session)) Publications.Restore(session); else Companions.VerifyOutputs(session);
        foreach (string owner in session.PendingRestore.ToArray())
            if (Preservation.Read(session, owner).State == "Restored") { session.Dormant.RemoveAll(d => d.StartsWith(owner + "/", StringComparison.Ordinal)); session.PendingRestore.Remove(owner); session.SuspendedPackages.Remove(owner); session.SuspendedPlugins.Remove(owner); }
        session.RecoveryRequired = false; session.Save();
    }
    public static void RestoreCheckpoint(Library library)
    {
        GameFiles.RequireClosed(); var options = Options(library); var previous = Session(library, options.SessionId);
        string game = library.State.GameFolder;
        var actual = new BuildIdentity(FileSafety.Hash(Path.Combine(game, "GameAssembly.dll")), FileSafety.Hash(Path.Combine(game, "Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat")), FileSafety.Hash(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")));
        if (actual != previous.Identity) throw new InvalidDataException("Qualify the current save adapter before restoring a checkpoint from another game build.");
        foreach (var input in previous.Inputs) if (FileSafety.Hash(FileSafety.Under(game, input.Key)) != input.Value) throw new IOException("Installed inputs differ from this checkpoint's qualified session.");
        var restored = TimelineRollback.Prepare(previous);
        JsonFiles.Write(OptionsPath(library), options with { SessionId = restored.Id }); TimelineRollback.Publish(restored);
    }
    public static void Suspend(Library library, string capability)
    {
        GameFiles.RequireClosed(); var options = Options(library); var session = Session(library, options.SessionId);
        // Native serialization remains active. Only explicitly isolated features may sleep.
        if (capability is not ("bettertime/module" or "citylife/module" or "framework/behaviours")) throw new InvalidDataException("No verified isolated preservation adapter for this capability.");
        bool added = !session.Dormant.Contains(capability); if (added) session.Dormant.Add(capability);
        string owner = capability.Split('/')[0];
        try { Preservation.Capture(session, owner); session.PendingRestore.Remove(owner); SetModuleAvailability(library, session, owner, false); session.Save(); }
        catch { if (added) session.Dormant.Remove(capability); throw; }
    }
    public static IReadOnlyList<Capability> Capabilities(Library library)
    {
        var options = Options(library); if (options.SessionId.Length == 0) return Array.Empty<Capability>();
        var session = Session(library, options.SessionId);
        return SafeFiles.Read<AdapterCatalog>(Path.Combine(library.State.GameFolder, "BepInEx/plugins/LUZContinuity/catalog.json")).Resolve(session.Identity, session.Dormant.ToHashSet());
    }
    public static void RequestRestore(Library library, string owner)
    {
        GameFiles.RequireClosed(); var session = Session(library, Options(library).SessionId); Companions.VerifyOutputs(session);
        if (!session.Dormant.Any(d => d.StartsWith(owner + "/", StringComparison.Ordinal))) return;
        _ = Preservation.Read(session, owner);
        if (!session.PendingRestore.Contains(owner)) session.PendingRestore.Add(owner);
        SetModuleAvailability(library, session, owner, true); session.Save();
    }
    private static void SetModuleAvailability(Library library, ContinuitySession session, string owner, bool enabled)
    {
        string? guid = owner == "citylife" ? "hvizeu.nivalis.citylife" : owner == "bettertime" ? "hvizeu.nivalis.slowdays" : null;
        if (guid == null) return; // Framework serialization remains installed during behavior dormancy.
        if (!session.SuspendedPackages.TryGetValue(owner, out var savedPackages)) session.SuspendedPackages[owner] = savedPackages = new();
        if (!session.SuspendedPlugins.TryGetValue(owner, out var savedPlugins)) session.SuspendedPlugins[owner] = savedPlugins = new();
        if (enabled)
        {
            foreach (var entry in library.Active.Mods)
            {
                var package = library.State.Packages.Single(p => p.Id == entry.PackageId);
                if (savedPackages.Contains(entry.PackageId) || package.Plugins.Any(p => p.Guid == guid || savedPlugins.Contains(p.Guid))) entry.Enabled = true;
            }
        }
        else
        {
            var unavailable = new HashSet<string> { guid }; bool changed;
            do
            {
                changed = false;
                foreach (var entry in library.Active.Mods.Where(e => e.Enabled))
                {
                    var package = library.State.Packages.Single(p => p.Id == entry.PackageId);
                    if (!package.Plugins.Any(p => unavailable.Contains(p.Guid) || p.Dependencies.Any(d => !d.Optional && unavailable.Contains(d.Guid))) && !package.RequiredPlugins.Any(d => !d.Optional && unavailable.Contains(d.Guid))) continue;
                    if (!savedPackages.Contains(entry.PackageId)) savedPackages.Add(entry.PackageId);
                    foreach (var plugin in package.Plugins) { unavailable.Add(plugin.Guid); if (!savedPlugins.Contains(plugin.Guid)) savedPlugins.Add(plugin.Guid); }
                    entry.Enabled = false; changed = true;
                }
            } while (changed);
        }
        library.Save();
    }
    public static void SynchronizeBundle(Library library)
    {
        if (!Options(library).Enabled) return;
        GameFiles.RequireClosed();
        bool applied = library.State.AppliedProfileId == library.Active.Id && library.State.AppliedFingerprint == library.Fingerprint(library.Active);
        if (InstallBundle(library) && applied) new Deployment(library).Apply();
    }
}
