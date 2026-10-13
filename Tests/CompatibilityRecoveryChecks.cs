using Mono.Cecil;
using Mono.Cecil.Cil;
using NivalisRecovery;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luz;

internal static class CompatibilityRecoveryChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        ContractEdgeChecks.Run(root,check,reject);
        string game = Path.Combine(root, "ContractGame"), config = Path.Combine(game, "BepInEx/config"), bindings = Path.Combine(game, "BepInEx/interop");
        Directory.CreateDirectory(bindings); File.WriteAllText(Path.Combine(game, "Nivalis Nights.exe"), "fixture"); File.WriteAllText(Path.Combine(game, "GameAssembly.dll"), "fixture");
        var native = AssemblyDefinition.CreateAssembly(new("Assembly-CSharp", new(1, 0)), "Assembly-CSharp", ModuleKind.Dll);
        var type = new TypeDefinition("Nivalis.UI", "ItemListUI", TypeAttributes.Public | TypeAttributes.Class, native.MainModule.TypeSystem.Object); native.MainModule.Types.Add(type);
        var method = new MethodDefinition("EndUpdate", MethodAttributes.Public | MethodAttributes.Static, native.MainModule.TypeSystem.Void); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); type.Methods.Add(method);
        native.Write(Path.Combine(bindings, "Assembly-CSharp.dll")); native.Dispose();
        string plugin = Path.Combine(root, "Caller.dll");
        using (var caller = AssemblyDefinition.CreateAssembly(new("Caller", new(1, 0)), "Caller", ModuleKind.Dll))
        {
            var scope = new AssemblyNameReference("Assembly-CSharp", new(1, 0)); caller.MainModule.AssemblyReferences.Add(scope);
            var target = new TypeReference("Nivalis.UI", "ItemListUI", caller.MainModule, scope);
            var missing = new MethodReference("set_isUpdating", caller.MainModule.TypeSystem.Void, target); missing.Parameters.Add(new(caller.MainModule.TypeSystem.Boolean));
            var owner = new TypeDefinition("Fixture", "Caller", TypeAttributes.Public | TypeAttributes.Class, caller.MainModule.TypeSystem.Object); caller.MainModule.Types.Add(owner);
            var invoke = new MethodDefinition("Invoke", MethodAttributes.Public | MethodAttributes.Static, caller.MainModule.TypeSystem.Void); owner.Methods.Add(invoke);
            invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0)); invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Call, missing)); invoke.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); caller.Write(plugin);
        }
        var snapshot = new CompatibilitySnapshot(game, "fixture", false, new(StringComparer.OrdinalIgnoreCase) { ["plugins/Caller.dll"] = plugin });
        var report = Compatibility.InspectSnapshot(snapshot);
        check(report.Status == CompatibilityStatus.Incompatible && report.Findings.Any(f => f.Code == "MissingMember" && f.Expected.Contains("set_isUpdating")), "actual removed setter is rejected before invocation");
        check(report.Findings.Any(f => f.Code == "BindingsUnverified"), "missing binding provenance remains unverified");
        var acknowledgment = Compatibility.Acknowledge(report); Compatibility.Authorize(report, acknowledgment);
        reject(() => Compatibility.Authorize(report, acknowledgment), "override is single use");
        reject(() => Compatibility.Authorize(report with { Fingerprint = "changed" }, Compatibility.Acknowledge(report)), "changed inputs invalidate acknowledgment");
        reject(() => Compatibility.Authorize(report with { Action = "Play" }, Compatibility.Acknowledge(report)), "Apply approval does not authorize Play");
        var hard = report with { Findings = new[] { new CompatibilityFinding("Recovery", "Error", "LUZ", "complete journal", "pending", false) } };
        reject(() => Compatibility.Acknowledge(hard), "pending recovery cannot be overridden");
        using (var cancellation = new CancellationTokenSource())
        { cancellation.Cancel(); try { Compatibility.InspectSnapshot(snapshot, cancellation.Token); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { check(true, "metadata inspection cancels before parsing"); } }

        string saves = Path.Combine(root, "RecoverySaves"); Directory.CreateDirectory(saves); Directory.CreateDirectory(config);
        const string slot = "Recovery Fixture"; string nativeSave = Path.Combine(saves, slot + ".sav"); File.WriteAllText(nativeSave, "native fixture"); string hash = RecoveryFiles.Hash(nativeSave);
        string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        string greenhouse = "2e590131-3ebc-41f0-a0ac-8f248839c1cb", item = "e0d06c77-9c9e-48d5-ab37-70dea79ab71c";
        var candidates = Enumerable.Range(0, 3).Select(i => new { Id = "a11e2026" + Key("Hvizeu.Hauler/" + greenhouse + "/" + i).ToLowerInvariant()[..24], Template = item }).ToArray();
        string companion = Path.Combine(config, "NivalisUnofficialPatch/Hauling", Key(slot) + ".json"); Directory.CreateDirectory(Path.GetDirectoryName(companion)!);
        File.WriteAllText(companion, JsonSerializer.Serialize(new { Schema = 4, Revision = hash, Greenhouses = new Dictionary<string, object> { [greenhouse] = new { Greenhouse = greenhouse, Slot = 0, Candidates = candidates, Cargo = new Dictionary<string, int[][]> { [item] = new[] { new[] { 1, 2, 3, 4 } } }, Surplus = new Dictionary<string, int[][]> { [item] = new[] { new[] { -5, 6, 7, 8 } } } } } }));
        var generation = RecoveryFiles.Inspect(saves, config, slot);
        check(generation.Workers.Count == 3 && generation.Goods[item].Count == 2 && generation.Goods[item][1][0] == -5, "recovery retains exact worker identities and every native tuple value");
        string folder = RecoveryFiles.Prepare(saves, config, generation);
        RecoveryFiles.ValidateCapture(folder,generation);
        generation.Goods[item].Add(new[]{99,99,99,99});reject(()=>RecoveryFiles.ValidateCapture(folder,generation),"altered recovery requests cannot inject goods");generation.Goods[item].RemoveAt(2);
        check(RecoveryFiles.Hash(nativeSave) == hash && !File.Exists(Path.Combine(saves, generation.TargetSlot + ".sav")), "preparation preserves original and waits for explicit native execution");
        reject(() => RecoveryFiles.Prepare(saves, config, generation), "repeated preparation cannot overwrite a generation");
        File.WriteAllText(nativeSave, "changed timeline"); reject(() => RecoveryFiles.Prepare(saves, config, generation), "changed native revision prevents capture"); File.WriteAllText(nativeSave, "native fixture");
        File.WriteAllText(companion + ".required", hash); File.Move(companion, companion + ".held"); reject(() => RecoveryFiles.Inspect(saves, config, slot), "missing required inventory companion refuses recovery"); File.Move(companion + ".held", companion);
        string raw = File.ReadAllText(companion); File.WriteAllText(companion, raw.Replace(candidates[0].Id, "a11e2026" + new string('0', 24))); reject(() => RecoveryFiles.Inspect(saves, config, slot), "lookalike worker identity is never retired"); File.WriteAllText(companion, raw);
        reject(() => RecoveryFiles.Under(config, "../outside"), "recovery paths cannot escape their owner");
        generation.State = "Complete"; generation.PublishedSlot = generation.TargetSlot;
        string destination = Path.Combine(saves, generation.TargetSlot + ".sav"); File.WriteAllText(destination, "last good"); generation.OutputHash = RecoveryFiles.Hash(destination);
        RecoveryFiles.Write(Path.Combine(folder, "journal.json"), generation);
        RecoveryWriteSet.Capture(saves, config, folder, generation, generation.TargetSlot);
        File.WriteAllText(destination, "partially published"); generation.Goods.Clear();
        RecoveryWriteSet.Observe(saves, config, folder, false);
        var restored = RecoveryWriteSet.Restore(saves, config, folder);
        check(File.ReadAllText(destination) == "last good" && restored.Goods[item].Count == 2 && restored.State == "Complete", "partial publication restores native bytes and unclaimed goods together");
        RecoveryWriteSet.Capture(saves, config, folder, restored, restored.TargetSlot); File.WriteAllText(destination, "owned failure"); RecoveryWriteSet.Observe(saves, config, folder, false); File.WriteAllText(destination, "external edit");
        reject(() => RecoveryWriteSet.Restore(saves, config, folder), "external save edits block rollback instead of being overwritten");
    }
}
