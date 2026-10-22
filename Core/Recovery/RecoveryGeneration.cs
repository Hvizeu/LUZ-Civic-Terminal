using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NivalisRecovery;

public sealed class RecoveryGeneration
{
    public int Schema { get; set; } = 1;
    public string Id { get; set; } = "";
    public string SourceSlot { get; set; } = "";
    public string TargetSlot { get; set; } = "";
    public string NativeHash { get; set; } = "";
    public string State { get; set; } = "Prepared";
    public string OutputHash { get; set; } = "";
    public string PublishedSlot { get; set; } = "";
    public string SaveDirectory { get; set; } = "";
    public bool DestinationOwned { get; set; }
    public List<string> OwnedCopies { get; set; } = new();
    public string FirstFailure { get; set; } = "";
    public int FailureOccurrences { get; set; }
    public List<string> CleanupFailures { get; set; } = new();
    public Dictionary<string, string> Workers { get; set; } = new();
    public Dictionary<string, List<int[]>> Goods { get; set; } = new();
    public Dictionary<string, string> Companions { get; set; } = new();
    public Dictionary<string, string> CompanionHashes { get; set; } = new();
    public string Describe() => $"{SourceSlot} → {TargetSlot}\n{Workers.Count} retired worker identities; {Goods.Sum(p => p.Value.Count)} original inventory records.\nNative save and companions are preserved. Tables require a native relationship audit. State: {State}.";
    public void Validate()
    {
        if (Schema != 1 || Id == null || Id.Length != 32 || !Id.All(Uri.IsHexDigit) || !RecoveryFiles.IsHash(NativeHash) || Workers == null || Goods == null || Companions == null || CompanionHashes == null || PublishedSlot == null) throw new InvalidDataException("Unsupported recovery generation.");
        RecoveryFiles.Slot(SourceSlot); RecoveryFiles.Slot(TargetSlot);
        if (PublishedSlot.Length != 0) RecoveryFiles.Slot(PublishedSlot);
        if (SourceSlot == TargetSlot || !TargetSlot.StartsWith("Recovered ", StringComparison.Ordinal) || Workers.Count > 3840 || Goods.Count > 4096 || Goods.Sum(p => p.Value.Count) > 65536)
            throw new InvalidDataException("Recovery limits or destination identity are invalid.");
        if (State is not ("Prepared" or "Loading" or "Publishing" or "Complete" or "Failed" or "Claiming" or "Abandoned")) throw new InvalidDataException("Unknown recovery state.");
        if (State == "Complete" && (PublishedSlot.Length == 0 || !RecoveryFiles.IsHash(OutputHash))) throw new InvalidDataException("Completed recovery has no verified native publication.");
        foreach (var worker in Workers) if (worker.Key.Length != 32 || !worker.Key.StartsWith("a11e2026", StringComparison.Ordinal) || !Guid.TryParse(worker.Value, out _)) throw new InvalidDataException("Invalid retired worker identity.");
        foreach (var item in Goods) if (!Guid.TryParse(item.Key, out _) || item.Value == null || item.Value.Any(r => r == null || r.Length != 4)) throw new InvalidDataException("Invalid recovery inventory record.");
        if (Companions.Count > 3 || CompanionHashes.Count != Companions.Count || OwnedCopies == null || OwnedCopies.Count > 8) throw new InvalidDataException("Invalid companion ownership set.");
        foreach (var copy in OwnedCopies) RecoveryFiles.Relative(copy);
        foreach (var companion in Companions) { RecoveryFiles.Relative(companion.Key); if (companion.Value is not ("hauling" or "manager" or "city") || !CompanionHashes.TryGetValue(companion.Key,out var hash) || !RecoveryFiles.IsHash(hash)) throw new InvalidDataException("Missing or invalid companion receipt."); }
    }
}

