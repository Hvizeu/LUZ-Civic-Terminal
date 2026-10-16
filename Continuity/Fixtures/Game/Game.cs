namespace Nivalis;
// Test-owned model. This assembly contains no game source or game runtime.
public sealed class TimeOfDayManager
{
    public float Seconds { get; private set; }
    public void AddTime(float value) => Seconds += value;
}
public sealed class SerializationManager
{
    public static int MaxAutoSaves { get; set; } = 2;
    public string Root { get; set; } = "";
    public Func<string, bool, bool>? SaveCallback { get; set; }
    public bool Save(string name, bool auto = false) => SaveCallback?.Invoke(name, auto) ?? false;
    public void Load(string name) { }
}
