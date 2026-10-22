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
    }
}
