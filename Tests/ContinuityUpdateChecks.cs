using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Luz;

public static class ContinuityUpdateChecks
{
    public static void Run(string root, Action<bool, string> check, Action<Action, string> reject)
    {
        void RejectWith(Action action, string expected, string label)
        {
            try { action(); }
            catch (Exception error) when (error is IOException or InvalidDataException)
            { check(error.Message.Contains(expected, StringComparison.Ordinal), label); return; }
            throw new Exception("Accepted: " + label);
        }
        string Bundle(string name, bool maintained, bool additive = false, Func<Dictionary<string, string>, string>? manifestText = null)
        {
            string folder = Path.Combine(root, "continuity-update-" + name);
            Directory.CreateDirectory(folder);
            var files = new Dictionary<string, string> {
                ["Luz.Continuity.Runtime.dll"] = "runtime fixture; never execute",
                ["Luz.Continuity.dll"] = "api fixture; never execute",
                ["catalog.json"] = "{\"Schema\":1}" };
            if (maintained) files["maintained-support.json"] = "{\"Schema\":1,\"Modules\":[]}";
            if (additive) files["support/extra.json"] = "{\"Schema\":1}";
            var manifest = new Dictionary<string, string>();
            foreach (var file in files)
            {
                string path = Path.Combine(folder, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Value, Encoding.UTF8);
                manifest[file.Key] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            }
            File.WriteAllText(Path.Combine(folder, "payload.json"), manifestText?.Invoke(manifest) ?? JsonSerializer.Serialize(manifest));
            return folder;
        }

        string historic = Bundle("historic", false);
        check(ModContinuity.VerifyUpdateBundle(historic, new Version(0, 5, 0)).Count == 3
            && ModContinuity.VerifyUpdateBundle(historic, new Version(0, 7, 1)).Count == 3,
            "Historical three-file releases use their destination-era Continuity requirements");
        RejectWith(() => ModContinuity.VerifyUpdateBundle(historic, new Version(0, 7, 2)), "required manifest entries: maintained-support.json",
            "Newer destination releases still require maintained support");
        reject(() => ModContinuity.VerifyBundle(historic), "An older update bundle is not accepted as the installed current runtime");

        string current = Bundle("current", true);
        check(ModContinuity.VerifyUpdateBundle(current, new Version(0, 8, 8)).Count == 4
            && ModContinuity.VerifyBundle(current).Count == 4,
            "Current four-file update and installed runtime bundles verify");
        string extra = Bundle("additive", true, true);
        check(ModContinuity.VerifyUpdateBundle(extra, new Version(1, 0, 0)).Count == 5,
            "Staged future releases permit safe hashed additions without an exact entry-count dependency");
        reject(() => ModContinuity.VerifyBundle(extra), "Installed current support refuses an unrecognized runtime file set");
        File.AppendAllText(Path.Combine(extra, "support/extra.json"), "changed");
        RejectWith(() => ModContinuity.VerifyUpdateBundle(extra, new Version(1, 0, 0)), "checksum mismatch: support/extra.json", "Every additive update file is checksum verified");
        string missing = Bundle("missing-file", true, true);
        File.Delete(Path.Combine(missing, "support/extra.json"));
        RejectWith(() => ModContinuity.VerifyUpdateBundle(missing, new Version(1, 0, 0)), "payload file is missing: support/extra.json", "Declared additional files must exist");

        foreach (var (name, text) in new (string, Func<Dictionary<string, string>, string>)[] {
            ("duplicate", m => JsonSerializer.Serialize(m)[..^1] + ",\"catalog.json\":\"" + m["catalog.json"] + "\"}"),
            ("case-conflict", m => { m["CATALOG.JSON"] = m["catalog.json"]; return JsonSerializer.Serialize(m); }),
            ("traversal", m => { m["../escape.json"] = m["catalog.json"]; return JsonSerializer.Serialize(m); }),
            ("absolute", m => { m["/outside.json"] = m["catalog.json"]; return JsonSerializer.Serialize(m); }),
            ("bad-digest", m => { m["catalog.json"] = "not-a-sha256"; return JsonSerializer.Serialize(m); }),
            ("future-schema", _ => "{\"Schema\":2,\"Files\":{}}"),
            ("array", _ => "[]"),
            ("malformed-json", _ => "{") })
        {
            string invalid = Bundle(name, true, manifestText: text);
            reject(() => ModContinuity.VerifyUpdateBundle(invalid, new Version(1, 0, 0)), "Continuity update rejects " + name);
        }
        string lowercase = Bundle("lowercase-digests", true, manifestText: m => JsonSerializer.Serialize(m.ToDictionary(p => p.Key, p => p.Value.ToLowerInvariant())));
        check(ModContinuity.VerifyUpdateBundle(lowercase, new Version(0, 8, 8)).Count == 4,
            "SHA-256 hexadecimal casing does not change verified bytes");
        string absent = Bundle("missing-manifest", true);
        File.Delete(Path.Combine(absent, "payload.json"));
        reject(() => ModContinuity.VerifyUpdateBundle(absent, new Version(0, 8, 8)), "A missing update manifest is reported before installation");
    }
}
