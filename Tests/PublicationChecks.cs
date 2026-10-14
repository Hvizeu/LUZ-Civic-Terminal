using NivalisRecovery;

internal static class PublicationChecks
{
    internal static void Run(string root,Action<bool,string> check,Action<Action,string> reject)
    {
        bool result=true;var cause=new IOException("companion publication failed");Exception? retained=null;int publications=0;
        RecoveryPublication.CompleteSave(ref result,()=>{publications++;throw cause;},error=>retained=error);
        check(!result&&ReferenceEquals(retained,cause)&&publications==1,"failed companion publication changes the actual Save result and retains its first cause");
        result=false;publications=0;RecoveryPublication.CompleteSave(ref result,()=>publications++,error=>retained=error);
        check(!result&&publications==0,"a refused native save cannot continue into companion publication");
        result=true;
        try{RecoveryPublication.CompleteSave(ref result,()=>throw cause,error=>throw new IOException("retention failed"));throw new Exception("Missing failure");}
        catch(AggregateException error){check(!result&&ReferenceEquals(error.InnerExceptions[0],cause)&&error.InnerExceptions.Count==2,"failure-retention errors preserve the failed Save result and original publication cause");}
        string saves=Path.Combine(root,"PublicationSaves"),config=Path.Combine(root,"PublicationConfig");Directory.CreateDirectory(saves);Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(saves,"Original.sav"),"preserved original");
        var generation=RecoveryFiles.Inspect(saves,config,"Original");string folder=RecoveryFiles.Prepare(saves,config,generation);
        string native=Path.Combine(saves,generation.TargetSlot+".sav"),receipt=Path.Combine(config,RecoveryWriteSet.ReceiptRelative(generation.TargetSlot)),journal=Path.Combine(folder,"journal.json");
        RecoveryWriteSet.Capture(saves,config,folder,generation,generation.TargetSlot);
        File.WriteAllText(native,"first recovered generation");generation.State="Complete";generation.PublishedSlot=generation.TargetSlot;generation.OutputHash=RecoveryFiles.Hash(native);
        RecoveryFiles.Write(receipt,generation);RecoveryFiles.Write(journal,generation);
        check(RecoveryPublication.Inspect(saves,config,folder).State==RecoveryPublicationState.Interrupted,"complete receipt and journal cannot bypass pending publication");
        reject(()=>RecoveryPublication.RequireSlot(saves,config,folder,generation.TargetSlot),"unfinished checkpoint refuses native load admission");
        RecoveryPublication.Commit(saves,config,folder,generation);
        check(RecoveryPublication.Inspect(saves,config,folder).State==RecoveryPublicationState.Complete,"complete native generation and committed receipt admit recovery");
        string good=File.ReadAllText(native);
        RecoveryWriteSet.Capture(saves,config,folder,generation,generation.TargetSlot);
        File.WriteAllText(native,"interrupted new timeline");generation.OutputHash=RecoveryFiles.Hash(native);RecoveryFiles.Write(receipt,generation);RecoveryFiles.Write(journal,generation);RecoveryWriteSet.Observe(saves,config,folder,false);
        check(RecoveryPublication.Inspect(saves,config,folder).State==RecoveryPublicationState.Interrupted,"a later save remains quarantined even when records say Complete");
        if(OperatingSystem.IsWindows())
        {
            using(var locked=new FileStream(native,FileMode.Open,FileAccess.Read,FileShare.Read))reject(()=>RecoveryWriteSet.Restore(saves,config,folder),"partial rollback retains checkpoint when native replacement is locked");
        }
        generation.FirstFailure="fixture publication fault";generation.FailureOccurrences=2;generation.CleanupFailures.Add("fixture cleanup fault");RecoveryFiles.Write(journal,generation);
        generation=RecoveryFiles.RestorePending(saves,config,generation.Id,false);
        check(File.ReadAllText(native)==good&&RecoveryPublication.Inspect(saves,config,folder).State==RecoveryPublicationState.Complete,"rollback resumes after partial replacement and restores coherent receipt/native state");
        check(File.ReadAllText(Path.Combine(folder,"last-restoration.json")).Contains("fixture publication fault"),"verified restoration keeps the first failure and cleanup evidence outside the cleared transaction");
        RecoveryWriteSet.Restore(saves,config,folder);
        check(RecoveryPublication.RequireSlot(saves,config,folder,generation.TargetSlot).OutputHash==generation.OutputHash,"repeated checkpoint restoration is idempotent");
        for(int i=0;i<5;i++)
        {
            RecoveryWriteSet.Capture(saves,config,folder,generation,generation.TargetSlot);File.WriteAllText(native,"new generation "+i);generation.OutputHash=RecoveryFiles.Hash(native);
            RecoveryFiles.Write(receipt,generation);RecoveryFiles.Write(journal,generation);RecoveryPublication.Commit(saves,config,folder,generation);
        }
        check(Directory.GetDirectories(folder,"checkpoint-*").Length==1,"completed saves retain one full rollback checkpoint instead of unbounded history");
        check(File.ReadAllText(Path.Combine(saves,"Original.sav"))=="preserved original","publication and restoration never modify the original timeline");
        File.WriteAllText(native,"external change");reject(()=>RecoveryPublication.RequireSlot(saves,config,folder,generation.TargetSlot),"externally changed native generations are not admitted");
        string oversized=Path.Combine(root,"TooManyGenerations");Directory.CreateDirectory(oversized);for(int i=0;i<65;i++)Directory.CreateDirectory(Path.Combine(oversized,i.ToString()));
        reject(()=>RecoveryPublication.Directories(oversized),"generation limits refuse instead of silently omitting journals");
    }
}
