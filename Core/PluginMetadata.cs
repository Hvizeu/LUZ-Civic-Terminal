using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Luz;

public static class PluginMetadata
{
    public static List<PluginInfo> Read(string path)
    {
        using var stream = File.OpenRead(path); using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return [];
        var reader = pe.GetMetadataReader(); var result = new List<PluginInfo>();
        foreach (var handle in reader.TypeDefinitions)
        {
            string guid = "", name = "", version = "";
            List<PluginDependency> deps = []; List<string> conflicts = [];
            foreach (var h in reader.GetTypeDefinition(handle).GetCustomAttributes())
            {
                var attr = reader.GetCustomAttribute(h);
                EntityHandle owner = attr.Constructor.Kind == HandleKind.MemberReference ? reader.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent :
                    reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor).GetDeclaringType();
                string type = owner.Kind == HandleKind.TypeReference ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)owner).Name) :
                    owner.Kind == HandleKind.TypeDefinition ? reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)owner).Name) : "";
                if (type is not ("BepInPlugin" or "BepInDependency" or "BepInIncompatibility")) continue;
                var args = attr.DecodeValue(new AttributeTypes()).FixedArguments;
                string S(int index) => args[index].Value?.ToString() ?? "";
                if (type == "BepInPlugin") { guid = S(0); name = S(1); version = S(2); }
                if (type == "BepInIncompatibility") conflicts.Add(S(0));
                if (type == "BepInDependency") deps.Add(new(S(0), args.Length > 1 && args[1].Value is string ? S(1) : "", args.Length > 1 && args[1].Value is int flags && (flags & 2) != 0));
            }
            if (guid.Length > 0) result.Add(new(guid, name, version, deps, conflicts));
        }
        return result;
    }
    private sealed class AttributeTypes : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => r.GetString(r.GetTypeDefinition(h).Name);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => r.GetString(r.GetTypeReference(h).Name);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
