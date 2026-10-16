namespace Luz.Continuity;

/// <summary>Stable call names keep consumers independent of adapter implementation versions.</summary>
public sealed class Operations
{
    private readonly Dictionary<string, (CapabilityState State, Func<object?[], object?> Invoke)> entries = new(StringComparer.Ordinal);
    private bool sealedRegistry;
    public void Register(string name, CapabilityState state, Func<object?[], object?> invoke)
    {
        if (sealedRegistry || state is CapabilityState.Dormant or CapabilityState.Blocked || !entries.TryAdd(name, (state, invoke))) throw new InvalidOperationException("Invalid or duplicate compatibility operation.");
    }
    public void Seal() => sealedRegistry = true;
    public T Invoke<T>(string name, params object?[] args)
    {
        if (!sealedRegistry || !entries.TryGetValue(name, out var entry)) throw new NotSupportedException("Compatibility operation is unavailable: " + name);
        // No retry/fallback after invocation: this call may already have native side effects.
        object? result = entry.Invoke(args);
        return result is T value ? value : throw new InvalidOperationException("Compatibility operation returned an invalid result: " + name);
    }
    public CapabilityState State(string name) => entries.TryGetValue(name, out var entry) ? entry.State : CapabilityState.Blocked;
}

public static class InputPolicy
{
    public static bool BindingValid(int index, int count, string? path) => index >= 0 && index < count && !string.IsNullOrEmpty(path);
}
