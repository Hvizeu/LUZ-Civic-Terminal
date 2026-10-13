namespace NivalisRecovery;

public static class RecoveryRecordRules
{
    public static bool Retired(IReadOnlyDictionary<string,string> workers,string packet,string person)
    {
        if(!workers.ContainsKey(packet))return false;
        if(packet!=person)throw new InvalidDataException("Retired person packet identity is ambiguous.");
        return true;
    }
    public static void PayloadBoundary(long position,long length,long end)
    {if(position<0||end<position||end>length)throw new InvalidDataException("Invalid retired ghost object boundary.");}
    public static void Table(string id,bool runtime,bool placement,bool ghost,string? parent,IReadOnlySet<string> retired)
    {
        if(!runtime||!placement)throw new InvalidDataException("Retired table "+id+" lacks a unique native placement record.");
        if(!ghost)throw new InvalidDataException("Table "+id+" has no surviving ghost record to establish contents and relationships uniquely. Destination was not published.");
        if(parent!=null&&retired.Contains(parent))throw new InvalidDataException("Table "+id+" still references a retired worker.");
    }
}
