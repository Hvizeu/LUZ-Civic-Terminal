using System.Text.Json;
using System.Text.Json.Nodes;

namespace NivalisRecovery;

public enum RecoveryPublicationState { Prepared, Complete, Interrupted, Abandoned }
public sealed record RecoveryPublicationResult(RecoveryGeneration Generation, RecoveryPublicationState State, string Detail);

// One admission rule for the runtime and the launcher. Receipts alone are not commits.
public static class RecoveryPublication
{
    internal static void CompleteSave(ref bool result,Action publish,Action<Exception> retain)
    {
        try{if(!result)throw new IOException("Native save refused during recovery publication.");publish();}
        catch(Exception first)
        {
            result=false;
            try{retain(first);}catch(Exception cleanup){throw new AggregateException("Save publication and failure retention both failed.",first,cleanup);}
        }
    }
    public static IEnumerable<string> Directories(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var folders = Directory.EnumerateDirectories(root).Where(d => Path.GetFileName(d) != "slots").Take(65).ToArray();
        if (folders.Length > 64) throw new InvalidDataException("Recovery exceeds 64 generations; inspection stopped without omitting records.");
        foreach (string folder in folders) RecoveryFiles.Under(root, Path.GetFileName(folder) + "/journal.json");
        return folders;
    }
    private static string Marker(string folder, string slot) => RecoveryFiles.Under(folder, "commits/" + Path.GetFileNameWithoutExtension(RecoveryWriteSet.ReceiptRelative(slot)) + ".json");
    public static RecoveryPublicationResult Inspect(string saves, string config, string folder)
    {
        var generation = RecoveryFiles.Read(Path.Combine(folder, "journal.json"));
        if (RecoveryWriteSet.HasPending(folder)) return new(generation, RecoveryPublicationState.Interrupted, "Recovery checkpoint is unfinished.");
        if (generation.State == "Abandoned") return new(generation, RecoveryPublicationState.Abandoned, "Abandoned");
        if (generation.State == "Prepared") return new(generation, RecoveryPublicationState.Prepared, "Prepared for explicit native recovery.");
        if (generation.State != "Complete") return new(generation, RecoveryPublicationState.Interrupted, "Recovery is " + generation.State + ".");
        try { RequireSlot(saves, config, folder, generation.PublishedSlot); return new(generation, RecoveryPublicationState.Complete, "Publication verified."); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return new(generation, RecoveryPublicationState.Interrupted, error.Message); }
    }
    public static RecoveryGeneration RequireSlot(string saves, string config, string folder, string slot)
    {
        RecoveryFiles.Slot(slot);
        var journal = RecoveryFiles.Read(Path.Combine(folder, "journal.json"));
        if (journal.State != "Complete" || RecoveryWriteSet.HasPending(folder)) throw new InvalidDataException("Recovery publication is unfinished; restore its verified checkpoint in LUZ Maintenance.");
        var receipt = RecoveryFiles.Read(RecoveryFiles.Under(config, RecoveryWriteSet.ReceiptRelative(slot)));
        if (receipt.Id != journal.Id || receipt.State != "Complete" || receipt.PublishedSlot != slot || receipt.NativeHash != journal.NativeHash)
            throw new InvalidDataException("Recovery journal and slot receipt disagree.");
        VerifyOutputs(saves, config, receipt);
        if(journal.PublishedSlot==slot)RecoveryWriteSet.VerifyCompleted(saves,config,folder,slot);
        string marker = Marker(folder, slot);
        if (File.Exists(marker))
        {
            if (new FileInfo(marker).Length > 32768) throw new InvalidDataException("Recovery commit exceeds its size limit.");
            var commit = JsonSerializer.Deserialize<CommitRecord>(File.ReadAllText(marker)) ?? throw new InvalidDataException("Empty recovery commit.");
            if (commit.Schema != 1 || commit.Generation != receipt.Id || commit.Slot != slot || commit.NativeHash != receipt.OutputHash || commit.ReceiptHash != RecoveryFiles.Hash(RecoveryFiles.Under(config, RecoveryWriteSet.ReceiptRelative(slot))))
                throw new InvalidDataException("Recovery commit does not match this slot generation.");
        }
        else
        {
            // Existing schema-one timelines are admitted only with their actual completed checkpoint.
            RecoveryWriteSet.VerifyCompleted(saves, config, folder, slot);
            if (JsonSerializer.Serialize(receipt) != JsonSerializer.Serialize(journal)) throw new InvalidDataException("Legacy recovery journal and receipt disagree.");
        }
        RecoveryFiles.ValidateCapture(folder, receipt, true);
        return receipt;
    }
    internal static void VerifyOutputs(string saves, string config, RecoveryGeneration receipt)
    {
        if(new FileInfo(RecoveryFiles.Under(saves,receipt.PublishedSlot+".sav")).Length>128*1024*1024)throw new InvalidDataException("Recovered native output exceeds its supported size limit.");
        if (RecoveryFiles.Hash(RecoveryFiles.Under(saves, receipt.PublishedSlot + ".sav")) != receipt.OutputHash) throw new InvalidDataException("Recovery receipt differs from the native save generation.");
        string original = receipt.TargetSlot; receipt.TargetSlot = receipt.PublishedSlot;
        try
        {
            foreach (var pair in receipt.Companions.Where(p => p.Value != "hauling"))
            {
                string path = RecoveryFiles.Under(config, RecoveryFiles.TargetCompanion(receipt, pair.Key));
                if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Recovery companion exceeds its size limit.");
                var document = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Invalid recovery companion.");
                int schema=(int?)(document["Schema"]??document["Version"])??-1;
                if(pair.Value=="city"?schema is <1 or >3:schema!=2)throw new InvalidDataException("Unsupported recovered companion schema: "+pair.Key);
                if ((string?)document["Revision"] != receipt.OutputHash) throw new InvalidDataException("Recovery companion revision differs: " + pair.Key);
                if (File.Exists(path + ".required") && (new FileInfo(path + ".required").Length > 256 || File.ReadAllText(path + ".required").Trim() != receipt.OutputHash))
                    throw new InvalidDataException("Recovery companion requirement differs: " + pair.Key);
            }
        }
        finally { receipt.TargetSlot = original; }
    }
    public static void Commit(string saves, string config, string folder, RecoveryGeneration generation)
    {
        if (generation.State != "Complete") throw new InvalidDataException("Only a validated complete generation can be committed.");
        VerifyOutputs(saves, config, generation);
        var journal = RecoveryFiles.Read(Path.Combine(folder, "journal.json"));
        var receipt = RecoveryFiles.Read(RecoveryFiles.Under(config, RecoveryWriteSet.ReceiptRelative(generation.PublishedSlot)));
        if (JsonSerializer.Serialize(generation) != JsonSerializer.Serialize(journal) || JsonSerializer.Serialize(generation) != JsonSerializer.Serialize(receipt)) throw new InvalidDataException("Recovery publication records disagree.");
        string path = Marker(folder, generation.PublishedSlot); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var record = new CommitRecord { Generation = generation.Id, Slot = generation.PublishedSlot, NativeHash = generation.OutputHash, ReceiptHash = RecoveryFiles.Hash(RecoveryFiles.Under(config, RecoveryWriteSet.ReceiptRelative(generation.PublishedSlot))) };
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, record); stream.Flush(true); }
        RecoveryWriteSet.RecordExpected(folder,"config/NivalisUnofficialPatch/Recovery/"+generation.Id+"/commits/"+Path.GetFileName(path),RecoveryFiles.Hash(path+".tmp"));
        File.Move(path + ".tmp", path, true);
        RecoveryWriteSet.Observe(saves, config, folder, true);
    }
    private sealed class CommitRecord
    {
        public int Schema { get; set; } = 1;
        public string Generation { get; set; } = "";
        public string Slot { get; set; } = "";
        public string NativeHash { get; set; } = "";
        public string ReceiptHash { get; set; } = "";
    }
}
