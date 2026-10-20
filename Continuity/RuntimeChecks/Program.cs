using Luz.Continuity;
using Luz.Continuity.Runtime;
using Nivalis;
using System.Reflection;
using System.Text.Json.Nodes;

int count = 0;
void Check(bool value, string title) { if (!value) throw new Exception(title); Console.WriteLine("PASS " + title); count++; }
string root = Path.Combine(Path.GetTempPath(), "LuzRuntimeBoundaries-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string game = Path.Combine(root, "game"), config = Path.Combine(game, "BepInEx/config"), saves = Path.Combine(root, "saves"), library = Path.Combine(root, "library");
    Directory.CreateDirectory(config); Directory.CreateDirectory(saves); Directory.CreateDirectory(library);
    foreach (string path in new[] { "GameAssembly.dll", "Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat", "BepInEx/core/BepInEx.Unity.IL2CPP.dll" }) { string file = Path.Combine(game, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "test-owned identity " + path); }
    File.WriteAllText(Path.Combine(saves, "Original.sav"), "original native data");
    var identity = new BuildIdentity(SafeFiles.Hash(Path.Combine(game, "GameAssembly.dll")), SafeFiles.Hash(Path.Combine(game, "Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat")), SafeFiles.Hash(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")));
    var catalog = new AdapterCatalog { Builds = new[] { new AdapterBuild("test-boundary", identity, Array.Empty<CapabilityRule>()) } };
    string catalogPath = SafeFiles.Under(config, "LUZContinuity/catalog.json"); SafeFiles.Write(catalogPath, catalog);
    string checkpoint = Checkpoints.Capture(library, saves, config);
    string supportRoot = Path.Combine(game, "BepInEx/plugins/Fixture/LUZ"), adapterFolder = Path.Combine(supportRoot, "adapters"); Directory.CreateDirectory(adapterFolder);
    string adapterFile = Path.Combine(adapterFolder, "AdapterFixture.dll"); File.Copy(Path.Combine(AppContext.BaseDirectory, "AdapterFixture.dll"), adapterFile);
    var descriptor = new ModSupportDescriptor
    {
        Owner = "fixture", DisplayName = "Runtime fixture", SupportContact = "fixture",
        Plugin = new("fixture.plugin", "FixtureMod", "1.0.0", "FixtureMod.dll", new string('A', 64)),
        Compatibility = new(identity.Native, identity.Metadata, 2, new string('B', 64)),
        Capabilities = [new("module", [new("fixture-adapter", "adapter", "fixture-adapter", 1000)], [])],
        Companions = [new("config", "Fixture/{slot}.json", "raw", false, "json-revision", 1, 1)],
        Adapters = [new("fixture-adapter", "adapters/AdapterFixture.dll", SafeFiles.Hash(adapterFile), "RuntimeRecoveryAdapter", ["module"])]
    };
    string descriptorPath = Path.Combine(supportRoot, "mod-support.json"); SafeFiles.Write(descriptorPath, descriptor);
    var frozen = new FrozenModSupport { DescriptorPath = Path.GetRelativePath(game, descriptorPath).Replace('\\', '/'), DescriptorSha256 = SafeFiles.Hash(descriptorPath), Descriptor = descriptor, SelectedImplementations = new() { ["module"] = "fixture-adapter" } };
    var session = new ContinuitySession { LibraryRoot = library, Saves = saves, Config = config, SourceSlot = "Original", BranchSlot = "Protected - Original fixture", Checkpoint = checkpoint, Identity = identity, CatalogHash = SafeFiles.Hash(catalogPath), Support = [frozen] };
    session.Inputs[frozen.DescriptorPath] = frozen.DescriptorSha256; session.Inputs["BepInEx/plugins/Fixture/LUZ/adapters/AdapterFixture.dll"] = SafeFiles.Hash(adapterFile);
    session.Slots.Add(session.SourceSlot, session.BranchSlot); session.Save(); Companions.Clone(session); SafeFiles.Write(SafeFiles.Under(config, "LUZContinuity/session.json"), session);
    UnityEngine.Application.persistentDataPath = saves;
    var clock = new TimeOfDayManager();
    string adapterLog = Path.Combine(root, "adapter-lifecycle.log"); Environment.SetEnvironmentVariable("LUZ_ADAPTER_FIXTURE_LOG", adapterLog);
    var messages = new List<string>(); Host.Initialize(game, config, messages.Add);
    typeof(Host).GetProperty(nameof(Host.Ready))!.SetValue(null, true);
    Host.AddTime(clock, 15); Check(clock.Seconds == 15, "production clock adapter invokes the current method exactly once");
    var inventory = new Nivalis.InventorySystem.ItemContainer();
    Check(Host.TryAdd(inventory, new Nivalis.InventorySystem.ItemInstanceData(1)) == 2 && inventory.Count == 1 && Host.TryAdd(inventory, new Nivalis.InventorySystem.ItemInstanceData(0)) == 0 && inventory.Count == 1, "production inventory facade preserves native success and refusal without fabricating goods");
    bool failedInsert = false;
    try { Host.TryAdd(inventory, new Nivalis.InventorySystem.ItemInstanceData(-1)); } catch (InvalidOperationException error) { failedInsert = error.Message == "native insertion rejected"; }
    Check(failedInsert && inventory.Attempts == 3 && inventory.Count == 1, "production inventory failure preserves first native cause and does not retry the mutation");
    var vendor = new Nivalis.Economy.Vendor(); Host.RegisterVendorItem(vendor, new Nivalis.Economy.EconomyManager.VendorItemDefinition(), new Nivalis.InventorySystem.ItemType());
    Check(vendor.Registered == 1, "production object registration facade calls the native owner once");
    Check(Host.ValidateConsumer(game, "NivalisSlowDays", identity.Native, identity.Metadata) && !Host.ValidateConsumer(game, "UnknownMod", identity.Native, identity.Metadata), "production consumer admission accepts declared suite and rejects unknown owner");
    Check(!Host.ValidateConsumer(game, "NivalisSlowDays", "old-native-layout", identity.Metadata), "residual raw native access cannot bypass its build guard");
    string load = "Original"; Host.BeforeLoad(ref load); Check(load == session.BranchSlot, "production load callback selects branch before a gameplay module loads companions");
    string unspecified = null!; bool rejectedUnspecified = false;
    try { Host.BeforeLoad(ref unspecified); } catch (InvalidDataException) { rejectedUnspecified = true; }
    Check(rejectedUnspecified, "unspecified or new-game load cannot silently replace the selected protected timeline");
    bool Save(string logical, bool auto)
    {
        bool result = false; string routed = logical;
        if (!Host.BeforeSave(ref routed, ref result, out bool started)) return false;
        File.WriteAllText(Path.Combine(saves, routed + ".sav"), "native progress:" + logical);
        var companion = Companions.ForSlot(Host.Session!, routed).Single(c => c.Owner == "fixture"); string companionFile = Host.Session!.File(companion.Root + "/" + companion.Relative); Directory.CreateDirectory(Path.GetDirectoryName(companionFile)!);
        File.WriteAllText(companionFile, new JsonObject { ["Schema"] = 1, ["Revision"] = SafeFiles.Hash(Path.Combine(saves, routed + ".sav")) }.ToJsonString());
        result = true; Exception? error = Host.AfterSave(routed, ref result, started, null);
        if (error != null) throw error; return result;
    }
    Check(Save("Manual", false) && Save("Quick Save 1", false), "production manual-save and quicksave callbacks publish branches");
    var manualRule = Companions.ForSlot(Host.Session!, Host.Session!.Slots["Manual"]).Single(c => c.Owner == "fixture");
    string manualCompanion = Host.Session.File(manualRule.Root + "/" + manualRule.Relative);
    Check((string?)JsonNode.Parse(File.ReadAllText(manualCompanion))!["Adapter"] == "complete" && File.ReadAllText(adapterLog).Contains("Preflight") && File.ReadAllText(adapterLog).Contains("CompleteSave"), "author recovery adapter transforms staged companions before publication");
    var native = new SerializationManager { Root = saves, SaveCallback = Save };
    for (int turn = 0; turn < 5; turn++) { bool result = false; Check(!Host.BeforeAutoSave(native, ref result) && result, "production autosave route completes cycle " + turn); }
    var stored = SafeFiles.Read<ContinuitySession>(session.Record);
    Check(stored.Slots.Keys.Count(k => k.StartsWith("LUZ Auto Save")) == 2 && File.ReadAllText(Path.Combine(saves, "Original.sav")) == "original native data", "autosave rotation remains bounded and does not rename or delete original saves");
    bool deleted = true; Check(!Host.BeforeDelete(ref deleted) && !deleted, "production deletion callback protects source and retained branches");
    string refusedSlot = "Native Saving Locked"; bool refusedResult = false;
    Check(Host.BeforeSave(ref refusedSlot, ref refusedResult, out bool refusedState) && Host.AfterSave(refusedSlot, ref refusedResult, refusedState, null) == null && !refusedResult && !Publications.Pending(Host.Session!), "ordinary native refusal with no file changes leaves retry available and preserves false outcome");
    UnityEngine.Application.persistentDataPath = Path.Combine(root, "wrong-directory");
    bool accepted = false; string blocked = "Manual"; Check(!Host.BeforeSave(ref blocked, ref accepted, out _) && !accepted, "runtime save-directory mismatch refuses writes");
    UnityEngine.Application.persistentDataPath = saves;
    string failing = "Failing Save"; bool saved = false;
    Check(Host.BeforeSave(ref failing, ref saved, out bool began), "failure fixture enters the actual production publication");
    File.WriteAllText(Path.Combine(saves, failing + ".sav"), "partial native output");
    saved = true; var first = new IOException("native serialization fixture failure");
    Check(ReferenceEquals(Host.AfterSave(failing, ref saved, began, first), first) && !saved, "native exception survives production finalizer and success is revoked");
    stored = SafeFiles.Read<ContinuitySession>(session.Record);
    Check(stored.RecoveryRequired && stored.FirstFailure.Contains(first.Message) && Publications.Pending(stored), "failed production publication retains original cause and an actionable journal");
    Publications.Restore(stored); Check(!File.Exists(Path.Combine(saves, failing + ".sav")), "production failed new output is retained outside the live save tree during recovery");
    Check(File.ReadAllText(adapterLog).Contains("CleanupFailure"), "author recovery adapter receives failure cleanup without retrying the native transaction");
    Console.WriteLine(count + " production runtime boundary checks passed. No game was executed.");
}
finally { Environment.SetEnvironmentVariable("LUZ_ADAPTER_FIXTURE_LOG", null); Environment.SetEnvironmentVariable("LUZ_ADAPTER_FIXTURE_FAIL", null); if (Directory.Exists(root)) Directory.Delete(root, true); }
