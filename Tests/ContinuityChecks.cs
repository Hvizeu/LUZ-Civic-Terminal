using System.Text.Json;
using System.Text.Json.Nodes;
using Luz;
using Luz.Continuity;

internal static class ContinuityChecks
{
    private sealed class MigratedClockClient(Operations adapters)
    {
        public void Advance(float seconds) => adapters.Invoke<bool>("clock.add-time.v1", seconds);
    }
    public static void Run(string parent, Action<bool, string> check, Action<Action, string> reject)
    {
        string root = Path.Combine(parent, "continuity"), saves = Path.Combine(root, "game-saves"), config = Path.Combine(root, "game-config"), library = Path.Combine(root, "luz-library");
        Directory.CreateDirectory(saves); Directory.CreateDirectory(config); Directory.CreateDirectory(library);
        float gameTime = 0; var old = new Operations(); old.Register("clock.add-time.v1", CapabilityState.Native, args => { gameTime += (float)args[0]!; return true; }); old.Seal();
        new MigratedClockClient(old).Advance(5);
        var replacement = new Operations();
        void NewNativeSignature(float seconds, float factor) => gameTime += seconds * factor;
        replacement.Register("clock.add-time.v1", CapabilityState.Fallback, args => { NewNativeSignature((float)args[0]!, 1); return true; }); replacement.Seal();
        new MigratedClockClient(replacement).Advance(7);
        check(gameTime == 12 && replacement.State("clock.add-time.v1") == CapabilityState.Fallback, "unchanged migrated client executes through replacement signature adapter and mutates the same simulated clock once");
        int attempts = 0; var mutation = new Operations(); mutation.Register("purchase", CapabilityState.Native, _ => { attempts++; throw new IOException("native purchase failed"); }); mutation.Seal();
        reject(() => mutation.Invoke<bool>("purchase"), "native mutation failure propagates"); check(attempts == 1, "failed mutation is never retried through a fallback");
        check(!InputPolicy.BindingValid(-1, 2, "x") && !InputPolicy.BindingValid(2, 2, "x") && !InputPolicy.BindingValid(0, 2, "") && InputPolicy.BindingValid(1, 2, "<Gamepad>/a"), "input fallback validates missing, out-of-range, empty and valid native bindings");
        var identity = new BuildIdentity("native-a", "metadata-a", "loader-a");
        var catalog = new AdapterCatalog { Builds = new[] { new AdapterBuild("fixture", identity, new[] { new CapabilityRule("core", "save", "native", Array.Empty<string>()), new CapabilityRule("citylife", "module", "native", new[] { "core/save" }) }) } };
        reject(() => catalog.Require(identity with { Native = "unknown" }), "unknown native layout blocks continuity");
        reject(() => catalog.Require(identity with { Metadata = "stale" }), "stale metadata blocks continuity");
        reject(() => catalog.Require(identity with { Loader = "changed" }), "unsupported loader blocks continuity");
        check(catalog.Resolve(identity, new HashSet<string> { "core/save" }).Single(c => c.Owner == "citylife").State == CapabilityState.Blocked, "missing save capability blocks dependent modules");
        string unchangedClient = SafeFiles.Key("unchanged migrated DLL");
        catalog.Builds[0] = catalog.Builds[0] with { Grants = new[] { new ConsumerGrant("NivalisSlowDays", unchangedClient, "previous-native", "previous-metadata", SafeFiles.Key("reviewed member and native layout qualification")) } };
        check(catalog.Authorizes(identity, "NivalisSlowDays", unchangedClient, "previous-native", "previous-metadata"), "adapter catalog alone can admit an unchanged frozen consumer after reviewed native-contract qualification");
        check(!catalog.Authorizes(identity, "NivalisSlowDays", SafeFiles.Key("changed DLL"), "previous-native", "previous-metadata") && !catalog.Authorizes(identity, "OtherMod", unchangedClient, "previous-native", "previous-metadata"), "qualification never transfers to another payload or module");
        string original = Path.Combine(saves, "Quick Save 3.sav");
        File.WriteAllText(original, "vanilla-xp=10;money=500;inventory=item:1;home=home-1");
        string city = Companions.ForSlot("Quick Save 3").Single(c => c.Owner == "citylife").Relative;
        string companion = SafeFiles.Under(config, city); Directory.CreateDirectory(Path.GetDirectoryName(companion)!);
        var payload = new JsonObject { ["Schema"] = 3, ["Revision"] = SafeFiles.Hash(original), ["Needs"] = new JsonObject { ["Food"] = 81 }, ["Character"] = new JsonObject { ["Home"] = "home-1" }, ["UnknownFutureField"] = new JsonObject { ["ExactData"] = "retained" } };
        SafeFiles.Write(companion, payload); File.WriteAllText(companion + ".required", SafeFiles.Hash(original));
        string checkpoint = Checkpoints.Capture(library, saves, config);
        var manifest = Checkpoints.Verify(checkpoint);
        check(manifest.Label == "Backup Save 001" && manifest.Files.ContainsKey("config/" + city + ".required"), "verified checkpoint captures native save, companion and required marker together");
        var session = new ContinuitySession { LibraryRoot = library, Saves = saves, Config = config, SourceSlot = "Quick Save 3", BranchSlot = "Protected - Quick Save 3 abcd", Checkpoint = checkpoint, Identity = identity };
        session.Slots.Add(session.SourceSlot, session.BranchSlot); session.Save(); Companions.Clone(session);
        string branch = session.File("saves/" + session.BranchSlot + ".sav");
        check(File.ReadAllText(branch) == File.ReadAllText(original) && Companions.ForSlot(session.BranchSlot).Where(c => c.Owner == "citylife").All(c => File.Exists(session.File("config/" + c.Relative))), "branch starts from matching native bytes and remapped companion identity");
        check(!Directory.Exists(Path.Combine(library, "continuity/dormant")), "working compatibility creates no dormant-data cache");
        reject(() => Preservation.Capture(session, "citylife"), "active capabilities cannot cache dormant data");
        session.Dormant.Add("citylife/module"); Preservation.Capture(session, "citylife"); session.Save();
        var dormantRecord = Preservation.Read(session, "citylife");
        string dormantFile = Preservation.StateFile(session, dormantRecord, dormantRecord.Captured.Keys.Single());
        string frozen = SafeFiles.Hash(dormantFile);
        string originalHash = SafeFiles.Hash(original);
        for (int turn = 1; turn <= 3; turn++)
        {
            string slot = turn == 1 ? session.Route("Quick Save 4", false) : turn == 2 ? session.Route("LUZ Auto Save 1", false) : session.Route("Quick Save 4", false);
            Publications.Begin(session, Companions.WriteSet(session, slot));
            File.WriteAllText(session.File("saves/" + slot + ".sav"), "vanilla-xp=" + (10 + turn * 10) + ";money=500;inventory=item:1;home=home-1");
            Preservation.CarryForward(session, slot); Publications.Commit(session);
            var record = Companions.ForSlot(slot).Single(c => c.Owner == "citylife");
            var data = SafeFiles.Read<JsonObject>(session.File("config/" + record.Relative));
            check((string?)data["Revision"] == SafeFiles.Hash(session.File("saves/" + slot + ".sav")) && (string?)data["UnknownFutureField"]?["ExactData"] == "retained", "dormant record survives actual continuation save " + turn + " without dropping unknown fields");
        }
        check(SafeFiles.Hash(original) == originalHash && session.Slots.Count == 3 && session.Route("Quick Save 4", true) == session.Route("Quick Save 4", false), "manual and rotating autosave routes reuse branch slots and preserve the original");
        reject(() => session.Route("Different historical save", true), "unselected historical timeline cannot silently replace the active branch");
        reject(() => Preservation.Restore(session, "citylife", _ => new[] { "home-1 has conflicting ownership" }), "conflicting restored ownership retains dormant records");
        check(Preservation.Read(session, "citylife").State == "Conflict" && session.Dormant.Contains("citylife/module"), "conflict cannot be reported as completed restoration");
        string newest = session.File("saves/" + session.Route("Quick Save 4", true) + ".sav"), newestBytes = File.ReadAllText(newest);
        Preservation.Restore(session, "citylife", _ => Array.Empty<string>());
        Preservation.Restore(session, "citylife", _ => throw new Exception("repeat restore must be idempotent"));
        check(File.ReadAllText(newest) == newestBytes && newestBytes.Contains("vanilla-xp=40") && newestBytes.Contains("inventory=item:1") && !session.Dormant.Any(), "restoration retains newer vanilla progress and cannot duplicate inventory on repeated invocation");
        check(SafeFiles.Hash(dormantFile) == frozen, "opaque dormant checkpoint remains immutable after restoration");
        session.ActiveSlot = session.Route("Quick Save 4", true);
        string latestCity = session.File("config/" + Companions.ForSlot(session.ActiveSlot).Single(c => c.Owner == "citylife").Relative);
        Publications.Begin(session, Companions.WriteSet(session, session.ActiveSlot));
        var latestState = SafeFiles.Read<JsonObject>(latestCity); latestState["Needs"]!["Food"] = 72; SafeFiles.Write(latestCity, latestState); Publications.Commit(session);
        session.Dormant.Add("citylife/module"); var secondCapture = Preservation.Capture(session, "citylife"); session.Save();
        check(secondCapture.Generation != dormantRecord.Generation && secondCapture.SourceSlot == session.ActiveSlot && (int?)SafeFiles.Read<JsonObject>(Preservation.StateFile(session, secondCapture, secondCapture.Captured.Keys.Single()))["Needs"]?["Food"] == 72 && SafeFiles.Hash(dormantFile) == frozen, "a second suspension captures the latest active timeline and retains the previous immutable generation");
        Preservation.Restore(session, "citylife", _ => Array.Empty<string>());
        string acknowledged = session.Route("Acknowledged Save", false), acknowledgedPath = "saves/" + acknowledged + ".sav";
        Publications.Begin(session, new[] { acknowledgedPath }); File.WriteAllText(session.File(acknowledgedPath), "committed newer progress"); Publications.Observe(session);
        var pendingCommit = Publications.Read(session); session.LastOutputs[acknowledgedPath] = SafeFiles.Hash(session.File(acknowledgedPath)); session.Save(); pendingCommit.State = "Committed";
        SafeFiles.Write(Path.Combine(Publications.Root(session), "commit-" + pendingCommit.Id + ".json"), pendingCommit);
        Publications.Restore(session);
        check(File.ReadAllText(session.File(acknowledgedPath)) == "committed newer progress" && !Publications.Pending(session), "restart after commit receipt publication finalizes success without rolling newer progress back");
        string destination = session.Route("Quick Save 4", true);
        Publications.Begin(session, Companions.WriteSet(session, destination)); File.WriteAllText(newest, "partially written native save"); Publications.Observe(session); session.Fail(new IOException("simulated disk fault"));
        Publications.Restore(session);
        check(File.ReadAllText(newest) == newestBytes && session.FirstFailure.Contains("simulated disk fault") && session.FailureCount == 1, "interrupted native and companion publication restores together and retains first failure");
        Publications.Begin(session, Companions.WriteSet(session, destination)); File.WriteAllText(newest, "owned partial output"); Publications.Observe(session); File.WriteAllText(newest, "cloud replaced output");
        reject(() => Publications.Restore(session), "external or cloud replacement is never overwritten by rollback");
        check(File.ReadAllText(newest) == "cloud replaced output" && Publications.Pending(session), "ambiguous publication retains both external output and recovery journal");
        File.WriteAllText(newest, "owned partial output"); Publications.Restore(session);
        File.WriteAllText(original, "cloud replaced original"); reject(() => Companions.VerifyOutputs(session), "external original-save replacement pauses the protected session");
        File.Copy(Path.Combine(checkpoint, "saves/Quick Save 3.sav"), original, true);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); bool stopped = false;
            try { Checkpoints.Capture(library, saves, config, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            check(stopped && SafeFiles.Hash(original) == originalHash, "checkpoint cancellation preserves the source timeline");
        }
        reject(() => Checkpoints.Capture(saves, saves, config), "checkpoint cannot be placed inside the live save tree");
        reject(() => SafeFiles.Under(root, "../escape"), "continuity records cannot escape owned paths");
        reject(() => SafeFiles.Slot("../original"), "native slot traversal is rejected");
        string capturedNative = Path.Combine(checkpoint, "saves/Quick Save 3.sav"), capturedText = File.ReadAllText(capturedNative);
        File.WriteAllText(capturedNative, "tampered checkpoint"); reject(() => Checkpoints.Verify(checkpoint), "corrupt checkpoint is never offered as a restore point"); File.WriteAllText(capturedNative, capturedText);
        check(Checkpoints.Verify(checkpoint).Files["saves/Quick Save 3.sav"] == originalHash, "original checkpoint still independently verifies after negative controls");
        string currentProgress = File.ReadAllText(newest);
        File.WriteAllText(original, "externally replaced original retained");
        var rollback = TimelineRollback.Prepare(session); TimelineRollback.Publish(rollback);
        check(File.ReadAllText(rollback.File("saves/" + rollback.BranchSlot + ".sav")) == capturedText && File.ReadAllText(original) == "externally replaced original retained" && File.ReadAllText(newest) == currentProgress, "original checkpoint creates a fresh verified timeline without overwriting current progress or external originals");
        Companions.VerifyOutputs(rollback);
        check(rollback.Id != session.Id && rollback.Checkpoint == session.Checkpoint, "rollback retains independent timeline identity and the immutable recovery point");
        string savedReceipt = rollback.Record + ".held"; File.Move(rollback.Record, savedReceipt); Directory.CreateDirectory(rollback.Record);
        var cause = new IOException("first owned operation failure"); var retention = Publications.RetainFailure(rollback, cause);
        check(retention is AggregateException aggregate && ReferenceEquals(aggregate.InnerExceptions[0], cause) && rollback.FirstFailure.Contains(cause.Message) && rollback.CleanupFailures.Count != 0, "failure retention errors preserve the original cause, occurrence count and cleanup failures");
        Directory.Delete(rollback.Record); File.Move(savedReceipt, rollback.Record);
        Availability(root, check);
    }
    private static void Availability(string parent, Action<bool, string> check)
    {
        string area = Path.Combine(parent, "availability"), game = Path.Combine(area, "game"), config = Path.Combine(game, "BepInEx/config"), saves = Path.Combine(area, "saves");
        Directory.CreateDirectory(config); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, "Source.sav"), "native ownership fixture");
        using var library = new Library(Path.Combine(area, "library")); library.State.GameFolder = game;
        var city = new ModPackage { Plugins = new() { new("hvizeu.nivalis.citylife", "City Life", "1.2.0", new(), new()) } };
        var dependent = new ModPackage { Plugins = new() { new("fixture.dependent", "Dependent", "1.0.0", new() { new("hvizeu.nivalis.citylife", "1.0.0", false) }, new()) } };
        var unrelated = new ModPackage { Plugins = new() { new("fixture.unrelated", "Unrelated", "1.0.0", new(), new()) } };
        library.State.Packages.AddRange(new[] { city, dependent, unrelated });
        library.Active.Mods.AddRange(new[] { new ModEntry { PackageId = city.Id }, new ModEntry { PackageId = dependent.Id }, new ModEntry { PackageId = unrelated.Id, Enabled = false } }); library.Save();
        var session = new ContinuitySession { LibraryRoot = library.Root, Saves = saves, Config = config, SourceSlot = "Source", BranchSlot = "Protected - Source fixture", Checkpoint = Checkpoints.Capture(library.Root, saves, config) };
        session.Slots.Add(session.SourceSlot, session.BranchSlot); session.Save(); Companions.Clone(session);
        JsonFiles.Write(Path.Combine(library.Root, "continuity/options-" + library.Active.Id + ".json"), new ContinuityOptions(true, saves, "Source", session.Id));
        ModContinuity.Suspend(library, "citylife/module");
        check(!library.Active.Mods[0].Enabled && !library.Active.Mods[1].Enabled && !library.Active.Mods[2].Enabled, "module suspension removes the failing module and hard dependants from the staged profile without enabling unrelated entries");
        ModContinuity.RequestRestore(library, "citylife");
        check(library.Active.Mods[0].Enabled && library.Active.Mods[1].Enabled && !library.Active.Mods[2].Enabled && ModContinuity.Session(library, session.Id).PendingRestore.Contains("citylife"), "restoration reenables the recorded dependency set and waits for the loaded-state acknowledgement");
        var other = new ContinuitySession { LibraryRoot = library.Root, Saves = saves, Config = config, SourceSlot = "Source", BranchSlot = "Protected - Another timeline", Checkpoint = session.Checkpoint };
        other.Slots.Add(other.SourceSlot, other.BranchSlot); other.Save(); Companions.Clone(other);
        ModContinuity.SelectTimeline(library, other.Id); ModContinuity.SelectTimeline(library, session.Id);
        check(ModContinuity.Options(library).SessionId == session.Id && ModContinuity.Timelines(library).Count == 2 && File.Exists(Path.Combine(saves, other.BranchSlot + ".sav")), "retained timelines can be selected again through LUZ without deleting either timeline's progress");
        if (OperatingSystem.IsWindows())
        {
            session = ModContinuity.Session(library, session.Id); session.Fail(new IOException("unresolved persistence fixture"));
            bool blocked = false;
            try { ModContinuity.Prepare(library, new(1, "Play", "fixture", CompatibilityStatus.CheckedContractsPassed, Array.Empty<CompatibilityFinding>())); }
            catch (IOException error) { blocked = error.Message.Contains("before launching"); }
            check(blocked, "a recorded persistence failure blocks LUZ launch admission even without a pending file publication");
        }
    }
}
