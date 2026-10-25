using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luz.Continuity;
using NivalisRecovery;

internal static class ManagerSchemaChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        static string Key(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        static void WriteManager(string path, int schema, string revision)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { Version = schema, Revision = revision }));
        }
        static void Reject(Action action, Action<bool, string> check, string title)
        {
            try { action(); }
            catch (Exception error) when (error is InvalidDataException or JsonException)
            {
                check(true, title);
                return;
            }
            throw new Exception("FAILED accepted: " + title);
        }

        string preservationNative = Path.Combine(root, "ManagerSchemaPreservation.sav");
        File.WriteAllText(preservationNative, "native preservation fixture");
        string preservationHash = RecoveryFiles.Hash(preservationNative);
        var rule = Companions.ForSlot("Manager Schema Preservation").Single(item => item.Relative.Contains("ManagerPolicies", StringComparison.Ordinal));
        string preservationCompanion = Path.Combine(root, "manager-preservation.json");
        foreach (int schema in new[] { 2, 3, 4 })
        {
            WriteManager(preservationCompanion, schema, preservationHash);
            Companions.Validate(preservationCompanion, rule, preservationHash);
            check(true, $"continuity preservation accepts manager schema {schema}");
        }
        WriteManager(preservationCompanion, 5, preservationHash);
        Reject(() => Companions.Validate(preservationCompanion, rule, preservationHash), check, "continuity preservation rejects manager schema 5");
        File.WriteAllText(preservationCompanion, "{\"Version\":");
        Reject(() => Companions.Validate(preservationCompanion, rule, preservationHash), check, "continuity preservation rejects malformed manager state");

        string saves = Path.Combine(root, "ManagerSchemaSaves"), config = Path.Combine(root, "ManagerSchemaConfig"), sourceSlot = "Manager Schema Source";
        Directory.CreateDirectory(saves);
        string sourceNative = Path.Combine(saves, sourceSlot + ".sav");
        File.WriteAllText(sourceNative, "native recovery fixture");
        string sourceHash = RecoveryFiles.Hash(sourceNative);
        string sourceCompanion = Path.Combine(config, "NivalisUnofficialPatch", "ManagerPolicies", Key(sourceSlot) + ".json");
        RecoveryGeneration? generation = null;
        foreach (int schema in new[] { 2, 3, 4 })
        {
            WriteManager(sourceCompanion, schema, sourceHash);
            generation = RecoveryFiles.Inspect(saves, config, sourceSlot);
            check(generation.Companions.Values.Contains("manager"), $"recovery generation accepts manager schema {schema}");
        }
        WriteManager(sourceCompanion, 5, sourceHash);
        Reject(() => RecoveryFiles.Inspect(saves, config, sourceSlot), check, "recovery generation rejects manager schema 5");
        File.WriteAllText(sourceCompanion, "{\"Version\":");
        Reject(() => RecoveryFiles.Inspect(saves, config, sourceSlot), check, "recovery generation rejects malformed manager state");

        WriteManager(sourceCompanion, 4, sourceHash);
        generation = RecoveryFiles.Inspect(saves, config, sourceSlot);
        generation.State = "Complete";
        generation.PublishedSlot = generation.TargetSlot;
        string publishedNative = Path.Combine(saves, generation.PublishedSlot + ".sav");
        File.WriteAllText(publishedNative, "published recovery fixture");
        generation.OutputHash = RecoveryFiles.Hash(publishedNative);
        string sourceRelative = generation.Companions.Single(item => item.Value == "manager").Key;
        string publishedCompanion = RecoveryFiles.Under(config, RecoveryFiles.TargetCompanion(generation, sourceRelative));
        foreach (int schema in new[] { 2, 3, 4 })
        {
            WriteManager(publishedCompanion, schema, generation.OutputHash);
            RecoveryPublication.VerifyOutputs(saves, config, generation);
            check(true, $"recovery publication accepts manager schema {schema}");
        }
        WriteManager(publishedCompanion, 5, generation.OutputHash);
        Reject(() => RecoveryPublication.VerifyOutputs(saves, config, generation), check, "recovery publication rejects manager schema 5");
        File.WriteAllText(publishedCompanion, "{\"Version\":");
        Reject(() => RecoveryPublication.VerifyOutputs(saves, config, generation), check, "recovery publication rejects malformed manager state");

        string repairSlot = "Manager Revision Repair", repairNative = Path.Combine(saves, repairSlot + ".sav");
        File.WriteAllText(repairNative, "original manager generation");
        string oldRevision = RecoveryFiles.Hash(repairNative);
        string repairCompanion = Path.Combine(config, "NivalisUnofficialPatch", "ManagerPolicies", Key(repairSlot) + ".json");
        WriteManager(repairCompanion, 4, oldRevision);
        File.WriteAllText(repairCompanion + ".required", oldRevision);
        File.WriteAllText(repairNative, "new native generation written while the patch was absent");
        string newRevision = RecoveryFiles.Hash(repairNative), sourceCompanionHash = RecoveryFiles.Hash(repairCompanion);
        var repair = RecoveryFiles.Inspect(saves, config, repairSlot);
        check(repair.NativeHash == newRevision && repair.Companions.Values.Single() == "manager-reset", "validated manager revision mismatch is classified for reset repair");
        check(repair.Describe().Contains("resets Unofficial Patch manager policies/accounting", StringComparison.Ordinal)
            && repair.Describe().Contains("Native save and companions are preserved", StringComparison.Ordinal), "repair preview discloses reset scope and source preservation");
        string repairFolder = RecoveryFiles.Prepare(saves, config, repair);
        check(RecoveryFiles.Hash(repairNative) == newRevision && RecoveryFiles.Hash(repairCompanion) == sourceCompanionHash
            && File.ReadAllText(repairCompanion + ".required") == oldRevision, "repair preparation leaves the original native and manager files unchanged");
        check(RecoveryFiles.Hash(Path.Combine(repairFolder, "source.sav")) == newRevision
            && RecoveryFiles.Hash(RecoveryFiles.Under(repairFolder, "source/" + repair.Companions.Single().Key)) == sourceCompanionHash,
            "repair preparation captures the exact mismatched native and manager inputs");
        string repairedRelative = RecoveryFiles.TargetCompanion(repair, repair.Companions.Single().Key);
        check(repairedRelative.Contains("ManagerPolicies", StringComparison.Ordinal) && !File.Exists(RecoveryFiles.Under(config, repairedRelative)),
            "repair owns a distinct destination and does not prepublish stale manager state");

        WriteManager(repairCompanion + ".bak", 3, oldRevision);
        var repairWithBackup = RecoveryFiles.Inspect(saves, config, repairSlot);
        check(repairWithBackup.Companions.Values.Count(owner => owner == "manager-reset") == 1
            && repairWithBackup.Companions.Values.Count(owner => owner == "manager-reset-backup") == 1,
            "all valid mismatched manager candidates are captured before reset repair");
        string backupFolder = RecoveryFiles.Prepare(saves, config, repairWithBackup);
        string backupRelative = repairWithBackup.Companions.Single(item => item.Value == "manager-reset-backup").Key;
        check(RecoveryFiles.Hash(RecoveryFiles.Under(backupFolder, "source/" + backupRelative)) == RecoveryFiles.Hash(repairCompanion + ".bak"),
            "stale manager backup is retained as immutable recovery evidence");
        WriteManager(repairCompanion + ".bak", 4, newRevision);
        Reject(() => RecoveryFiles.Inspect(saves, config, repairSlot), check, "matching manager backup takes precedence over reset repair");
        File.WriteAllText(repairCompanion + ".bak", "{\"Version\":");
        Reject(() => RecoveryFiles.Inspect(saves, config, repairSlot), check, "malformed manager backup is not ignored by reset repair");
        File.Delete(repairCompanion + ".bak");

        File.WriteAllText(repairCompanion + ".required", "pending");
        Reject(() => RecoveryFiles.Inspect(saves, config, repairSlot), check, "interrupted manager publication is not converted into reset repair");
        File.WriteAllText(repairCompanion + ".required", new string('A', 64));
        Reject(() => RecoveryFiles.Inspect(saves, config, repairSlot), check, "manager marker disagreement is not converted into reset repair");
        File.WriteAllText(repairCompanion + ".required", oldRevision);
        WriteManager(repairCompanion, 4, "not-a-hash");
        Reject(() => RecoveryFiles.Inspect(saves, config, repairSlot), check, "malformed manager revision is not converted into reset repair");
    }
}
