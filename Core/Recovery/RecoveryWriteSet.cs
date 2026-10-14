using System.Text.Json;

namespace NivalisRecovery;

public sealed class RecoveryWriteSet
{
    public int Schema { get; set; } = 2;
    public string State { get; set; } = "Pending";
    public string Slot { get; set; } = "";
    public RecoveryGeneration Before { get; set; } = new();
    public Dictionary<string, string> Hashes { get; set; } = new();
    public Dictionary<string, string> Observed { get; set; } = new();
    public static string ReceiptRelative(string slot) => "NivalisUnofficialPatch/Recovery/slots/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(slot))) + ".json";
    private static string Location(string saves, string config, string key)
        => key.StartsWith("save/", StringComparison.Ordinal) ? RecoveryFiles.Under(saves, key[5..]) : key.StartsWith("config/", StringComparison.Ordinal) ? RecoveryFiles.Under(config, key[7..]) : throw new InvalidDataException("Unrecognized recovery write owner.");
    private static string HashOrAbsent(string path) => File.Exists(path) ? RecoveryFiles.Hash(path) : "absent";
    internal static RecoveryWriteSet Read(string path)
    {
        if(new FileInfo(path).Length>32*1024*1024)throw new InvalidDataException("Recovery write set exceeds its size limit.");
        var value=JsonSerializer.Deserialize<RecoveryWriteSet>(File.ReadAllText(path))??throw new InvalidDataException("Missing recovery write set.");
        if(value.Schema is not (1 or 2)||value.State is not ("Pending" or "Committed" or "Restored")||value.Hashes==null||value.Observed==null)throw new InvalidDataException("Unsupported recovery write set.");
        value.Before.Validate();RecoveryFiles.Slot(value.Slot);
        var expected=Keys(value.Before,value.Slot,value.Schema);
        if(!expected.OrderBy(k=>k).SequenceEqual(value.Hashes.Keys.OrderBy(k=>k))||value.Hashes.Values.Any(h=>h!="absent"&&!RecoveryFiles.IsHash(h))||value.Observed.Any(p=>!value.Hashes.ContainsKey(p.Key)||p.Value!="absent"&&!RecoveryFiles.IsHash(p.Value)))throw new InvalidDataException("Recovery write set has ambiguous file ownership.");
        return value;
    }
    private static List<string> Keys(RecoveryGeneration generation,string slot,int schema=2)
    {
        var keys=new List<string>{"config/"+ReceiptRelative(slot)};
        if(schema>=2)keys.Add("config/NivalisUnofficialPatch/Recovery/"+generation.Id+"/commits/"+Path.GetFileName(ReceiptRelative(slot)));
        foreach(string extension in new[]{".sav",".png"})foreach(string suffix in new[]{"",".bak",".temp"})keys.Add("save/"+slot+extension+suffix);
        string original=generation.TargetSlot;generation.TargetSlot=slot;
        try{foreach(var pair in generation.Companions.Where(p=>p.Value!="hauling")){string target=RecoveryFiles.TargetCompanion(generation,pair.Key);keys.Add("config/"+target);keys.Add("config/"+target+".required");if(schema>=2)keys.Add("config/"+target+".bak");}}
        finally{generation.TargetSlot=original;}
        return keys;
    }
    private static void Write(string path, RecoveryWriteSet value)
    {
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, value); stream.Flush(true); }
        File.Move(path + ".tmp", path, true);
    }
    private static string? Current(string folder)
    {
        string pending=Path.Combine(folder,"pending","write-set.json");
        if(File.Exists(pending))return pending;
        return Directory.EnumerateDirectories(folder,"checkpoint-*").OrderByDescending(d=>Directory.GetLastWriteTimeUtc(d)).Select(d=>Path.Combine(d,"write-set.json")).FirstOrDefault(File.Exists);
    }
    public static bool HasPending(string folder)
    { string? path=Current(folder);return path!=null&&Read(path).State=="Pending"; }
    public static void VerifyCompleted(string saves,string config,string folder,string slot)
    {
        string path=Current(folder)??throw new InvalidDataException("Recovery has no committed checkpoint.");var value=Read(path);
        if(value.State=="Pending"||value.Slot!=slot)throw new InvalidDataException("Recovery checkpoint is unfinished or belongs to another slot.");
        var expected=value.State=="Restored"?value.Hashes:value.Observed;
        if(expected.Count!=value.Hashes.Count||expected.Any(p=>HashOrAbsent(Location(saves,config,p.Key))!=p.Value))throw new InvalidDataException("Recovery checkpoint outputs differ from their committed generation.");
    }
    public static void RecordExpected(string folder,string key,string hash)
    {
        string path=Path.Combine(folder,"pending","write-set.json");var value=Read(path);
        if(value.State!="Pending"||!value.Hashes.ContainsKey(key)||!RecoveryFiles.IsHash(hash))throw new InvalidDataException("Unowned recovery publication output.");
        value.Observed[key]=hash;Write(path,value);
    }
    public static void Capture(string saves, string config, string folder, RecoveryGeneration generation, string slot)
    {
        RecoveryFiles.Slot(slot);
        if (slot == generation.SourceSlot) throw new InvalidOperationException("Recovery must preserve its original slot.");
        string pending = Path.Combine(folder, "pending"), journal = Path.Combine(pending, "write-set.json");
        if (File.Exists(journal))
        {
            var old = Read(journal);
            if (old.State == "Pending") throw new InvalidOperationException("A recovery write is already pending.");
            Directory.Move(pending, Path.Combine(folder, "checkpoint-" + Guid.NewGuid().ToString("N")));
        }
        Directory.CreateDirectory(pending);
        var value = new RecoveryWriteSet { Slot = slot, Before = JsonSerializer.Deserialize<RecoveryGeneration>(JsonSerializer.Serialize(generation))! };
        var keys=Keys(generation,slot);
        foreach (string key in keys)
        {
            string source = Location(saves, config, key), hash = HashOrAbsent(source); value.Hashes.Add(key, hash);
            if (hash == "absent") continue;
            string target = RecoveryFiles.Under(pending, "before/" + key); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true);
            if (RecoveryFiles.Hash(target) != hash || HashOrAbsent(source) != hash) throw new IOException("Recovery write inputs changed while checkpointing.");
        }
        Write(journal, value);
    }
    public static void Observe(string saves, string config, string folder, bool committed)
    {
        string path = Path.Combine(folder, "pending", "write-set.json"); if (!File.Exists(path)) return;
        var value = Read(path);
        value.Observed = value.Hashes.ToDictionary(p => p.Key, p => HashOrAbsent(Location(saves, config, p.Key)));
        value.State = committed ? "Committed" : "Pending"; Write(path, value);
        if(committed)Prune(folder);
    }
    private static void Prune(string folder)
    {
        var checkpoints=Directory.EnumerateDirectories(folder,"checkpoint-*").OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        foreach(string checkpoint in checkpoints.Skip(1))
        {
            string manifest=RecoveryFiles.Under(folder,Path.GetFileName(checkpoint)+"/write-set.json");var old=Read(manifest);
            if(!old.Before.Id.Equals(Path.GetFileName(folder),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archived checkpoint belongs to another generation; it was preserved.");
            if(old.State=="Pending")throw new InvalidDataException("An archived recovery checkpoint is unfinished; it was preserved.");
            foreach(var entry in old.Hashes.Where(p=>p.Value!="absent"))
                if(RecoveryFiles.Hash(RecoveryFiles.Under(checkpoint,"before/"+entry.Key))!=entry.Value)throw new InvalidDataException("Archived recovery evidence is incomplete; it was preserved.");
            var owned=old.Hashes.Where(p=>p.Value!="absent").Select(p=>"before/"+p.Key).Append("write-set.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(string file in Directory.EnumerateFiles(checkpoint,"*",SearchOption.AllDirectories))
            {string relative=Path.GetRelativePath(checkpoint,file).Replace('\\','/');RecoveryFiles.Under(checkpoint,relative);if(!owned.Contains(relative))throw new InvalidDataException("Unowned archived checkpoint evidence was retained.");}
            string history=RecoveryFiles.Under(folder,"checkpoint-history.json");
            var entries=File.Exists(history)?JsonSerializer.Deserialize<List<string>>(File.ReadAllText(history))??new():new List<string>();
            entries.Add(Path.GetFileName(checkpoint)+":"+old.Slot+":"+RecoveryFiles.Hash(manifest));
            File.WriteAllText(history,JsonSerializer.Serialize(entries.TakeLast(128)));
            Directory.Delete(checkpoint,true);
        }
    }
    public static RecoveryGeneration Restore(string saves, string config, string folder)
    {
        string pending = Path.Combine(folder, "pending"), path = Path.Combine(pending, "write-set.json");
        var value = Read(path);
        if (value.State == "Committed") throw new InvalidDataException("No pending recovery write to restore.");
        value.Before.Validate(); RecoveryFiles.Slot(value.Slot);
        foreach (var entry in value.Hashes)
        {
            string current = HashOrAbsent(Location(saves, config, entry.Key));
            if (current != entry.Value && (!value.Observed.TryGetValue(entry.Key, out string? observed) || current != observed)) throw new IOException("Recovery destination changed externally: " + entry.Key);
            if (entry.Value != "absent" && RecoveryFiles.Hash(RecoveryFiles.Under(pending, "before/" + entry.Key)) != entry.Value) throw new InvalidDataException("Recovery checkpoint is incomplete.");
        }
        foreach (var entry in value.Hashes)
        {
            string target = Location(saves, config, entry.Key);
            if (entry.Value == "absent") { if (File.Exists(target)) File.Delete(target); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temp = target + ".recovery-restore"; File.Copy(RecoveryFiles.Under(pending, "before/" + entry.Key), temp, true); File.Move(temp, target, true);
            }
            if (HashOrAbsent(target) != entry.Value) throw new IOException("Recovery checkpoint restoration verification failed.");
        }
        var failed=RecoveryFiles.Read(Path.Combine(folder,"journal.json"));
        if(failed.FirstFailure.Length!=0)
        {
            string diagnostic=RecoveryFiles.Under(folder,"last-restoration.json");
            using(var stream=new FileStream(diagnostic+".tmp",FileMode.Create,FileAccess.Write,FileShare.None))
            {JsonSerializer.Serialize(stream,new{Schema=1,failed.Id,Slot=value.Slot,failed.FirstFailure,failed.CleanupFailures,failed.FailureOccurrences,Outcome="Restored",Checkpoint=RecoveryFiles.Hash(path)});stream.Flush(true);}
            File.Move(diagnostic+".tmp",diagnostic,true);
        }
        RecoveryFiles.Write(Path.Combine(folder, "journal.json"), value.Before);
        value.State = "Restored"; Write(path, value);
        return value.Before;
    }
}