public static class RecoveryFiles
{
    public static bool IsHash(string? value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
    public static string Hash(string path) { using var stream = File.OpenRead(path); using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(stream)); }
    public static string Slot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.IndexOfAny(new[] { '/', '\\', ':', '\0','<','>','|','?','*','"' }) >= 0 || value != Path.GetFileName(value) || value.EndsWith('.') || value.EndsWith(' ')) throw new InvalidDataException("Invalid save slot name.");
        return value;
    }
    public static string Relative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('/') || value.Contains('\\') || value.Contains(':') || value.Split('/').Any(s => s is "" or "." or "..")) throw new InvalidDataException("Invalid recovery relative path.");
        return value;
    }
    public static string Under(string root, string relative)
    {
        Relative(relative); string path = Path.GetFullPath(Path.Combine(root, relative));
        var current = new FileInfo(path).Directory;
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery does not follow file links.");
        string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        while (current != null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Recovery does not follow directory links.");
            if (current.FullName.Equals(boundary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return path;
            current = current.Parent;
        }
        throw new IOException("Recovery path escapes its owner.");
    }
    public static void Write(string path, RecoveryGeneration value)
    {
        value.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
        File.Move(temp, path, true);
    }
    public static RecoveryGeneration Read(string path)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Recovery record exceeds its size limit.");
        var value = JsonSerializer.Deserialize<RecoveryGeneration>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty recovery record."); value.Validate(); return value;
    }
    private static string Key(string slot) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slot)));
    public static RecoveryGeneration Inspect(string saves, string config, string slot) => InspectNative(Under(saves, Slot(slot) + ".sav"), config, slot);
    private static RecoveryGeneration InspectNative(string native, string config, string slot)
    {
        Slot(slot);
        if (new FileInfo(native).Length > 128 * 1024 * 1024) throw new InvalidDataException("Native recovery input exceeds 128 MiB.");
        string revision = Hash(native), id = Guid.NewGuid().ToString("N");
        var result = new RecoveryGeneration { Id = id, SourceSlot = slot, TargetSlot = "Recovered " + slot[..Math.Min(48, slot.Length)] + " " + id[..8], NativeHash = revision };
        string stem = Key(slot), upper = Key(Path.GetFileNameWithoutExtension(slot).ToUpperInvariant());
        var supported = new Dictionary<string, string> {
            ["NivalisUnofficialPatch/Hauling/" + stem + ".json"] = "hauling",
            ["NivalisUnofficialPatch/ManagerPolicies/" + stem + ".json"] = "manager",
            ["NivalisCityLife/Saves/" + upper + ".json"] = "city" };
        foreach (var pair in supported)
        {
            string file = Under(config, pair.Key);
            if (!File.Exists(file)) { if (File.Exists(file + ".required")) throw new InvalidDataException("Required companion is missing: " + pair.Key); continue; }
            if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new InvalidDataException("Companion exceeds its size limit.");
            var data = JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? throw new InvalidDataException("Invalid companion.");
            int schema = (int?)(data["Schema"] ?? data["Version"]) ?? -1;
            if (pair.Value == "hauling" ? schema is < 1 or > 4 : pair.Value == "city" ? schema is < 1 or > 3 : schema is < 2 or > 4) throw new InvalidDataException("Unsupported companion schema: " + pair.Key);
            if ((string?)data["Revision"] != revision) throw new InvalidDataException("Companion revision mismatch: " + pair.Key);
            if(File.Exists(file+".required")&&(new FileInfo(file+".required").Length>256||!File.ReadAllText(file+".required").Trim().Equals(revision,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Required companion marker differs from the native generation: "+pair.Key);
            result.Companions[pair.Key] = pair.Value; result.CompanionHashes[pair.Key] = Hash(file);
            if (pair.Value == "hauling") ExtractHauling(data, result);
        }
        foreach (string marker in RequiredMarkers(config))
            if ((Path.GetFileName(marker).StartsWith(stem) || Path.GetFileName(marker).StartsWith(upper)) && !supported.ContainsKey(Path.GetRelativePath(config, marker[..^9]).Replace('\\', '/')))
                throw new InvalidDataException("Unsupported required save companion: " + Path.GetRelativePath(config, marker));
        if (Hash(native) != revision) throw new IOException("Native save changed during recovery inspection.");
        result.Validate(); return result;
    }
    private static IEnumerable<string> RequiredMarkers(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var pending = new Stack<string>(); pending.Push(root); int count = 0;
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 20000) throw new InvalidDataException("Save companion inspection exceeds its file limit.");
                string safe = Under(root, Path.GetRelativePath(root, entry).Replace('\\','/'));
                if (Path.GetRelativePath(root,safe).Replace('\\','/').Equals("NivalisUnofficialPatch/Recovery",StringComparison.Ordinal)) continue;
                if (Directory.Exists(safe)) pending.Push(safe);
                else if (safe.EndsWith(".required",StringComparison.Ordinal)) yield return safe;
            }
        }
    }
    public static void ValidateCapture(string folder, RecoveryGeneration value, bool remaining = false)
    {
        value.Validate();
        var expected = InspectNative(Under(folder,"source.sav"), Under(folder,"source"), value.SourceSlot);
        if (expected.NativeHash != value.NativeHash || !expected.Companions.OrderBy(p=>p.Key).SequenceEqual(value.Companions.OrderBy(p=>p.Key)) || !expected.CompanionHashes.OrderBy(p=>p.Key).SequenceEqual(value.CompanionHashes.OrderBy(p=>p.Key)) || !expected.Workers.OrderBy(p=>p.Key).SequenceEqual(value.Workers.OrderBy(p=>p.Key)))
            throw new InvalidDataException("Recovery request differs from its captured ownership records.");
        foreach (var item in value.Goods)
        {
            if (!expected.Goods.TryGetValue(item.Key,out var original)) throw new InvalidDataException("Recovery contains an uncaptured item type.");
            var available = original.GroupBy(r=>string.Join(",",r)).ToDictionary(g=>g.Key,g=>g.Count());
            foreach (var row in item.Value)
            { string key=string.Join(",",row); if (!available.TryGetValue(key,out int count)||count==0) throw new InvalidDataException("Recovery contains additional or altered goods."); available[key]=count-1; }
            if (!remaining && available.Values.Any(n=>n!=0)) throw new InvalidDataException("Recovery dropped captured goods.");
        }
        if (!remaining && expected.Goods.Keys.Any(k=>!value.Goods.ContainsKey(k))) throw new InvalidDataException("Recovery dropped a captured inventory.");
    }
    private static void ExtractHauling(JsonObject document, RecoveryGeneration result)
    {
        var crews = document["Greenhouses"]?.AsObject() ?? throw new InvalidDataException("Missing greenhouse records.");
        if (crews.Count > 640) throw new InvalidDataException("Too many greenhouse records.");
        foreach (var pair in crews)
        {
            var crew = pair.Value!.AsObject(); string greenhouse = (string?)crew["Greenhouse"] ?? ""; int slot = (int?)crew["Slot"] ?? 0;
            if (!Guid.TryParse(greenhouse, out _) || slot is < 0 or > 4) throw new InvalidDataException("Invalid greenhouse ownership.");
            var candidates = crew["Candidates"]!.AsArray(); if (candidates.Count is not (3 or 6)) throw new InvalidDataException("Unsupported worker candidate record.");
            for (int i = 0; i < candidates.Count; i++)
            {
                int index = i < 3 ? slot * 3 + i : 15 + slot * 3 + i - 3;
                string expected = "a11e2026" + Key("Hvizeu.Hauler/" + greenhouse + "/" + index).ToLowerInvariant()[..24];
                if ((string?)candidates[i]!["Id"] != expected) throw new InvalidDataException("Retired worker identity does not match its owner.");
                result.Workers.Add(expected, (string?)candidates[i]!["Template"] ?? "");
            }
            foreach (string field in new[] { "Cargo", "Surplus" })
            {
                var stock = crew[field]?.AsObject() ?? throw new InvalidDataException("Missing greenhouse inventory: " + field);
                foreach (var item in stock)
                {
                    if (!result.Goods.TryGetValue(item.Key, out var rows)) result.Goods.Add(item.Key, rows = new());
                    foreach (var tuple in item.Value!.AsArray()) rows.Add(tuple!.AsArray().Select(n => n!.GetValue<int>()).ToArray());
                }
            }
        }
    }
    public static string Prepare(string saves, string config, RecoveryGeneration value)
    {
        value.Validate(); var fresh = Inspect(saves, config, value.SourceSlot);
        if (value.State != "Prepared" || fresh.NativeHash != value.NativeHash || !fresh.CompanionHashes.OrderBy(p => p.Key).SequenceEqual(value.CompanionHashes.OrderBy(p => p.Key)) || !fresh.Workers.OrderBy(p=>p.Key).SequenceEqual(value.Workers.OrderBy(p=>p.Key)) || JsonSerializer.Serialize(fresh.Goods)!=JsonSerializer.Serialize(value.Goods)) throw new IOException("Recovery inputs changed after preview.");
        string folder = Under(config, "NivalisUnofficialPatch/Recovery/" + value.Id);
        value.SaveDirectory=Path.GetFullPath(saves);
        if (Directory.Exists(folder)) throw new IOException("Recovery generation already exists.");
        Directory.CreateDirectory(folder);
        var incomplete=JsonSerializer.Deserialize<RecoveryGeneration>(JsonSerializer.Serialize(value))!;incomplete.State="Failed";
        Write(Path.Combine(folder, "journal.json"), incomplete);
        File.Copy(Under(saves, value.SourceSlot + ".sav"), Path.Combine(folder, "source.sav"));
        foreach (var companion in value.Companions)
        {
            string target = Under(folder, "source/" + companion.Key); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Under(config, companion.Key), target);
            if (File.Exists(Under(config, companion.Key) + ".required")) File.Copy(Under(config, companion.Key) + ".required", target + ".required");
            if (Hash(target) != value.CompanionHashes[companion.Key]) throw new IOException("Companion changed while capturing recovery.");
        }
        if (Hash(Path.Combine(folder, "source.sav")) != value.NativeHash) throw new IOException("Save changed while capturing recovery.");
        ValidateCapture(folder,value); Write(Path.Combine(folder, "request.json"), value); Write(Path.Combine(folder,"journal.json"),value); return folder;
    }
    public static string TargetCompanion(RecoveryGeneration value, string source)
    {
        string owner = value.Companions[source]; if (owner == "hauling") throw new InvalidOperationException("Retired hauling state is archived, never resumed.");
        string key = Key(owner == "city" ? Path.GetFileNameWithoutExtension(value.TargetSlot).ToUpperInvariant() : value.TargetSlot);
        return source[..(source.LastIndexOf('/') + 1)] + key + ".json";
    }
    public static RecoveryGeneration RestorePending(string saves, string config, string id, bool abandon)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid recovery generation identity.");
        string folder = Under(config, "NivalisUnofficialPatch/Recovery/" + id);
        var current = Read(Path.Combine(folder, "journal.json"));
        if (current.State is "Prepared" or "Abandoned" && !RecoveryWriteSet.HasPending(folder)) throw new InvalidOperationException("This generation has no interrupted recovery.");
        string writeSet = Path.Combine(folder, "pending", "write-set.json");
        if (File.Exists(writeSet))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(writeSet));
            if (json.RootElement.GetProperty("State").GetString() is "Pending" or "Restored") current = RecoveryWriteSet.Restore(saves, config, folder);
        }
        if (current.State == "Complete") return RecoveryPublication.RequireSlot(saves,config,folder,current.PublishedSlot);
        string requestPath=Path.Combine(folder,"request.json");
        if(!File.Exists(requestPath))
        {
            if(!abandon||current.DestinationOwned||current.OwnedCopies.Count!=0)throw new InvalidDataException("Capture is incomplete; only abandonment of its private journal is available.");
            current.State="Abandoned";Write(Path.Combine(folder,"journal.json"),current);return current;
        }
        var request = Read(requestPath);ValidateCapture(folder,request);
        string native = Under(saves, current.TargetSlot + ".sav");
        var remove = new List<string>();
        if (current.DestinationOwned && File.Exists(native))
        {
            if (!CapturedPrefix(native,Path.Combine(folder,"source.sav"))) throw new IOException("Unpublished native destination differs from its verified captured source.");
            remove.Add(native);
        }
        foreach (var source in request.Companions.Where(p => p.Value != "hauling"))
        {
            string target = Under(config, TargetCompanion(request, source.Key));
            if (File.Exists(target))
            {
                if (!current.OwnedCopies.Contains("config/" + TargetCompanion(request,source.Key)) || !CapturedPrefix(target,Under(folder,"source/"+source.Key))) throw new IOException("Unpublished companion is unowned or changed externally.");
                remove.Add(target);
            }
            if (File.Exists(target + ".required"))
            {
                if (!current.OwnedCopies.Contains("config/" + TargetCompanion(request,source.Key) + ".required") || File.ReadAllText(target + ".required").Trim() != request.NativeHash) throw new IOException("Unpublished requirement changed externally.");
                remove.Add(target + ".required");
            }
        }
        foreach (string target in remove) File.Delete(target);
        request.State = abandon ? "Abandoned" : "Prepared";
        Write(Path.Combine(folder, "journal.json"), request); return request;
    }
    private static bool CapturedPrefix(string candidate,string source)
    {
        using var actual=File.OpenRead(candidate);using var expected=File.OpenRead(source);
        if(actual.Length>expected.Length)return false;
        byte[] a=new byte[65536],b=new byte[65536];int count;
        while((count=actual.Read(a,0,a.Length))>0){int offset=0;while(offset<count){int n=expected.Read(b,offset,count-offset);if(n==0)return false;offset+=n;}if(!a.AsSpan(0,count).SequenceEqual(b.AsSpan(0,count)))return false;}
        return true;
    }
}
