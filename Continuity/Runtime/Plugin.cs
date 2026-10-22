using System.Reflection;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Luz.Continuity;

namespace Luz.Continuity.Runtime;

[BepInPlugin(Guid, "LUZ Mod Continuity", "0.2.0")]
[BepInProcess("Nivalis Nights.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "hvizeu.luz.continuity";
    private readonly Harmony hooks = new(Guid);
    public override void Load()
    {
        try
        {
            Host.Initialize(Paths.GameRootPath, Paths.ConfigPath, error => Log.LogError(error));
            Type native = Host.GameType("Nivalis.SerializationManager");
            var save = native.GetMethod("Save", new[] { typeof(string), typeof(bool) }) ?? throw new MissingMethodException("Native Save(string,bool) is unavailable.");
            var load = native.GetMethod("Load", new[] { typeof(string) }) ?? throw new MissingMethodException("Native Load(string) is unavailable.");
            if (save.ReturnType != typeof(bool) || load.ReturnType != typeof(void)) throw new MissingMethodException("Native save/load return contracts changed.");
            hooks.Patch(save, new HarmonyMethod(typeof(Host), nameof(Host.BeforeSave)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(Host), nameof(Host.AfterSave)) { priority = Priority.Last });
            hooks.Patch(load, new HarmonyMethod(typeof(Host), nameof(Host.BeforeLoad)) { priority = Priority.First });
            var autoSave = native.GetMethod("AutoSave", Type.EmptyTypes) ?? throw new MissingMethodException("Native autosave contract is unavailable.");
            hooks.Patch(autoSave, new HarmonyMethod(typeof(Host), nameof(Host.BeforeAutoSave)) { priority = Priority.First });
            var delete = native.GetMethod("DeleteSaveFiles", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException("Native save deletion contract is unavailable.");
            hooks.Patch(delete, new HarmonyMethod(typeof(Host), nameof(Host.BeforeDelete)) { priority = Priority.First });
            Host.Ready = true;
            Log.LogInfo("Protected timeline: " + Host.Session!.BranchSlot + ". Original checkpoint: " + Path.GetFileName(Host.Session.Checkpoint));
        }
        catch (Exception error)
        {
            Host.Ready = false;
            try { hooks.UnpatchSelf(); } catch (Exception cleanup) { Log.LogError(new AggregateException(error, cleanup)); throw; }
            Log.LogError("Continuity could not activate. Migrated modules remain disabled: " + error); throw;
        }
    }
    public override bool Unload() => false; // Save ownership lasts for the process lifetime.
}

/// <summary>No game types appear in public signatures or bootstrap fields.</summary>
public static class Host
{
    public const int ApiVersion = 1;
    public static bool Ready { get; internal set; }
    public static ContinuitySession? Session { get; private set; }
    private static AdapterCatalog catalog = null!;
    private static Action<string> report = null!;
    private static string game = "";
    private static Operations operations = null!;
    private static readonly List<LoadedSupportAdapter> supportAdapters = new();
    private static readonly Dictionary<string, string> owners = new(StringComparer.Ordinal)
    {
        ["NivalisSlowDays"] = "bettertime", ["NivalisUnofficialPatch"] = "unofficialpatch", ["NivalisExpanded"] = "expanded", ["NivalisCityLife"] = "citylife", ["NivalisFurnishings"] = "framework", ["NivalisFurnitureLab"] = "furniturelab"
    };
    public static Type GameType(string name) => (AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp") ?? Assembly.Load("Assembly-CSharp")).GetType(name, true)!;
    public static void Initialize(string gameRoot, string config, Action<string> log)
    {
        game = gameRoot; report = log;
        string request = SafeFiles.Under(config, "LUZContinuity/session.json");
        var session = SafeFiles.Read<ContinuitySession>(request); session.Validate();
        if (Path.GetFullPath(config) != Path.GetFullPath(session.Config)) throw new InvalidDataException("Continuity config path differs from the active installation.");
        string persisted = session.Record;
        if (!File.Exists(persisted)) throw new InvalidDataException("LUZ session receipt is missing.");
        var current = SafeFiles.Read<ContinuitySession>(persisted); current.Validate();
        if (current.Id != session.Id || current.Fingerprint != session.Fingerprint || current.CatalogHash != session.CatalogHash) throw new InvalidDataException("LUZ session request is stale.");
        Session = current;
        string adapter = SafeFiles.Under(config, "LUZContinuity/catalog.json");
        if (SafeFiles.Hash(adapter) != current.CatalogHash) throw new IOException("Continuity adapter differs from the LUZ session.");
        catalog = SafeFiles.Read<AdapterCatalog>(adapter);
        var observed = new BuildIdentity(SafeFiles.Hash(Path.Combine(game, "GameAssembly.dll")), SafeFiles.Hash(Path.Combine(game, "Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat")), SafeFiles.Hash(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")));
        _ = catalog.Require(observed);
        if (observed != current.Identity) throw new IOException("Game identity changed after LUZ preflight.");
        foreach (var input in current.Inputs) if (SafeFiles.Hash(SafeFiles.Under(game, input.Key)) != input.Value) throw new IOException("Installed input changed after LUZ preflight: " + input.Key);
        Checkpoints.Verify(current.Checkpoint); Companions.VerifyOutputs(current);
        if (catalog.Resolve(observed, current.Dormant.ToHashSet()).Any(c => c.State == CapabilityState.Blocked)) throw new InvalidDataException("A required continuity capability is unavailable.");
        var clock = GameType("Nivalis.TimeOfDayManager").GetMethod("AddTime", new[] { typeof(float) }) ?? throw new MissingMethodException("Current clock adapter requires AddTime(float).");
        operations = new Operations();
        operations.Register("clock.add-time.v1", CapabilityState.Native, args => { try { clock.Invoke(args[0], new[] { args[1] }); return true; } catch (TargetInvocationException error) when (error.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; } });
        operations.Register("input.binding-valid.v1", CapabilityState.Fallback, args => InputPolicy.BindingValid((int)args[0]!, (int)args[1]!, args[2] as string));
        var insert = GameType("Nivalis.InventorySystem.ItemContainer").GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(m => m.Name == "TryAdd" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "ItemInstanceData");
        operations.Register("inventory.try-add.v1", CapabilityState.Native, args => InvokeNative(insert, args[0]!, new[] { args[1] })?.ToString() switch { "Failed" => 0, "Partial" => 1, "Succeeded" => 2, _ => throw new InvalidDataException("Unrecognized native inventory outcome; no retry was attempted.") });
        var vendor = GameType("Nivalis.Economy.Vendor").GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single(m => m.Name == "TryRegisterItem" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType.Name == "VendorItemDefinition" && m.GetParameters()[1].ParameterType.Name == "ItemType");
        if (vendor.ReturnType != typeof(void)) throw new MissingMethodException("Native vendor registration return contract changed.");
        operations.Register("objects.register-vendor-item.v1", CapabilityState.Native, args => { InvokeNative(vendor, args[0]!, new[] { args[1], args[2] }); return true; });
        operations.Seal();
        LoadSupportAdapters();
    }
    public static bool ValidateConsumer(string gameRoot, string assembly, string nativeHash, string metadataHash)
    {
        if (!Ready || Session == null || Path.GetFullPath(gameRoot) != Path.GetFullPath(game)) return false;
        string? owner = Session.Support.SingleOrDefault(s => s.Descriptor.Plugin.Assembly == assembly)?.Descriptor.Owner;
        if (owner == null && !owners.TryGetValue(assembly, out owner)) return false;
        // A different source build needs a reviewed grant for this exact frozen consumer.
        var payloads = Session.Inputs.Where(p => Path.GetFileName(p.Key).Equals(assembly + ".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        string payload = payloads.Length == 1 ? payloads[0].Value : "";
        if (!catalog.Authorizes(Session.Identity, assembly, payload, nativeHash, metadataHash)) return false;
        return !IsDormant(owner, "module");
    }
    public static bool IsDormant(string owner, string capability) => Session?.Dormant.Contains(owner + "/" + capability) == true && !Session.PendingRestore.Contains(owner);
    public static bool BindingValid(int index, int count, string? path) => operations.Invoke<bool>("input.binding-valid.v1", index, count, path);
    public static void AddTime(object clock, float seconds) { if (!Ready || IsDormant("bettertime", "module")) throw new InvalidOperationException("Clock operation is unavailable."); _ = operations.Invoke<bool>("clock.add-time.v1", clock, seconds); }
    private static object? InvokeNative(MethodInfo method, object target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException error) when (error.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    public static int TryAdd(object destination, object item)
    {
        if (!Ready || Session?.RecoveryRequired != false) throw new InvalidOperationException("Inventory insertion is unavailable before recovery completes.");
        return operations.Invoke<int>("inventory.try-add.v1", destination, item);
    }
    public static void RegisterVendorItem(object vendor, object definition, object item)
    {
        if (!Ready || Session?.RecoveryRequired != false) throw new InvalidOperationException("Object registration is unavailable before recovery completes.");
        _ = operations.Invoke<bool>("objects.register-vendor-item.v1", vendor, definition, item);
    }
    public static string Status() => Session == null ? "Continuity unavailable" : Session.BranchSlot + (Session.RecoveryRequired ? " · save recovery required" : Session.PendingRestore.Count != 0 ? " · restoring: " + string.Join(", ", Session.PendingRestore) : Session.Dormant.Count == 0 ? " · verified adapters selected" : " · dormant: " + string.Join(", ", Session.Dormant));
    public static bool RestorationPending(string owner) => Session?.PendingRestore.Contains(owner) == true;
    public static void FailRestore(string owner, Exception error) { report(owner + " restoration failed: " + error); Session?.Fail(error); }
    public static void CompleteRestore(string owner)
    {
        if (Session == null || !Session.PendingRestore.Contains(owner)) return;
        // Called only after the owning module has successfully validated/loaded its state.
        Preservation.Restore(Session, owner, _ => Array.Empty<string>());
        Session.PendingRestore.Remove(owner); Session.SuspendedPackages.Remove(owner); Session.SuspendedPlugins.Remove(owner); Session.Save();
    }
    private sealed class LoadedSupportAdapter
    {
        public string Owner { get; }
        private readonly object instance;
        private readonly Dictionary<string, MethodInfo> methods;
        public LoadedSupportAdapter(string owner, object instance)
        {
            Owner = owner; this.instance = instance;
            methods = new[] { "Preflight", "Activate", "PrepareSave", "CompleteSave", "Suspend", "Restore", "CleanupFailure" }.ToDictionary(name => name, name => instance.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null) ?? throw new MissingMethodException(instance.GetType().FullName, name));
            if (methods.Values.Any(m => m.ReturnType != typeof(string))) throw new MissingMethodException("LUZ recovery adapter methods must return string.");
        }
        public void Invoke(string operation, Exception? failure = null, string stage = "", IReadOnlyList<StagedSupportFile>? files = null)
        {
            string context = JsonSerializer.Serialize(new { Api = 1, Operation = operation, Owner, Session = Host.Session!.Id, Host.Session.SourceSlot, Host.Session.ActiveSlot, Host.Session.BranchSlot, Host.Session.PendingRestore, Host.Session.Dormant, StageRoot = stage, Files = files ?? Array.Empty<StagedSupportFile>(), Failure = failure?.ToString() });
            try
            {
                object? result = methods[operation].Invoke(instance, new object[] { context });
                if (!string.Equals(result as string, "ok", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Recovery adapter rejected " + operation + ": " + (result as string ?? "invalid response"));
            }
            catch (TargetInvocationException error) when (error.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }
    private sealed record StagedSupportFile(string Root, string Relative, string Path, string NativeHash, bool Existed);
    private static void LoadSupportAdapters()
    {
        supportAdapters.Clear();
        foreach (var support in Session!.Support)
        foreach (string adapterId in support.Descriptor.Capabilities.Select(c => c.Implementations.Single(i => i.Id == support.SelectedImplementations[c.Name])).Where(i => i.Kind == "adapter").Select(i => i.Adapter).Distinct(StringComparer.Ordinal))
        {
            var declaration = support.Descriptor.Adapters.Single(a => a.Id == adapterId);
            string descriptor = SafeFiles.Under(game, support.DescriptorPath), file = SafeFiles.Under(Path.GetDirectoryName(descriptor)!, declaration.File);
            if (SafeFiles.Hash(file) != declaration.Sha256) throw new IOException("Recovery adapter changed after LUZ preflight: " + adapterId);
            // Load the verified bytes without holding the package DLL open for the process lifetime.
            var assembly = Assembly.Load(File.ReadAllBytes(file)); var type = assembly.GetType(declaration.Type, true)!;
            supportAdapters.Add(new(support.Descriptor.Owner, Activator.CreateInstance(type) ?? throw new InvalidOperationException("Recovery adapter could not be created.")));
        }
        foreach (var adapter in supportAdapters) adapter.Invoke("Preflight");
        var activated = new List<LoadedSupportAdapter>();
        try
        {
            foreach (var adapter in supportAdapters) { adapter.Invoke("Activate"); activated.Add(adapter); }
            foreach (var adapter in supportAdapters.Where(a => Session.Dormant.Any(d => d.StartsWith(a.Owner + "/", StringComparison.Ordinal)))) TransformSupport(adapter, "Suspend", Session.Slots.Values, true);
            foreach (var adapter in supportAdapters.Where(a => Session.PendingRestore.Contains(a.Owner))) { TransformSupport(adapter, "Restore", Session.Slots.Values, true); CompleteRestore(adapter.Owner); }
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            foreach (var adapter in activated.AsEnumerable().Reverse()) try { adapter.Invoke("CleanupFailure", error); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { Session!.Fail(error); } catch (Exception retention) { failures.Add(retention); }
            if (failures.Count == 1) throw; throw new AggregateException("Recovery adapter activation and cleanup failed.", failures);
        }
    }
    private static void InvokeSupport(string operation, Exception? failure = null)
    {
        var failures = new List<Exception>();
        foreach (var adapter in operation == "CleanupFailure" ? supportAdapters.AsEnumerable().Reverse() : supportAdapters)
        {
            try { adapter.Invoke(operation, failure); }
            catch (Exception error) { if (operation != "CleanupFailure") throw; failures.Add(error); }
        }
        if (failures.Count != 0) throw new AggregateException("Recovery adapter cleanup failed.", failures);
    }
    private static void TransformSupport(LoadedSupportAdapter adapter, string operation, IEnumerable<string> slots, bool ownJournal)
    {
        var session = Session!; var entries = new List<(CompanionFile Rule, string Live, string Stage, string NativeHash, bool Existed)>();
        string transaction = Publications.Root(session), stage = SafeFiles.Under(transaction, "adapter-stage-" + adapter.Owner + "-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        foreach (string slot in slots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string native = session.File("saves/" + slot + ".sav"); if (!File.Exists(native)) continue;
            string nativeHash = SafeFiles.Hash(native);
            foreach (var companion in Companions.ForSlot(session, slot).Where(c => c.Owner == adapter.Owner))
            {
                string relative = companion.Root + "/" + companion.Relative, live = session.File(relative), copy = SafeFiles.Under(stage, relative); bool existed = File.Exists(live);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!); if (existed) File.Copy(live, copy, true);
                entries.Add((companion, live, copy, nativeHash, existed));
            }
        }
        string[] paths = entries.Select(e => e.Rule.Root + "/" + e.Rule.Relative).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ownJournal && paths.Length != 0) Publications.Begin(session, paths);
        try
        {
            adapter.Invoke(operation, null, stage, entries.Select(e => new StagedSupportFile(e.Rule.Root, e.Rule.Relative, e.Stage, e.NativeHash, e.Existed)).ToArray());
            ValidateStage(stage, entries.Select(e => e.Stage).ToHashSet(StringComparer.OrdinalIgnoreCase));
            foreach (var entry in entries)
            {
                if (File.Exists(entry.Stage))
                {
                    Companions.Validate(entry.Stage, entry.Rule, entry.NativeHash, verifyMarker: false);
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Live)!); File.Copy(entry.Stage, entry.Live, true);
                }
                else if (entry.Rule.Required) throw new InvalidDataException("Recovery adapter removed a required companion: " + entry.Rule.Relative);
                else if (File.Exists(entry.Live)) File.Delete(entry.Live);
            }
            if (ownJournal && paths.Length != 0) Publications.Commit(session);
            Directory.Delete(stage, true);
        }
        catch (Exception error)
        {
            if (ownJournal && paths.Length != 0) throw Publications.RetainFailure(session, error);
            throw;
        }
    }
    private static void TransformSupport(string operation, string slot)
    {
        foreach (var adapter in supportAdapters) TransformSupport(adapter, operation, new[] { slot }, false);
    }
    private static void ValidateStage(string folder, IReadOnlySet<string> allowed)
    {
        void Visit(string directory)
        {
            SafeFiles.NoLinks(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                SafeFiles.NoLinks(entry);
                if (Directory.Exists(entry)) Visit(entry);
                else if (!allowed.Contains(Path.GetFullPath(entry))) throw new InvalidDataException("Recovery adapter created an undeclared staged file.");
            }
        }
        Visit(folder);
    }
    private static void RequireRuntime()
    {
        if (!Ready || Session == null || Session.RecoveryRequired) throw new IOException("Continuity is unavailable or an operation remains unresolved. Preserve the log and recover in LUZ.");
        var application = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "UnityEngine.CoreModule").GetType("UnityEngine.Application", true)!;
        string actual = (string)application.GetProperty("persistentDataPath")!.GetValue(null)!;
        if (!Path.GetFullPath(actual).Equals(Path.GetFullPath(Session.Saves), StringComparison.OrdinalIgnoreCase)) throw new IOException("Native save directory differs from the protected session.");
    }
    public static void BeforeLoad(ref string __0)
    {
        RequireRuntime(); Companions.VerifyOutputs(Session!); __0 = Session!.Route(__0, true);
        if (!File.Exists(Session.File("saves/" + __0 + ".sav"))) throw new IOException("Protected native save is absent; world teardown was not started.");
        Session.ActiveSlot = __0; Session.Save();
    }
    public static bool BeforeDelete(ref bool __result) { __result = false; report("Protected-session save deletion is unavailable. Manage retained timelines explicitly in LUZ."); return false; }
    public static bool BeforeAutoSave(object __instance, ref bool __result)
    {
        try
        {
            RequireRuntime(); int count = (int)GameType("Nivalis.SerializationManager").GetProperty("MaxAutoSaves")!.GetValue(null)!;
            if (count == 0) { __result = false; return false; }
            if (count is < 0 or > 64) throw new InvalidDataException("Unsupported autosave count.");
            int index = Session!.AutoSaveCursor % count;
            __result = (bool)GameType("Nivalis.SerializationManager").GetMethod("Save", new[] { typeof(string), typeof(bool) })!.Invoke(__instance, new object[] { "LUZ Auto Save " + (index + 1), true })!;
            if (__result) { Session.AutoSaveCursor = (index + 1) % count; Session.Save(); }
        }
        catch (Exception error) { __result = false; report("Protected autosave failed: " + error); Session?.Fail(error); }
        return false;
    }
    public static bool BeforeSave(ref string __0, ref bool __result, out bool __state)
    {
        __state = false;
        try
        {
            RequireRuntime(); Companions.VerifyOutputs(Session!); __0 = Session!.Route(__0, false);
            if (Session.PendingRestore.Count != 0) throw new InvalidOperationException("Restored modules have not acknowledged their native state; saving remains blocked.");
            InvokeSupport("PrepareSave");
            Publications.Begin(Session, Companions.WriteSet(Session, __0)); __state = true; return true;
        }
        catch (Exception error)
        {
            try { InvokeSupport("CleanupFailure", error); }
            catch (Exception cleanup) { error = new AggregateException("Continuity save preflight and adapter cleanup failed.", error, cleanup); }
            report("Continuity save preflight failed: " + error); __result = false; return false;
        }
    }
    public static Exception? AfterSave(string __0, ref bool __result, bool __state, Exception? __exception)
    {
        if (!__state) return __exception;
        try
        {
            if (__exception != null) throw new IOException("Native save raised an exception.", __exception);
            if (!__result)
            {
                Publications.Observe(Session!); var refused = Publications.Read(Session!);
                if (refused.Before.All(p => refused.After[p.Key] == (p.Value ?? ""))) { InvokeSupport("CleanupFailure", new IOException("Native save returned false.")); Publications.Restore(Session!); return null; }
                throw new IOException("Native save was refused after changing its write set; publication remains pending.");
            }
            Preservation.CarryForward(Session!, __0); TransformSupport("CompleteSave", __0);
            string hash = SafeFiles.Hash(Session!.File("saves/" + __0 + ".sav"));
            foreach (var companion in Companions.ForSlot(Session, __0))
            {
                string file = Session.File(companion.Root + "/" + companion.Relative);
                if (File.Exists(file)) { Companions.Validate(file, companion, hash, verifyMarker: false); if (File.Exists(file + ".required")) File.WriteAllText(file + ".required", hash); }
                else if (File.Exists(file + ".required")) throw new InvalidDataException("Required companion was not published.");
            }
            Companions.VerifyOriginal(Session); Session.ActiveSlot = __0; Publications.Commit(Session);
        }
        catch (Exception error)
        {
            __result = false; report("Continuity publication failed; original checkpoint retained: " + error);
            try { InvokeSupport("CleanupFailure", error); Publications.Observe(Session!); Session!.Fail(error); }
            catch (Exception cleanup) { report(new AggregateException("Continuity failure retention also failed.", error, cleanup).ToString()); return new AggregateException(error, cleanup); }
            return __exception ?? error;
        }
        return __exception;
    }
}
