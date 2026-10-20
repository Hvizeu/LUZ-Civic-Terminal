using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class RuntimeRecoveryAdapter
{
    public string Preflight(string context) => Record("Preflight", context);
    public string Activate(string context) => Record("Activate", context);
    public string PrepareSave(string context) => Record("PrepareSave", context);
    public string CompleteSave(string context)
    {
        using var document = JsonDocument.Parse(context);
        foreach (var file in document.RootElement.GetProperty("Files").EnumerateArray())
        {
            string path = file.GetProperty("Path").GetString()!;
            if (!File.Exists(path) || !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var data = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); data["Adapter"] = "complete"; File.WriteAllText(path, data.ToJsonString());
        }
        return Record("CompleteSave", context);
    }
    public string Suspend(string context) => Record("Suspend", context);
    public string Restore(string context) => Record("Restore", context);
    public string CleanupFailure(string context) => Record("CleanupFailure", context);
    private static string Record(string operation, string context)
    {
        string? log = Environment.GetEnvironmentVariable("LUZ_ADAPTER_FIXTURE_LOG"); if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, operation + Environment.NewLine);
        string? failure = Environment.GetEnvironmentVariable("LUZ_ADAPTER_FIXTURE_FAIL"); return failure == operation ? "fixture failure" : "ok";
    }
}
