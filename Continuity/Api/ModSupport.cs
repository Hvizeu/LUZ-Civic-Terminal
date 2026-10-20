using System.Text.RegularExpressions;

namespace Luz.Continuity;

public sealed record SupportPlugin(string Guid, string Assembly, string Version, string File, string Sha256);
public sealed record SupportCompatibility(string Native, string Metadata, int RequirementsSchema, string ContractsSha256);
public sealed record SupportPayload(string File, string Sha256);
public sealed record SupportImplementation(string Id, string Kind, string Adapter, int Priority);
public sealed record SupportCapability(string Name, SupportImplementation[] Implementations, string[] Dependencies);
public sealed record SupportCompanion(string Root, string Pattern, string Slot, bool Required, string Validation, int SchemaMinimum = 0, int SchemaMaximum = 0, string RevisionProperty = "Revision");
public sealed record SupportAdapter(string Id, string File, string Sha256, string Type, string[] Capabilities);

/// <summary>Package-local, author-supplied recovery and compatibility declaration.</summary>
public sealed class ModSupportDescriptor
{
    public int Schema { get; set; } = 1;
    public int RequiredApi { get; set; } = 1;
    public string Owner { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Provenance { get; set; } = "author";
    public string SupportContact { get; set; } = "";
    public SupportPlugin Plugin { get; set; } = new("", "", "", "", "");
    public SupportCompatibility Compatibility { get; set; } = new("", "", 0, "");
    public SupportPayload[] Payloads { get; set; } = Array.Empty<SupportPayload>();
    public SupportCapability[] Capabilities { get; set; } = Array.Empty<SupportCapability>();
    public SupportCompanion[] Companions { get; set; } = Array.Empty<SupportCompanion>();
    public SupportAdapter[] Adapters { get; set; } = Array.Empty<SupportAdapter>();

    public void Validate()
    {
        if (Schema != 1 || RequiredApi != 1) throw new InvalidDataException("Unsupported LUZ mod-support schema or API.");
        if (!Regex.IsMatch(Owner, "^[a-z0-9][a-z0-9._-]{2,95}$") || string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 120)
            throw new InvalidDataException("Invalid LUZ support owner or display name.");
        if (Provenance is not ("author" or "luz-maintained") || string.IsNullOrWhiteSpace(SupportContact) || SupportContact.Length > 512)
            throw new InvalidDataException("Invalid LUZ support provenance or contact.");
        if (string.IsNullOrWhiteSpace(Plugin.Guid) || string.IsNullOrWhiteSpace(Plugin.Assembly) || Plugin.File.Length is < 5 or > 240 || !Plugin.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("LUZ support must identify one BepInEx plugin DLL.");
        Relative(Plugin.File);
        Hash(Plugin.Sha256, "plugin");
        if (Compatibility.RequirementsSchema is < 0 or > 2) throw new InvalidDataException("Unsupported compiled-contract schema in LUZ support.");
        OptionalHash(Compatibility.Native, "native build"); OptionalHash(Compatibility.Metadata, "metadata build"); OptionalHash(Compatibility.ContractsSha256, "compiled contracts");
        if (Compatibility.RequirementsSchema == 0 && Compatibility.ContractsSha256.Length != 0 || Compatibility.RequirementsSchema == 1 && Compatibility.ContractsSha256.Length != 0 || Compatibility.RequirementsSchema == 2 && Compatibility.ContractsSha256.Length != 64)
            throw new InvalidDataException("Compiled-contract metadata does not match its schema.");
        if (Capabilities.Length > 128 || Companions.Length > 128 || Adapters.Length > 32 || Payloads.Length > 4096) throw new InvalidDataException("LUZ support declaration exceeds its limits.");
        if (Payloads.Select(p => p.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Payloads.Length) throw new InvalidDataException("Duplicate LUZ payload identity.");
        foreach (var payload in Payloads) { Relative(payload.File); Hash(payload.Sha256, "payload"); }
        if (Capabilities.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != Capabilities.Length ||
            Adapters.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != Adapters.Length)
            throw new InvalidDataException("Duplicate LUZ capability or adapter identity.");
        var adapterIds = Adapters.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var adapter in Adapters)
        {
            if (!Token(adapter.Id) || string.IsNullOrWhiteSpace(adapter.Type) || adapter.Type.Length > 240) throw new InvalidDataException("Invalid LUZ adapter identity.");
            Relative(adapter.File); if (!adapter.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("LUZ adapter must be a managed DLL.");
            Hash(adapter.Sha256, "adapter");
            if (adapter.Capabilities.Length > 128 || adapter.Capabilities.Any(c => !Capabilities.Any(d => d.Name == c))) throw new InvalidDataException("LUZ adapter names an undeclared capability.");
        }
        foreach (var capability in Capabilities)
        {
            if (!Token(capability.Name) || capability.Implementations.Length is < 1 or > 16 || capability.Dependencies.Length > 32)
                throw new InvalidDataException("Invalid LUZ capability declaration.");
            if (capability.Implementations.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != capability.Implementations.Length)
                throw new InvalidDataException("Duplicate LUZ capability implementation.");
            foreach (var implementation in capability.Implementations)
            {
                if (!Token(implementation.Id) || implementation.Kind is not ("native" or "adapter" or "dormant") || implementation.Priority is < -1000 or > 1000)
                    throw new InvalidDataException("Invalid LUZ capability implementation.");
                if (implementation.Kind == "adapter" && !adapterIds.Contains(implementation.Adapter)) throw new InvalidDataException("LUZ capability references a missing adapter.");
                if (implementation.Kind != "adapter" && implementation.Adapter.Length != 0) throw new InvalidDataException("Only adapter implementations can name an adapter.");
                if (implementation.Kind == "adapter" && !Adapters.Single(a => a.Id == implementation.Adapter).Capabilities.Contains(capability.Name, StringComparer.Ordinal)) throw new InvalidDataException("LUZ adapter does not declare the capability that selects it.");
            }
            if (capability.Dependencies.Any(d => !Token(d) || !Capabilities.Any(c => c.Name == d))) throw new InvalidDataException("LUZ capability references an unknown dependency.");
        }
        var visiting = new HashSet<string>(StringComparer.Ordinal); var complete = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            if (complete.Contains(name)) return;
            if (!visiting.Add(name)) throw new InvalidDataException("Cyclic LUZ capability dependency.");
            foreach (string dependency in Capabilities.Single(c => c.Name == name).Dependencies) Visit(dependency);
            visiting.Remove(name); complete.Add(name);
        }
        foreach (var capability in Capabilities) Visit(capability.Name);
        foreach (var companion in Companions)
        {
            if (companion.Root is not ("config" or "saves") || companion.Slot is not ("raw" or "lower" or "upper" or "sha256" or "sha256-upper") || companion.Validation is not ("opaque" or "json-revision"))
                throw new InvalidDataException("Invalid LUZ companion rule.");
            if (!companion.Pattern.Contains("{slot}", StringComparison.Ordinal) || companion.Pattern.Count(c => c == '{') != 1 || companion.Pattern.Count(c => c == '}') != 1)
                throw new InvalidDataException("LUZ companion paths must contain exactly one {slot} token.");
            Relative(companion.Pattern.Replace("{slot}", "slot", StringComparison.Ordinal));
            if (companion.SchemaMinimum < 0 || companion.SchemaMaximum < companion.SchemaMinimum || companion.SchemaMaximum > 1_000_000 || companion.RevisionProperty.Length > 80)
                throw new InvalidDataException("Invalid LUZ companion validation rule.");
        }
    }

    internal static bool Token(string value) => Regex.IsMatch(value, "^[a-z0-9][a-z0-9._-]{0,95}$");
    internal static void Hash(string value, string label) { if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid LUZ " + label + " hash."); }
    internal static void OptionalHash(string value, string label) { if (value.Length != 0) Hash(value, label); }
    internal static void Relative(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.StartsWith('/') || value.Contains(':') || value.Split('/').Any(p => p is "" or "." or ".." || p.Any(c => c < 32)))
            throw new InvalidDataException("Unsafe path in LUZ mod support.");
    }
}

public sealed class FrozenModSupport
{
    public string DescriptorPath { get; set; } = "";
    public string DescriptorSha256 { get; set; } = "";
    public ModSupportDescriptor Descriptor { get; set; } = new();
    public Dictionary<string, string> SelectedImplementations { get; set; } = new(StringComparer.Ordinal);
    public void Validate()
    {
        Descriptor.Validate(); ModSupportDescriptor.Hash(DescriptorSha256, "descriptor"); ModSupportDescriptor.Relative(DescriptorPath);
        if (SelectedImplementations.Count != Descriptor.Capabilities.Length) throw new InvalidDataException("LUZ support implementation selection is incomplete.");
        foreach (var capability in Descriptor.Capabilities)
            if (!SelectedImplementations.TryGetValue(capability.Name, out string? selected) || !capability.Implementations.Any(i => i.Id == selected))
                throw new InvalidDataException("LUZ support selected an unknown capability implementation.");
    }
}

public sealed record CompanionFile(string Owner, string Root, string Relative, bool Required, string Validation, int SchemaMinimum, int SchemaMaximum, string RevisionProperty);

public static class ModSupportPaths
{
    public static string Slot(SupportCompanion rule, string slot)
    {
        string safe = SafeFiles.Slot(slot);
        string key = rule.Slot switch
        {
            "raw" => safe,
            "lower" => safe.ToLowerInvariant(),
            "upper" => safe.ToUpperInvariant(),
            "sha256" => SafeFiles.Key(safe),
            "sha256-upper" => SafeFiles.Key(safe.ToUpperInvariant()),
            _ => throw new InvalidDataException("Unknown LUZ companion slot mapping.")
        };
        string relative = rule.Pattern.Replace("{slot}", key, StringComparison.Ordinal); ModSupportDescriptor.Relative(relative); return relative;
    }
}
