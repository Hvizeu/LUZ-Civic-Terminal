using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Luz.Continuity;

namespace Luz.Continuity.Runtime;

[BepInPlugin(Guid, "LUZ Mod Continuity", "0.1.0")]
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
    private static readonly Dictionary<string, string> owners = new(StringComparer.Ordinal)
    {
        ["NivalisSlowDays"] = "bettertime", ["NivalisUnofficialPatch"] = "unofficialpatch", ["NivalisCityLife"] = "citylife", ["NivalisFurnishings"] = "framework", ["NivalisFurnitureLab"] = "furniturelab"
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
    }
    public static bool ValidateConsumer(string gameRoot, string assembly, string nativeHash, string metadataHash)
    {
        if (!Ready || Session == null || Path.GetFullPath(gameRoot) != Path.GetFullPath(game)) return false;
        if (!owners.TryGetValue(assembly, out var owner)) return false;
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
            Publications.Begin(Session, Companions.WriteSet(Session, __0)); __state = true; return true;
        }
        catch (Exception error) { report("Continuity save preflight failed: " + error); __result = false; return false; }
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
                if (refused.Before.All(p => refused.After[p.Key] == (p.Value ?? ""))) { Publications.Restore(Session!); return null; }
                throw new IOException("Native save was refused after changing its write set; publication remains pending.");
            }
            Preservation.CarryForward(Session!, __0);
            string hash = SafeFiles.Hash(Session!.File("saves/" + __0 + ".sav"));
            foreach (var companion in Companions.ForSlot(__0))
            {
                string file = Session.File("config/" + companion.Relative);
                if (File.Exists(file)) { Companions.Validate(file, companion.Owner, hash, verifyMarker: false); if (File.Exists(file + ".required")) File.WriteAllText(file + ".required", hash); }
                else if (File.Exists(file + ".required")) throw new InvalidDataException("Required companion was not published.");
            }
            Companions.VerifyOriginal(Session); Session.ActiveSlot = __0; Publications.Commit(Session);
        }
        catch (Exception error)
        {
            __result = false; report("Continuity publication failed; original checkpoint retained: " + error);
            try { Publications.Observe(Session!); Session!.Fail(error); }
            catch (Exception cleanup) { report(new AggregateException("Continuity failure retention also failed.", error, cleanup).ToString()); return new AggregateException(error, cleanup); }
            return __exception ?? error;
        }
        return __exception;
    }
}
