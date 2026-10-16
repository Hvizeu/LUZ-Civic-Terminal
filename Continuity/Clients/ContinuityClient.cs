using System.Reflection;

#if LUZ_CONTINUITY
[assembly: AssemblyMetadata("Nivalis.Continuity.Api", "1")]
[assembly: AssemblyMetadata("Nivalis.Continuity.Coverage", "activation,input-display;residual-native-references-require-exact-build")]
#endif

namespace NivalisCompatibility;

internal static class ContinuityClient
{
    private static Type? cachedHost;
    private static Type? Host => cachedHost ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Luz.Continuity.Runtime")?.GetType("Luz.Continuity.Runtime.Host");
    internal static bool Validate(string game, string assembly, string native, string metadata)
    {
        var host = Host;
        if (host == null || host.GetField("ApiVersion")?.GetRawConstantValue() is not 1) return false;
        return host.GetMethod("ValidateConsumer")?.Invoke(null, new object[] { game, assembly, native, metadata }) is true;
    }
    internal static bool IsDormant(string owner, string feature)
    {
#if LUZ_CONTINUITY
        return Host?.GetMethod("IsDormant")?.Invoke(null, new object[] { owner, feature }) is true;
#else
        return false;
#endif
    }
    private static Func<int, int, string?, bool>? binding;
    private static Action<object, float>? advance;
    private static Func<object, object, int>? insert;
    private static Action<object, object, object>? registerVendor;
    internal enum MutationOutcome { Failed = 0, Partial = 1, Succeeded = 2 }
    internal static MutationOutcome TryAdd(object destination, object item)
    {
        insert ??= Host?.GetMethod("TryAdd")?.CreateDelegate<Func<object, object, int>>() ?? throw new MissingMethodException("LUZ inventory adapter is unavailable.");
        return (MutationOutcome)insert(destination, item);
    }
    internal static void RegisterVendorItem(object vendor, object definition, object item)
    {
        registerVendor ??= Host?.GetMethod("RegisterVendorItem")?.CreateDelegate<Action<object, object, object>>() ?? throw new MissingMethodException("LUZ object registration adapter is unavailable.");
        registerVendor(vendor, definition, item);
    }
    internal static bool BindingValid(int index, int count, string? path)
    {
#if LUZ_CONTINUITY
        binding ??= Host?.GetMethod("BindingValid")?.CreateDelegate<Func<int, int, string?, bool>>() ?? throw new MissingMethodException("LUZ input adapter is unavailable.");
        return binding(index, count, path);
#else
        return index >= 0 && index < count && !string.IsNullOrEmpty(path);
#endif
    }
    internal static void AddTime(object clock, float seconds)
    {
        advance ??= Host?.GetMethod("AddTime")?.CreateDelegate<Action<object, float>>() ?? throw new MissingMethodException("LUZ clock adapter is unavailable.");
        advance(clock, seconds);
    }
    internal static string Status()
    {
#if LUZ_CONTINUITY
        return Host?.GetMethod("Status")?.Invoke(null, null) as string ?? "LUZ continuity unavailable";
#else
        return "";
#endif
    }
    internal static void CompleteRestore(string owner)
    {
#if LUZ_CONTINUITY
        var method = Host?.GetMethod("CompleteRestore") ?? throw new MissingMethodException("LUZ continuity restoration service is unavailable.");
        method.Invoke(null, new object[] { owner });
#endif
    }
    internal static bool RestorationPending(string owner) => Host?.GetMethod("RestorationPending")?.Invoke(null, new object[] { owner }) is true;
    internal static void FailRestore(string owner, Exception error)
    {
        var method = Host?.GetMethod("FailRestore") ?? throw new MissingMethodException("LUZ restoration failure service is unavailable.");
        method.Invoke(null, new object[] { owner, error });
    }
}
